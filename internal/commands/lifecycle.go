package commands

import (
	"context"
	"log/slog"
)

func (c *AppCommands) StartAIRestore() bool {
	return c.startBackgroundTask(func(ctx context.Context) {
		if c.aiJobQueue != nil {
			defer c.aiJobQueue.SetStartupHold(false)
		}
		// Only background-analysis runtimes are eligible for automatic startup
		// restore. Advanced Vision and Prompt Engine are foreground tools backed
		// by separate llama.cpp sidecars; eagerly restoring all previously loaded
		// foreground models can leave several full-GPU processes resident at once.
		// Keep those capabilities enabled but unloaded until the user actually
		// invokes them.
		restore := []struct {
			name string
			run  func() error
		}{
			{name: "Semantic Search", run: c.RestoreDefaultSemanticModel},
			{name: "Lightweight Vision", run: c.RestoreDefaultLightweightVisionModel},
		}

		for _, item := range restore {
			if ctx.Err() != nil {
				return
			}
			if err := item.run(); err != nil && ctx.Err() == nil {
				slog.Warn("failed to restore AI runtime", "capability", item.name, "error", err)
			}
		}
	})
}
