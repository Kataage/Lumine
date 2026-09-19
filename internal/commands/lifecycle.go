package commands

import (
	"context"
	"log/slog"
)

func (c *AppCommands) StartAIRestore() bool {
	return c.startBackgroundTask(func(ctx context.Context) {
		restore := []struct {
			name string
			run  func() error
		}{
			{name: "Semantic Search", run: c.RestoreDefaultSemanticModel},
			{name: "Lightweight Vision", run: c.RestoreDefaultLightweightVisionModel},
			{name: "Advanced Vision", run: c.RestoreAdvancedVisionModel},
			{name: "Prompt Engine", run: c.RestorePromptEngineModel},
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
