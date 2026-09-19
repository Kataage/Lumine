package commands

import (
	"context"
	"errors"
	"time"

	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/domain"
)

const aiLifecycleOperationTimeout = 20 * time.Second

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
	return c.llamaRuntimeStore.Remove(llamacpp.DefaultRuntimeManifest())
}
