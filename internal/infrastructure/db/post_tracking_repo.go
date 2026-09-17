package db

import (
	"database/sql"
	"fmt"
	"strings"
	"time"
)

// PostRecordCreate captures what was actually published. Common searchable
// fields stay first-class while platform-specific details are stored as JSON.
type PostRecordCreate struct {
	Title                string
	Body                 string
	Hashtags             string
	PlatformMetadataJSON string
	AssetIDs             []int64
	TargetID             int64
	AccountID            int64
	ExternalPostID       string
	ExternalURL          string
	PublishedAt          *time.Time
}

// PostRecordView is the publication snapshot read model used by the viewer.
type PostRecordView struct {
	ID                   int64
	Title                string
	Body                 string
	Hashtags             string
	PlatformMetadataJSON string
	Status               string
	PublishedAt          *time.Time
	CreatedAt            time.Time
	UpdatedAt            time.Time
	AssetIDs             []int64
	TargetID             int64
	TargetName           string
	TargetKind           string
	AccountID            int64
	AccountDisplay       string
	AccountIdentifier    string
	ExternalPostID       string
	ExternalURL          string
}

func (r *PostRepo) CreateTrackingRecord(input PostRecordCreate) (int64, error) {
	if len(input.AssetIDs) == 0 {
		return 0, fmt.Errorf("at least one asset is required")
	}
	if input.TargetID <= 0 || input.AccountID <= 0 {
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
		input.AccountID,
		input.TargetID,
	).Scan(&accountMatches); err != nil {
		return 0, fmt.Errorf("validate post account: %w", err)
	}
	if accountMatches == 0 {
		return 0, fmt.Errorf("selected account does not belong to the selected target")
	}

	title := strings.TrimSpace(input.Title)
	if title == "" {
		title = "投稿記録"
	}
	metadataJSON := strings.TrimSpace(input.PlatformMetadataJSON)
	if metadataJSON == "" {
		metadataJSON = "{}"
	}
	publishedAt := time.Now().UTC()
	if input.PublishedAt != nil {
		publishedAt = input.PublishedAt.UTC()
	}

	result, err := tx.Exec(
		"INSERT INTO posts (title, body, hashtags, status, published_at, platform_metadata_json) VALUES (?, ?, ?, 'published', ?, ?)",
		title,
		strings.TrimSpace(input.Body),
		strings.TrimSpace(input.Hashtags),
		publishedAt,
		metadataJSON,
	)
	if err != nil {
		return 0, fmt.Errorf("create post record: %w", err)
	}
	postID, err := result.LastInsertId()
	if err != nil {
		return 0, err
	}

	if _, err := tx.Exec(
		"INSERT INTO post_destinations (post_id, post_target_id, post_account_id, status, published_at, external_post_id, external_url) VALUES (?, ?, ?, 'published', ?, ?, ?)",
		postID,
		input.TargetID,
		input.AccountID,
		publishedAt,
		strings.TrimSpace(input.ExternalPostID),
		strings.TrimSpace(input.ExternalURL),
	); err != nil {
		return 0, fmt.Errorf("create post destination: %w", err)
	}

	seen := make(map[int64]struct{}, len(input.AssetIDs))
	sortOrder := 0
	for _, assetID := range input.AssetIDs {
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
		SELECT p.id, p.title, p.body, p.hashtags, p.platform_metadata_json,
		       p.status, p.published_at, p.created_at, p.updated_at,
		       t.id, t.name, t.kind,
		       a.id, a.display_name, a.account_identifier,
		       COALESCE(d.external_post_id, ''), COALESCE(d.external_url, '')
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
		SELECT p.id, p.title, p.body, p.hashtags, p.platform_metadata_json,
		       p.status, p.published_at, p.created_at, p.updated_at,
		       t.id, t.name, t.kind,
		       a.id, a.display_name, a.account_identifier,
		       COALESCE(d.external_post_id, ''), COALESCE(d.external_url, '')
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
		&record.Body,
		&record.Hashtags,
		&record.PlatformMetadataJSON,
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
		&record.ExternalURL,
	); err != nil {
		return PostRecordView{}, err
	}
	if publishedAt.Valid {
		record.PublishedAt = &publishedAt.Time
	}
	return record, nil
}
