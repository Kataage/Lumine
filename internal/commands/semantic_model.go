package commands

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/siglip2"
	"github.com/kataage/lumine/internal/domain"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

type SemanticModelInfo struct {
	ID          string           `json:"id"`
	Version     string           `json:"version"`
	Engine      string           `json:"engine"`
	DisplayName string           `json:"displayName"`
	License     string           `json:"license"`
	SizeBytes   int64            `json:"sizeBytes"`
	Installed   bool             `json:"installed"`
	Runtime     ai.RuntimeStatus `json:"runtime"`
}

func (c *AppCommands) GetDefaultSemanticModelInfo() SemanticModelInfo {
	manifest := siglip2.DefaultManifest()
	info := SemanticModelInfo{
		ID:          manifest.ID,
		Version:     manifest.Version,
		Engine:      manifest.Engine,
		DisplayName: manifest.DisplayName,
		License:     manifest.License,
		SizeBytes:   manifest.SizeBytes,
		Runtime: ai.RuntimeStatus{
			Capability: domain.AICapabilitySemanticSearch,
			State:      ai.RuntimeStateModelNotInstalled,
		},
	}
	if c.aiManager == nil {
		return info
	}
	if _, err := c.aiManager.VerifyModel(manifest.ID, manifest.Version); err == nil {
		info.Installed = true
	}
	info.Runtime = c.aiManager.Status(domain.AICapabilitySemanticSearch)
	return info
}

func (c *AppCommands) InstallDefaultSemanticModel() (*ai.InstalledModelInfo, error) {
	if c.aiManager == nil {
		return nil, errors.New("AI model manager is not available")
	}
	manifest := siglip2.DefaultManifest()
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

	settings, settingsErr := c.GetAISettings()
	if settingsErr == nil && settings.CapabilityEnabled(domain.AICapabilitySemanticSearch) {
		if err := c.loadDefaultSemanticModel(ctx, settings); err != nil {
			return nil, fmt.Errorf("model installed but runtime load failed: %w", err)
		}
	}

	return installedModelInfo(installed), nil
}

func (c *AppCommands) LoadDefaultSemanticModel() error {
	if c.aiManager == nil {
		return errors.New("AI model manager is not available")
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilitySemanticSearch) {
		return ErrSemanticSearchDisabled
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	return c.loadDefaultSemanticModel(ctx, settings)
}

// RestoreDefaultSemanticModel is used at startup. It never downloads anything:
// if the pinned model is absent, Lumine stays in model_not_installed state.
func (c *AppCommands) RestoreDefaultSemanticModel() error {
	if c.aiManager == nil {
		return nil
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilitySemanticSearch) {
		return nil
	}
	manifest := siglip2.DefaultManifest()
	if _, err := c.aiManager.VerifyModel(manifest.ID, manifest.Version); err != nil {
		manifestPath := filepath.Join(
			c.aiManager.Store().Root(),
			manifest.ID,
			manifest.Version,
			"manifest.json",
		)
		if _, statErr := os.Stat(manifestPath); errors.Is(statErr, os.ErrNotExist) {
			return nil
		}
		return err
	}
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	return c.loadDefaultSemanticModel(ctx, settings)
}

func (c *AppCommands) loadDefaultSemanticModel(ctx context.Context, settings domain.AISettings) error {
	manifest := siglip2.DefaultManifest()
	if err := c.aiManager.Load(
		ctx,
		domain.AICapabilitySemanticSearch,
		manifest.ID,
		manifest.Version,
		ai.LoadOptions{AllowGPU: settings.GPUAcceleration},
	); err != nil {
		return err
	}
	_, err := c.EnqueueSemanticBackfill()
	return err
}

func installedModelInfo(installed ai.InstalledModel) *ai.InstalledModelInfo {
	return &ai.InstalledModelInfo{
		ID:          installed.Manifest.ID,
		Version:     installed.Manifest.Version,
		Engine:      installed.Manifest.Engine,
		DisplayName: installed.Manifest.DisplayName,
		License:     installed.Manifest.License,
		SizeBytes:   installed.Manifest.SizeBytes,
		RootDir:     installed.RootDir,
	}
}
