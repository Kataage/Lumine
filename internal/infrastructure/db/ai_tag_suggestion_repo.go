package db

import (
	"database/sql"
	"errors"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

var ErrAITagSuggestionNotFound = errors.New("AI tag suggestion not found")

type AITagSuggestionRepo struct {
	db *DB
}

func NewAITagSuggestionRepo(db *DB) *AITagSuggestionRepo {
	return &AITagSuggestionRepo{db: db}
}

func validateSuggestion(value domain.AITagSuggestion) error {
	if !domain.IsAITagSuggestionKind(value.Kind) {
		return fmt.Errorf("invalid AI tag suggestion kind %q", value.Kind)
	}
	if strings.TrimSpace(value.Name) == "" {
		return errors.New("AI tag suggestion name is required")
	}
	if value.Confidence < 0 || value.Confidence > 1 {
		return fmt.Errorf("AI tag suggestion confidence %.4f is outside [0,1]", value.Confidence)
	}
	if value.Threshold < 0 || value.Threshold > 1 {
		return fmt.Errorf("AI tag suggestion threshold %.4f is outside [0,1]", value.Threshold)
	}
	return nil
}

// ReplaceForAsset stores the latest Tagger analysis suggestions for one asset.
// Previously accepted user tags remain in tags/asset_tags; only the transient
// AI suggestion rows are replaced.
func (r *AITagSuggestionRepo) ReplaceForAsset(
	assetID int64,
	engine, modelID, modelVersion string,
	suggestions []domain.AITagSuggestion,
) error {
	if assetID <= 0 {
		return errors.New("asset id is required")
	}
	for _, suggestion := range suggestions {
		if err := validateSuggestion(suggestion); err != nil {
			return err
		}
	}

	tx, err := r.db.Begin()
	if err != nil {
		return fmt.Errorf("begin replace AI tag suggestions: %w", err)
	}
	defer tx.Rollback()

	if _, err := tx.Exec("DELETE FROM ai_tag_suggestions WHERE asset_id = ?", assetID); err != nil {
		return fmt.Errorf("clear AI tag suggestions for asset %d: %w", assetID, err)
	}

	for _, suggestion := range suggestions {
		name := strings.TrimSpace(suggestion.Name)
		if _, err := tx.Exec(
			"INSERT INTO ai_tag_suggestions (asset_id, kind, name, confidence, state, threshold, engine, model_id, model_version) VALUES (?, ?, ?, ?, 'pending', ?, ?, ?, ?)",
			assetID,
			suggestion.Kind,
			name,
			suggestion.Confidence,
			suggestion.Threshold,
			engine,
			modelID,
			modelVersion,
		); err != nil {
			return fmt.Errorf("insert AI tag suggestion %q: %w", name, err)
		}
	}

	if err := tx.Commit(); err != nil {
		return fmt.Errorf("commit AI tag suggestions: %w", err)
	}
	return nil
}

func (r *AITagSuggestionRepo) ListByAsset(assetID int64) ([]domain.AITagSuggestion, error) {
	rows, err := r.db.Query(
		"SELECT id, asset_id, kind, name, confidence, state, threshold, engine, model_id, model_version, created_at, updated_at FROM ai_tag_suggestions WHERE asset_id = ? ORDER BY CASE kind WHEN 'character' THEN 0 WHEN 'general' THEN 1 ELSE 2 END, confidence DESC, name ASC",
		assetID,
	)
	if err != nil {
		return nil, fmt.Errorf("list AI tag suggestions for asset %d: %w", assetID, err)
	}
	defer rows.Close()

	result := make([]domain.AITagSuggestion, 0)
	for rows.Next() {
		var value domain.AITagSuggestion
		if err := rows.Scan(
			&value.ID,
			&value.AssetID,
			&value.Kind,
			&value.Name,
			&value.Confidence,
			&value.State,
			&value.Threshold,
			&value.Engine,
			&value.ModelID,
			&value.ModelVersion,
			&value.CreatedAt,
			&value.UpdatedAt,
		); err != nil {
			return nil, fmt.Errorf("scan AI tag suggestion: %w", err)
		}
		result = append(result, value)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("iterate AI tag suggestions: %w", err)
	}
	return result, nil
}

func (r *AITagSuggestionRepo) Accept(id int64) (*domain.Tag, error) {
	tx, err := r.db.Begin()
	if err != nil {
		return nil, fmt.Errorf("begin accept AI tag suggestion: %w", err)
	}
	defer tx.Rollback()

	suggestion, err := getAITagSuggestionTx(tx, id)
	if err != nil {
		return nil, err
	}
	tag, err := acceptAITagSuggestionTx(tx, suggestion)
	if err != nil {
		return nil, err
	}
	if err := tx.Commit(); err != nil {
		return nil, fmt.Errorf("commit accept AI tag suggestion: %w", err)
	}
	return tag, nil
}

func (r *AITagSuggestionRepo) AcceptAll(assetID int64) ([]domain.Tag, error) {
	tx, err := r.db.Begin()
	if err != nil {
		return nil, fmt.Errorf("begin accept all AI tag suggestions: %w", err)
	}
	defer tx.Rollback()

	rows, err := tx.Query(
		"SELECT id, asset_id, kind, name, confidence, state, threshold, engine, model_id, model_version, created_at, updated_at FROM ai_tag_suggestions WHERE asset_id = ? AND state = 'pending' ORDER BY id",
		assetID,
	)
	if err != nil {
		return nil, fmt.Errorf("list pending AI tag suggestions: %w", err)
	}

	var pending []domain.AITagSuggestion
	for rows.Next() {
		var value domain.AITagSuggestion
		if err := rows.Scan(
			&value.ID,
			&value.AssetID,
			&value.Kind,
			&value.Name,
			&value.Confidence,
			&value.State,
			&value.Threshold,
			&value.Engine,
			&value.ModelID,
			&value.ModelVersion,
			&value.CreatedAt,
			&value.UpdatedAt,
		); err != nil {
			rows.Close()
			return nil, fmt.Errorf("scan pending AI tag suggestion: %w", err)
		}
		pending = append(pending, value)
	}
	if err := rows.Err(); err != nil {
		rows.Close()
		return nil, fmt.Errorf("iterate pending AI tag suggestions: %w", err)
	}
	if err := rows.Close(); err != nil {
		return nil, err
	}

	tags := make([]domain.Tag, 0, len(pending))
	for _, suggestion := range pending {
		tag, err := acceptAITagSuggestionTx(tx, &suggestion)
		if err != nil {
			return nil, err
		}
		if tag != nil {
			tags = append(tags, *tag)
		}
	}

	if err := tx.Commit(); err != nil {
		return nil, fmt.Errorf("commit accept all AI tag suggestions: %w", err)
	}
	return tags, nil
}

func (r *AITagSuggestionRepo) Reject(id int64) error {
	result, err := r.db.Exec(
		"UPDATE ai_tag_suggestions SET state = 'rejected', updated_at = CURRENT_TIMESTAMP WHERE id = ? AND state = 'pending'",
		id,
	)
	if err != nil {
		return fmt.Errorf("reject AI tag suggestion %d: %w", id, err)
	}
	changed, err := result.RowsAffected()
	if err != nil {
		return err
	}
	if changed == 1 {
		return nil
	}

	var state domain.AITagSuggestionState
	err = r.db.QueryRow("SELECT state FROM ai_tag_suggestions WHERE id = ?", id).Scan(&state)
	if err == sql.ErrNoRows {
		return ErrAITagSuggestionNotFound
	}
	if err != nil {
		return fmt.Errorf("read AI tag suggestion %d after reject: %w", id, err)
	}
	return fmt.Errorf("AI tag suggestion %d is not pending (state=%s)", id, state)
}

func (r *AITagSuggestionRepo) RejectAll(assetID int64) error {
	_, err := r.db.Exec(
		"UPDATE ai_tag_suggestions SET state = 'rejected', updated_at = CURRENT_TIMESTAMP WHERE asset_id = ? AND state = 'pending'",
		assetID,
	)
	if err != nil {
		return fmt.Errorf("reject all AI tag suggestions for asset %d: %w", assetID, err)
	}
	return nil
}

func getAITagSuggestionTx(tx *sql.Tx, id int64) (*domain.AITagSuggestion, error) {
	var value domain.AITagSuggestion
	err := tx.QueryRow(
		"SELECT id, asset_id, kind, name, confidence, state, threshold, engine, model_id, model_version, created_at, updated_at FROM ai_tag_suggestions WHERE id = ?",
		id,
	).Scan(
		&value.ID,
		&value.AssetID,
		&value.Kind,
		&value.Name,
		&value.Confidence,
		&value.State,
		&value.Threshold,
		&value.Engine,
		&value.ModelID,
		&value.ModelVersion,
		&value.CreatedAt,
		&value.UpdatedAt,
	)
	if err == sql.ErrNoRows {
		return nil, ErrAITagSuggestionNotFound
	}
	if err != nil {
		return nil, fmt.Errorf("get AI tag suggestion %d: %w", id, err)
	}
	return &value, nil
}

func acceptAITagSuggestionTx(tx *sql.Tx, suggestion *domain.AITagSuggestion) (*domain.Tag, error) {
	if suggestion == nil {
		return nil, ErrAITagSuggestionNotFound
	}
	if suggestion.State == domain.AITagSuggestionRejected {
		return nil, fmt.Errorf("AI tag suggestion %d is rejected", suggestion.ID)
	}
	if suggestion.State == domain.AITagSuggestionAccepted {
		if suggestion.Kind == domain.AITagSuggestionRating {
			return nil, nil
		}
		return getTagByNameTx(tx, suggestion.Name)
	}

	var tag *domain.Tag
	if suggestion.Kind != domain.AITagSuggestionRating {
		var err error
		tag, err = ensureTagByNameTx(tx, suggestion.Name)
		if err != nil {
			return nil, err
		}
		if _, err := tx.Exec(
			"INSERT OR IGNORE INTO asset_tags (asset_id, tag_id) VALUES (?, ?)",
			suggestion.AssetID, tag.ID,
		); err != nil {
			return nil, fmt.Errorf("assign accepted AI tag %q: %w", suggestion.Name, err)
		}
	}

	if _, err := tx.Exec(
		"UPDATE ai_tag_suggestions SET state = 'accepted', updated_at = CURRENT_TIMESTAMP WHERE id = ?",
		suggestion.ID,
	); err != nil {
		return nil, fmt.Errorf("mark AI tag suggestion accepted: %w", err)
	}
	return tag, nil
}

func ensureTagByNameTx(tx *sql.Tx, name string) (*domain.Tag, error) {
	name = strings.TrimSpace(name)
	if name == "" {
		return nil, errors.New("tag name is required")
	}
	if _, err := tx.Exec("INSERT OR IGNORE INTO tags (name, color) VALUES (?, '')", name); err != nil {
		return nil, fmt.Errorf("ensure tag %q: %w", name, err)
	}
	return getTagByNameTx(tx, name)
}

func getTagByNameTx(tx *sql.Tx, name string) (*domain.Tag, error) {
	var tag domain.Tag
	if err := tx.QueryRow(
		"SELECT id, name, color, created_at FROM tags WHERE name = ?",
		name,
	).Scan(&tag.ID, &tag.Name, &tag.Color, &tag.CreatedAt); err != nil {
		return nil, fmt.Errorf("get tag %q: %w", name, err)
	}
	return &tag, nil
}
