package domain

import "time"

type AssetNote struct {
	ID        int64
	AssetID   int64
	Content   string
	CreatedAt time.Time
	UpdatedAt time.Time
}
