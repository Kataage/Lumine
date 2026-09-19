package commands

import (
	"context"
	"errors"

	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/domain"
)

func (c *AppCommands) unloadSharedLlamaCapabilities() error {
	if c.aiManager == nil {
		return nil
	}
	for _, capability := range []domain.AICapability{
		domain.AICapabilityLightweightVision,
		domain.AICapabilityAdvancedVision,
		domain.AICapabilityPromptEngine,
	} {
		if err := c.aiManager.Unload(context.Background(), capability); err != nil {
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
