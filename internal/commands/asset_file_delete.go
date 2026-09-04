package commands

import (
	"fmt"
	"os"
)

type DeleteAssetFilesResult struct {
	DeletedCount int      `json:"deletedCount"`
	FailedCount  int      `json:"failedCount"`
	DeletedIDs   []int64  `json:"deletedIds"`
	FailedIDs    []int64  `json:"failedIds"`
	Errors       []string `json:"errors,omitempty"`
}

// DeleteAssetFiles removes the original files and only then removes their
// database rows. Missing files are treated as already deleted and their stale
// database rows are cleaned up. A filesystem failure leaves the DB row intact.
func (c *AppCommands) DeleteAssetFiles(ids []int64) *DeleteAssetFilesResult {
	result := &DeleteAssetFilesResult{}
	seen := make(map[int64]struct{}, len(ids))

	for _, id := range ids {
		if _, duplicate := seen[id]; duplicate {
			continue
		}
		seen[id] = struct{}{}

		asset, err := c.assetRepo.GetByID(id)
		if err != nil {
			result.FailedCount++
			result.FailedIDs = append(result.FailedIDs, id)
			result.Errors = append(result.Errors, fmt.Sprintf("asset %d: %v", id, err))
			continue
		}
		if asset == nil {
			continue
		}

		if err := os.Remove(asset.FilePath); err != nil && !os.IsNotExist(err) {
			result.FailedCount++
			result.FailedIDs = append(result.FailedIDs, id)
			result.Errors = append(result.Errors, fmt.Sprintf("%s: %v", asset.FilePath, err))
			continue
		}

		if err := c.assetRepo.Delete(id); err != nil {
			result.FailedCount++
			result.FailedIDs = append(result.FailedIDs, id)
			result.Errors = append(result.Errors, fmt.Sprintf("database cleanup for %s: %v", asset.FilePath, err))
			continue
		}

		result.DeletedCount++
		result.DeletedIDs = append(result.DeletedIDs, id)
	}

	return result
}
