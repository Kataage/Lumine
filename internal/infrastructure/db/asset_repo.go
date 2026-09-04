package db

import (
	"database/sql"
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

type AssetRepo struct {
	db *DB
}

func NewAssetRepo(db *DB) *AssetRepo {
	return &AssetRepo{db: db}
}

type AssetQuery struct {
	LibraryID   int64
	FolderPath  string
	Recurse     bool
	Search      string
	Rating      int
	StatusLabel string
	IsFavorite  *bool
	TagIDs      []int64
	HasNote     *bool
	Extension   string
	ColorLabel  string
	SortBy      string
	SortDesc    bool
	Offset      int
	Limit       int
}

type AssetListResult struct {
	Assets     []domain.Asset
	TotalCount int
}

func (r *AssetRepo) List(q AssetQuery) (*AssetListResult, error) {
	if q.Limit <= 0 {
		q.Limit = 100
	}

	where := "WHERE 1=1"
	args := []interface{}{}

	if q.LibraryID > 0 {
		where += " AND a.library_id = ?"
		args = append(args, q.LibraryID)
	}
	if q.FolderPath != "" {
		if q.Recurse {
			where += " AND (a.folder_path = ? OR a.folder_path LIKE ? OR a.folder_path LIKE ?)"
			args = append(args, q.FolderPath, q.FolderPath+"/%", q.FolderPath+"\\%")
		} else {
			where += " AND a.folder_path = ?"
			args = append(args, q.FolderPath)
		}
	}
	if q.Search != "" {
		where += " AND (a.file_name LIKE ? OR EXISTS (SELECT 1 FROM asset_notes n WHERE n.asset_id = a.id AND n.content LIKE ?))"
		args = append(args, "%"+q.Search+"%", "%"+q.Search+"%")
	}
	if q.Rating > 0 {
		where += " AND a.rating = ?"
		args = append(args, q.Rating)
	}
	if q.StatusLabel != "" {
		where += " AND a.status_label = ?"
		args = append(args, q.StatusLabel)
	}
	if q.IsFavorite != nil {
		where += " AND a.is_favorite = ?"
		args = append(args, *q.IsFavorite)
	}
	if q.Extension != "" {
		where += " AND a.extension = ?"
		args = append(args, q.Extension)
	}
	if q.ColorLabel != "" {
		where += " AND a.color_label = ?"
		args = append(args, q.ColorLabel)
	}
	if q.HasNote != nil {
		if *q.HasNote {
			where += " AND EXISTS (SELECT 1 FROM asset_notes n WHERE n.asset_id = a.id)"
		} else {
			where += " AND NOT EXISTS (SELECT 1 FROM asset_notes n WHERE n.asset_id = a.id)"
		}
	}
	if len(q.TagIDs) > 0 {
		placeholders := ""
		for i, tid := range q.TagIDs {
			if i > 0 {
				placeholders += ","
			}
			placeholders += "?"
			args = append(args, tid)
		}
		where += fmt.Sprintf(" AND a.id IN (SELECT at2.asset_id FROM asset_tags at2 WHERE at2.tag_id IN (%s))", placeholders)
	}

	// COUNT is only required for the first page. Repeating it on every infinite-
	// scroll request doubled the DB work for large libraries.
	totalCount := -1
	if q.Offset == 0 {
		countArgs := append([]interface{}(nil), args...)
		countSQL := fmt.Sprintf("SELECT COUNT(*) FROM assets a %s", where)
		if err := r.db.QueryRow(countSQL, countArgs...).Scan(&totalCount); err != nil {
			return nil, fmt.Errorf("count assets: %w", err)
		}
	}

	sortCol := "a.modified_at_fs"
	switch q.SortBy {
	case "name":
		sortCol = "a.file_name"
	case "size":
		sortCol = "a.file_size"
	case "rating":
		sortCol = "a.rating"
	case "status":
		sortCol = "a.status_label"
	case "created":
		sortCol = "a.created_at_fs"
	default:
		sortCol = "a.modified_at_fs"
	}
	sortDir := "ASC"
	if q.SortDesc {
		sortDir = "DESC"
	}

	// The grid does not need notes, EXIF, hashes, MIME data, or audit timestamps.
	// Fetch a compact row here and load the full record only for the detail panel.
	querySQL := fmt.Sprintf(
		"SELECT a.id, a.library_id, a.folder_path, a.file_name, a.file_path, a.extension, a.file_size, a.modified_at_fs, a.width, a.height, a.rating, a.status_label, a.is_favorite, a.color_label FROM assets a %s ORDER BY %s %s, a.id %s LIMIT ? OFFSET ?",
		where, sortCol, sortDir, sortDir,
	)
	args = append(args, q.Limit, q.Offset)

	rows, err := r.db.Query(querySQL, args...)
	if err != nil {
		return nil, fmt.Errorf("list assets: %w", err)
	}
	defer rows.Close()

	assets := make([]domain.Asset, 0, q.Limit)
	for rows.Next() {
		var a domain.Asset
		var modifiedAtFS, colorLabel sql.NullString
		if err := rows.Scan(
			&a.ID,
			&a.LibraryID,
			&a.FolderPath,
			&a.FileName,
			&a.FilePath,
			&a.Extension,
			&a.FileSize,
			&modifiedAtFS,
			&a.Width,
			&a.Height,
			&a.Rating,
			&a.StatusLabel,
			&a.IsFavorite,
			&colorLabel,
		); err != nil {
			return nil, fmt.Errorf("scan asset list row: %w", err)
		}
		if modifiedAtFS.Valid {
			a.ModifiedAtFS, _ = timeParse(modifiedAtFS.String)
		}
		if colorLabel.Valid {
			a.ColorLabel = colorLabel.String
		}
		assets = append(assets, a)
	}

	return &AssetListResult{Assets: assets, TotalCount: totalCount}, rows.Err()
}

func (r *AssetRepo) GetByID(id int64) (*domain.Asset, error) {
	var a domain.Asset
	var createdAtFS, modifiedAtFS, hashBlake3, colorLabel sql.NullString
	var mimeType sql.NullString
	err := r.db.QueryRow(
		"SELECT id, library_id, folder_path, file_name, file_path, extension, file_size, created_at_fs, modified_at_fs, width, height, mime_type, hash_blake3, thumb_status, rating, status_label, is_favorite, color_label, camera_model, lens_model, focal_length, aperture, shutter_speed, iso, exif_date, gps_latitude, gps_longitude, indexed_at, updated_at FROM assets WHERE id = ?", id,
	).Scan(
		&a.ID, &a.LibraryID, &a.FolderPath, &a.FileName, &a.FilePath, &a.Extension, &a.FileSize,
		&createdAtFS, &modifiedAtFS, &a.Width, &a.Height, &mimeType, &hashBlake3,
		&a.ThumbStatus, &a.Rating, &a.StatusLabel, &a.IsFavorite, &colorLabel,
		&a.CameraModel, &a.LensModel, &a.FocalLength, &a.Aperture, &a.ShutterSpeed, &a.ISO,
		&a.ExifDate, &a.GPSLatitude, &a.GPSLongitude,
		&a.IndexedAt, &a.UpdatedAt,
	)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get asset %d: %w", id, err)
	}
	if createdAtFS.Valid {
		a.CreatedAtFS, _ = timeParse(createdAtFS.String)
	}
	if modifiedAtFS.Valid {
		a.ModifiedAtFS, _ = timeParse(modifiedAtFS.String)
	}
	if mimeType.Valid {
		a.MimeType = mimeType.String
	}
	if hashBlake3.Valid {
		a.HashBlake3 = hashBlake3.String
	}
	if colorLabel.Valid {
		a.ColorLabel = colorLabel.String
	}
	return &a, nil
}

