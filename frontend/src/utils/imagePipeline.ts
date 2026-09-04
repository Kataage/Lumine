import { getLocalImageUrl } from "../api/client";

export type ImageFit = "cover" | "contain";
export type ImageDecodePriority = "normal" | "high";

export interface ImageBitmapRequest {
  filePath: string;
  modifiedAtFs?: string;
  sourceWidth?: number;
  sourceHeight?: number;
  targetWidth: number;
  targetHeight: number;
  fit: ImageFit;
  priority?: ImageDecodePriority;
}

export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

const CACHE_BUDGET_BYTES = 96 * 1024 * 1024;
const MAX_CONCURRENT_DECODES = 4;

interface CacheEntry {
  bitmap: ImageBitmap;
  bytes: number;
}

interface DecodeWaiter {
  resolve: () => void;
  priority: ImageDecodePriority;
}

const cache = new Map<string, CacheEntry>();
const inflight = new Map<string, Promise<ImageBitmap>>();
let cacheBytes = 0;
let activeDecodes = 0;
const decodeWaiters: DecodeWaiter[] = [];

export function computeCoverCrop(
  sourceWidth: number,
  sourceHeight: number,
  targetWidth: number,
  targetHeight: number
): Rect {
  const safeSourceWidth = Math.max(1, sourceWidth);
  const safeSourceHeight = Math.max(1, sourceHeight);
  const safeTargetWidth = Math.max(1, targetWidth);
  const safeTargetHeight = Math.max(1, targetHeight);
  const sourceRatio = safeSourceWidth / safeSourceHeight;
  const targetRatio = safeTargetWidth / safeTargetHeight;

  if (sourceRatio > targetRatio) {
    const width = safeSourceHeight * targetRatio;
    return {
      x: (safeSourceWidth - width) / 2,
      y: 0,
      width,
      height: safeSourceHeight,
    };
  }

  const height = safeSourceWidth / targetRatio;
  return {
    x: 0,
    y: (safeSourceHeight - height) / 2,
    width: safeSourceWidth,
    height,
  };
}

export function computeContainSize(
  sourceWidth: number,
  sourceHeight: number,
  targetWidth: number,
  targetHeight: number
): { width: number; height: number } {
  const safeSourceWidth = Math.max(1, sourceWidth);
  const safeSourceHeight = Math.max(1, sourceHeight);
  const safeTargetWidth = Math.max(1, targetWidth);
  const safeTargetHeight = Math.max(1, targetHeight);
  const scale = Math.min(
    safeTargetWidth / safeSourceWidth,
    safeTargetHeight / safeSourceHeight,
    1
  );

  return {
    width: Math.max(1, Math.round(safeSourceWidth * scale)),
    height: Math.max(1, Math.round(safeSourceHeight * scale)),
  };
}

function requestKey(request: ImageBitmapRequest): string {
  return [
    request.filePath,
    request.modifiedAtFs ?? "",
    request.sourceWidth ?? 0,
    request.sourceHeight ?? 0,
    Math.round(request.targetWidth),
    Math.round(request.targetHeight),
    request.fit,
  ].join("|");
}

function touchCache(key: string, entry: CacheEntry): void {
  cache.delete(key);
  cache.set(key, entry);
}

function evictToBudget(): void {
  while (cacheBytes > CACHE_BUDGET_BYTES && cache.size > 1) {
    const oldest = cache.entries().next().value as [string, CacheEntry] | undefined;
    if (!oldest) break;
    const [key, entry] = oldest;
    cache.delete(key);
    cacheBytes -= entry.bytes;
    entry.bitmap.close();
  }
}

function cacheBitmap(key: string, bitmap: ImageBitmap): void {
  const bytes = bitmap.width * bitmap.height * 4;
  const previous = cache.get(key);
  if (previous) {
    cacheBytes -= previous.bytes;
    if (previous.bitmap !== bitmap) previous.bitmap.close();
    cache.delete(key);
  }
  cache.set(key, { bitmap, bytes });
  cacheBytes += bytes;
  evictToBudget();
}

