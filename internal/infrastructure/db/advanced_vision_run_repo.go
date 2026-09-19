package db

import (
	"database/sql"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

type AdvancedVisionRunRepo struct {
	db *DB
}

func NewAdvancedVisionRunRepo(db *DB) *AdvancedVisionRunRepo {
	return &AdvancedVisionRunRepo{db: db}
}

func (r *AdvancedVisionRunRepo) CreateRunning(
	operation string,
	instruction string,
	engine string,
	modelID string,
	modelVersion string,
	assetIDs []int64,
) (*domain.AdvancedVisionRun, error) {
	if len(assetIDs) == 0 {
		return nil, fmt.Errorf("Advanced Vision run requires at least one asset")
	}
	tx, err := r.db.Begin()
	if err != nil {
		return nil, fmt.Errorf("begin Advanced Vision run: %w", err)
	}
	defer tx.Rollback()

	result, err := tx.Exec(
		`INSERT INTO advanced_vision_runs
			(operation, instruction, state, engine, model_id, model_version, result_json, error_message)
		 VALUES (?, ?, 'running', ?, ?, ?, '{}', '')`,
		operation, strings.TrimSpace(instruction), engine, modelID, modelVersion,
	)
	if err != nil {
		return nil, fmt.Errorf("insert Advanced Vision run: %w", err)
	}
	runID, err := result.LastInsertId()
	if err != nil {
		return nil, fmt.Errorf("get Advanced Vision run id: %w", err)
	}
	for position, assetID := range assetIDs {
		if assetID <= 0 {
			return nil, fmt.Errorf("invalid Advanced Vision asset id %d", assetID)
		}
		if _, err := tx.Exec(
			"INSERT INTO advanced_vision_run_assets (run_id, asset_id, position) VALUES (?, ?, ?)",
			runID, assetID, position,
		); err != nil {
			return nil, fmt.Errorf("attach asset %d to Advanced Vision run: %w", assetID, err)
		}
	}
	if err := tx.Commit(); err != nil {
		return nil, fmt.Errorf("commit Advanced Vision run: %w", err)
	}
	return r.GetByID(runID)
}

func (r *AdvancedVisionRunRepo) Complete(runID int64, resultJSON string) error {
	result, err := r.db.Exec(
		`UPDATE advanced_vision_runs
		 SET state = 'ready', result_json = ?, error_message = '', completed_at = CURRENT_TIMESTAMP
		 WHERE id = ?`,
		resultJSON, runID,
	)
	if err != nil {
		return fmt.Errorf("complete Advanced Vision run: %w", err)
	}
	count, err := result.RowsAffected()
	if err != nil {
		return err
	}
	if count != 1 {
		return fmt.Errorf("Advanced Vision run not found: %d", runID)
	}
	return nil
}

func (r *AdvancedVisionRunRepo) Fail(runID int64, message string) error {
	result, err := r.db.Exec(
		`UPDATE advanced_vision_runs
		 SET state = 'failed', error_message = ?, completed_at = CURRENT_TIMESTAMP
		 WHERE id = ?`,
		strings.TrimSpace(message), runID,
	)
	if err != nil {
		return fmt.Errorf("fail Advanced Vision run: %w", err)
	}
	count, err := result.RowsAffected()
	if err != nil {
		return err
	}
	if count != 1 {
		return fmt.Errorf("Advanced Vision run not found: %d", runID)
	}
	return nil
}

func (r *AdvancedVisionRunRepo) GetByID(runID int64) (*domain.AdvancedVisionRun, error) {
	var run domain.AdvancedVisionRun
	var completedAt sql.NullTime
	err := r.db.QueryRow(
		`SELECT id, operation, instruction, state, engine, model_id, model_version,
		        result_json, error_message, created_at, completed_at
		   FROM advanced_vision_runs WHERE id = ?`,
		runID,
	).Scan(
		&run.ID, &run.Operation, &run.Instruction, &run.State, &run.Engine,
		&run.ModelID, &run.ModelVersion, &run.ResultJSON, &run.ErrorMessage,
		&run.CreatedAt, &completedAt,
	)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get Advanced Vision run %d: %w", runID, err)
	}
	if completedAt.Valid {
		run.CompletedAt = &completedAt.Time
	}
	assetIDs, err := r.listAssetIDs(runID)
	if err != nil {
		return nil, err
	}
	run.AssetIDs = assetIDs
	return &run, nil
}

func (r *AdvancedVisionRunRepo) ListByAsset(assetID int64, limit int) ([]domain.AdvancedVisionRun, error) {
	if limit <= 0 || limit > 100 {
		limit = 20
	}
	rows, err := r.db.Query(
		`SELECT r.id
		   FROM advanced_vision_runs r
		   INNER JOIN advanced_vision_run_assets a ON a.run_id = r.id
		  WHERE a.asset_id = ?
		  ORDER BY r.id DESC
		  LIMIT ?`,
		assetID, limit,
	)
	if err != nil {
		return nil, fmt.Errorf("list Advanced Vision runs for asset %d: %w", assetID, err)
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
	if err := rows.Err(); err != nil {
		return nil, err
	}

	result := make([]domain.AdvancedVisionRun, 0, len(ids))
	for _, id := range ids {
		run, err := r.GetByID(id)
		if err != nil {
			return nil, err
		}
		if run != nil {
			result = append(result, *run)
		}
	}
	return result, nil
}

func (r *AdvancedVisionRunRepo) listAssetIDs(runID int64) ([]int64, error) {
	rows, err := r.db.Query(
		"SELECT asset_id FROM advanced_vision_run_assets WHERE run_id = ? ORDER BY position",
		runID,
	)
	if err != nil {
		return nil, fmt.Errorf("list Advanced Vision run assets: %w", err)
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
