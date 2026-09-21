package domain

// AICapability identifies a user-controllable local AI feature.
//
// Keep these values stable: the AI runtime and job scheduler introduced by
// later issues use the same names when checking whether work is permitted.
type AICapability string

const (
	AICapabilitySemanticSearch    AICapability = "semantic_search"
	AICapabilityTagger            AICapability = "tagger"
	AICapabilityLightweightVision AICapability = "lightweight_vision"
	AICapabilityAdvancedVision    AICapability = "advanced_vision"
	AICapabilityPromptEngine      AICapability = "prompt_engine"
	AICapabilityAutoAnalyze       AICapability = "auto_analyze"
)

// AISettings is the persisted user policy for all local AI features.
//
// Zero values are deliberately safe: a fresh install performs no AI work,
// downloads no models, and starts no AI runtime until the user opts in.
type AISettings struct {
	Enabled             bool `json:"enabled"`
	SemanticSearch      bool `json:"semanticSearch"`
	Tagger              bool `json:"tagger"`
	LightweightVision   bool `json:"lightweightVision"`
	AdvancedVision      bool `json:"advancedVision"`
	PromptEngine        bool `json:"promptEngine"`
	AutoAnalyze         bool `json:"autoAnalyze"`
	GPUAcceleration     bool `json:"gpuAcceleration"`
	Diagnostics         bool `json:"diagnostics"`
}

// DefaultAISettings returns the opt-in defaults used when no settings have
// been persisted yet.
func DefaultAISettings() AISettings {
	return AISettings{}
}

// CapabilityEnabled returns the effective state of a feature.
//
// The global switch is authoritative: when it is off every model-backed
// feature and automatic analysis path is disabled regardless of the stored
// per-feature switches. Future runtime/model/job code should use this policy
// rather than checking individual booleans directly.
func (s AISettings) CapabilityEnabled(capability AICapability) bool {
	if !s.Enabled {
		return false
	}

	switch capability {
	case AICapabilitySemanticSearch:
		return s.SemanticSearch
	case AICapabilityTagger:
		return s.Tagger
	case AICapabilityLightweightVision:
		return s.LightweightVision
	case AICapabilityAdvancedVision:
		return s.AdvancedVision
	case AICapabilityPromptEngine:
		return s.PromptEngine
	case AICapabilityAutoAnalyze:
		return s.AutoAnalyze
	default:
		return false
	}
}

func IsModelBackedAICapability(capability AICapability) bool {
	switch capability {
	case AICapabilitySemanticSearch,
		AICapabilityTagger,
		AICapabilityLightweightVision,
		AICapabilityAdvancedVision,
		AICapabilityPromptEngine:
		return true
	default:
		return false
	}
}
