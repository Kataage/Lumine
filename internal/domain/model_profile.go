package domain

import "time"

// ModelProfile is a replaceable knowledge layer describing how a target image
// model prefers prompts to be written. Built-in profiles and user profiles use
// the same shape so the Prompt Engine never needs model-family-specific logic.
type ModelProfile struct {
	ID                   string
	Name                 string
	Family               string
	CheckpointName       string
	PromptStyle          string
	QualityTags          []string
	NegativePromptPolicy string
	TagOrder             []string
	TriggerWords         []string
	LoRATriggerSyntax    string
	WeightSyntax         string
	SystemGuidance       string
	Notes                string
	BuiltIn              bool
	CreatedAt            time.Time
	UpdatedAt            time.Time
}
