package db

import (
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

type FolderRepo struct {
	db *DB
}

func NewFolderRepo(db *DB) *FolderRepo {
	return &FolderRepo{db: db}
}

func (r *FolderRepo) GetTreeByLibrary(libraryID int64) ([]domain.Folder, error) {
	rows, err := r.db.Query(
		"SELECT id, library_id, path, parent_path, is_excluded, created_at, updated_at FROM folders WHERE library_id = ? AND is_excluded = 0 ORDER BY path",
		libraryID,
	)
	if err != nil {
		return nil, fmt.Errorf("get folder tree: %w", err)
	}
	defer rows.Close()

	var folders []domain.Folder
	for rows.Next() {
		var f domain.Folder
		if err := rows.Scan(&f.ID, &f.LibraryID, &f.Path, &f.ParentPath, &f.IsExcluded, &f.CreatedAt, &f.UpdatedAt); err != nil {
			return nil, fmt.Errorf("scan folder: %w", err)
		}
		folders = append(folders, f)
	}
	return folders, rows.Err()
}

func (r *FolderRepo) UpsertFolder(libraryID int64, path, parentPath string) error {
	_, err := r.db.Exec(
		"INSERT OR IGNORE INTO folders (library_id, path, parent_path) VALUES (?, ?, ?)",
		libraryID, path, parentPath,
	)
	return err
}

func (r *FolderRepo) DeletePath(libraryID int64, path string) error {
	_, err := r.db.Exec("DELETE FROM folders WHERE library_id = ? AND path = ?", libraryID, path)
	return err
}
