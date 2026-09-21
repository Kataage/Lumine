package domain

import "testing"

func TestDefaultAISettingsOptOutOfAllAI(t *testing.T) {
	settings := DefaultAISettings()

	if settings.Enabled ||
		settings.SemanticSearch ||
		settings.Tagger ||
		settings.LightweightVision ||
		settings.AdvancedVision ||
		settings.PromptEngine ||
		settings.AutoAnalyze ||
		settings.GPUAcceleration ||
		settings.Diagnostics {
		t.Fatalf("fresh installs must not enable AI by default: %+v", settings)
	}
}

func TestAISettingsGlobalSwitchIsAuthoritative(t *testing.T) {
	settings := AISettings{
		Enabled:             false,
		SemanticSearch:      true,
		Tagger:              true,
		LightweightVision:   true,
		AdvancedVision:      true,
		PromptEngine:        true,
		AutoAnalyze:         true,
		GPUAcceleration:     true,
	}

	for _, capability := range []AICapability{
		AICapabilitySemanticSearch,
		AICapabilityTagger,
		AICapabilityLightweightVision,
		AICapabilityAdvancedVision,
		AICapabilityPromptEngine,
		AICapabilityAutoAnalyze,
	} {
		if settings.CapabilityEnabled(capability) {
			t.Fatalf("%s should be disabled while global AI is off", capability)
		}
	}

	settings.Enabled = true
	for _, capability := range []AICapability{
		AICapabilitySemanticSearch,
		AICapabilityTagger,
		AICapabilityLightweightVision,
		AICapabilityAdvancedVision,
		AICapabilityPromptEngine,
		AICapabilityAutoAnalyze,
	} {
		if !settings.CapabilityEnabled(capability) {
			t.Fatalf("%s should be enabled when both switches are on", capability)
		}
	}
}

func TestAISettingsUnknownCapabilityIsDisabled(t *testing.T) {
	settings := AISettings{Enabled: true}
	if settings.CapabilityEnabled(AICapability("unknown")) {
		t.Fatal("unknown capability must fail closed")
	}
}