func (r *AssetRepo) GetByFilePath(filePath string) (*domain.Asset, error) {
	var a domain.Asset
	err := r.db.QueryRow(
		"SELECT id, library_id, folder_path, file_name, file_path, extension, file_size, camera_model, lens_model, focal_length, aperture, shutter_speed, iso, exif_date, gps_latitude, gps_longitude FROM assets WHERE file_path = ?", filePath,
	).Scan(&a.ID, &a.LibraryID, &a.FolderPath, &a.FileName, &a.FilePath, &a.Extension, &a.FileSize,
		&a.CameraModel, &a.LensModel, &a.FocalLength, &a.Aperture, &a.ShutterSpeed, &a.ISO,
		&a.ExifDate, &a.GPSLatitude, &a.GPSLongitude)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get asset by path %s: %w", filePath, err)
	}
	return &a, nil
}

func (r *AssetRepo) Create(a *domain.Asset) (int64, error) {
	result, err := r.db.Exec(
		"INSERT INTO assets (library_id, folder_path, file_name, file_path, extension, file_size, created_at_fs, modified_at_fs, width, height, mime_type, hash_blake3, thumb_status, rating, status_label, is_favorite, color_label, camera_model, lens_model, focal_length, aperture, shutter_speed, iso, exif_date, gps_latitude, gps_longitude) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
		a.LibraryID, a.FolderPath, a.FileName, a.FilePath, a.Extension, a.FileSize,
		a.CreatedAtFS, a.ModifiedAtFS, a.Width, a.Height, a.MimeType, a.HashBlake3,
		a.ThumbStatus, a.Rating, a.StatusLabel, a.IsFavorite, a.ColorLabel,
		a.CameraModel, a.LensModel, a.FocalLength, a.Aperture, a.ShutterSpeed, a.ISO,
		a.ExifDate, a.GPSLatitude, a.GPSLongitude,
	)
	if err != nil {
		return 0, fmt.Errorf("create asset: %w", err)
	}
	return result.LastInsertId()
}

