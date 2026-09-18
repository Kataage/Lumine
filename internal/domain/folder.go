package domain

import "time"

type Folder struct {
	ID         int64
	LibraryID  int64
	Path       string
	ParentPath string
	IsExcluded bool
	CreatedAt  time.Time
	UpdatedAt  time.Time
}
