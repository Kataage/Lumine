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

const promptEngineActiveModelKey = "promptEngineActiveModelV1"

var (
	ErrPromptEngineDisabled      = errors.New("Prompt Engine is disabled")
	ErrPromptEngineModelNotFound = errors.New("Prompt Engine model candidate not found")
)

type PromptEngineCandidateInfo struct {
	ID          string `json:"id"`
	Version     string `json:"version"`
	Engine      string `json:"engine"`
	DisplayName string `json:"displayName"`
	License     string `json:"license"`
	SizeBytes   int64  `json:"sizeBytes"`
	Installed   bool   `json:"installed"`
	Reference   bool   `json:"reference"`
}

type PromptEngineStatusInfo struct {
	Runtime       ai.RuntimeStatus             `json:"runtime"`
	LlamaRuntime  LightweightRuntimeInfo       `json:"llamaRuntime"`
	Models        []PromptEngineCandidateInfo  `json:"models"`
	ActiveModelID string                       `json:"activeModelId,omitempty"`
	SelectionNote string                       `json:"selectionNote"`
}

func (c *AppCommands) GetPromptEngineStatus() PromptEngineStatusInfo {
	runtimeManifest := llamacpp.DefaultRuntimeManifest()
	info := PromptEngineStatusInfo{
		Runtime: ai.RuntimeStatus{
			Capability: domain.AICapabilityPromptEngine,
			State:      ai.RuntimeStateModelNotInstalled,
		},
		LlamaRuntime: LightweightRuntimeInfo{
			ID:           runtimeManifest.ID,
			Version:      runtimeManifest.Version,
			SizeBytes:    runtimeManifest.SizeBytes,
			Platform:     runtimeManifest.Platform,
			Architecture: runtimeManifest.Architecture,
		},
		SelectionNote: "標準Prompt LLMはIssue #169の実測で決定予定です。現在のQwen3.5 4B MはAPI/統合検証用の固定reference candidateです。",
	}
	if c.aiManager != nil {
		info.Runtime = c.aiManager.Status(domain.AICapabilityPromptEngine)
		info.ActiveModelID = info.Runtime.ModelID
	}
	if c.llamaRuntimeStore != nil {
		if installed, err := c.llamaRuntimeStore.Verify(runtimeManifest); err == nil {
			info.LlamaRuntime.Installed = true
			info.LlamaRuntime.ExecutablePath = installed.ExecutablePath
		}
	}
	for _, manifest := range llamacpp.PromptEngineCandidateManifests() {
		candidate := PromptEngineCandidateInfo{
			ID:          manifest.ID,
			Version:     manifest.Version,
			Engine:      manifest.Engine,
			DisplayName: manifest.DisplayName,
			License:     manifest.License,
			SizeBytes:   manifest.SizeBytes,
			Reference:   manifest.ID == llamacpp.PromptReferenceQwen35ModelID,
		}
		if c.aiManager != nil {
			if _, err := c.aiManager.VerifyModel(manifest.ID, manifest.Version); err == nil {
				candidate.Installed = true
			}
		}
		info.Models = append(info.Models, candidate)
	}
	return info
}

func (c *AppCommands) InstallPromptEngineRuntime() (*LightweightRuntimeInfo, error) {
	if c.llamaRuntimeStore == nil {
		return nil, errors.New("llama.cpp runtime store is not available")
	}
	manifest := llamacpp.DefaultRuntimeManifest()
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	installed, err := c.llamaRuntimeStore.Install(ctx, manifest, func(progress llamacpp.RuntimeDownloadProgress) {
		if c.ctx != nil {
			runtime.EventsEmit(c.ctx, "ai:runtime-download", progress)
		}
	})
	if err != nil {
		return nil, err
	}
	return &LightweightRuntimeInfo{
		ID:             installed.ID,
		Version:        installed.Version,
		SizeBytes:      manifest.SizeBytes,
		Installed:      true,
		ExecutablePath: installed.ExecutablePath,
		Platform:       installed.Platform,
		Architecture:   installed.Architecture,
	}, nil
}

func (c *AppCommands) RemovePromptEngineRuntime() error {
	return c.removeSharedLlamaRuntime()
}

