package domain

import "time"

type Library struct {
	ID           int64
	Name         string
	RootPath     string
	IsEnabled    bool
	CreatedAt    time.Time
	UpdatedAt    time.Time
	LastScannedAt *time.Time
}
