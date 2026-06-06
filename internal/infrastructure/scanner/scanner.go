package scanner

import (
	"encoding/json"
	"fmt"
	"log/slog"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

var DefaultImageExtensions = map[string]bool{
	".jpg": true, ".jpeg": true, ".png": true, ".gif": true,
	".bmp": true, ".webp": true, ".tiff": true, ".tif": true,
	".ico": true, ".svg": true, ".avif": true, ".apng": true,
}

type Scanner struct {
	assetRepo   *db.AssetRepo
	libraryRepo *db.LibraryRepo
	jobLogRepo  *db.JobLogRepo
	settingRepo *db.AppSettingRepo
	cancelled   atomic.Bool
	scanning    atomic.Bool
	customExts  map[string]bool
	extsOnce    sync.Once
	extsMu      sync.RWMutex
}

func NewScanner(assetRepo *db.AssetRepo, libraryRepo *db.LibraryRepo, jobLogRepo *db.JobLogRepo) *Scanner {
	return &Scanner{
		assetRepo:   assetRepo,
		libraryRepo: libraryRepo,
		jobLogRepo:  jobLogRepo,
	}
}

func (s *Scanner) SetSettingRepo(repo *db.AppSettingRepo) {
	s.settingRepo = repo
}

func (s *Scanner) getExtensions() map[string]bool {
	s.extsMu.RLock()
	if s.customExts != nil {
		exts := s.customExts
		s.extsMu.RUnlock()
		return exts
	}
	s.extsMu.RUnlock()

	s.extsOnce.Do(func() {
		exts := make(map[string]bool)
		for k, v := range DefaultImageExtensions {
			exts[k] = v
		}
		if s.settingRepo != nil {
			setting, err := s.settingRepo.Get("scanExtensions")
			if err == nil && setting != nil && setting.ValueJSON != "" {
				var customList []string
				if err := json.Unmarshal([]byte(setting.ValueJSON), &customList); err == nil && len(customList) > 0 {
					exts = make(map[string]bool)
					for _, ext := range customList {
						e := strings.TrimSpace(ext)
						if e != "" {
							exts[strings.ToLower(e)] = true
						}
					}
				}
			}
		}
		s.extsMu.Lock()
		s.customExts = exts
		s.extsMu.Unlock()
	})

	s.extsMu.RLock()
	exts := s.customExts
	s.extsMu.RUnlock()
	return exts
}

type ScanProgress struct {
	LibraryID    int64 `json:"libraryId"`
	ScannedCount int   `json:"scannedCount"`
	AddedCount   int   `json:"addedCount"`
	UpdatedCount int   `json:"updatedCount"`
	SkippedCount int   `json:"skippedCount"`
	FailedCount  int   `json:"failedCount"`
	IsDone       bool  `json:"isDone"`
}

func (s *Scanner) ScanLibrary(library *domain.Library, excludedDirs []string, progressCB func(ScanProgress)) error {
	if !s.scanning.CompareAndSwap(false, true) {
		return fmt.Errorf("scan already in progress")
	}
	defer s.scanning.Store(false)

	s.cancelled.Store(false)

	jobID, err := s.jobLogRepo.Create(domain.JobTypeScan, library.RootPath)
	if err != nil {
		return err
	}

	progress := ScanProgress{LibraryID: library.ID}
	excludedSet := make(map[string]bool)
	for _, d := range excludedDirs {
		excludedSet[strings.ToLower(d)] = true
	}

	exts := s.getExtensions()

	existingMap, err := s.assetRepo.GetAllFilePathsMap(library.ID)
	if err != nil {
		return fmt.Errorf("preload existing assets: %w", err)
	}

	var newAssets []*domain.Asset
	var updatedAssets []*domain.Asset
	const batchSize = 500

	flushNew := func() {
		if len(newAssets) == 0 {
			return
		}
		if err := s.assetRepo.CreateBatch(newAssets); err != nil {
			slog.Warn("error batch creating assets", "error", err)
			progress.FailedCount += len(newAssets)
		} else {
			progress.AddedCount += len(newAssets)
		}
		newAssets = newAssets[:0]
	}

	flushUpdated := func() {
		if len(updatedAssets) == 0 {
			return
		}
		if err := s.assetRepo.UpdateBatch(updatedAssets); err != nil {
			slog.Warn("error batch updating assets", "error", err)
			progress.FailedCount += len(updatedAssets)
		} else {
			progress.UpdatedCount += len(updatedAssets)
		}
		updatedAssets = updatedAssets[:0]
	}

	err = filepath.Walk(library.RootPath, func(path string, info os.FileInfo, walkErr error) error {
		if s.cancelled.Load() {
			return filepath.SkipDir
		}

		if walkErr != nil {
			progress.FailedCount++
			slog.Warn("walk error", "path", path, "error", walkErr)
			return nil
		}

		if info.IsDir() {
			if strings.HasPrefix(info.Name(), ".") || excludedSet[strings.ToLower(path)] {
				return filepath.SkipDir
			}
			return nil
		}

		ext := strings.ToLower(filepath.Ext(path))
		if !exts[ext] {
			return nil
		}

		progress.ScannedCount++

		existing := existingMap[path]

		modTime := info.ModTime()
		if existing != nil {
			delete(existingMap, path)
			if !existing.ModifiedAtFS.IsZero() && existing.ModifiedAtFS.Equal(modTime) && existing.FileSize == info.Size() {
				progress.SkippedCount++
				if progressCB != nil && progress.ScannedCount%50 == 0 {
					progressCB(progress)
				}
				return nil
			}
			existing.FileSize = info.Size()
			existing.ModifiedAtFS = modTime
			existing.ThumbStatus = domain.ThumbStatusNone
			updatedAssets = append(updatedAssets, existing)
			if len(updatedAssets) >= batchSize {
				flushUpdated()
			}
			return nil
		}

		asset := &domain.Asset{
			LibraryID:   library.ID,
			FolderPath:  filepath.Dir(path),
			FileName:    info.Name(),
			FilePath:    path,
			Extension:   ext,
			FileSize:    info.Size(),
			ModifiedAtFS: modTime,
			ThumbStatus: domain.ThumbStatusQueued,
			Rating:      0,
			StatusLabel: domain.StatusUnsorted,
		}

		newAssets = append(newAssets, asset)
		if len(newAssets) >= batchSize {
			flushNew()
		}

		if progressCB != nil && progress.ScannedCount%50 == 0 {
			progressCB(progress)
		}

		return nil
	})

	flushNew()
	flushUpdated()

	for _, existing := range existingMap {
		if err := s.assetRepo.Delete(existing.ID); err != nil {
			slog.Warn("error deleting removed asset", "path", existing.FilePath, "error", err)
		}
	}

	if s.cancelled.Load() {
		_ = s.jobLogRepo.MarkCancelled(jobID)
	} else if err != nil {
		_ = s.jobLogRepo.MarkFailed(jobID, err.Error())
	} else {
		progress.IsDone = true
		if progressCB != nil {
			progressCB(progress)
		}
		_ = s.jobLogRepo.MarkCompleted(jobID, formatScanResult(progress))
		_ = s.libraryRepo.UpdateLastScanned(library.ID)
	}

	return err
}

func (s *Scanner) Cancel() {
	s.cancelled.Store(true)
}

func formatScanResult(p ScanProgress) string {
	return strings.Join([]string{
		"scanned=" + strconv.Itoa(p.ScannedCount),
		"added=" + strconv.Itoa(p.AddedCount),
		"updated=" + strconv.Itoa(p.UpdatedCount),
		"skipped=" + strconv.Itoa(p.SkippedCount),
		"failed=" + strconv.Itoa(p.FailedCount),
	}, " ")
}

type FolderScanResult struct {
	Images     []ImageEntry `json:"images"`
	TotalCount int          `json:"totalCount"`
	HasMore    bool         `json:"hasMore"`
}

type ImageEntry struct {
	FilePath   string `json:"filePath"`
	FileName   string `json:"fileName"`
	FolderPath string `json:"folderPath"`
	Extension  string `json:"extension"`
	FileSize   int64  `json:"fileSize"`
}

func ScanFolderDirect(folderPath string, offset, limit int) (*FolderScanResult, error) {
	absPath, err := filepath.Abs(folderPath)
	if err != nil {
		return nil, err
	}

	var allImages []ImageEntry

	err = filepath.Walk(absPath, func(path string, info os.FileInfo, walkErr error) error {
		if walkErr != nil {
			return nil
		}
		if info.IsDir() {
			if strings.HasPrefix(info.Name(), ".") {
				return filepath.SkipDir
			}
			return nil
		}

		ext := strings.ToLower(filepath.Ext(path))
		if !DefaultImageExtensions[ext] {
			return nil
		}

		allImages = append(allImages, ImageEntry{
			FilePath:   path,
			FileName:   info.Name(),
			FolderPath: filepath.Dir(path),
			Extension:  ext,
			FileSize:   info.Size(),
		})

		return nil
	})

	if err != nil {
		return nil, err
	}

	totalCount := len(allImages)
	if offset >= totalCount {
		return &FolderScanResult{
			Images:     []ImageEntry{},
			TotalCount: totalCount,
			HasMore:    false,
		}, nil
	}

	end := offset + limit
	if end > totalCount {
		end = totalCount
	}

	return &FolderScanResult{
		Images: allImages[offset:end],
		TotalCount: totalCount,
		HasMore: end < totalCount,
	}, nil
}
