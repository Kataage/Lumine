export interface AISettings {
  enabled: boolean;
  semanticSearch: boolean;
  tagger: boolean;
  lightweightVision: boolean;
  advancedVision: boolean;
  promptEngine: boolean;
  autoAnalyze: boolean;
  gpuAcceleration: boolean;
}

export type AIModelFeatureKey =
  | "semanticSearch"
  | "tagger"
  | "lightweightVision"
  | "advancedVision"
  | "promptEngine";

export type AIEngineStatus =
  | "disabled"
  | "model_not_installed"
  | "ready"
  | "running"
  | "error";

export const DEFAULT_AI_SETTINGS: AISettings = {
  enabled: false,
  semanticSearch: false,
  tagger: false,
  lightweightVision: false,
  advancedVision: false,
  promptEngine: false,
  autoAnalyze: false,
  gpuAcceleration: false,
};

export function normalizeAISettings(value: Partial<AISettings> | null | undefined): AISettings {
  if (!value) return { ...DEFAULT_AI_SETTINGS };
  return {
    enabled: value.enabled === true,
    semanticSearch: value.semanticSearch === true,
    tagger: value.tagger === true,
    lightweightVision: value.lightweightVision === true,
    advancedVision: value.advancedVision === true,
    promptEngine: value.promptEngine === true,
    autoAnalyze: value.autoAnalyze === true,
    gpuAcceleration: value.gpuAcceleration === true,
  };
}

export function isAIModelFeatureEffectivelyEnabled(
  settings: AISettings,
  feature: AIModelFeatureKey,
): boolean {
  return settings.enabled && settings[feature];
}

// Until #162 provides live model-manager state, an enabled model-backed
// feature is explicitly shown as "model not installed" rather than implying
// that a runtime/model is already available.
export function getInitialAIEngineStatus(
  settings: AISettings,
  feature: AIModelFeatureKey,
): AIEngineStatus {
  return isAIModelFeatureEffectivelyEnabled(settings, feature)
    ? "model_not_installed"
    : "disabled";
}
