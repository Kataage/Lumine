export type ViewerImagePriority = "prefetch" | "normal";

const MIN_OVERSCAN_ITEMS = 6;
const MAX_OVERSCAN_ITEMS = 48;
const OVERSCAN_SCREENS = 3;
const FETCH_AHEAD_SCREENS = 4;

function visibleItemCount(viewportSize: number, itemExtent: number): number {
  return Math.max(1, Math.ceil(Math.max(1, viewportSize) / Math.max(1, itemExtent)));
}

export function computeViewerOverscan(viewportSize: number, itemExtent: number): number {
  const visible = visibleItemCount(viewportSize, itemExtent);
  return Math.max(MIN_OVERSCAN_ITEMS, Math.min(MAX_OVERSCAN_ITEMS, visible * OVERSCAN_SCREENS));
}

export function getViewerVisibleRange(
  scrollOffset: number,
  viewportSize: number,
  itemExtent: number,
  itemCount: number
): { first: number; last: number } {
  if (itemCount <= 0) return { first: 0, last: -1 };
  const extent = Math.max(1, itemExtent);
  const first = Math.max(0, Math.min(itemCount - 1, Math.floor(Math.max(0, scrollOffset) / extent)));
  const last = Math.max(first, Math.min(itemCount - 1, Math.ceil((Math.max(0, scrollOffset) + Math.max(1, viewportSize)) / extent) - 1));
  return { first, last };
}

export function viewerImagePriority(index: number, firstVisible: number, lastVisible: number): ViewerImagePriority {
  return index >= firstVisible && index <= lastVisible ? "normal" : "prefetch";
}

export function shouldFetchViewerPageAhead(
  lastRenderedIndex: number,
  loadedItemCount: number,
  viewportSize: number,
  itemExtent: number
): boolean {
  if (loadedItemCount <= 0 || lastRenderedIndex < 0) return false;
  const visible = visibleItemCount(viewportSize, itemExtent);
  const threshold = Math.max(MIN_OVERSCAN_ITEMS, visible * FETCH_AHEAD_SCREENS);
  return lastRenderedIndex >= loadedItemCount - threshold;
}
