package commands

import (
	"fmt"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
)

var modelBackedAICapabilities = []domain.AICapability{
	domain.AICapabilitySemanticSearch,
	domain.AICapabilityTagger,
	domain.AICapabilityLightweightVision,
	domain.AICapabilityAdvancedVision,
	domain.AICapabilityPromptEngine,
}

func (c *AppCommands) SetAIManager(manager *ai.Manager) {
	c.aiManager = manager
}

func (c *AppCommands) GetAIRuntimeStatuses() []ai.RuntimeStatus {
	statuses := make([]ai.RuntimeStatus, 0, len(modelBackedAICapabilities))
	for _, capability := range modelBackedAICapabilities {
		if c.aiManager == nil {
			enabled, err := c.IsAICapabilityEnabled(string(capability))
			if err != nil {
				statuses = append(statuses, ai.RuntimeStatus{
					Capability: capability,
					State:      ai.RuntimeStateError,
					Error:      err.Error(),
				})
				continue
			}
			state := ai.RuntimeStateDisabled
			if enabled {
				state = ai.RuntimeStateModelNotInstalled
			}
			statuses = append(statuses, ai.RuntimeStatus{Capability: capability, State: state})
			continue
		}
		statuses = append(statuses, c.aiManager.Status(capability))
	}
	return statuses
}

func (c *AppCommands) ListInstalledAIModels() ([]ai.InstalledModelInfo, error) {
	if c.aiManager == nil {
		return []ai.InstalledModelInfo{}, nil
	}
	return c.aiManager.ListInstalledModels()
}

func (c *AppCommands) VerifyAIModel(modelID, version string) (*ai.InstalledModelInfo, error) {
	if c.aiManager == nil {
		return nil, fmt.Errorf("AI model manager is not available")
	}
	installed, err := c.aiManager.VerifyModel(modelID, version)
	if err != nil {
		return nil, err
	}
	info := &ai.InstalledModelInfo{
		ID:          installed.Manifest.ID,
		Version:     installed.Manifest.Version,
		Engine:      installed.Manifest.Engine,
		DisplayName: installed.Manifest.DisplayName,
		License:     installed.Manifest.License,
		SizeBytes:   installed.Manifest.SizeBytes,
		RootDir:     installed.RootDir,
	}
	return info, nil
}

func (c *AppCommands) RemoveAIModel(modelID, version string) error {
	if c.aiManager == nil {
		return fmt.Errorf("AI model manager is not available")
	}
	ctx, cancel := c.aiLifecycleOperationContext()
	defer cancel()
	return c.aiManager.RemoveModelContext(ctx, modelID, version)
}

func (c *AppCommands) GetAIModelCachePath() string {
	if c.aiManager == nil {
		return ""
	}
	return c.aiManager.Store().Root()
}
