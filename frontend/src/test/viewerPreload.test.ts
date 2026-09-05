import { describe, expect, it } from "vitest";
import {
  computeViewerOverscan,
  getViewerVisibleRange,
  shouldFetchViewerPageAhead,
  viewerImagePriority,
} from "../utils/viewerPreload";

describe("viewer preload policy", () => {
  it("keeps roughly two viewports mounted around the visible rows", () => {
    expect(computeViewerOverscan(800, 200)).toBe(8);
    expect(computeViewerOverscan(800, 100)).toBe(16);
  });

  it("distinguishes visible work from low-priority prefetch work", () => {
    const range = getViewerVisibleRange(400, 600, 200, 100);
    expect(range).toEqual({ first: 2, last: 4 });
    expect(viewerImagePriority(1, range.first, range.last)).toBe("prefetch");
    expect(viewerImagePriority(3, range.first, range.last)).toBe("normal");
    expect(viewerImagePriority(5, range.first, range.last)).toBe("prefetch");
  });

  it("requests the next metadata page several viewports before the loaded edge", () => {
    expect(shouldFetchViewerPageAhead(80, 100, 600, 100)).toBe(true);
    expect(shouldFetchViewerPageAhead(40, 100, 600, 100)).toBe(false);
  });
});