func (r *AssetRepo) Update(a *domain.Asset) error {
	_, err := r.db.Exec(
		"UPDATE assets SET folder_path = ?, file_name = ?, file_path = ?, file_size = ?, modified_at_fs = ?, width = ?, height = ?, mime_type = ?, rating = ?, status_label = ?, is_favorite = ?, color_label = ?, thumb_status = ?, camera_model = ?, lens_model = ?, focal_length = ?, aperture = ?, shutter_speed = ?, iso = ?, exif_date = ?, gps_latitude = ?, gps_longitude = ?, updated_at = CURRENT_TIMESTAMP WHERE id = ?",
		a.FolderPath, a.FileName, a.FilePath, a.FileSize, a.ModifiedAtFS,
		a.Width, a.Height, a.MimeType, a.Rating, a.StatusLabel, a.IsFavorite, a.ColorLabel, a.ThumbStatus,
		a.CameraModel, a.LensModel, a.FocalLength, a.Aperture, a.ShutterSpeed, a.ISO,
		a.ExifDate, a.GPSLatitude, a.GPSLongitude, a.ID,
	)
	return err
}

func (r *AssetRepo) UpdateMetadata(a *domain.Asset) error {
	_, err := r.db.Exec(
		"UPDATE assets SET width = ?, height = ?, mime_type = ?, camera_model = ?, lens_model = ?, focal_length = ?, aperture = ?, shutter_speed = ?, iso = ?, exif_date = ?, gps_latitude = ?, gps_longitude = ?, updated_at = CURRENT_TIMESTAMP WHERE id = ?",
		a.Width, a.Height, a.MimeType,
		a.CameraModel, a.LensModel, a.FocalLength, a.Aperture, a.ShutterSpeed, a.ISO,
		a.ExifDate, a.GPSLatitude, a.GPSLongitude, a.ID,
	)
	return err
}

func (r *AssetRepo) Delete(id int64) error {
	_, err := r.db.Exec("DELETE FROM assets WHERE id = ?", id)
	return err
}

