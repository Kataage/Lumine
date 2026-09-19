import { describe, expect, it } from "vitest";
import { formatPromptLoRALines, parseIDList, parsePromptLoRALines, promptVersionLabel } from "./promptStudio";

describe("promptStudio helpers", () => {
  it("round-trips LoRA editing lines", () => {
    const parsed = parsePromptLoRALines("style.safetensors | 0.8 | trigger a, trigger b\ncharacter | 1 | char");
    expect(parsed).toHaveLength(2);
    expect(parsed[0]).toEqual({
      name: "style.safetensors",
      weight: 0.8,
      triggerWords: ["trigger a", "trigger b"],
    });
    expect(formatPromptLoRALines(parsed)).toContain("style.safetensors | 0.8 | trigger a, trigger b");
  });

  it("normalizes asset id lists", () => {
    expect(parseIDList("1, 2 2 x -1 3")).toEqual([1, 2, 3]);
  });

  it("labels descending version history", () => {
    expect(promptVersionLabel(0, 3)).toBe("v3");
    expect(promptVersionLabel(2, 3)).toBe("v1");
  });
});
