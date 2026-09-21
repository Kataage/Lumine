package commands

import (
	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
)

// ConfigureSemanticDiagnostics wires the in-memory diagnostics collector into
// commands/index internals without exposing configuration through the Wails
// bridge. The user-facing enable switch remains part of persisted AI settings.
func ConfigureSemanticDiagnostics(c *AppCommands, diagnostics *ai.SemanticDiagnostics) {
	if c == nil {
		return
	}
	c.semanticDiagnostics = diagnostics
	if c.semanticIndex != nil {
		c.semanticIndex.SetDiagnostics(diagnostics)
	}
	if diagnostics == nil {
		return
	}
	settings, err := c.GetAISettings()
	if err == nil {
		diagnostics.SetEnabled(settings.Diagnostics)
	}
}

func (c *AppCommands) GetSemanticPipelineDiagnostics() ai.SemanticPipelineDiagnosticsSnapshot {
	var snapshot ai.SemanticPipelineDiagnosticsSnapshot
	if c.aiJobQueue != nil {
		snapshot = c.aiJobQueue.SemanticDiagnosticsSnapshot()
	} else if c.semanticDiagnostics != nil {
		snapshot = c.semanticDiagnostics.Snapshot()
	}

	if c.aiManager != nil {
		status := c.aiManager.Status(domain.AICapabilitySemanticSearch)
		snapshot.ExecutionProvider = status.ExecutionProvider
		snapshot.AdapterID = status.AdapterID
	}
	return snapshot
}

func (c *AppCommands) ResetSemanticPipelineDiagnostics() ai.SemanticPipelineDiagnosticsSnapshot {
	if c.semanticDiagnostics != nil {
		c.semanticDiagnostics.Reset()
	}
	return c.GetSemanticPipelineDiagnostics()
}