func (c *AppCommands) InstallPromptEngineModel(modelID string) (*ai.InstalledModelInfo, error) {
	if c.aiManager == nil {
		return nil, errors.New("AI model manager is not available")
	}
	manifest, ok := promptEngineManifest(modelID)
	if !ok {
		return nil, fmt.Errorf("%w: %s", ErrPromptEngineModelNotFound, modelID)
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

func (c *AppCommands) RemovePromptEngineModel(modelID string) error {
	if c.aiManager == nil {
		return errors.New("AI model manager is not available")
	}
	manifest, ok := promptEngineManifest(modelID)
	if !ok {
		return fmt.Errorf("%w: %s", ErrPromptEngineModelNotFound, modelID)
	}
	status := c.aiManager.Status(domain.AICapabilityPromptEngine)
	if status.ModelID == manifest.ID {
		if err := c.aiManager.Unload(context.Background(), domain.AICapabilityPromptEngine); err != nil {
			return err
		}
	}
	if err := c.aiManager.RemoveModel(manifest.ID, manifest.Version); err != nil {
		return err
	}
	active, _ := c.getPromptEngineActiveModelID()
	if active == manifest.ID {
		_ = c.settingRepo.Set(promptEngineActiveModelKey, "\"\"")
	}
	return nil
}

func (c *AppCommands) LoadPromptEngineModel(modelID string) error {
	if c.aiManager == nil {
		return errors.New("AI model manager is not available")
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityPromptEngine) {
		return ErrPromptEngineDisabled
	}
	if c.llamaRuntimeStore == nil {
		return errors.New("llama.cpp runtime store is not available")
	}
	if _, err := c.llamaRuntimeStore.Verify(llamacpp.DefaultRuntimeManifest()); err != nil {
		return fmt.Errorf("llama.cpp runtime is not installed or valid: %w", err)
	}
	manifest, ok := promptEngineManifest(modelID)
	if !ok {
		return fmt.Errorf("%w: %s", ErrPromptEngineModelNotFound, modelID)
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	if err := c.aiManager.Load(
		ctx,
		domain.AICapabilityPromptEngine,
		manifest.ID,
		manifest.Version,
		ai.LoadOptions{AllowGPU: settings.GPUAcceleration},
	); err != nil {
		return err
	}
	value, _ := json.Marshal(manifest.ID)
	return c.settingRepo.Set(promptEngineActiveModelKey, string(value))
}

// RestorePromptEngineModel never downloads. It restores only the model that the
// user explicitly loaded before, when both the shared runtime and model verify.
func (c *AppCommands) RestorePromptEngineModel() error {
	if c.aiManager == nil || c.llamaRuntimeStore == nil {
		return nil
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityPromptEngine) {
		return nil
	}
	modelID, err := c.getPromptEngineActiveModelID()
	if err != nil || modelID == "" {
		return err
	}
	manifest, ok := promptEngineManifest(modelID)
	if !ok {
		return nil
	}
	runtimeManifest := llamacpp.DefaultRuntimeManifest()
	if _, err := c.llamaRuntimeStore.Verify(runtimeManifest); err != nil {
		metadata := filepath.Join(c.llamaRuntimeStore.Root(), runtimeManifest.ID, runtimeManifest.Version, "runtime.json")
		if _, statErr := os.Stat(metadata); errors.Is(statErr, os.ErrNotExist) {
			return nil
		}
		return err
	}
	if _, err := c.aiManager.VerifyModel(manifest.ID, manifest.Version); err != nil {
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
		domain.AICapabilityPromptEngine,
		manifest.ID,
		manifest.Version,
		ai.LoadOptions{AllowGPU: settings.GPUAcceleration},
	)
}

func (c *AppCommands) getPromptEngineActiveModelID() (string, error) {
	setting, err := c.settingRepo.Get(promptEngineActiveModelKey)
	if err != nil || setting == nil || setting.ValueJSON == "" {
		return "", err
	}
	var modelID string
	if err := json.Unmarshal([]byte(setting.ValueJSON), &modelID); err != nil {
		return "", fmt.Errorf("decode Prompt Engine active model: %w", err)
	}
	return modelID, nil
}

func promptEngineManifest(modelID string) (ai.ModelManifest, bool) {
	for _, manifest := range llamacpp.PromptEngineCandidateManifests() {
		if manifest.ID == modelID {
			return manifest, true
		}
	}
	return ai.ModelManifest{}, false
}
