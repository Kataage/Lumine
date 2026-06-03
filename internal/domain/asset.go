package domain

import "time"

type ThumbStatus string

const (
	ThumbStatusNone   ThumbStatus = "none"
	ThumbStatusQueued ThumbStatus = "queued"
	ThumbStatusReady  ThumbStatus = "ready"
	ThumbStatusFailed ThumbStatus = "failed"
)

type StatusLabel string

const (
	StatusUnsorted   StatusLabel = "unsorted"
	StatusReviewed   StatusLabel = "reviewed"
	StatusCandidate  StatusLabel = "candidate"
	StatusPublished  StatusLabel = "published"
)

type Asset struct {
	ID            int64
	LibraryID     int64
	FolderPath    string
	FileName      string
	FilePath      string
	Extension     string
	FileSize      int64
	CreatedAtFS   time.Time
	ModifiedAtFS  time.Time
	Width         int
	Height        int
	MimeType      string
	HashBlake3    string
	ThumbStatus   ThumbStatus
	Rating        int
	StatusLabel   StatusLabel
	IsFavorite    bool
	ColorLabel    string
	IndexedAt     time.Time
	UpdatedAt     time.Time
}
