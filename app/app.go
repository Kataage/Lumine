package app

import (
	"context"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
)

// App struct
type App struct {
	ctx context.Context
}

// ImageInfo represents minimal image metadata for grid display
type ImageInfo struct {
	FilePath   string `json:"filePath"`
	FileName   string `json:"fileName"`
	FolderPath string `json:"folderPath"`
	Extension  string `json:"extension"`
	FileSize   int64  `json:"fileSize"`
}

// ScanResult represents a paginated scan response
type ScanResult struct {
	Images      []ImageInfo `json:"images"`
	TotalCount  int         `json:"totalCount"`
	HasMore     bool        `json:"hasMore"`
}

// NewApp creates a new App application struct
func NewApp() *App {
	return &App{}
}

// Startup is called when the app starts
func (a *App) Startup(ctx context.Context) {
	a.ctx = ctx
}

var imageExtensions = map[string]bool{
	".jpg": true, ".jpeg": true, ".png": true, ".gif": true,
	".bmp": true, ".webp": true, ".tiff": true, ".tif": true,
	".ico": true, ".svg": true, ".avif": true, ".apng": true,
}

// ScanFolder scans a folder for images with pagination
func (a *App) ScanFolder(folderPath string, offset int, limit int) (*ScanResult, error) {
	absPath, err := filepath.Abs(folderPath)
	if err != nil {
		return nil, err
	}

	// Fast parallel scan using goroutines
	var mu sync.Mutex
	var allImages []ImageInfo
	var wg sync.WaitGroup

	err = filepath.Walk(absPath, func(path string, info os.FileInfo, walkErr error) error {
		if walkErr != nil {
			return nil // Skip errors (permission denied, etc.)
		}

		if info.IsDir() {
			// Skip hidden directories
			if strings.HasPrefix(info.Name(), ".") {
				return filepath.SkipDir
			}
			return nil
		}

		ext := strings.ToLower(filepath.Ext(path))
		if !imageExtensions[ext] {
			return nil
		}

		wg.Add(1)
		go func() {
			defer wg.Done()
			filePath := path
			fileName := info.Name()
			folderPath := filepath.Dir(path)
			fileSize := info.Size()

			mu.Lock()
			allImages = append(allImages, ImageInfo{
				FilePath:   filePath,
				FileName:   fileName,
				FolderPath: folderPath,
				Extension:  ext,
				FileSize:   fileSize,
			})
			mu.Unlock()
		}()

		return nil
	})

	if err != nil {
		return nil, err
	}

	wg.Wait()

	// Sort by file path for consistent ordering
	sort.Slice(allImages, func(i, j int) bool {
		return allImages[i].FilePath < allImages[j].FilePath
	})

	totalCount := len(allImages)

	// Apply pagination
	if offset >= totalCount {
		return &ScanResult{
			Images:     []ImageInfo{},
			TotalCount: totalCount,
			HasMore:    false,
		}, nil
	}

	end := offset + limit
	if end > totalCount {
		end = totalCount
	}

	pagedImages := allImages[offset:end]

	return &ScanResult{
		Images:     pagedImages,
		TotalCount: totalCount,
		HasMore:    end < totalCount,
	}, nil
}

// OpenFolderDialog opens a folder picker dialog
func (a *App) OpenFolderDialog() (string, error) {
	// This will be handled by frontend using runtime.OpenDirectoryDialog
	return "", nil
}
