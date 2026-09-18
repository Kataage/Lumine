import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("../api/client", () => ({
  getLocalImageUrl: (path: string) => `/local?path=${encodeURIComponent(path)}`,
}));

import {
  clearMemoryImageCache,
  computeContainSize,
  computeCoverCrop,
  loadMemoryBitmap,
} from "../utils/imagePipeline";

describe("computeCoverCrop", () => {
  it("center-crops a wide image for a square preview", () => {
    expect(computeCoverCrop(4000, 2000, 200, 200)).toEqual({
      x: 1000,
      y: 0,
      width: 2000,
      height: 2000,
    });
  });

  it("center-crops a tall image for a square preview", () => {
    expect(computeCoverCrop(2000, 4000, 200, 200)).toEqual({
      x: 0,
      y: 1000,
      width: 2000,
      height: 2000,
    });
  });
});

describe("computeContainSize", () => {
  it("fits a landscape image without upscaling it", () => {
    expect(computeContainSize(4000, 2000, 1000, 1000)).toEqual({
      width: 1000,
      height: 500,
    });
  });

  it("does not enlarge an image smaller than the target", () => {
    expect(computeContainSize(320, 240, 1920, 1080)).toEqual({
      width: 320,
      height: 240,
    });
  });

  it("guards invalid source dimensions", () => {
    expect(computeContainSize(0, 0, 100, 100)).toEqual({
      width: 1,
      height: 1,
    });
  });
});

describe("decode priority", () => {
  afterEach(() => {
    clearMemoryImageCache();
    vi.unstubAllGlobals();
  });

  function response(): Response {
    return {
      ok: true,
      blob: async () => new Blob(["image"]),
    } as Response;
  }

  it("通常3件が実行中でもhigh要求を4番目の予約枠で先に開始する", async () => {
    type PendingFetch = {
      url: string;
      resolve: (value: Response) => void;
    };
    const pending: PendingFetch[] = [];

    const fetchMock = vi.fn((input: RequestInfo | URL) => new Promise<Response>((resolve) => {
      pending.push({ url: String(input), resolve });
    }));
    const close = vi.fn();
    const createImageBitmapMock = vi.fn(async () => ({ width: 32, height: 32, close } as unknown as ImageBitmap));
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("createImageBitmap", createImageBitmapMock);

    const request = (name: string, priority: "prefetch" | "normal" | "high") => loadMemoryBitmap({
      filePath: `C:\\images\\${name}.png`,
      modifiedAtFs: "2026-09-04T00:00:00Z",
      sourceWidth: 100,
      sourceHeight: 100,
      targetWidth: 32,
      targetHeight: 32,
      fit: "cover",
      priority,
    });

    const normal1 = request("normal-1", "normal");
    const normal2 = request("normal-2", "normal");
    const normal3 = request("normal-3", "normal");
    const normal4 = request("normal-4", "normal");

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));

    const focused = request("focused", "high");
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(4));
    expect(pending[3]?.url).toContain("focused.png");

    pending.slice(0, 4).forEach((item) => item.resolve(response()));
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(5));
    expect(pending[4]?.url).toContain("normal-4.png");
    pending[4]?.resolve(response());

    await Promise.all([normal1, normal2, normal3, normal4, focused]);
  });

  it("先読み待ちの画像が画面内に入ったら通常優先度へ昇格する", async () => {
    type PendingFetch = {
      url: string;
      resolve: (value: Response) => void;
    };
    const pending: PendingFetch[] = [];
    const fetchMock = vi.fn((input: RequestInfo | URL) => new Promise<Response>((resolve) => {
      pending.push({ url: String(input), resolve });
    }));
    const close = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("createImageBitmap", vi.fn(async () => ({ width: 32, height: 32, close } as unknown as ImageBitmap)));

    const request = (name: string, priority: "prefetch" | "normal") => loadMemoryBitmap({
      filePath: `C:\\images\\${name}.png`,
      modifiedAtFs: "2026-09-04T00:00:00Z",
      sourceWidth: 100,
      sourceHeight: 100,
      targetWidth: 32,
      targetHeight: 32,
      fit: "cover",
      priority,
    });

    const first = request("prefetch-1", "prefetch");
    const second = request("prefetch-2", "prefetch");
    const third = request("prefetch-3", "prefetch");
    const becomesVisible = request("prefetch-4", "prefetch");

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));

    // Same bitmap request, now requested by a visible card. This must promote
    // its queued work rather than waiting behind prefetch-3.
    const visiblePromise = request("prefetch-4", "normal");
    pending[0]?.resolve(response());

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3));
    expect(pending[2]?.url).toContain("prefetch-4.png");

    pending[1]?.resolve(response());
    pending[2]?.resolve(response());
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(4));
    expect(pending[3]?.url).toContain("prefetch-3.png");
    pending[3]?.resolve(response());

    await Promise.all([first, second, third, becomesVisible, visiblePromise]);
  });
});
