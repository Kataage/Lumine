package domain

import "time"

type AppSetting struct {
	Key       string
	ValueJSON string
	UpdatedAt time.Time
}
