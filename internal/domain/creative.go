package domain

import "time"

type Work struct {
	ID           int64
	Title        string
	Description  string
	CoverAssetID *int64
	CreatedAt    time.Time
	UpdatedAt    time.Time
}

type GenerationGroup struct {
	ID             int64
	WorkID         *int64
	Name           string
	Prompt         string
	NegativePrompt string
	ModelName      string
	Sampler        string
	Scheduler      string
	Steps          int
	CFGScale       float64
	WorkflowJSON   string
	Notes          string
	CreatedAt      time.Time
	UpdatedAt      time.Time
}

type AssetRelation struct {
	ID            int64
	ParentAssetID int64
	ChildAssetID  int64
	RelationType  string
	Note          string
	CreatedAt     time.Time
}
