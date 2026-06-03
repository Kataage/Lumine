package domain

type Folder struct {
	ID         int64
	LibraryID  int64
	Path       string
	ParentPath string
	IsExcluded bool
	CreatedAt  string
	UpdatedAt  string
}
