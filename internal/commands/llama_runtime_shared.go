package commands

import (
	"context"
	"errors"
	"fmt"
	"time"

	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/domain"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

const aiLifecycleOperationTimeout = 20 * time.Second

func (c *AppCommands) currentLlamaRuntimeInfo(allowGPU bool) LightweightRuntimeInfo {
	selected := llamacpp.PreferredRuntimeManifest(allowGPU)
	info := LightweightRuntimeInfo{
		ID:           selected.ID,
		Version:      selected.Version,
		Backend:      llamacpp.RuntimeBackend(selected),
		SizeBytes:    selected.SizeBytes,
		Platform:     selected.Platform,
		Architecture: selected.Architecture,
	}
	if allowGPU {
		info.FallbackSizeBytes = llamacpp.CPURuntimeManifest().SizeBytes
	}
	if c.llamaRuntimeStore == nil {
		return info
	}
	if installed, err := c.llamaRuntimeStore.Verify(selected); err == nil {
		info.Installed = true
		info.ExecutablePath = installed.ExecutablePath
	}
	if allowGPU {
		if _, err := c.llamaRuntimeStore.Verify(llamacpp.CPURuntimeManifest()); err == nil {
			info.FallbackInstalled = true
		}
	}
	return info
}

func (c *AppCommands) installSharedLlamaRuntimeBundle(
	ctx context.Context,
	allowGPU bool,
) (*LightweightRuntimeInfo, error) {
	if c.llamaRuntimeStore == nil {
		return nil, errors.New("llama.cpp runtime store is not available")
	}
	// Install CPU first so a deterministic fallback remains available even if
	// the optional GPU runtime download or startup later fails.
	installOrder := []llamacpp.RuntimeManifest{llamacpp.CPURuntimeManifest()}
	if allowGPU {
		installOrder = append(installOrder, llamacpp.VulkanRuntimeManifest())
	}
	for _, manifest := range installOrder {
		if _, err := c.llamaRuntimeStore.Verify(manifest); err == nil {
			continue
		}
		if _, err := c.llamaRuntimeStore.Install(ctx, manifest, func(progress llamacpp.RuntimeDownloadProgress) {
			if c.ctx != nil {
				runtime.EventsEmit(c.ctx, "ai:runtime-download", progress)
			}
		}); err != nil {
			info := c.currentLlamaRuntimeInfo(allowGPU)
			if allowGPU && manifest.ID == llamacpp.VulkanRuntimeManifest().ID && info.FallbackInstalled {
				return &info, fmt.Errorf("CPU fallback is installed but Vulkan runtime installation failed: %w", err)
			}
			return &info, err
		}
	}
	info := c.currentLlamaRuntimeInfo(allowGPU)
	return &info, nil
}

func (c *AppCommands) verifyUsableLlamaRuntime(allowGPU bool) error {
	if c.llamaRuntimeStore == nil {
		return errors.New("llama.cpp runtime store is not available")
	}
	var combined error
	for _, manifest := range llamacpp.RuntimeManifestsForPolicy(allowGPU) {
		if _, err := c.llamaRuntimeStore.Verify(manifest); err == nil {
			return nil
		} else {
			combined = errors.Join(combined, err)
		}
	}
	return combined
}

func (c *AppCommands) aiLifecycleOperationContext() (context.Context, context.CancelFunc) {
	parent := c.ctx
	if parent == nil {
		parent = context.Background()
	}
	return context.WithTimeout(parent, aiLifecycleOperationTimeout)
}

func (c *AppCommands) unloadSharedLlamaCapabilities() error {
	if c.aiManager == nil {
		return nil
	}
	ctx, cancel := c.aiLifecycleOperationContext()
	defer cancel()
	for _, capability := range []domain.AICapability{
		domain.AICapabilityLightweightVision,
		domain.AICapabilityAdvancedVision,
		domain.AICapabilityPromptEngine,
	} {
		if err := c.aiManager.Unload(ctx, capability); err != nil {
			return err
		}
	}
	return nil
}

func (c *AppCommands) removeSharedLlamaRuntime() error {
	if c.llamaRuntimeStore == nil {
		return errors.New("llama.cpp runtime store is not available")
	}
	if err := c.unloadSharedLlamaCapabilities(); err != nil {
		return err
	}
	var combined error
	for _, manifest := range []llamacpp.RuntimeManifest{
		llamacpp.VulkanRuntimeManifest(),
		llamacpp.CPURuntimeManifest(),
	} {
		if err := c.llamaRuntimeStore.Remove(manifest); err != nil {
			combined = errors.Join(combined, err)
		}
	}
	return combined
}
