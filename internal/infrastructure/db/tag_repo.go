package db

import (
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

type TagRepo struct {
	db *DB
}

func NewTagRepo(db *DB) *TagRepo {
	return &TagRepo{db: db}
}

func (r *TagRepo) List() ([]domain.Tag, error) {
	rows, err := r.db.Query("SELECT id, name, color, created_at FROM tags ORDER BY name")
	if err != nil {
		return nil, fmt.Errorf("list tags: %w", err)
	}
	defer rows.Close()

	var tags []domain.Tag
	for rows.Next() {
		var t domain.Tag
		if err := rows.Scan(&t.ID, &t.Name, &t.Color, &t.CreatedAt); err != nil {
			return nil, fmt.Errorf("scan tag: %w", err)
		}
		tags = append(tags, t)
	}
	return tags, rows.Err()
}

func (r *TagRepo) Create(name, color string) (*domain.Tag, error) {
	result, err := r.db.Exec("INSERT INTO tags (name, color) VALUES (?, ?)", name, color)
	if err != nil {
		return nil, fmt.Errorf("create tag: %w", err)
	}
	id, _ := result.LastInsertId()
	var t domain.Tag
	err = r.db.QueryRow("SELECT id, name, color, created_at FROM tags WHERE id = ?", id).Scan(&t.ID, &t.Name, &t.Color, &t.CreatedAt)
	if err != nil {
		return nil, err
	}
	return &t, nil
}

func (r *TagRepo) Delete(id int64) error {
	_, err := r.db.Exec("DELETE FROM tags WHERE id = ?", id)
	return err
}

func (r *TagRepo) GetByAssetID(assetID int64) ([]domain.Tag, error) {
	rows, err := r.db.Query(
		"SELECT t.id, t.name, t.color, t.created_at FROM tags t INNER JOIN asset_tags at2 ON t.id = at2.tag_id WHERE at2.asset_id = ? ORDER BY t.name", assetID,
	)
	if err != nil {
		return nil, fmt.Errorf("get tags for asset %d: %w", assetID, err)
	}
	defer rows.Close()

	var tags []domain.Tag
	for rows.Next() {
		var t domain.Tag
		if err := rows.Scan(&t.ID, &t.Name, &t.Color, &t.CreatedAt); err != nil {
			return nil, err
		}
		tags = append(tags, t)
	}
	return tags, rows.Err()
}

func (r *TagRepo) SetAssetTags(assetID int64, tagIDs []int64) error {
	tx, err := r.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	if _, err := tx.Exec("DELETE FROM asset_tags WHERE asset_id = ?", assetID); err != nil {
		return err
	}

	for _, tagID := range tagIDs {
		if _, err := tx.Exec("INSERT INTO asset_tags (asset_id, tag_id) VALUES (?, ?)", assetID, tagID); err != nil {
			return err
		}
	}

	return tx.Commit()
}