func (r *AssetRepo) BulkUpdateRating(ids []int64, rating int) error {
	if len(ids) == 0 {
		return nil
	}
	placeholders := ""
	args := []interface{}{rating}
	for i, id := range ids {
		if i > 0 {
			placeholders += ","
		}
		placeholders += "?"
		args = append(args, id)
	}
	_, err := r.db.Exec(
		fmt.Sprintf("UPDATE assets SET rating = ?, updated_at = CURRENT_TIMESTAMP WHERE id IN (%s)", placeholders),
		args...,
	)
	return err
}

func (r *AssetRepo) BulkUpdateStatus(ids []int64, status domain.StatusLabel) error {
	if len(ids) == 0 {
		return nil
	}
	placeholders := ""
	args := []interface{}{status}
	for i, id := range ids {
		if i > 0 {
			placeholders += ","
		}
		placeholders += "?"
		args = append(args, id)
	}
	_, err := r.db.Exec(
		fmt.Sprintf("UPDATE assets SET status_label = ?, updated_at = CURRENT_TIMESTAMP WHERE id IN (%s)", placeholders),
		args...,
	)
	return err
}

func (r *AssetRepo) BulkUpdateFavorite(ids []int64, favorite bool) error {
	if len(ids) == 0 {
		return nil
	}
	placeholders := ""
	args := []interface{}{favorite}
	for i, id := range ids {
		if i > 0 {
			placeholders += ","
		}
		placeholders += "?"
		args = append(args, id)
	}
	_, err := r.db.Exec(
		fmt.Sprintf("UPDATE assets SET is_favorite = ?, updated_at = CURRENT_TIMESTAMP WHERE id IN (%s)", placeholders),
		args...,
	)
	return err
}

func (r *AssetRepo) BulkUpdateColorLabel(ids []int64, label string) error {
	if len(ids) == 0 {
		return nil
	}
	placeholders := ""
	args := []interface{}{label}
	for i, id := range ids {
		if i > 0 {
			placeholders += ","
		}
		placeholders += "?"
		args = append(args, id)
	}
	_, err := r.db.Exec(
		fmt.Sprintf("UPDATE assets SET color_label = ?, updated_at = CURRENT_TIMESTAMP WHERE id IN (%s)", placeholders),
		args...,
	)
	return err
}

func (r *AssetRepo) UpdateFilePath(id int64, newPath, newFolder, newName string) error {
	_, err := r.db.Exec(
		"UPDATE assets SET file_path = ?, folder_path = ?, file_name = ?, updated_at = CURRENT_TIMESTAMP WHERE id = ?",
		newPath, newFolder, newName, id,
	)
	return err
}

func (r *AssetRepo) GetAllFilePathsMap(libraryID int64) (map[string]*domain.Asset, error) {
	rows, err := r.db.Query(
		"SELECT id, library_id, folder_path, file_name, file_path, extension, file_size, modified_at_fs, width, height, mime_type, thumb_status, rating, status_label, is_favorite, color_label, camera_model, lens_model, focal_length, aperture, shutter_speed, iso, exif_date, gps_latitude, gps_longitude FROM assets WHERE library_id = ?",
		libraryID,
	)
	if err != nil {
		return nil, fmt.Errorf("get all file paths: %w", err)
	}
	defer rows.Close()

	result := make(map[string]*domain.Asset)
	for rows.Next() {
		var a domain.Asset
		var modifiedAtFS, colorLabel, mimeType sql.NullString
		if err := rows.Scan(
			&a.ID, &a.LibraryID, &a.FolderPath, &a.FileName, &a.FilePath,
			&a.Extension, &a.FileSize, &modifiedAtFS, &a.Width, &a.Height, &mimeType, &a.ThumbStatus,
			&a.Rating, &a.StatusLabel, &a.IsFavorite, &colorLabel,
			&a.CameraModel, &a.LensModel, &a.FocalLength, &a.Aperture, &a.ShutterSpeed, &a.ISO,
			&a.ExifDate, &a.GPSLatitude, &a.GPSLongitude,
		); err != nil {
			return nil, fmt.Errorf("scan asset path: %w", err)
		}
		if modifiedAtFS.Valid {
			a.ModifiedAtFS, _ = timeParse(modifiedAtFS.String)
		}
		if mimeType.Valid {
			a.MimeType = mimeType.String
		}
		if colorLabel.Valid {
			a.ColorLabel = colorLabel.String
		}
		result[a.FilePath] = &a
	}
	return result, rows.Err()
}

