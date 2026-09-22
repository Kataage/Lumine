package commands

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/domain"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

const advancedVisionActiveModelKey = "advancedVisionActiveModelV1"

var (
	ErrAdvancedVisionDisabled      = errors.New("Advanced Vision is disabled")
	ErrAdvancedVisionModelNotFound = errors.New("Advanced Vision model candidate not found")
)

type AdvancedVisionCandidateInfo struct {
	ID          string `json:"id"`
	Version     string `json:"version"`
	Engine      string `json:"engine"`
	DisplayName string `json:"displayName"`
	License     string `json:"license"`
	SizeBytes   int64  `json:"sizeBytes"`
	Installed   bool   `json:"installed"`
}

type AdvancedVisionStatusInfo struct {
	Runtime       ai.RuntimeStatus              `json:"runtime"`
	LlamaRuntime  LightweightRuntimeInfo        `json:"llamaRuntime"`
	Models        []AdvancedVisionCandidateInfo `json:"models"`
	ActiveModelID string                        `json:"activeModelId,omitempty"`
}

func (c *AppCommands) GetAdvancedVisionStatus() AdvancedVisionStatusInfo {
	settings, _ := c.GetAISettings()
	info := AdvancedVisionStatusInfo{
		Runtime: ai.RuntimeStatus{
			Capability: domain.AICapabilityAdvancedVision,
			State:      ai.RuntimeStateModelNotInstalled,
		},
		LlamaRuntime: c.currentLlamaRuntimeInfo(settings.GPUAcceleration),
	}

	if c.aiManager != nil {
		info.Runtime = c.aiManager.Status(domain.AICapabilityAdvancedVision)
		info.ActiveModelID = info.Runtime.ModelID
		if info.ActiveModelID == "" {
			if active, err := c.getAdvancedVisionActiveModelID(); err == nil {
				info.ActiveModelID = active
			}
		}
	}
	for _, manifest := range llamacpp.AdvancedVisionCandidateManifests() {
		candidate := AdvancedVisionCandidateInfo{
			ID:          manifest.ID,
			Version:     manifest.Version,
			Engine:      manifest.Engine,
			DisplayName: manifest.DisplayName,
			License:     manifest.License,
			SizeBytes:   manifest.SizeBytes,
		}
		if c.aiManager != nil {
			if _, err := c.aiManager.ProbeModel(manifest.ID, manifest.Version); err == nil {
				candidate.Installed = true
			}
			if manifest.ID == info.ActiveModelID {
				info.Runtime = runtimeStatusForInstalledModel(info.Runtime, manifest, candidate.Installed)
			}
		}
		info.Models = append(info.Models, candidate)
	}
	return info
}

