package commands

import (
	"fmt"
	"time"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
)

type AIHealthSnapshot struct {
	Settings              domain.AISettings   `json:"settings"`
	SettingsPersisted     bool                `json:"settingsPersisted"`
	SettingsUpdatedAt     *time.Time          `json:"settingsUpdatedAt,omitempty"`
	SemanticRuntime       ai.RuntimeStatus    `json:"semanticRuntime"`
	SemanticIndex         SemanticIndexStatus `json:"semanticIndex"`
	Queue                 ai.JobQueueStatus   `json:"queue"`
	ShuttingDown          bool                `json:"shuttingDown"`
	SemanticSearchEnabled bool                `json:"semanticSearchEnabled"`
}

func (c *AppCommands) GetAIHealthSnapshot() (AIHealthSnapshot, error) {
	settings, err := c.GetAISettings()
	if err != nil {
		return AIHealthSnapshot{}, fmt.Errorf("read persisted AI settings: %w", err)
	}

	snapshot := AIHealthSnapshot{
		Settings:              settings,
		ShuttingDown:          c.IsShuttingDown(),
		SemanticSearchEnabled: settings.CapabilityEnabled(domain.AICapabilitySemanticSearch),
		SemanticRuntime: ai.RuntimeStatus{
			Capability: domain.AICapabilitySemanticSearch,
			State:      ai.RuntimeStateModelNotInstalled,
		},
		SemanticIndex: SemanticIndexStatus{State: "unavailable"},
	}

	if raw, rawErr := c.settingRepo.Get(aiSettingsKey); rawErr != nil {
		return AIHealthSnapshot{}, fmt.Errorf("read raw AI settings metadata: %w", rawErr)
	} else if raw != nil {
		snapshot.SettingsPersisted = true
		updatedAt := raw.UpdatedAt
		snapshot.SettingsUpdatedAt = &updatedAt
	}

	if c.aiManager != nil {
		// Use the model-aware status path so an installed-but-idle Semantic model
		// is reported as not_loaded rather than model_not_installed. In-flight
		// Manager.Load calls retain their explicit loading state.
		snapshot.SemanticRuntime = c.GetDefaultSemanticModelInfo().Runtime
	}
	if c.semanticIndex != nil {
		snapshot.SemanticIndex = c.semanticIndex.Status()
	}
	if c.aiJobQueue != nil {
		snapshot.Queue = c.aiJobQueue.Status()
	}

	return snapshot, nil
}
