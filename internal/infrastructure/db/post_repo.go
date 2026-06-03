package db

import (
	"database/sql"
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

type PostRepo struct {
	db *DB
}

func NewPostRepo(db *DB) *PostRepo {
	return &PostRepo{db: db}
}

func (r *PostRepo) List(offset, limit int) ([]domain.Post, error) {
	if limit <= 0 {
		limit = 100
	}
	rows, err := r.db.Query(
		"SELECT id, title, body, hashtags, status, scheduled_at, published_at, created_at, updated_at FROM posts ORDER BY updated_at DESC LIMIT ? OFFSET ?", limit, offset,
	)
	if err != nil {
		return nil, fmt.Errorf("list posts: %w", err)
	}
	defer rows.Close()

	var posts []domain.Post
	for rows.Next() {
		var p domain.Post
		var scheduledAt, publishedAt sql.NullTime
		if err := rows.Scan(&p.ID, &p.Title, &p.Body, &p.Hashtags, &p.Status, &scheduledAt, &publishedAt, &p.CreatedAt, &p.UpdatedAt); err != nil {
			return nil, err
		}
		if scheduledAt.Valid {
			p.ScheduledAt = &scheduledAt.Time
		}
		if publishedAt.Valid {
			p.PublishedAt = &publishedAt.Time
		}
		posts = append(posts, p)
	}
	return posts, rows.Err()
}

func (r *PostRepo) GetByID(id int64) (*domain.Post, error) {
	var p domain.Post
	var scheduledAt, publishedAt sql.NullTime
	err := r.db.QueryRow(
		"SELECT id, title, body, hashtags, status, scheduled_at, published_at, created_at, updated_at FROM posts WHERE id = ?", id,
	).Scan(&p.ID, &p.Title, &p.Body, &p.Hashtags, &p.Status, &scheduledAt, &publishedAt, &p.CreatedAt, &p.UpdatedAt)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	if scheduledAt.Valid {
		p.ScheduledAt = &scheduledAt.Time
	}
	if publishedAt.Valid {
		p.PublishedAt = &publishedAt.Time
	}
	return &p, nil
}

func (r *PostRepo) Create(p *domain.Post) (int64, error) {
	result, err := r.db.Exec(
		"INSERT INTO posts (title, body, hashtags, status) VALUES (?, ?, ?, ?)",
		p.Title, p.Body, p.Hashtags, p.Status,
	)
	if err != nil {
		return 0, fmt.Errorf("create post: %w", err)
	}
	return result.LastInsertId()
}

func (r *PostRepo) Update(p *domain.Post) error {
	_, err := r.db.Exec(
		"UPDATE posts SET title = ?, body = ?, hashtags = ?, status = ?, scheduled_at = ?, published_at = ?, updated_at = CURRENT_TIMESTAMP WHERE id = ?",
		p.Title, p.Body, p.Hashtags, p.Status, p.ScheduledAt, p.PublishedAt, p.ID,
	)
	return err
}

func (r *PostRepo) Delete(id int64) error {
	_, err := r.db.Exec("DELETE FROM posts WHERE id = ?", id)
	return err
}

func (r *PostRepo) AttachAssets(postID int64, assetIDs []int64) error {
	tx, err := r.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()

	if _, err := tx.Exec("DELETE FROM post_assets WHERE post_id = ?", postID); err != nil {
		return err
	}

	for i, assetID := range assetIDs {
		if _, err := tx.Exec("INSERT INTO post_assets (post_id, asset_id, sort_order) VALUES (?, ?, ?)", postID, assetID, i); err != nil {
			return err
		}
	}

	return tx.Commit()
}

func (r *PostRepo) GetAssetsByPostID(postID int64) ([]int64, error) {
	rows, err := r.db.Query(
		"SELECT asset_id FROM post_assets WHERE post_id = ? ORDER BY sort_order", postID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var ids []int64
	for rows.Next() {
		var id int64
		if err := rows.Scan(&id); err != nil {
			return nil, err
		}
		ids = append(ids, id)
	}
	return ids, rows.Err()
}

func (r *PostRepo) GetPostsByAssetID(assetID int64) ([]domain.Post, error) {
	rows, err := r.db.Query(
		"SELECT p.id, p.title, p.body, p.hashtags, p.status, p.scheduled_at, p.published_at, p.created_at, p.updated_at FROM posts p INNER JOIN post_assets pa ON p.id = pa.post_id WHERE pa.asset_id = ? ORDER BY p.updated_at DESC", assetID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var posts []domain.Post
	for rows.Next() {
		var p domain.Post
		var scheduledAt, publishedAt sql.NullTime
		if err := rows.Scan(&p.ID, &p.Title, &p.Body, &p.Hashtags, &p.Status, &scheduledAt, &publishedAt, &p.CreatedAt, &p.UpdatedAt); err != nil {
			return nil, err
		}
		if scheduledAt.Valid {
			p.ScheduledAt = &scheduledAt.Time
		}
		if publishedAt.Valid {
			p.PublishedAt = &publishedAt.Time
		}
		posts = append(posts, p)
	}
	return posts, rows.Err()
}

type PostTargetRepo struct {
	db *DB
}

func NewPostTargetRepo(db *DB) *PostTargetRepo {
	return &PostTargetRepo{db: db}
}

func (r *PostTargetRepo) List() ([]domain.PostTarget, error) {
	rows, err := r.db.Query("SELECT id, name, kind, created_at FROM post_targets ORDER BY name")
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var targets []domain.PostTarget
	for rows.Next() {
		var t domain.PostTarget
		if err := rows.Scan(&t.ID, &t.Name, &t.Kind, &t.CreatedAt); err != nil {
			return nil, err
		}
		targets = append(targets, t)
	}
	return targets, rows.Err()
}

func (r *PostTargetRepo) Create(name, kind string) (int64, error) {
	result, err := r.db.Exec("INSERT INTO post_targets (name, kind) VALUES (?, ?)", name, kind)
	if err != nil {
		return 0, err
	}
	return result.LastInsertId()
}

func (r *PostTargetRepo) Delete(id int64) error {
	_, err := r.db.Exec("DELETE FROM post_targets WHERE id = ?", id)
	return err
}

type PostAccountRepo struct {
	db *DB
}

func NewPostAccountRepo(db *DB) *PostAccountRepo {
	return &PostAccountRepo{db: db}
}

func (r *PostAccountRepo) List() ([]domain.PostAccount, error) {
	rows, err := r.db.Query("SELECT id, post_target_id, display_name, account_identifier, is_active, created_at, updated_at FROM post_accounts ORDER BY display_name")
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var accounts []domain.PostAccount
	for rows.Next() {
		var a domain.PostAccount
		if err := rows.Scan(&a.ID, &a.PostTargetID, &a.DisplayName, &a.AccountIdentifier, &a.IsActive, &a.CreatedAt, &a.UpdatedAt); err != nil {
			return nil, err
		}
		accounts = append(accounts, a)
	}
	return accounts, rows.Err()
}

func (r *PostAccountRepo) Create(targetID int64, displayName, identifier string) (int64, error) {
	result, err := r.db.Exec(
		"INSERT INTO post_accounts (post_target_id, display_name, account_identifier) VALUES (?, ?, ?)",
		targetID, displayName, identifier,
	)
	if err != nil {
		return 0, err
	}
	return result.LastInsertId()
}

func (r *PostAccountRepo) Delete(id int64) error {
	_, err := r.db.Exec("DELETE FROM post_accounts WHERE id = ?", id)
	return err
}
