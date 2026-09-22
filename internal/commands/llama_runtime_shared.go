package commands

import (
	"context"
	"errors"
	"fmt"
	"time"

	"github.com/kataage/lumine/internal/ai"
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
	if installed, err := c.llamaRuntimeStore.Probe(selected); err == nil {
		info.Installed = true
		info.ExecutablePath = installed.ExecutablePath
	}
	if allowGPU {
		if _, err := c.llamaRuntimeStore.Probe(llamacpp.CPURuntimeManifest()); err == nil {
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
	installedGPU := false
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
		if manifest.ID == llamacpp.VulkanRuntimeManifest().ID {
			installedGPU = true
		}
	}

	// A GPU-requested session can already be running on the CPU fallback.
	// Merely installing Vulkan does not change LoadOptions, so Manager.Load's
	// idempotent fast path would keep that CPU session forever. Reload loaded
	// llama capabilities exactly once after a new Vulkan runtime is committed.
	if installedGPU {
		if err := c.reloadSharedLlamaCapabilities(ctx, true); err != nil {
			info := c.currentLlamaRuntimeInfo(allowGPU)
			return &info, fmt.Errorf("Vulkan runtime installed but active llama.cpp runtime reload failed: %w", err)
		}
	}

	info := c.currentLlamaRuntimeInfo(allowGPU)
	return &info, nil
}

func sharedLlamaCapabilities() []domain.AICapability {
	return []domain.AICapability{
		domain.AICapabilityLightweightVision,
		domain.AICapabilityAdvancedVision,
		domain.AICapabilityPromptEngine,
	}
}

func (c *AppCommands) reloadSharedLlamaCapabilities(ctx context.Context, allowGPU bool) error {
	if c.aiManager == nil {
		return nil
	}
	type loadedRuntime struct {
		capability domain.AICapability
		modelID    string
		version    string
	}
	var loaded []loadedRuntime
	for _, capability := range sharedLlamaCapabilities() {
		status := c.aiManager.Status(capability)
		if status.ModelID == "" || status.Version == "" {
			continue
		}
		switch status.State {
		case ai.RuntimeStateReady, ai.RuntimeStateRunning:
			loaded = append(loaded, loadedRuntime{
				capability: capability,
				modelID:    status.ModelID,
				version:    status.Version,
			})
		}
	}

	var combined error
	for _, current := range loaded {
		if err := c.aiManager.Unload(ctx, current.capability); err != nil {
			combined = errors.Join(combined, fmt.Errorf("unload %s for Vulkan activation: %w", current.capability, err))
			continue
		}
		if err := c.aiManager.Load(
			ctx,
			current.capability,
			current.modelID,
			current.version,
			ai.LoadOptions{AllowGPU: allowGPU},
		); err != nil {
			combined = errors.Join(combined, fmt.Errorf("reload %s for Vulkan activation: %w", current.capability, err))
		}
	}
	return combined
}

func (c *AppCommands) reloadSharedLlamaCapabilities(ctx context.Context, allowGPU bool) error {
	if c.aiManager == nil {
		return nil
	}
	type loadedRuntime struct {
		capability domain.AICapability
		modelID    string
		version    string
	}
	var loaded []loadedRuntime
	for _, capability := range sharedLlamaCapabilities() {
		status := c.aiManager.Status(capability)
		if status.ModelID == "" || status.Version == "" {
			continue
		}
		switch status.State {
		case ai.RuntimeStateReady, ai.RuntimeStateRunning:
			loaded = append(loaded, loadedRuntime{
				capability: capability,
				modelID:    status.ModelID,
				version:    status.Version,
			})
		}
	}
	if len(loaded) == 0 {
		return nil
	}

	// Legacy builds could leave several llama.cpp sidecars resident. Collapse
	// that state before activating a newly installed Vulkan runtime. Prefer the
	// first background-capable runtime in the stable capability order and keep
	// the others unloaded until explicitly requested.
	var combined error
	for _, extra := range loaded[1:] {
		if err := c.aiManager.Unload(ctx, extra.capability); err != nil {
			combined = errors.Join(combined, fmt.Errorf("unload extra %s before Vulkan activation: %w", extra.capability, err))
		}
	}
	current := loaded[0]
	if err := c.aiManager.Unload(ctx, current.capability); err != nil {
		return errors.Join(combined, fmt.Errorf("unload %s for Vulkan activation: %w", current.capability, err))
	}
	if err := c.aiManager.Load(
		ctx,
		current.capability,
		current.modelID,
		current.version,
		ai.LoadOptions{AllowGPU: allowGPU},
	); err != nil {
		combined = errors.Join(combined, fmt.Errorf("reload %s for Vulkan activation: %w", current.capability, err))
	}
	return combined
}

func (c *AppCommands) verifyUsableLlamaRuntime(allowGPU bool) error {
	if c.llamaRuntimeStore == nil {
		return errors.New("llama.cpp runtime store is not available")
	}
	var combined error
	for _, manifest := range llamacpp.RuntimeManifestsForPolicy(allowGPU) {
		if _, err := c.llamaRuntimeStore.Probe(manifest); err == nil {
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
	for _, capability := range sharedLlamaCapabilities() {
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
		llamacpp.LegacyCPURuntimeManifest(),
	} {
		if err := c.llamaRuntimeStore.Remove(manifest); err != nil {
			combined = errors.Join(combined, err)
		}
	}
	return combined
}
