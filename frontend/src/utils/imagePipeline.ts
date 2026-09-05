import { getLocalImageUrl } from "../api/client";

export type ImageFit = "cover" | "contain";
export type ImageDecodePriority = "prefetch" | "normal" | "high";

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

// Keep enough recent decoded thumbnails for real viewer-style back/forward
// scrolling while retaining a strict upper bound. This is memory-only; Lumine
// still never writes generated thumbnails to disk.
const CACHE_BUDGET_BYTES = 256 * 1024 * 1024;
const MAX_CONCURRENT_DECODES = 4;
const MAX_NORMAL_CONCURRENT_DECODES = 3;
const MAX_PREFETCH_CONCURRENT_DECODES = 2;

interface CacheEntry {
  bitmap: ImageBitmap;
  bytes: number;
  filePath: string;
  modifiedAtFs: string;
  sourceWidth: number;
  sourceHeight: number;
  targetWidth: number;
  targetHeight: number;
  fit: ImageFit;
}

interface DecodeWaiter {
  resolve: () => void;
  priority: ImageDecodePriority;
  key: string;
}

const cache = new Map<string, CacheEntry>();
const cacheKeysBySource = new Map<string, Set<string>>();
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

function sourceKey(
  filePath: string,
  modifiedAtFs: string,
  sourceWidth: number,
  sourceHeight: number,
  fit: ImageFit
): string {
  return [filePath, modifiedAtFs, sourceWidth, sourceHeight, fit].join("|");
}

function requestSourceKey(request: ImageBitmapRequest): string {
  return sourceKey(
    request.filePath,
    request.modifiedAtFs ?? "",
    request.sourceWidth ?? 0,
    request.sourceHeight ?? 0,
    request.fit
  );
}

function entrySourceKey(entry: CacheEntry): string {
  return sourceKey(
    entry.filePath,
    entry.modifiedAtFs,
    entry.sourceWidth,
    entry.sourceHeight,
    entry.fit
  );
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

function addToSourceIndex(key: string, entry: CacheEntry): void {
  const indexed = entrySourceKey(entry);
  let keys = cacheKeysBySource.get(indexed);
  if (!keys) {
    keys = new Set<string>();
    cacheKeysBySource.set(indexed, keys);
  }
  keys.add(key);
}

function removeFromSourceIndex(key: string, entry: CacheEntry): void {
  const indexed = entrySourceKey(entry);
  const keys = cacheKeysBySource.get(indexed);
  if (!keys) return;
  keys.delete(key);
  if (keys.size === 0) cacheKeysBySource.delete(indexed);
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
    removeFromSourceIndex(key, entry);
    cacheBytes -= entry.bytes;
    entry.bitmap.close();
  }
}

function cacheBitmap(key: string, bitmap: ImageBitmap, request: ImageBitmapRequest): void {
  const bytes = bitmap.width * bitmap.height * 4;
  const previous = cache.get(key);
  if (previous) {
    cacheBytes -= previous.bytes;
    removeFromSourceIndex(key, previous);
    if (previous.bitmap !== bitmap) previous.bitmap.close();
    cache.delete(key);
  }

  const entry: CacheEntry = {
    bitmap,
    bytes,
    filePath: request.filePath,
    modifiedAtFs: request.modifiedAtFs ?? "",
    sourceWidth: request.sourceWidth ?? 0,
    sourceHeight: request.sourceHeight ?? 0,
    targetWidth: Math.max(1, Math.round(request.targetWidth)),
    targetHeight: Math.max(1, Math.round(request.targetHeight)),
    fit: request.fit,
  };

  cache.set(key, entry);
  addToSourceIndex(key, entry);
  cacheBytes += bytes;
  evictToBudget();
}

