package scanner

import (
	"log/slog"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

var imageExtensions = map[string]bool{
	".jpg": true, ".jpeg": true, ".png": true, ".gif": true,
	".bmp": true, ".webp": true, ".tiff": true, ".tif": true,
	".ico": true, ".svg": true, ".avif": true, ".apng": true,
}

type Scanner struct {
	assetRepo   *db.AssetRepo
	libraryRepo *db.LibraryRepo
	jobLogRepo  *db.JobLogRepo
	cancelled   atomic.Bool
}

func NewScanner(assetRepo *db.AssetRepo, libraryRepo *db.LibraryRepo, jobLogRepo *db.JobLogRepo) *Scanner {
	return &Scanner{
		assetRepo:   assetRepo,
		libraryRepo: libraryRepo,
		jobLogRepo:  jobLogRepo,
	}
}

type ScanProgress struct {
	ScannedCount int `json:"scannedCount"`
	AddedCount   int `json:"addedCount"`
	UpdatedCount int `json:"updatedCount"`
	SkippedCount int `json:"skippedCount"`
	FailedCount  int `json:"failedCount"`
	IsDone       bool `json:"isDone"`
}

func (s *Scanner) ScanLibrary(library *domain.Library, excludedDirs []string, progressCB func(ScanProgress)) error {
	s.cancelled.Store(false)

	jobID, err := s.jobLogRepo.Create(domain.JobTypeScan, library.RootPath)
	if err != nil {
		return err
	}

	progress := ScanProgress{}
	excludedSet := make(map[string]bool)
	for _, d := range excludedDirs {
		excludedSet[strings.ToLower(d)] = true
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
		if !imageExtensions[ext] {
			return nil
		}

		progress.ScannedCount++

		existing, err := s.assetRepo.GetByFilePath(path)
		if err != nil {
			slog.Warn("error checking existing asset", "path", path, "error", err)
			progress.FailedCount++
			return nil
		}

		modTime := info.ModTime()
		if existing != nil {
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
			if err := s.assetRepo.Update(existing); err != nil {
				slog.Warn("error updating asset", "path", path, "error", err)
				progress.FailedCount++
			} else {
				progress.UpdatedCount++
			}
			return nil
		}

		asset := &domain.Asset{
			LibraryID:    library.ID,
			FolderPath:   filepath.Dir(path),
			FileName:     info.Name(),
			FilePath:     path,
			Extension:    ext,
			FileSize:     info.Size(),
			ModifiedAtFS: modTime,
			ThumbStatus:  domain.ThumbStatusQueued,
			Rating:       0,
			StatusLabel:  domain.StatusUnsorted,
		}

		if _, err := s.assetRepo.Create(asset); err != nil {
			slog.Warn("error creating asset", "path", path, "error", err)
			progress.FailedCount++
		} else {
			progress.AddedCount++
		}

		if progressCB != nil && progress.ScannedCount%50 == 0 {
			progressCB(progress)
		}

		return nil
	})

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
		"scanned=" + itoa(p.ScannedCount),
		"added=" + itoa(p.AddedCount),
		"updated=" + itoa(p.UpdatedCount),
		"skipped=" + itoa(p.SkippedCount),
		"failed=" + itoa(p.FailedCount),
	}, " ")
}

func itoa(n int) string {
	return strings.TrimSpace(string(append([]byte{}, byte('0'+n%10))))
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

	var mu sync.Mutex
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
		if !imageExtensions[ext] {
			return nil
		}

		mu.Lock()
		allImages = append(allImages, ImageEntry{
			FilePath:   path,
			FileName:   info.Name(),
			FolderPath: filepath.Dir(path),
			Extension:  ext,
			FileSize:   info.Size(),
		})
		mu.Unlock()

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
		Images:     allImages[offset:end],
		TotalCount: totalCount,
		HasMore:    end < totalCount,
	}, nil
}

type ThumbnailService struct {
	cacheDir string
}

func NewThumbnailService(cacheDir string) *ThumbnailService {
	return &ThumbnailService{cacheDir: cacheDir}
}

func (t *ThumbnailService) GetThumbPath(assetID int64, modifiedAt time.Time, size int) string {
	return filepath.Join(t.cacheDir, itoa64(assetID)+"_"+modifiedAt.Format("20060102150405")+"_"+itoa(size)+".webp")
}

func itoa64(n int64) string {
	if n == 0 {
		return "0"
	}
	var buf [20]byte
	pos := len(buf)
	for n > 0 {
		pos--
		buf[pos] = byte('0' + n%10)
		n /= 10
	}
	return string(buf[pos:])
}
