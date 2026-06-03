package domain

import "time"

type Tag struct {
	ID        int64
	Name      string
	Color     string
	CreatedAt time.Time
}

type AssetTag struct {
	AssetID int64
	TagID   int64
}
