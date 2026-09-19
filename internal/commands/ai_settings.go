package commands

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"

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

// SetAISettings replaces the complete AI policy atomically.
//
// Prefer PatchAISettings for UI interactions so a stale frontend snapshot
// cannot overwrite unrelated feature switches. Full replacement remains for
// migration/tests and callers that intentionally own the whole settings object.
func (c *AppCommands) SetAISettings(settings domain.AISettings) (domain.AISettings, error) {
	c.aiSettingsMu.Lock()
	defer c.aiSettingsMu.Unlock()
	return c.persistAISettings(settings)
}

// PatchAISettings performs a backend-side read/modify/write under one lock.
// This makes persisted settings authoritative even if the frontend is still
// loading or has an older snapshot.
func (c *AppCommands) PatchAISettings(patch map[string]bool) (domain.AISettings, error) {
	c.aiSettingsMu.Lock()
	defer c.aiSettingsMu.Unlock()

	settings, err := c.GetAISettings()
	if err != nil {
		return domain.AISettings{}, err
	}
	for key, value := range patch {
		switch key {
		case "enabled":
			settings.Enabled = value
		case "semanticSearch":
			settings.SemanticSearch = value
		case "tagger":
			settings.Tagger = value
		case "lightweightVision":
			settings.LightweightVision = value
		case "advancedVision":
			settings.AdvancedVision = value
		case "promptEngine":
			settings.PromptEngine = value
		case "autoAnalyze":
			settings.AutoAnalyze = value
		case "gpuAcceleration":
			settings.GPUAcceleration = value
		default:
			return domain.AISettings{}, fmt.Errorf("unknown AI settings field %q", key)
		}
	}
	return c.persistAISettings(settings)
}

func (c *AppCommands) persistAISettings(settings domain.AISettings) (domain.AISettings, error) {
	value, err := json.Marshal(settings)
	if err != nil {
		return domain.AISettings{}, fmt.Errorf("encode AI settings: %w", err)
	}
	if err := c.settingRepo.Set(aiSettingsKey, string(value)); err != nil {
		return domain.AISettings{}, fmt.Errorf("save AI settings: %w", err)
	}

	if c.aiManager != nil {
		applyContext := c.ctx
		if applyContext == nil {
			applyContext = context.Background()
		}
		if err := c.aiManager.ApplySettings(applyContext, settings); err != nil {
			// The persisted setting is authoritative. Runtime shutdown failures are
			// surfaced through logs/status but must not roll the user's setting back.
			slog.Error("failed to apply AI settings to runtime", "error", err)
		}
	}

	if c.aiJobQueue != nil {
		if err := c.aiJobQueue.ApplySettings(settings); err != nil {
			// The persisted setting remains authoritative. Queue shutdown failures
			// are diagnostic and must not silently restore a disabled feature.
			slog.Error("failed to apply AI settings to job queue", "error", err)
		}
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