// Returns the nearest already-decoded representation of the same source image.
// This is what makes a remounted row display immediately when the user scrolls
// back instead of flashing an empty frame while decoding again.
export function getCachedMemoryBitmap(request: ImageBitmapRequest): ImageBitmap | null {
  const targetWidth = Math.max(1, Math.round(request.targetWidth));
  const targetHeight = Math.max(1, Math.round(request.targetHeight));
  const indexed = requestSourceKey(request);
  const keys = cacheKeysBySource.get(indexed);
  if (!keys || keys.size === 0) return null;

  let bestKey: string | null = null;
  let bestEntry: CacheEntry | null = null;
  let bestDistance = Number.POSITIVE_INFINITY;

  for (const key of keys) {
    const entry = cache.get(key);
    if (!entry) continue;
    const distance = Math.abs(entry.targetWidth - targetWidth) + Math.abs(entry.targetHeight - targetHeight);
    if (distance < bestDistance) {
      bestDistance = distance;
      bestKey = key;
      bestEntry = entry;
    }
  }

  if (!bestKey || !bestEntry) return null;
  touchCache(bestKey, bestEntry);
  return bestEntry.bitmap;
}

function priorityRank(priority: ImageDecodePriority): number {
  switch (priority) {
    case "high": return 2;
    case "normal": return 1;
    default: return 0;
  }
}

function canStartDecode(priority: ImageDecodePriority): boolean {
  const limit = priority === "high"
    ? MAX_CONCURRENT_DECODES
    : priority === "normal"
      ? MAX_NORMAL_CONCURRENT_DECODES
      : MAX_PREFETCH_CONCURRENT_DECODES;
  return activeDecodes < limit;
}

function insertWaiter(waiter: DecodeWaiter): void {
  const rank = priorityRank(waiter.priority);
  const firstLower = decodeWaiters.findIndex((item) => priorityRank(item.priority) < rank);
  if (firstLower >= 0) decodeWaiters.splice(firstLower, 0, waiter);
  else decodeWaiters.push(waiter);
}

function queueDecode(key: string, priority: ImageDecodePriority): Promise<void> {
  return new Promise<void>((resolve) => {
    insertWaiter({ resolve, priority, key });
  });
}

function promoteQueuedDecode(key: string, priority: ImageDecodePriority): void {
  const index = decodeWaiters.findIndex((item) => item.key === key);
  if (index < 0) return;
  const waiter = decodeWaiters[index];
  if (priorityRank(priority) <= priorityRank(waiter.priority)) return;
  decodeWaiters.splice(index, 1);
  waiter.priority = priority;
  insertWaiter(waiter);
}

function wakeNextDecode(): void {
  const index = decodeWaiters.findIndex((item) => canStartDecode(item.priority));
  if (index < 0) return;
  const [next] = decodeWaiters.splice(index, 1);
  activeDecodes += 1;
  next.resolve();
}

async function acquireDecodeSlot(key: string, priority: ImageDecodePriority): Promise<void> {
  if (canStartDecode(priority)) {
    activeDecodes += 1;
    return;
  }
  await queueDecode(key, priority);
}

async function withDecodeSlot<T>(
  key: string,
  work: () => Promise<T>,
  priority: ImageDecodePriority = "normal"
): Promise<T> {
  await acquireDecodeSlot(key, priority);
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

  const requestedPriority = request.priority ?? "normal";
  const pending = inflight.get(key);
  if (pending) {
    // An item that was only being prefetched may become visible during a fast
    // scroll. Promote its queued decode instead of leaving the visible card
    // waiting behind unrelated prefetch work.
    promoteQueuedDecode(key, requestedPriority);
    return pending;
  }

  const promise = withDecodeSlot(key, async () => {
    const response = await fetch(getLocalImageUrl(request.filePath), {
      cache: "no-store",
      credentials: "same-origin",
    });
    if (!response.ok) {
      throw new Error(`image fetch failed: ${response.status}`);
    }
    const blob = await response.blob();
    const bitmap = await createSizedBitmap(blob, request);
    cacheBitmap(key, bitmap, request);
    return bitmap;
  }, requestedPriority);

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
  cacheKeysBySource.clear();
  cacheBytes = 0;
}

export function getMemoryImageCacheStats(): { entries: number; bytes: number; budgetBytes: number } {
  return { entries: cache.size, bytes: cacheBytes, budgetBytes: CACHE_BUDGET_BYTES };
}
