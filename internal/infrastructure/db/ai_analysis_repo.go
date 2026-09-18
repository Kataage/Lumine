package db

import (
	"database/sql"
	"errors"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

var ErrAIJobNotRetryable = errors.New("AI job is not retryable")

type AIAnalysisRepo struct {
	db *DB
}

func NewAIAnalysisRepo(db *DB) *AIAnalysisRepo {
	return &AIAnalysisRepo{db: db}
}

func (r *AIAnalysisRepo) Enqueue(
	assetID int64,
	capability domain.AICapability,
	source domain.AIJobSource,
	priority int,
	maxAttempts int,
) (domain.AIJob, bool, error) {
	if maxAttempts <= 0 {
		maxAttempts = 3
	}

	tx, err := r.db.Begin()
	if err != nil {
		return domain.AIJob{}, false, fmt.Errorf("begin AI enqueue: %w", err)
	}
	defer tx.Rollback()

	var existingID int64
	var existingStatus domain.AIJobStatus
	err = tx.QueryRow(
		"SELECT id, status FROM ai_jobs WHERE asset_id = ? AND capability = ? AND status IN ('queued','running') ORDER BY id LIMIT 1",
		assetID, capability,
	).Scan(&existingID, &existingStatus)
	if err != nil && err != sql.ErrNoRows {
		return domain.AIJob{}, false, fmt.Errorf("find active AI job: %w", err)
	}

	created := false
	jobID := existingID
	if err == sql.ErrNoRows {
		result, insertErr := tx.Exec(
			"INSERT INTO ai_jobs (asset_id, capability, source, priority, status, max_attempts) VALUES (?, ?, ?, ?, 'queued', ?)",
			assetID, capability, source, priority, maxAttempts,
		)
		if insertErr != nil {
			return domain.AIJob{}, false, fmt.Errorf("insert AI job: %w", insertErr)
		}
		jobID, insertErr = result.LastInsertId()
		if insertErr != nil {
			return domain.AIJob{}, false, fmt.Errorf("get AI job id: %w", insertErr)
		}
		created = true
		existingStatus = domain.AIJobQueued
	} else if existingStatus == domain.AIJobQueued {
		if _, err := tx.Exec("UPDATE ai_jobs SET priority = MAX(priority, ?) WHERE id = ?", priority, jobID); err != nil {
			return domain.AIJob{}, false, fmt.Errorf("raise AI job priority: %w", err)
		}
	}

	if existingStatus == domain.AIJobQueued {
		if _, err := tx.Exec(`
			INSERT INTO ai_asset_analysis (asset_id, capability, state, error_message, updated_at)
			VALUES (?, ?, 'queued', '', CURRENT_TIMESTAMP)
			ON CONFLICT(asset_id, capability) DO UPDATE SET
				state = 'queued',
				error_message = '',
				updated_at = CURRENT_TIMESTAMP
		`, assetID, capability); err != nil {
			return domain.AIJob{}, false, fmt.Errorf("mark AI analysis queued: %w", err)
		}
	}

	if err := tx.Commit(); err != nil {
		return domain.AIJob{}, false, fmt.Errorf("commit AI enqueue: %w", err)
	}
	job, err := r.GetJob(jobID)
	if err != nil {
		return domain.AIJob{}, false, err
	}
	if job == nil {
		return domain.AIJob{}, false, fmt.Errorf("AI job disappeared after enqueue: %d", jobID)
	}
	return *job, created, nil
}

func (r *AIAnalysisRepo) EnqueueBatch(
	assetIDs []int64,
	capability domain.AICapability,
	source domain.AIJobSource,
	priority int,
	maxAttempts int,
) (int, error) {
	if len(assetIDs) == 0 {
		return 0, nil
	}
	if maxAttempts <= 0 {
		maxAttempts = 3
	}

	tx, err := r.db.Begin()
	if err != nil {
		return 0, fmt.Errorf("begin AI batch enqueue: %w", err)
	}
	defer tx.Rollback()

	created := 0
	for _, assetID := range assetIDs {
		var existingID int64
		var existingStatus domain.AIJobStatus
		findErr := tx.QueryRow(
			"SELECT id, status FROM ai_jobs WHERE asset_id = ? AND capability = ? AND status IN ('queued','running') ORDER BY id LIMIT 1",
			assetID, capability,
		).Scan(&existingID, &existingStatus)
		if findErr != nil && findErr != sql.ErrNoRows {
			return 0, fmt.Errorf("find active AI job for asset %d: %w", assetID, findErr)
		}

		if findErr == sql.ErrNoRows {
			if _, err := tx.Exec(
				"INSERT INTO ai_jobs (asset_id, capability, source, priority, status, max_attempts) VALUES (?, ?, ?, ?, 'queued', ?)",
				assetID, capability, source, priority, maxAttempts,
			); err != nil {
				return 0, fmt.Errorf("insert AI job for asset %d: %w", assetID, err)
			}
			created++
			existingStatus = domain.AIJobQueued
		} else if existingStatus == domain.AIJobQueued {
			if _, err := tx.Exec("UPDATE ai_jobs SET priority = MAX(priority, ?) WHERE id = ?", priority, existingID); err != nil {
				return 0, fmt.Errorf("raise AI job priority for asset %d: %w", assetID, err)
			}
		}

		if existingStatus == domain.AIJobQueued {
			if _, err := tx.Exec(`
				INSERT INTO ai_asset_analysis (asset_id, capability, state, error_message, updated_at)
				VALUES (?, ?, 'queued', '', CURRENT_TIMESTAMP)
				ON CONFLICT(asset_id, capability) DO UPDATE SET
					state = 'queued',
					error_message = '',
					updated_at = CURRENT_TIMESTAMP
			`, assetID, capability); err != nil {
				return 0, fmt.Errorf("mark AI analysis queued for asset %d: %w", assetID, err)
			}
		}
	}

	if err := tx.Commit(); err != nil {
		return 0, fmt.Errorf("commit AI batch enqueue: %w", err)
	}
	return created, nil
}

func (r *AIAnalysisRepo) ClaimNext(capabilities []domain.AICapability) (*domain.AIJob, error) {
	if len(capabilities) == 0 {
		return nil, nil
	}
	placeholders := strings.TrimRight(strings.Repeat("?,", len(capabilities)), ",")
	args := make([]any, 0, len(capabilities))
	for _, capability := range capabilities {
		args = append(args, capability)
	}

	tx, err := r.db.Begin()
	if err != nil {
		return nil, fmt.Errorf("begin AI claim: %w", err)
	}
	defer tx.Rollback()

	query := fmt.Sprintf(
		"SELECT id FROM ai_jobs WHERE status = 'queued' AND capability IN (%s) ORDER BY priority DESC, id ASC LIMIT 1",
		placeholders,
	)
	var id int64
	if err := tx.QueryRow(query, args...).Scan(&id); err != nil {
		if err == sql.ErrNoRows {
			return nil, nil
		}
		return nil, fmt.Errorf("select queued AI job: %w", err)
	}

	result, err := tx.Exec(`
		UPDATE ai_jobs
		SET status = 'running',
			attempt_count = attempt_count + 1,
			cancel_requested = 0,
			started_at = CURRENT_TIMESTAMP,
			finished_at = NULL
		WHERE id = ? AND status = 'queued'
	`, id)
	if err != nil {
		return nil, fmt.Errorf("claim AI job: %w", err)
	}
	changed, err := result.RowsAffected()
	if err != nil {
		return nil, fmt.Errorf("claim AI job rows affected: %w", err)
	}
	if changed != 1 {
		return nil, nil
	}

	var assetID int64
	var capability domain.AICapability
	if err := tx.QueryRow("SELECT asset_id, capability FROM ai_jobs WHERE id = ?", id).Scan(&assetID, &capability); err != nil {
		return nil, fmt.Errorf("read claimed AI job: %w", err)
	}
	if _, err := tx.Exec(`
		INSERT INTO ai_asset_analysis (asset_id, capability, state, attempt_count, error_message, updated_at)
		VALUES (?, ?, 'running', 1, '', CURRENT_TIMESTAMP)
		ON CONFLICT(asset_id, capability) DO UPDATE SET
			state = 'running',
			attempt_count = ai_asset_analysis.attempt_count + 1,
			error_message = '',
			updated_at = CURRENT_TIMESTAMP
	`, assetID, capability); err != nil {
		return nil, fmt.Errorf("mark AI analysis running: %w", err)
	}

	if err := tx.Commit(); err != nil {
		return nil, fmt.Errorf("commit AI claim: %w", err)
	}
	return r.GetJob(id)
}

func (r *AIAnalysisRepo) CompleteJob(
	jobID int64,
	engine string,
	modelID string,
	modelVersion string,
	resultJSON string,
) error {
	tx, err := r.db.Begin()
	if err != nil {
		return fmt.Errorf("begin AI completion: %w", err)
	}
	defer tx.Rollback()

	job, err := getAIJobTx(tx, jobID)
	if err != nil {
		return err
	}
	if job == nil {
		return fmt.Errorf("AI job not found: %d", jobID)
	}
	if job.Status != domain.AIJobRunning {
		return nil
	}

	if _, err := tx.Exec(`
		UPDATE ai_jobs
		SET status = 'completed', last_error = '', cancel_requested = 0, finished_at = CURRENT_TIMESTAMP
		WHERE id = ? AND status = 'running'
	`, jobID); err != nil {
		return fmt.Errorf("complete AI job: %w", err)
	}
	if _, err := tx.Exec(`
		INSERT INTO ai_asset_analysis
			(asset_id, capability, state, engine, model_id, model_version, result_json, error_message, attempt_count, analyzed_at, updated_at)
		VALUES (?, ?, 'ready', ?, ?, ?, ?, '', ?, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
		ON CONFLICT(asset_id, capability) DO UPDATE SET
			state = 'ready',
			engine = excluded.engine,
			model_id = excluded.model_id,
			model_version = excluded.model_version,
			result_json = excluded.result_json,
			error_message = '',
			attempt_count = excluded.attempt_count,
			analyzed_at = CURRENT_TIMESTAMP,
			updated_at = CURRENT_TIMESTAMP
	`, job.AssetID, job.Capability, engine, modelID, modelVersion, resultJSON, job.AttemptCount); err != nil {
		return fmt.Errorf("store AI analysis result: %w", err)
	}

	return tx.Commit()
}

func (r *AIAnalysisRepo) FailOrRequeue(jobID int64, message string) (bool, error) {
	tx, err := r.db.Begin()
	if err != nil {
		return false, fmt.Errorf("begin AI failure: %w", err)
	}
	defer tx.Rollback()

	job, err := getAIJobTx(tx, jobID)
	if err != nil {
		return false, err
	}
	if job == nil {
		return false, fmt.Errorf("AI job not found: %d", jobID)
	}
	if job.Status == domain.AIJobCancelled {
		return false, nil
	}
	if job.Status != domain.AIJobRunning {
		return false, nil
	}

	requeue := job.AttemptCount < job.MaxAttempts
	if requeue {
		if _, err := tx.Exec(`
			UPDATE ai_jobs
			SET status = 'queued', last_error = ?, started_at = NULL, finished_at = NULL
			WHERE id = ?
		`, message, jobID); err != nil {
			return false, fmt.Errorf("requeue AI job: %w", err)
		}
		if _, err := tx.Exec(`
			UPDATE ai_asset_analysis
			SET state = 'queued', error_message = ?, updated_at = CURRENT_TIMESTAMP
			WHERE asset_id = ? AND capability = ?
		`, message, job.AssetID, job.Capability); err != nil {
			return false, fmt.Errorf("mark AI analysis requeued: %w", err)
		}
	} else {
		if _, err := tx.Exec(`
			UPDATE ai_jobs
			SET status = 'failed', last_error = ?, finished_at = CURRENT_TIMESTAMP
			WHERE id = ?
		`, message, jobID); err != nil {
			return false, fmt.Errorf("fail AI job: %w", err)
		}
		if _, err := tx.Exec(`
			UPDATE ai_asset_analysis
			SET state = 'failed', error_message = ?, updated_at = CURRENT_TIMESTAMP
			WHERE asset_id = ? AND capability = ?
		`, message, job.AssetID, job.Capability); err != nil {
			return false, fmt.Errorf("mark AI analysis failed: %w", err)
		}
	}

	if err := tx.Commit(); err != nil {
		return false, fmt.Errorf("commit AI failure: %w", err)
	}
	return requeue, nil
}

func (r *AIAnalysisRepo) CancelJob(jobID int64) error {
	tx, err := r.db.Begin()
	if err != nil {
		return fmt.Errorf("begin AI cancellation: %w", err)
	}
	defer tx.Rollback()

	job, err := getAIJobTx(tx, jobID)
	if err != nil {
		return err
	}
	if job == nil {
		return fmt.Errorf("AI job not found: %d", jobID)
	}
	if job.Status != domain.AIJobQueued && job.Status != domain.AIJobRunning {
		return tx.Commit()
	}

	if _, err := tx.Exec(`
		UPDATE ai_jobs
		SET status = 'cancelled', cancel_requested = 1, last_error = 'cancelled', finished_at = CURRENT_TIMESTAMP
		WHERE id = ?
	`, jobID); err != nil {
		return fmt.Errorf("cancel AI job: %w", err)
	}
	if _, err := tx.Exec(`
		UPDATE ai_asset_analysis
		SET state = 'stale', error_message = 'cancelled', updated_at = CURRENT_TIMESTAMP
		WHERE asset_id = ? AND capability = ?
	`, job.AssetID, job.Capability); err != nil {
		return fmt.Errorf("mark cancelled AI analysis stale: %w", err)
	}
	return tx.Commit()
}

func (r *AIAnalysisRepo) RetryJob(jobID int64) error {
	tx, err := r.db.Begin()
	if err != nil {
		return fmt.Errorf("begin AI retry: %w", err)
	}
	defer tx.Rollback()

	job, err := getAIJobTx(tx, jobID)
	if err != nil {
		return err
	}
	if job == nil {
		return fmt.Errorf("AI job not found: %d", jobID)
	}
	if job.Status != domain.AIJobFailed && job.Status != domain.AIJobCancelled {
		return ErrAIJobNotRetryable
	}

	var active int
	if err := tx.QueryRow(
		"SELECT COUNT(*) FROM ai_jobs WHERE asset_id = ? AND capability = ? AND status IN ('queued','running') AND id <> ?",
		job.AssetID, job.Capability, jobID,
	).Scan(&active); err != nil {
		return fmt.Errorf("check active AI retry: %w", err)
	}
	if active > 0 {
		return fmt.Errorf("another AI job is already active for asset %d / %s", job.AssetID, job.Capability)
	}

	if _, err := tx.Exec(`
		UPDATE ai_jobs
		SET status = 'queued', attempt_count = 0, last_error = '', cancel_requested = 0,
			started_at = NULL, finished_at = NULL
		WHERE id = ?
	`, jobID); err != nil {
		return fmt.Errorf("retry AI job: %w", err)
	}
	if _, err := tx.Exec(`
		INSERT INTO ai_asset_analysis (asset_id, capability, state, error_message, updated_at)
		VALUES (?, ?, 'queued', '', CURRENT_TIMESTAMP)
		ON CONFLICT(asset_id, capability) DO UPDATE SET
			state = 'queued', error_message = '', updated_at = CURRENT_TIMESTAMP
	`, job.AssetID, job.Capability); err != nil {
		return fmt.Errorf("mark retried AI analysis queued: %w", err)
	}
	return tx.Commit()
}

func (r *AIAnalysisRepo) RequeueInterrupted(jobID int64, message string) error {
	tx, err := r.db.Begin()
	if err != nil {
		return fmt.Errorf("begin interrupted AI requeue: %w", err)
	}
	defer tx.Rollback()

	job, err := getAIJobTx(tx, jobID)
	if err != nil {
		return err
	}
	if job == nil || job.Status != domain.AIJobRunning {
		return tx.Commit()
	}

	if _, err := tx.Exec(`
		UPDATE ai_jobs
		SET status = 'queued',
			attempt_count = MAX(attempt_count - 1, 0),
			last_error = ?,
			cancel_requested = 0,
			started_at = NULL,
			finished_at = NULL
		WHERE id = ? AND status = 'running'
	`, message, jobID); err != nil {
		return fmt.Errorf("requeue interrupted AI job: %w", err)
	}
	if _, err := tx.Exec(`
		UPDATE ai_asset_analysis
		SET state = 'queued', error_message = ?, updated_at = CURRENT_TIMESTAMP
		WHERE asset_id = ? AND capability = ?
	`, message, job.AssetID, job.Capability); err != nil {
		return fmt.Errorf("mark interrupted AI analysis queued: %w", err)
	}
	return tx.Commit()
}

func (r *AIAnalysisRepo) RecoverInterrupted() (int64, error) {
	tx, err := r.db.Begin()
	if err != nil {
		return 0, fmt.Errorf("begin AI recovery: %w", err)
	}
	defer tx.Rollback()

	result, err := tx.Exec(`
		UPDATE ai_jobs
		SET status = 'queued',
			attempt_count = MAX(attempt_count - 1, 0),
			cancel_requested = 0,
			started_at = NULL,
			finished_at = NULL,
			last_error = CASE WHEN last_error = '' THEN 'recovered after app restart' ELSE last_error END
		WHERE status = 'running'
	`)
	if err != nil {
		return 0, fmt.Errorf("recover interrupted AI jobs: %w", err)
	}
	count, _ := result.RowsAffected()

	if _, err := tx.Exec(`
		UPDATE ai_asset_analysis
		SET state = 'queued',
			error_message = CASE WHEN error_message = '' THEN 'recovered after app restart' ELSE error_message END,
			updated_at = CURRENT_TIMESTAMP
		WHERE state = 'running'
		  AND EXISTS (
			SELECT 1 FROM ai_jobs j
			WHERE j.asset_id = ai_asset_analysis.asset_id
			  AND j.capability = ai_asset_analysis.capability
			  AND j.status = 'queued'
		  )
	`); err != nil {
		return 0, fmt.Errorf("recover interrupted AI analysis: %w", err)
	}

	if err := tx.Commit(); err != nil {
		return 0, fmt.Errorf("commit AI recovery: %w", err)
	}
	return count, nil
}

func (r *AIAnalysisRepo) MarkStaleForAssets(
	capability domain.AICapability,
	assetIDs []int64,
) (int64, error) {
	if len(assetIDs) == 0 {
		return 0, nil
	}
	if len(assetIDs) > 5000 {
		return 0, fmt.Errorf("too many assets to mark stale: %d", len(assetIDs))
	}

	placeholders := make([]string, len(assetIDs))
	args := make([]any, 0, len(assetIDs)+1)
	args = append(args, capability)
	for i, id := range assetIDs {
		placeholders[i] = "?"
		args = append(args, id)
	}
	result, err := r.db.Exec(
		fmt.Sprintf(`
			UPDATE ai_asset_analysis
			SET state = 'stale', error_message = '', updated_at = CURRENT_TIMESTAMP
			WHERE capability = ?
			  AND asset_id IN (%s)
			  AND state = 'ready'
		`, strings.Join(placeholders, ",")),
		args...,
	)
	if err != nil {
		return 0, fmt.Errorf("mark changed asset analysis stale: %w", err)
	}
	count, _ := result.RowsAffected()
	return count, nil
}

func (r *AIAnalysisRepo) MarkStaleForModel(
	capability domain.AICapability,
	engine string,
	modelID string,
	modelVersion string,
) (int64, error) {
	result, err := r.db.Exec(`
		UPDATE ai_asset_analysis
		SET state = 'stale', updated_at = CURRENT_TIMESTAMP
		WHERE capability = ?
		  AND state = 'ready'
		  AND (engine <> ? OR model_id <> ? OR model_version <> ?)
	`, capability, engine, modelID, modelVersion)
	if err != nil {
		return 0, fmt.Errorf("mark AI analysis stale: %w", err)
	}
	count, _ := result.RowsAffected()
	return count, nil
}

func (r *AIAnalysisRepo) GetByAsset(assetID int64) ([]domain.AIAnalysis, error) {
	rows, err := r.db.Query(`
		SELECT id, asset_id, capability, state, engine, model_id, model_version,
		       result_json, error_message, attempt_count, analyzed_at, created_at, updated_at
		FROM ai_asset_analysis
		WHERE asset_id = ?
		ORDER BY capability
	`, assetID)
	if err != nil {
		return nil, fmt.Errorf("list AI analysis for asset: %w", err)
	}
	defer rows.Close()

	var values []domain.AIAnalysis
	for rows.Next() {
		value, err := scanAIAnalysis(rows)
		if err != nil {
			return nil, err
		}
		values = append(values, value)
	}
	return values, rows.Err()
}

func (r *AIAnalysisRepo) GetJob(id int64) (*domain.AIJob, error) {
	row := r.db.QueryRow(`
		SELECT id, asset_id, capability, source, priority, status, attempt_count, max_attempts,
		       last_error, cancel_requested, created_at, started_at, finished_at
		FROM ai_jobs WHERE id = ?
	`, id)
	return scanAIJobRow(row)
}

func (r *AIAnalysisRepo) ListJobs(limit int) ([]domain.AIJob, error) {
	if limit <= 0 {
		limit = 100
	}
	if limit > 1000 {
		limit = 1000
	}
	rows, err := r.db.Query(`
		SELECT id, asset_id, capability, source, priority, status, attempt_count, max_attempts,
		       last_error, cancel_requested, created_at, started_at, finished_at
		FROM ai_jobs ORDER BY id DESC LIMIT ?
	`, limit)
	if err != nil {
		return nil, fmt.Errorf("list AI jobs: %w", err)
	}
	defer rows.Close()

	var jobs []domain.AIJob
	for rows.Next() {
		job, err := scanAIJob(rows)
		if err != nil {
			return nil, err
		}
		jobs = append(jobs, job)
	}
	return jobs, rows.Err()
}

type aiRowScanner interface {
	Scan(dest ...any) error
}

func scanAIAnalysis(row aiRowScanner) (domain.AIAnalysis, error) {
	var value domain.AIAnalysis
	var analyzed sql.NullTime
	if err := row.Scan(
		&value.ID, &value.AssetID, &value.Capability, &value.State,
		&value.Engine, &value.ModelID, &value.ModelVersion, &value.ResultJSON,
		&value.ErrorMessage, &value.AttemptCount, &analyzed, &value.CreatedAt, &value.UpdatedAt,
	); err != nil {
		return domain.AIAnalysis{}, fmt.Errorf("scan AI analysis: %w", err)
	}
	if analyzed.Valid {
		value.AnalyzedAt = &analyzed.Time
	}
	return value, nil
}

func scanAIJobRow(row aiRowScanner) (*domain.AIJob, error) {
	job, err := scanAIJob(row)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	return &job, nil
}

func scanAIJob(row aiRowScanner) (domain.AIJob, error) {
	var job domain.AIJob
	var started, finished sql.NullTime
	if err := row.Scan(
		&job.ID, &job.AssetID, &job.Capability, &job.Source, &job.Priority, &job.Status,
		&job.AttemptCount, &job.MaxAttempts, &job.LastError, &job.CancelRequested,
		&job.CreatedAt, &started, &finished,
	); err != nil {
		return domain.AIJob{}, err
	}
	if started.Valid {
		job.StartedAt = &started.Time
	}
	if finished.Valid {
		job.FinishedAt = &finished.Time
	}
	return job, nil
}

func getAIJobTx(tx *sql.Tx, id int64) (*domain.AIJob, error) {
	row := tx.QueryRow(`
		SELECT id, asset_id, capability, source, priority, status, attempt_count, max_attempts,
		       last_error, cancel_requested, created_at, started_at, finished_at
		FROM ai_jobs WHERE id = ?
	`, id)
	return scanAIJobRow(row)
}
