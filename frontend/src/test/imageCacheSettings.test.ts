import { afterEach, describe, expect, it, vi } from "vitest";

const getSettingMock = vi.fn();
const setSettingMock = vi.fn();

vi.mock("../api/client", () => ({
  getSetting: getSettingMock,
  setSetting: setSettingMock,
  getLocalImageUrl: (path: string) => `/local?path=${encodeURIComponent(path)}`,
}));

import {
  DEFAULT_MEMORY_IMAGE_CACHE_BUDGET_MIB,
  getMemoryImageCacheStats,
  setMemoryImageCacheBudgetMiB,
} from "../utils/imagePipeline";
import {
  getMemoryImageCacheBudgetMiB,
  initializeMemoryImageCacheBudget,
  MEMORY_IMAGE_CACHE_SETTING_KEY,
  saveMemoryImageCacheBudgetMiB,
} from "../utils/imageCacheSettings";

describe("image cache settings", () => {
  afterEach(() => {
    getSettingMock.mockReset();
    setSettingMock.mockReset();
    setMemoryImageCacheBudgetMiB(DEFAULT_MEMORY_IMAGE_CACHE_BUDGET_MIB);
  });

  it("defaults to 1 GiB", () => {
    expect(getMemoryImageCacheBudgetMiB()).toBe(1024);
    expect(getMemoryImageCacheStats().budgetBytes).toBe(1024 * 1024 * 1024);
  });

  it("restores a persisted supported budget at startup", async () => {
    getSettingMock.mockResolvedValue("512");

    await expect(initializeMemoryImageCacheBudget()).resolves.toBe(512);
    expect(getSettingMock).toHaveBeenCalledWith(MEMORY_IMAGE_CACHE_SETTING_KEY);
    expect(getMemoryImageCacheBudgetMiB()).toBe(512);
  });

  it("falls back to 1 GiB for an unsupported saved value", async () => {
    getSettingMock.mockResolvedValue("768");
    setMemoryImageCacheBudgetMiB(256);

    await expect(initializeMemoryImageCacheBudget()).resolves.toBe(1024);
    expect(getMemoryImageCacheBudgetMiB()).toBe(1024);
  });

  it("applies and persists a changed budget", async () => {
    setSettingMock.mockResolvedValue(undefined);

    await expect(saveMemoryImageCacheBudgetMiB(2048)).resolves.toBe(2048);
    expect(setSettingMock).toHaveBeenCalledWith(MEMORY_IMAGE_CACHE_SETTING_KEY, "2048");
    expect(getMemoryImageCacheBudgetMiB()).toBe(2048);
  });

  it("rolls back the runtime budget when persistence fails", async () => {
    setMemoryImageCacheBudgetMiB(512);
    setSettingMock.mockRejectedValue(new Error("save failed"));

    await expect(saveMemoryImageCacheBudgetMiB(2048)).rejects.toThrow("save failed");
    expect(getMemoryImageCacheBudgetMiB()).toBe(512);
  });
});
