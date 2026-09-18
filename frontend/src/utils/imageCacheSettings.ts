import { getSetting, setSetting } from "../api/client";
import {
  DEFAULT_MEMORY_IMAGE_CACHE_BUDGET_MIB,
  getMemoryImageCacheStats,
  normalizeMemoryImageCacheBudgetMiB,
  setMemoryImageCacheBudgetMiB,
} from "./imagePipeline";

export const MEMORY_IMAGE_CACHE_SETTING_KEY = "memoryImageCacheMiB";
const MIB = 1024 * 1024;

export function getMemoryImageCacheBudgetMiB(): number {
  return Math.round(getMemoryImageCacheStats().budgetBytes / MIB);
}

export async function initializeMemoryImageCacheBudget(): Promise<number> {
  try {
    const raw = await getSetting(MEMORY_IMAGE_CACHE_SETTING_KEY);
    if (!raw) {
      return setMemoryImageCacheBudgetMiB(DEFAULT_MEMORY_IMAGE_CACHE_BUDGET_MIB);
    }

    let value: unknown;
    try {
      value = JSON.parse(raw);
    } catch {
      value = raw;
    }
    return setMemoryImageCacheBudgetMiB(normalizeMemoryImageCacheBudgetMiB(value));
  } catch (error) {
    console.debug("image cache setting load failed; using default", error);
    return setMemoryImageCacheBudgetMiB(DEFAULT_MEMORY_IMAGE_CACHE_BUDGET_MIB);
  }
}

export async function saveMemoryImageCacheBudgetMiB(value: number): Promise<number> {
  const previous = getMemoryImageCacheBudgetMiB();
  const normalized = setMemoryImageCacheBudgetMiB(value);
  try {
    await setSetting(MEMORY_IMAGE_CACHE_SETTING_KEY, JSON.stringify(normalized));
    return normalized;
  } catch (error) {
    setMemoryImageCacheBudgetMiB(previous);
    throw error;
  }
}
