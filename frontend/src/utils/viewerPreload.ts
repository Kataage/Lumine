export type ViewerImagePriority = "prefetch" | "normal";

const MIN_OVERSCAN_ITEMS = 6;
const MAX_OVERSCAN_ITEMS = 32;
const OVERSCAN_SCREENS = 2;
const FETCH_AHEAD_SCREENS = 4;

function visibleItemCount(viewportSize: number, itemExtent: number): number {
  return Math.max(1, Math.ceil(Math.max(1, viewportSize) / Math.max(1, itemExtent)));
}

// Keep roughly two full viewports mounted above and below the visible range.
// This is large enough for ordinary wheel/trackpad scrolling without turning
// the entire library into DOM/canvas nodes.
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

// Asset metadata is cheap compared with image decoding. Fetch it farther ahead
// than bitmap overscan so a fast scrollbar drag does not hit the end of the
// currently loaded page before the next page exists.
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
