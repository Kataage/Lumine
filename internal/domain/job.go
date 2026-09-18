package domain

import "time"

type JobType  string
type JobStatus string

const (
	JobTypeScan      JobType  = "scan"
	JobTypeThumbnail JobType  = "thumbnail"
	JobTypeMove      JobType  = "move"
	JobTypeHash      JobType  = "hash"

	JobStatusPending   JobStatus = "pending"
	JobStatusRunning   JobStatus = "running"
	JobStatusCompleted JobStatus = "completed"
	JobStatusFailed    JobStatus = "failed"
	JobStatusCancelled JobStatus = "cancelled"
)

type JobLog struct {
	ID          int64
	JobType     JobType
	Status      JobStatus
	Message     string
	PayloadJSON string
	StartedAt   *time.Time
	FinishedAt  *time.Time
}
