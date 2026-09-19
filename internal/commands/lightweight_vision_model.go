package commands

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/domain"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

var ErrLightweightVisionDisabled = errors.New("Lightweight Vision is disabled")

type LightweightVisionModelInfo struct {
	ID               string                    `json:"id"`
	Version          string                    `json:"version"`
	Engine           string                    `json:"engine"`
	DisplayName      string                    `json:"displayName"`
	License          string                    `json:"license"`
	SizeBytes        int64                     `json:"sizeBytes"`
	Installed        bool                      `json:"installed"`
	Runtime          ai.RuntimeStatus          `json:"runtime"`
	LlamaRuntime     LightweightRuntimeInfo    `json:"llamaRuntime"`
}

type LightweightRuntimeInfo struct {
	ID               string `json:"id"`
	Version          string `json:"version"`
	SizeBytes        int64  `json:"sizeBytes"`
	Installed        bool   `json:"installed"`
	ExecutablePath   string `json:"executablePath,omitempty"`
	Platform         string `json:"platform"`
	Architecture     string `json:"architecture"`
}

func (c *AppCommands) SetLlamaRuntimeStore(store *llamacpp.RuntimeStore) {
	c.llamaRuntimeStore = store
}

func (c *AppCommands) GetDefaultLightweightVisionModelInfo() LightweightVisionModelInfo {
	manifest := llamacpp.DefaultVisionModelManifest()
	runtimeManifest := llamacpp.DefaultRuntimeManifest()
	info := LightweightVisionModelInfo{
		ID:          manifest.ID,
		Version:     manifest.Version,
		Engine:      manifest.Engine,
		DisplayName: manifest.DisplayName,
		License:     manifest.License,
		SizeBytes:   manifest.SizeBytes,
		Runtime: ai.RuntimeStatus{
			Capability: domain.AICapabilityLightweightVision,
			State:      ai.RuntimeStateModelNotInstalled,
		},
		LlamaRuntime: LightweightRuntimeInfo{
			ID:           runtimeManifest.ID,
			Version:      runtimeManifest.Version,
			SizeBytes:    runtimeManifest.SizeBytes,
			Platform:     runtimeManifest.Platform,
			Architecture: runtimeManifest.Architecture,
		},
	}
	if c.aiManager != nil {
		if _, err := c.aiManager.VerifyModel(manifest.ID, manifest.Version); err == nil {
			info.Installed = true
		}
		info.Runtime = c.aiManager.Status(domain.AICapabilityLightweightVision)
	}
	if c.llamaRuntimeStore != nil {
		if installed, err := c.llamaRuntimeStore.Verify(runtimeManifest); err == nil {
			info.LlamaRuntime.Installed = true
			info.LlamaRuntime.ExecutablePath = installed.ExecutablePath
		}
	}
	return info
}

func (c *AppCommands) InstallLightweightVisionRuntime() (*LightweightRuntimeInfo, error) {
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

	info := &LightweightRuntimeInfo{
		ID:             installed.ID,
		Version:        installed.Version,
		SizeBytes:      manifest.SizeBytes,
		Installed:      true,
		ExecutablePath: installed.ExecutablePath,
		Platform:       installed.Platform,
		Architecture:   installed.Architecture,
	}
	if err := c.tryLoadLightweightVisionAfterInstall(ctx); err != nil {
		return info, fmt.Errorf("runtime installed but VLM load failed: %w", err)
	}
	return info, nil
}

func (c *AppCommands) RemoveLightweightVisionRuntime() error {
	return c.removeSharedLlamaRuntime()
}

func (c *AppCommands) InstallDefaultLightweightVisionModel() (*ai.InstalledModelInfo, error) {
	if c.aiManager == nil {
		return nil, errors.New("AI model manager is not available")
	}
	manifest := llamacpp.DefaultVisionModelManifest()
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
	info := installedModelInfo(installed)
	if err := c.tryLoadLightweightVisionAfterInstall(ctx); err != nil {
		return info, fmt.Errorf("model installed but VLM load failed: %w", err)
	}
	return info, nil
}

func (c *AppCommands) RemoveDefaultLightweightVisionModel() error {
	if c.aiManager == nil {
		return errors.New("AI model manager is not available")
	}
	manifest := llamacpp.DefaultVisionModelManifest()
	ctx, cancel := c.aiLifecycleOperationContext()
	defer cancel()
	if err := c.aiManager.Unload(ctx, domain.AICapabilityLightweightVision); err != nil {
		return err
	}
	return c.aiManager.RemoveModelContext(ctx, manifest.ID, manifest.Version)
}

func (c *AppCommands) LoadDefaultLightweightVisionModel() error {
	if c.aiManager == nil {
		return errors.New("AI model manager is not available")
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityLightweightVision) {
		return ErrLightweightVisionDisabled
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	return c.loadDefaultLightweightVisionModel(ctx)
}

// RestoreDefaultLightweightVisionModel only restores already-installed assets.
// It never downloads either the llama.cpp runtime or the model.
func (c *AppCommands) RestoreDefaultLightweightVisionModel() error {
	if c.aiManager == nil || c.llamaRuntimeStore == nil {
		return nil
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityLightweightVision) {
		return nil
	}
	runtimeManifest := llamacpp.DefaultRuntimeManifest()
	if _, err := c.llamaRuntimeStore.Verify(runtimeManifest); err != nil {
		metadata := filepath.Join(
			c.llamaRuntimeStore.Root(),
			runtimeManifest.ID,
			runtimeManifest.Version,
			"runtime.json",
		)
		if _, statErr := os.Stat(metadata); errors.Is(statErr, os.ErrNotExist) {
			return nil
		}
		return err
	}

	modelManifest := llamacpp.DefaultVisionModelManifest()
	if _, err := c.aiManager.VerifyModel(modelManifest.ID, modelManifest.Version); err != nil {
		metadata := filepath.Join(
			c.aiManager.Store().Root(),
			modelManifest.ID,
			modelManifest.Version,
			"manifest.json",
		)
		if _, statErr := os.Stat(metadata); errors.Is(statErr, os.ErrNotExist) {
			return nil
		}
		return err
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	return c.loadDefaultLightweightVisionModel(ctx)
}

func (c *AppCommands) tryLoadLightweightVisionAfterInstall(ctx context.Context) error {
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityLightweightVision) {
		return nil
	}
	if c.llamaRuntimeStore == nil || c.aiManager == nil {
		return nil
	}
	if _, err := c.llamaRuntimeStore.Verify(llamacpp.DefaultRuntimeManifest()); err != nil {
		return nil
	}
	manifest := llamacpp.DefaultVisionModelManifest()
	if _, err := c.aiManager.VerifyModel(manifest.ID, manifest.Version); err != nil {
		return nil
	}
	return c.loadDefaultLightweightVisionModel(ctx)
}

func (c *AppCommands) loadDefaultLightweightVisionModel(ctx context.Context) error {
	if c.llamaRuntimeStore == nil {
		return errors.New("llama.cpp runtime store is not available")
	}
	if _, err := c.llamaRuntimeStore.Verify(llamacpp.DefaultRuntimeManifest()); err != nil {
		return fmt.Errorf("llama.cpp runtime is not installed or valid: %w", err)
	}
	manifest := llamacpp.DefaultVisionModelManifest()
	return c.aiManager.Load(
		ctx,
		domain.AICapabilityLightweightVision,
		manifest.ID,
		manifest.Version,
		ai.LoadOptions{AllowGPU: false},
	)
}
