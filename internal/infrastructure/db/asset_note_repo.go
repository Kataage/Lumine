package db

import (
	"database/sql"
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

type AssetNoteRepo struct {
	db *DB
}

func NewAssetNoteRepo(db *DB) *AssetNoteRepo {
	return &AssetNoteRepo{db: db}
}

func (r *AssetNoteRepo) GetByAssetID(assetID int64) (*domain.AssetNote, error) {
	var n domain.AssetNote
	err := r.db.QueryRow(
		"SELECT id, asset_id, content, created_at, updated_at FROM asset_notes WHERE asset_id = ?", assetID,
	).Scan(&n.ID, &n.AssetID, &n.Content, &n.CreatedAt, &n.UpdatedAt)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get note for asset %d: %w", assetID, err)
	}
	return &n, nil
}

func (r *AssetNoteRepo) Upsert(assetID int64, content string) error {
	_, err := r.db.Exec(
		"INSERT INTO asset_notes (asset_id, content) VALUES (?, ?) ON CONFLICT(asset_id) DO UPDATE SET content = ?, updated_at = CURRENT_TIMESTAMP",
		assetID, content, content,
	)
	if err != nil {
		return fmt.Errorf("upsert note for asset %d: %w", assetID, err)
	}
	return nil
}

func (r *AssetNoteRepo) Delete(assetID int64) error {
	_, err := r.db.Exec("DELETE FROM asset_notes WHERE asset_id = ?", assetID)
	return err
}
