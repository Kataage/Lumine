package scanner

import (
	"fmt"
	"log/slog"
	"os"
	"path/filepath"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

// SyncResult is the compact result used by the viewer's silent background sync.
type SyncResult struct {
	LibraryID    int64 `json:"libraryId"`
	ScannedCount int   `json:"scannedCount"`
	AddedCount   int   `json:"addedCount"`
	UpdatedCount int   `json:"updatedCount"`
	RemovedCount int   `json:"removedCount"`
	SkippedCount int   `json:"skippedCount"`
	FailedCount  int   `json:"failedCount"`
	Changed      bool  `json:"changed"`
}

// SyncLibrary performs a lightweight reconciliation without creating job-log
// rows, emitting scan progress events, or touching unchanged DB records. It is
// intended for frequent viewer checks while the app is open.
func (s *Scanner) SyncLibrary(library *domain.Library, excludedDirs []string) (*SyncResult, error) {
	if !s.scanning.CompareAndSwap(false, true) {
		return nil, fmt.Errorf("scan already in progress")
	}
	defer s.scanning.Store(false)
	s.cancelled.Store(false)

	result := &SyncResult{LibraryID: library.ID}
	existingMap, err := s.assetRepo.GetSyncFilePathsMap(library.ID)
	if err != nil {
		return nil, fmt.Errorf("preload existing assets: %w", err)
	}

	excludedSet := make(map[string]bool, len(excludedDirs))
	for _, path := range excludedDirs {
		excludedSet[strings.ToLower(path)] = true
	}
	exts := s.getExtensions()
	seenFolders := make(map[string]struct{})

	var newAssets []*domain.Asset
	var updatedAssets []*domain.Asset
	const batchSize = 250

	flushNew := func() {
		if len(newAssets) == 0 {
			return
		}
		if err := s.assetRepo.CreateBatch(newAssets); err != nil {
			slog.Warn("silent sync batch create failed", "error", err)
			result.FailedCount += len(newAssets)
		} else {
			result.AddedCount += len(newAssets)
		}
		newAssets = newAssets[:0]
	}

	flushUpdated := func() {
		if len(updatedAssets) == 0 {
			return
		}
		if err := s.assetRepo.UpdateBatch(updatedAssets); err != nil {
			slog.Warn("silent sync batch update failed", "error", err)
			result.FailedCount += len(updatedAssets)
		} else {
			result.UpdatedCount += len(updatedAssets)
		}
		updatedAssets = updatedAssets[:0]
	}

	walkErr := filepath.Walk(library.RootPath, func(path string, info os.FileInfo, err error) error {
		if s.cancelled.Load() {
			return filepath.SkipDir
		}
		if err != nil {
			result.FailedCount++
			return nil
		}

		if info.IsDir() {
			if strings.HasPrefix(info.Name(), ".") || excludedSet[strings.ToLower(path)] {
				return filepath.SkipDir
			}
			seenFolders[path] = struct{}{}
			if s.folderRepo != nil {
				parentPath := filepath.Dir(path)
				if path == library.RootPath {
					parentPath = ""
				}
				if err := s.folderRepo.UpsertFolder(library.ID, path, parentPath); err != nil {
					result.FailedCount++
				}
			}
			return nil
		}

		ext := strings.ToLower(filepath.Ext(path))
		if !exts[ext] {
			return nil
		}

		result.ScannedCount++
		existing := existingMap[path]
		modTime := info.ModTime()
		if existing != nil {
			delete(existingMap, path)
			if !existing.ModifiedAtFS.IsZero() && existing.ModifiedAtFS.Equal(modTime) && existing.FileSize == info.Size() {
				result.SkippedCount++
				return nil
			}

			existing.FileSize = info.Size()
			existing.ModifiedAtFS = modTime
			existing.ThumbStatus = domain.ThumbStatusNone
			existing.MetadataLoaded = false
			existing.CameraModel = ""
			existing.LensModel = ""
			existing.FocalLength = ""
			existing.Aperture = ""
			existing.ShutterSpeed = ""
			existing.ISO = 0
			existing.ExifDate = ""
			existing.GPSLatitude = ""
			existing.GPSLongitude = ""
			existing.Width, existing.Height, existing.MimeType = readImageConfig(path)
			updatedAssets = append(updatedAssets, existing)
			if len(updatedAssets) >= batchSize {
				flushUpdated()
			}
			return nil
		}

		width, height, mimeType := readImageConfig(path)
		newAssets = append(newAssets, &domain.Asset{
			LibraryID:      library.ID,
			FolderPath:     filepath.Dir(path),
			FileName:       info.Name(),
			FilePath:       path,
			Extension:      ext,
			FileSize:       info.Size(),
			ModifiedAtFS:   modTime,
			Width:          width,
			Height:         height,
			MimeType:       mimeType,
			ThumbStatus:    domain.ThumbStatusNone,
			MetadataLoaded: false,
			StatusLabel:    domain.StatusUnsorted,
		})
		if len(newAssets) >= batchSize {
			flushNew()
		}
		return nil
	})

	flushNew()
	flushUpdated()

	if walkErr != nil {
		return nil, walkErr
	}
	if s.cancelled.Load() {
		return result, nil
	}

	// Missing-path inference is destructive to DB-side metadata (notes, tags,
	// post associations cascade from assets). If this reconciliation was only a
	// partial view of the filesystem because any walk/DB step failed, preserve
	// unseen rows and retry deletion detection on a later clean pass.
	if result.FailedCount > 0 {
		result.Changed = result.AddedCount > 0 || result.UpdatedCount > 0
		return result, nil
	}

	for _, missing := range existingMap {
		if err := s.assetRepo.Delete(missing.ID); err != nil {
			result.FailedCount++
			continue
		}
		result.RemovedCount++
	}

	if s.folderRepo != nil {
		if folders, err := s.folderRepo.GetTreeByLibrary(library.ID); err == nil {
			for _, folder := range folders {
				if _, seen := seenFolders[folder.Path]; seen {
					continue
				}
				if err := s.folderRepo.DeletePath(library.ID, folder.Path); err != nil {
					result.FailedCount++
				}
			}
		}
	}

	result.Changed = result.AddedCount > 0 || result.UpdatedCount > 0 || result.RemovedCount > 0
	return result, nil
}
