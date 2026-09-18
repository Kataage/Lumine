package db

import (
	"database/sql"
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

// GetSyncFilePathsMap is the lightweight counterpart of GetAllFilePathsMap.
// Background reconciliation runs frequently, so it deliberately avoids loading
// hashes and EXIF strings that are never needed to decide whether a file
// changed. User-managed state needed by UpdateBatch is preserved.
func (r *AssetRepo) GetSyncFilePathsMap(libraryID int64) (map[string]*domain.Asset, error) {
	rows, err := r.db.Query(
		"SELECT id, library_id, folder_path, file_name, file_path, extension, file_size, modified_at_fs, width, height, mime_type, thumb_status, metadata_loaded, rating, status_label, is_favorite, color_label FROM assets WHERE library_id = ?",
		libraryID,
	)
	if err != nil {
		return nil, fmt.Errorf("get sync file paths: %w", err)
	}
	defer rows.Close()

	result := make(map[string]*domain.Asset)
	for rows.Next() {
		var asset domain.Asset
		var modifiedAtFS, mimeType, colorLabel sql.NullString
		if err := rows.Scan(
			&asset.ID,
			&asset.LibraryID,
			&asset.FolderPath,
			&asset.FileName,
			&asset.FilePath,
			&asset.Extension,
			&asset.FileSize,
			&modifiedAtFS,
			&asset.Width,
			&asset.Height,
			&mimeType,
			&asset.ThumbStatus,
			&asset.MetadataLoaded,
			&asset.Rating,
			&asset.StatusLabel,
			&asset.IsFavorite,
			&colorLabel,
		); err != nil {
			return nil, fmt.Errorf("scan sync asset path: %w", err)
		}
		if modifiedAtFS.Valid {
			asset.ModifiedAtFS, _ = timeParse(modifiedAtFS.String)
		}
		if mimeType.Valid {
			asset.MimeType = mimeType.String
		}
		if colorLabel.Valid {
			asset.ColorLabel = colorLabel.String
		}
		result[asset.FilePath] = &asset
	}
	return result, rows.Err()
}
