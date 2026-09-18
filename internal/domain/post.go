package domain

import "time"

type PostTarget struct {
	ID        int64
	Name      string
	Kind      string
	CreatedAt time.Time
}

type PostAccount struct {
	ID               int64
	PostTargetID     int64
	DisplayName      string
	AccountIdentifier string
	IsActive         bool
	CreatedAt        time.Time
	UpdatedAt        time.Time
}

type PostStatus string

const (
	PostStatusDraft     PostStatus = "draft"
	PostStatusScheduled PostStatus = "scheduled"
	PostStatusPublished PostStatus = "published"
	PostStatusFailed    PostStatus = "failed"
	PostStatusOnHold    PostStatus = "on_hold"
)

type Post struct {
	ID          int64
	Title       string
	Body        string
	Hashtags    string
	Status      PostStatus
	ScheduledAt *time.Time
	PublishedAt *time.Time
	CreatedAt   time.Time
	UpdatedAt   time.Time
}

type PostDestination struct {
	ID              int64
	PostID          int64
	PostTargetID    int64
	PostAccountID   int64
	Status          PostStatus
	ScheduledAt     *time.Time
	PublishedAt     *time.Time
	ExternalPostID  string
}

type PostAsset struct {
	PostID    int64
	AssetID   int64
	SortOrder int
}