func (c *AppCommands) InstallAdvancedVisionRuntime() (*LightweightRuntimeInfo, error) {
	if c.llamaRuntimeStore == nil {
		return nil, errors.New("llama.cpp runtime store is not available")
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return nil, err
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	return c.installSharedLlamaRuntimeBundle(ctx, settings.GPUAcceleration)
}

func (c *AppCommands) RemoveAdvancedVisionRuntime() error {
	return c.removeSharedLlamaRuntime()
}

func (c *AppCommands) InstallAdvancedVisionModel(modelID string) (*ai.InstalledModelInfo, error) {
	if c.aiManager == nil {
		return nil, errors.New("AI model manager is not available")
	}
	manifest, ok := advancedVisionManifest(modelID)
	if !ok {
		return nil, fmt.Errorf("%w: %s", ErrAdvancedVisionModelNotFound, modelID)
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	installed, err := c.aiManager.InstallModel(ctx, manifest, func(progress ai.DownloadProgress) {
		if c.ctx != nil {
			runtime.EventsEmit(c.ctx, "ai:model-download", progress)
		}
	})
	if err != nil {
		return nil, err
	}
	return installedModelInfo(installed), nil
}

func (c *AppCommands) RemoveAdvancedVisionModel(modelID string) error {
	if c.aiManager == nil {
		return errors.New("AI model manager is not available")
	}
	manifest, ok := advancedVisionManifest(modelID)
	if !ok {
		return fmt.Errorf("%w: %s", ErrAdvancedVisionModelNotFound, modelID)
	}
	ctx, cancel := c.aiLifecycleOperationContext()
	defer cancel()
	status := c.aiManager.Status(domain.AICapabilityAdvancedVision)
	if status.ModelID == manifest.ID {
		if err := c.aiManager.Unload(ctx, domain.AICapabilityAdvancedVision); err != nil {
			return err
		}
	}
	if err := c.aiManager.RemoveModelContext(ctx, manifest.ID, manifest.Version); err != nil {
		return err
	}
	active, _ := c.getAdvancedVisionActiveModelID()
	if active == manifest.ID {
		_ = c.settingRepo.Set(advancedVisionActiveModelKey, "\"\"")
	}
	return nil
}

func (c *AppCommands) LoadAdvancedVisionModel(modelID string) error {
	if c.aiManager == nil {
		return errors.New("AI model manager is not available")
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityAdvancedVision) {
		return ErrAdvancedVisionDisabled
	}
	if c.llamaRuntimeStore == nil {
		return errors.New("llama.cpp runtime store is not available")
	}
	if err := c.verifyUsableLlamaRuntime(settings.GPUAcceleration); err != nil {
		return fmt.Errorf("llama.cpp runtime is not installed or valid: %w", err)
	}
	manifest, ok := advancedVisionManifest(modelID)
	if !ok {
		return fmt.Errorf("%w: %s", ErrAdvancedVisionModelNotFound, modelID)
	}

	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	if err := c.aiManager.Load(
		ctx,
		domain.AICapabilityAdvancedVision,
		manifest.ID,
		manifest.Version,
		ai.LoadOptions{AllowGPU: settings.GPUAcceleration},
	); err != nil {
		return err
	}
	value, _ := json.Marshal(manifest.ID)
	return c.settingRepo.Set(advancedVisionActiveModelKey, string(value))
}

// RestoreAdvancedVisionModel never downloads. It only restores the model that
// the user explicitly loaded before, when both runtime and model still verify.
func (c *AppCommands) RestoreAdvancedVisionModel() error {
	if c.aiManager == nil || c.llamaRuntimeStore == nil {
		return nil
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityAdvancedVision) {
		return nil
	}
	modelID, err := c.getAdvancedVisionActiveModelID()
	if err != nil || modelID == "" {
		return err
	}
	manifest, ok := advancedVisionManifest(modelID)
	if !ok {
		return nil
	}
	if err := c.verifyUsableLlamaRuntime(settings.GPUAcceleration); err != nil {
		missing := true
		for _, runtimeManifest := range llamacpp.RuntimeManifestsForPolicy(settings.GPUAcceleration) {
			metadata := filepath.Join(c.llamaRuntimeStore.Root(), runtimeManifest.ID, runtimeManifest.Version, "runtime.json")
			if _, statErr := os.Stat(metadata); statErr == nil {
				missing = false
				break
			} else if !errors.Is(statErr, os.ErrNotExist) {
				return statErr
			}
		}
		if missing {
			return nil
		}
		return err
	}
	if _, err := c.aiManager.ProbeModel(manifest.ID, manifest.Version); err != nil {
		metadata := filepath.Join(c.aiManager.Store().Root(), manifest.ID, manifest.Version, "manifest.json")
		if _, statErr := os.Stat(metadata); errors.Is(statErr, os.ErrNotExist) {
			return nil
		}
		return err
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	return c.aiManager.Load(
		ctx,
		domain.AICapabilityAdvancedVision,
		manifest.ID,
		manifest.Version,
		ai.LoadOptions{AllowGPU: settings.GPUAcceleration},
	)
}

func (c *AppCommands) getAdvancedVisionActiveModelID() (string, error) {
	setting, err := c.settingRepo.Get(advancedVisionActiveModelKey)
	if err != nil || setting == nil || setting.ValueJSON == "" {
		return "", err
	}
	var modelID string
	if err := json.Unmarshal([]byte(setting.ValueJSON), &modelID); err != nil {
		return "", fmt.Errorf("decode Advanced Vision active model: %w", err)
	}
	return modelID, nil
}

func advancedVisionManifest(modelID string) (ai.ModelManifest, bool) {
	for _, manifest := range llamacpp.AdvancedVisionCandidateManifests() {
		if manifest.ID == modelID {
			return manifest, true
		}
	}
	return ai.ModelManifest{}, false
}

