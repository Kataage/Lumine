package db

import (
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

type JobLogRepo struct {
	db *DB
}

func NewJobLogRepo(db *DB) *JobLogRepo {
	return &JobLogRepo{db: db}
}

func (r *JobLogRepo) Create(jobType domain.JobType, payloadJSON string) (int64, error) {
	result, err := r.db.Exec(
		"INSERT INTO job_logs (job_type, status, payload_json, started_at) VALUES (?, 'running', ?, CURRENT_TIMESTAMP)",
		jobType, payloadJSON,
	)
	if err != nil {
		return 0, fmt.Errorf("create job log: %w", err)
	}
	return result.LastInsertId()
}

func (r *JobLogRepo) MarkCompleted(id int64, message string) error {
	_, err := r.db.Exec(
		"UPDATE job_logs SET status = 'completed', message = ?, finished_at = CURRENT_TIMESTAMP WHERE id = ?",
		message, id,
	)
	return err
}

func (r *JobLogRepo) MarkFailed(id int64, message string) error {
	_, err := r.db.Exec(
		"UPDATE job_logs SET status = 'failed', message = ?, finished_at = CURRENT_TIMESTAMP WHERE id = ?",
		message, id,
	)
	return err
}

func (r *JobLogRepo) MarkCancelled(id int64) error {
	_, err := r.db.Exec(
		"UPDATE job_logs SET status = 'cancelled', finished_at = CURRENT_TIMESTAMP WHERE id = ?", id,
	)
	return err
}
