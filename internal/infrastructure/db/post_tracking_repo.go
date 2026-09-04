package db

import (
	"database/sql"
	"fmt"
	"strings"
	"time"
)

// PostRecordView is a viewer-oriented read model for the actual purpose of
// Lumine's post feature: remembering which images were registered to which
// service/account. It intentionally uses the existing post_destinations table
// instead of treating posts as a text-composer first.
type PostRecordView struct {
	ID                int64
	Title             string
	Status            string
	PublishedAt       *time.Time
	CreatedAt         time.Time
	UpdatedAt         time.Time
	AssetIDs          []int64
	TargetID          int64
	TargetName        string
	TargetKind        string
	AccountID         int64
	AccountDisplay    string
	AccountIdentifier string
	ExternalPostID    string
}

func (r *PostRepo) CreateTrackingRecord(title string, assetIDs []int64, targetID, accountID int64, externalPostID string) (int64, error) {
	if len(assetIDs) == 0 {
		return 0, fmt.Errorf("at least one asset is required")
	}
	if targetID <= 0 || accountID <= 0 {
		return 0, fmt.Errorf("post target and account are required")
	}

	tx, err := r.db.Begin()
	if err != nil {
		return 0, err
	}
	defer tx.Rollback()

	var accountMatches int
	if err := tx.QueryRow(
		"SELECT COUNT(*) FROM post_accounts WHERE id = ? AND post_target_id = ? AND is_active = 1",
		accountID,
		targetID,
	).Scan(&accountMatches); err != nil {
		return 0, fmt.Errorf("validate post account: %w", err)
	}
	if accountMatches == 0 {
		return 0, fmt.Errorf("selected account does not belong to the selected target")
	}

	title = strings.TrimSpace(title)
	if title == "" {
		title = "投稿記録"
	}

	result, err := tx.Exec(
		"INSERT INTO posts (title, body, hashtags, status, published_at) VALUES (?, '', '', 'published', CURRENT_TIMESTAMP)",
		title,
	)
	if err != nil {
		return 0, fmt.Errorf("create post record: %w", err)
	}
	postID, err := result.LastInsertId()
	if err != nil {
		return 0, err
	}

	if _, err := tx.Exec(
		"INSERT INTO post_destinations (post_id, post_target_id, post_account_id, status, published_at, external_post_id) VALUES (?, ?, ?, 'published', CURRENT_TIMESTAMP, ?)",
		postID,
		targetID,
		accountID,
		strings.TrimSpace(externalPostID),
	); err != nil {
		return 0, fmt.Errorf("create post destination: %w", err)
	}

	seen := make(map[int64]struct{}, len(assetIDs))
	sortOrder := 0
	for _, assetID := range assetIDs {
		if assetID <= 0 {
			continue
		}
		if _, exists := seen[assetID]; exists {
			continue
		}
		seen[assetID] = struct{}{}
		if _, err := tx.Exec(
			"INSERT INTO post_assets (post_id, asset_id, sort_order) VALUES (?, ?, ?)",
			postID,
			assetID,
			sortOrder,
		); err != nil {
			return 0, fmt.Errorf("attach asset %d: %w", assetID, err)
		}
		sortOrder++
	}
	if sortOrder == 0 {
		return 0, fmt.Errorf("no valid assets were supplied")
	}

	if err := tx.Commit(); err != nil {
		return 0, err
	}
	return postID, nil
}

func (r *PostRepo) ListTrackingRecords(offset, limit int) ([]PostRecordView, error) {
	if limit <= 0 {
		limit = 100
	}
	rows, err := r.db.Query(`
		SELECT p.id, p.title, p.status, p.published_at, p.created_at, p.updated_at,
		       t.id, t.name, t.kind,
		       a.id, a.display_name, a.account_identifier,
		       COALESCE(d.external_post_id, '')
		FROM posts p
		INNER JOIN post_destinations d ON d.post_id = p.id
		INNER JOIN post_targets t ON t.id = d.post_target_id
		INNER JOIN post_accounts a ON a.id = d.post_account_id
		ORDER BY COALESCE(d.published_at, p.published_at, p.updated_at) DESC, p.id DESC
		LIMIT ? OFFSET ?`, limit, offset)
	if err != nil {
		return nil, fmt.Errorf("list post records: %w", err)
	}
	defer rows.Close()

	records := make([]PostRecordView, 0, limit)
	for rows.Next() {
		record, err := scanPostRecord(rows)
		if err != nil {
			return nil, err
		}
		assetIDs, err := r.GetAssetsByPostID(record.ID)
		if err != nil {
			return nil, err
		}
		record.AssetIDs = assetIDs
		records = append(records, record)
	}
	return records, rows.Err()
}

func (r *PostRepo) GetTrackingRecordsByAsset(assetID int64) ([]PostRecordView, error) {
	rows, err := r.db.Query(`
		SELECT p.id, p.title, p.status, p.published_at, p.created_at, p.updated_at,
		       t.id, t.name, t.kind,
		       a.id, a.display_name, a.account_identifier,
		       COALESCE(d.external_post_id, '')
		FROM posts p
		INNER JOIN post_assets pa ON pa.post_id = p.id
		INNER JOIN post_destinations d ON d.post_id = p.id
		INNER JOIN post_targets t ON t.id = d.post_target_id
		INNER JOIN post_accounts a ON a.id = d.post_account_id
		WHERE pa.asset_id = ?
		ORDER BY COALESCE(d.published_at, p.published_at, p.updated_at) DESC, p.id DESC`, assetID)
	if err != nil {
		return nil, fmt.Errorf("get post records by asset: %w", err)
	}
	defer rows.Close()

	var records []PostRecordView
	for rows.Next() {
		record, err := scanPostRecord(rows)
		if err != nil {
			return nil, err
		}
		assetIDs, err := r.GetAssetsByPostID(record.ID)
		if err != nil {
			return nil, err
		}
		record.AssetIDs = assetIDs
		records = append(records, record)
	}
	return records, rows.Err()
}

type rowScanner interface {
	Scan(dest ...interface{}) error
}

func scanPostRecord(row rowScanner) (PostRecordView, error) {
	var record PostRecordView
	var publishedAt sql.NullTime
	if err := row.Scan(
		&record.ID,
		&record.Title,
		&record.Status,
		&publishedAt,
		&record.CreatedAt,
		&record.UpdatedAt,
		&record.TargetID,
		&record.TargetName,
		&record.TargetKind,
		&record.AccountID,
		&record.AccountDisplay,
		&record.AccountIdentifier,
		&record.ExternalPostID,
	); err != nil {
		return PostRecordView{}, err
	}
	if publishedAt.Valid {
		record.PublishedAt = &publishedAt.Time
	}
	return record, nil
}
