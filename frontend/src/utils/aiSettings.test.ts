import { describe, expect, it } from "vitest";
import {
  DEFAULT_AI_SETTINGS,
  getInitialAIEngineStatus,
  isAIModelFeatureEffectivelyEnabled,
  normalizeAISettings,
} from "./aiSettings";

describe("AI settings policy", () => {
  it("defaults every AI path to off", () => {
    expect(DEFAULT_AI_SETTINGS).toEqual({
      enabled: false,
      semanticSearch: false,
      tagger: false,
      lightweightVision: false,
      advancedVision: false,
      promptEngine: false,
      autoAnalyze: false,
      gpuAcceleration: false,
    });
  });

  it("lets the global switch override child features", () => {
    const settings = normalizeAISettings({
      semanticSearch: true,
      promptEngine: true,
    });

    expect(isAIModelFeatureEffectivelyEnabled(settings, "semanticSearch")).toBe(false);
    expect(isAIModelFeatureEffectivelyEnabled(settings, "promptEngine")).toBe(false);
    expect(getInitialAIEngineStatus(settings, "semanticSearch")).toBe("disabled");
  });

  it("does not pretend an enabled feature already has a model", () => {
    const settings = normalizeAISettings({
      enabled: true,
      semanticSearch: true,
    });

    expect(isAIModelFeatureEffectivelyEnabled(settings, "semanticSearch")).toBe(true);
    expect(getInitialAIEngineStatus(settings, "semanticSearch")).toBe("model_not_installed");
  });
});
