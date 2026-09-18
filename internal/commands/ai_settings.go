package commands

import (
	"encoding/json"
	"fmt"

	"github.com/kataage/lumine/internal/domain"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

const aiSettingsKey = "aiSettingsV1"

// GetAISettings returns the complete local-AI policy.
//
// Missing settings intentionally resolve to an all-off configuration so that
// Lumine never downloads or starts AI components merely because the app was
// upgraded to a version that supports them.
func (c *AppCommands) GetAISettings() (domain.AISettings, error) {
	setting, err := c.settingRepo.Get(aiSettingsKey)
	if err != nil {
		return domain.AISettings{}, fmt.Errorf("get AI settings: %w", err)
	}
	if setting == nil || setting.ValueJSON == "" {
		return domain.DefaultAISettings(), nil
	}

	settings := domain.DefaultAISettings()
	if err := json.Unmarshal([]byte(setting.ValueJSON), &settings); err != nil {
		return domain.AISettings{}, fmt.Errorf("decode AI settings: %w", err)
	}
	return settings, nil
}

// SetAISettings persists the complete AI policy atomically.
//
// Enabling a feature here never downloads a model. Model installation is an
// explicit action owned by the Model Manager (#162). The event gives that
// future runtime a restart-free contract for stopping/starting allowed work.
func (c *AppCommands) SetAISettings(settings domain.AISettings) (domain.AISettings, error) {
	value, err := json.Marshal(settings)
	if err != nil {
		return domain.AISettings{}, fmt.Errorf("encode AI settings: %w", err)
	}
	if err := c.settingRepo.Set(aiSettingsKey, string(value)); err != nil {
		return domain.AISettings{}, fmt.Errorf("save AI settings: %w", err)
	}

	if c.ctx != nil {
		runtime.EventsEmit(c.ctx, "ai:settings-changed", settings)
	}
	return settings, nil
}

// IsAICapabilityEnabled is the common policy gate for model loading, workers,
// and job enqueueing. Later AI subsystems should call the same domain policy
// before starting work.
func (c *AppCommands) IsAICapabilityEnabled(capability string) (bool, error) {
	settings, err := c.GetAISettings()
	if err != nil {
		return false, err
	}
	return settings.CapabilityEnabled(domain.AICapability(capability)), nil
}