function wakeNextDecode(): void {
  const next = decodeWaiters.shift();
  next?.resolve();
}

async function withDecodeSlot<T>(
  work: () => Promise<T>,
  priority: ImageDecodePriority = "normal"
): Promise<T> {
  if (activeDecodes >= MAX_CONCURRENT_DECODES) {
    await new Promise<void>((resolve) => {
      const waiter = { resolve, priority };
      if (priority === "high") {
        const firstNormal = decodeWaiters.findIndex((item) => item.priority === "normal");
        if (firstNormal >= 0) decodeWaiters.splice(firstNormal, 0, waiter);
        else decodeWaiters.push(waiter);
      } else {
        decodeWaiters.push(waiter);
      }
    });
  }

  activeDecodes += 1;
  try {
    return await work();
  } finally {
    activeDecodes -= 1;
    wakeNextDecode();
  }
}

async function createSizedBitmap(
  blob: Blob,
  request: ImageBitmapRequest
): Promise<ImageBitmap> {
  const targetWidth = Math.max(1, Math.round(request.targetWidth));
  const targetHeight = Math.max(1, Math.round(request.targetHeight));
  let sourceWidth = request.sourceWidth ?? 0;
  let sourceHeight = request.sourceHeight ?? 0;
  let sourceBitmap: ImageBitmap | null = null;

  if (sourceWidth <= 0 || sourceHeight <= 0) {
    sourceBitmap = await createImageBitmap(blob);
    sourceWidth = sourceBitmap.width;
    sourceHeight = sourceBitmap.height;
  }

  try {
    const source: ImageBitmapSource = sourceBitmap ?? blob;
    if (request.fit === "cover") {
      const crop = computeCoverCrop(sourceWidth, sourceHeight, targetWidth, targetHeight);
      return await createImageBitmap(
        source,
        Math.round(crop.x),
        Math.round(crop.y),
        Math.max(1, Math.round(crop.width)),
        Math.max(1, Math.round(crop.height)),
        {
          resizeWidth: targetWidth,
          resizeHeight: targetHeight,
          resizeQuality: "high",
        }
      );
    }

    const contained = computeContainSize(sourceWidth, sourceHeight, targetWidth, targetHeight);
    return await createImageBitmap(source, {
      resizeWidth: contained.width,
      resizeHeight: contained.height,
      resizeQuality: "high",
    });
  } finally {
    sourceBitmap?.close();
  }
}

export async function loadMemoryBitmap(request: ImageBitmapRequest): Promise<ImageBitmap> {
  if (typeof createImageBitmap !== "function") {
    throw new Error("createImageBitmap is unavailable");
  }

  const key = requestKey(request);
  const cached = cache.get(key);
  if (cached) {
    touchCache(key, cached);
    return cached.bitmap;
  }

  const pending = inflight.get(key);
  if (pending) return pending;

  const promise = withDecodeSlot(async () => {
    const response = await fetch(getLocalImageUrl(request.filePath), {
      cache: "no-store",
      credentials: "same-origin",
    });
    if (!response.ok) {
      throw new Error(`image fetch failed: ${response.status}`);
    }
    const blob = await response.blob();
    const bitmap = await createSizedBitmap(blob, request);
    cacheBitmap(key, bitmap);
    return bitmap;
  }, request.priority ?? "normal");

  inflight.set(key, promise);
  try {
    return await promise;
  } finally {
    inflight.delete(key);
  }
}

export function clearMemoryImageCache(): void {
  for (const entry of cache.values()) {
    entry.bitmap.close();
  }
  cache.clear();
  cacheBytes = 0;
}

export function getMemoryImageCacheStats(): { entries: number; bytes: number } {
  return { entries: cache.size, bytes: cacheBytes };
}