func (r *AssetRepo) CreateBatch(assets []*domain.Asset) error {
	if len(assets) == 0 {
		return nil
	}
	tx, err := r.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	stmt, err := tx.Prepare(
		"INSERT INTO assets (library_id, folder_path, file_name, file_path, extension, file_size, created_at_fs, modified_at_fs, width, height, mime_type, hash_blake3, thumb_status, rating, status_label, is_favorite, color_label, camera_model, lens_model, focal_length, aperture, shutter_speed, iso, exif_date, gps_latitude, gps_longitude) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
	)
	if err != nil {
		return err
	}
	defer stmt.Close()

	for _, a := range assets {
		_, err := stmt.Exec(
			a.LibraryID, a.FolderPath, a.FileName, a.FilePath, a.Extension, a.FileSize,
			a.CreatedAtFS, a.ModifiedAtFS, a.Width, a.Height, a.MimeType, a.HashBlake3,
			a.ThumbStatus, a.Rating, a.StatusLabel, a.IsFavorite, a.ColorLabel,
			a.CameraModel, a.LensModel, a.FocalLength, a.Aperture, a.ShutterSpeed, a.ISO,
			a.ExifDate, a.GPSLatitude, a.GPSLongitude,
		)
		if err != nil {
			return fmt.Errorf("batch insert asset: %w", err)
		}
	}
	return tx.Commit()
}

func (r *AssetRepo) BulkDelete(ids []int64) error {
	if len(ids) == 0 {
		return nil
	}
	placeholders := ""
	args := make([]interface{}, len(ids))
	for i, id := range ids {
		if i > 0 {
			placeholders += ","
		}
		placeholders += "?"
		args[i] = id
	}
	_, err := r.db.Exec(
		fmt.Sprintf("DELETE FROM assets WHERE id IN (%s)", placeholders),
		args...,
	)
	return err
}

func (r *AssetRepo) UpdateBatch(assets []*domain.Asset) error {
	if len(assets) == 0 {
		return nil
	}
	tx, err := r.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	stmt, err := tx.Prepare(
		"UPDATE assets SET folder_path = ?, file_name = ?, file_path = ?, file_size = ?, modified_at_fs = ?, width = ?, height = ?, mime_type = ?, rating = ?, status_label = ?, is_favorite = ?, color_label = ?, thumb_status = ?, camera_model = ?, lens_model = ?, focal_length = ?, aperture = ?, shutter_speed = ?, iso = ?, exif_date = ?, gps_latitude = ?, gps_longitude = ?, updated_at = CURRENT_TIMESTAMP WHERE id = ?",
	)
	if err != nil {
		return err
	}
	defer stmt.Close()

	for _, a := range assets {
		_, err := stmt.Exec(
			a.FolderPath, a.FileName, a.FilePath, a.FileSize, a.ModifiedAtFS,
			a.Width, a.Height, a.MimeType, a.Rating, a.StatusLabel, a.IsFavorite, a.ColorLabel,
			a.ThumbStatus,
			a.CameraModel, a.LensModel, a.FocalLength, a.Aperture, a.ShutterSpeed, a.ISO,
			a.ExifDate, a.GPSLatitude, a.GPSLongitude, a.ID,
		)
		if err != nil {
			return fmt.Errorf("batch update asset: %w", err)
		}
	}
	return tx.Commit()
}
