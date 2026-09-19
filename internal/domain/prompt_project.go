package domain

import "time"

type PromptLoRA struct {
	Name         string   `json:"name"`
	Weight       float64  `json:"weight"`
	TriggerWords []string `json:"triggerWords"`
}

type PromptProject struct {
	SchemaVersion int
	ID              int64
	Title           string
	Idea            string
	Notes           string
	TargetProfileID string
	Characters      []string
	LoRAs           []PromptLoRA
	DeletedAt       *time.Time
	CreatedAt       time.Time
	UpdatedAt       time.Time
}

type PromptVariant struct {
	ID        int64
	ProjectID int64
	Name      string
	CreatedAt time.Time
}

type PromptVersion struct {
	SchemaVersion       int
	ID                  int64
	VariantID           int64
	ParentVersionID     *int64
	Positive            string
	Negative            string
	Source              string
	ChangeInstruction   string
	ProfileID           string
	ProfileSnapshotJSON string
	AIEngine            string
	AIModelID           string
	AIModelVersion      string
	MetadataJSON        string
	CreatedAt           time.Time
}
