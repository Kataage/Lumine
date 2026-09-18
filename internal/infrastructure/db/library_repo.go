package db

import (
	"database/sql"
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

type LibraryRepo struct {
	db *DB
}

func NewLibraryRepo(db *DB) *LibraryRepo {
	return &LibraryRepo{db: db}
}

func (r *LibraryRepo) List() ([]domain.Library, error) {
	rows, err := r.db.Query("SELECT id, name, root_path, is_enabled, created_at, updated_at, last_scanned_at FROM libraries ORDER BY id")
	if err != nil {
		return nil, fmt.Errorf("list libraries: %w", err)
	}
	defer rows.Close()

	var libs []domain.Library
	for rows.Next() {
		var lib domain.Library
		var lastScanned sql.NullTime
		if err := rows.Scan(&lib.ID, &lib.Name, &lib.RootPath, &lib.IsEnabled, &lib.CreatedAt, &lib.UpdatedAt, &lastScanned); err != nil {
			return nil, fmt.Errorf("scan library: %w", err)
		}
		if lastScanned.Valid {
			lib.LastScannedAt = &lastScanned.Time
		}
		libs = append(libs, lib)
	}
	return libs, rows.Err()
}

func (r *LibraryRepo) GetByID(id int64) (*domain.Library, error) {
	var lib domain.Library
	var lastScanned sql.NullTime
	err := r.db.QueryRow(
		"SELECT id, name, root_path, is_enabled, created_at, updated_at, last_scanned_at FROM libraries WHERE id = ?", id,
	).Scan(&lib.ID, &lib.Name, &lib.RootPath, &lib.IsEnabled, &lib.CreatedAt, &lib.UpdatedAt, &lastScanned)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get library %d: %w", id, err)
	}
	if lastScanned.Valid {
		lib.LastScannedAt = &lastScanned.Time
	}
	return &lib, nil
}

func (r *LibraryRepo) Create(name, rootPath string) (*domain.Library, error) {
	result, err := r.db.Exec(
		"INSERT INTO libraries (name, root_path) VALUES (?, ?)", name, rootPath,
	)
	if err != nil {
		return nil, fmt.Errorf("create library: %w", err)
	}
	id, _ := result.LastInsertId()
	return r.GetByID(id)
}

func (r *LibraryRepo) Update(lib *domain.Library) error {
	_, err := r.db.Exec(
		"UPDATE libraries SET name = ?, root_path = ?, is_enabled = ?, updated_at = CURRENT_TIMESTAMP WHERE id = ?",
		lib.Name, lib.RootPath, lib.IsEnabled, lib.ID,
	)
	return err
}

func (r *LibraryRepo) Delete(id int64) error {
	_, err := r.db.Exec("DELETE FROM libraries WHERE id = ?", id)
	return err
}

func (r *LibraryRepo) UpdateLastScanned(id int64) error {
	_, err := r.db.Exec(
		"UPDATE libraries SET last_scanned_at = CURRENT_TIMESTAMP, updated_at = CURRENT_TIMESTAMP WHERE id = ?", id,
	)
	return err
}
