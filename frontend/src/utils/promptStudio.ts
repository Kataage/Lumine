import type { PromptProjectLoRA } from "../api/client";

export function parsePromptLoRALines(value: string): PromptProjectLoRA[] {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const [rawName = "", rawWeight = "", rawTriggers = ""] = line.split("|").map((part) => part.trim());
      const parsedWeight = Number(rawWeight);
      return {
        name: rawName,
        weight: Number.isFinite(parsedWeight) && rawWeight !== "" ? parsedWeight : 1,
        triggerWords: rawTriggers.split(",").map((word) => word.trim()).filter(Boolean),
      };
    })
    .filter((item) => item.name.length > 0);
}

export function formatPromptLoRALines(values: PromptProjectLoRA[]): string {
  return (values ?? [])
    .map((item) => {
      const triggers = (item.triggerWords ?? []).join(", ");
      return [item.name, String(item.weight || 1), triggers].join(" | ").replace(/\s+\|\s*$/, "");
    })
    .join("\n");
}

export function parseIDList(value: string): number[] {
  const seen = new Set<number>();
  return value
    .split(/[\s,]+/)
    .map((part) => Number(part.trim()))
    .filter((id) => Number.isInteger(id) && id > 0)
    .filter((id) => {
      if (seen.has(id)) return false;
      seen.add(id);
      return true;
    });
}

export function promptVersionLabel(index: number, total: number): string {
  return `v${Math.max(1, total - index)}`;
}
