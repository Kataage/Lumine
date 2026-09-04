package commands

import (
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/infrastructure/scanner"
)

// SyncLibraryViewer performs a silent incremental reconciliation for the
// currently viewed library. Unlike the manual scan path it does not emit
// progress events, so the frontend can ignore no-op checks and only refresh
// when files were actually added, changed, or removed.
func (c *AppCommands) SyncLibraryViewer(libraryID int64) (*scanner.SyncResult, error) {
	library, err := c.libraryRepo.GetByID(libraryID)
	if err != nil || library == nil {
		return nil, fmt.Errorf("library not found: %d", libraryID)
	}
	if !library.IsEnabled {
		return nil, fmt.Errorf("library is disabled: %d", libraryID)
	}

	result, err := c.scanSvc.SyncLibrary(library, c.GetExcludedDirs(libraryID))
	if err != nil && strings.Contains(err.Error(), "scan already in progress") {
		return &scanner.SyncResult{LibraryID: libraryID}, nil
	}
	return result, err
}
