package scanner

import "github.com/kataage/lumine/internal/domain"

// SyncResult is the compact result used by the viewer's silent background sync.
// ScanLibrary already skips unchanged files using size + mtime, so the silent
// sync reuses that path while avoiding UI progress events and full refreshes
// when the filesystem has not changed.
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

// SyncLibrary performs the same cheap incremental filesystem walk as a manual
// scan, but returns a delta summary instead of emitting progress events.
func (s *Scanner) SyncLibrary(library *domain.Library, excludedDirs []string) (*SyncResult, error) {
	before, err := s.assetRepo.GetAllFilePathsMap(library.ID)
	if err != nil {
		return nil, err
	}

	var final ScanProgress
	if err := s.ScanLibrary(library, excludedDirs, func(progress ScanProgress) {
		final = progress
	}); err != nil {
		return nil, err
	}

	after, err := s.assetRepo.GetAllFilePathsMap(library.ID)
	if err != nil {
		return nil, err
	}

	removed := 0
	for path := range before {
		if _, exists := after[path]; !exists {
			removed++
		}
	}

	result := &SyncResult{
		LibraryID:    library.ID,
		ScannedCount: final.ScannedCount,
		AddedCount:   final.AddedCount,
		UpdatedCount: final.UpdatedCount,
		RemovedCount: removed,
		SkippedCount: final.SkippedCount,
		FailedCount:  final.FailedCount,
	}
	result.Changed = result.AddedCount > 0 || result.UpdatedCount > 0 || result.RemovedCount > 0
	return result, nil
}
