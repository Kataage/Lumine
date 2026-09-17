import { describe, expect, it } from "vitest";
import { computeViewerSelection } from "../utils/viewerInteraction";

describe("viewer selection", () => {
  it("single click replaces the selection", () => {
    const result = computeViewerSelection(new Set([1, 2]), [1, 2, 3, 4], 1, 4, false, false);
    expect(Array.from(result.selectedIds)).toEqual([4]);
    expect(result.lastSelectedIndex).toBe(3);
  });

  it("ctrl/cmd click toggles without disturbing the rest", () => {
    const add = computeViewerSelection(new Set([1, 2]), [1, 2, 3, 4], 1, 4, true, false);
    expect(Array.from(add.selectedIds)).toEqual([1, 2, 4]);

    const remove = computeViewerSelection(add.selectedIds, [1, 2, 3, 4], add.lastSelectedIndex, 2, true, false);
    expect(Array.from(remove.selectedIds)).toEqual([1, 4]);
  });

  it("shift click selects a contiguous range", () => {
    const result = computeViewerSelection(new Set([2]), [1, 2, 3, 4, 5], 1, 5, false, true);
    expect(Array.from(result.selectedIds)).toEqual([2, 3, 4, 5]);
    expect(result.lastSelectedIndex).toBe(4);
  });

  it("ctrl/cmd + shift adds a range to the existing selection", () => {
    const result = computeViewerSelection(new Set([1]), [1, 2, 3, 4, 5], 2, 5, true, true);
    expect(Array.from(result.selectedIds)).toEqual([1, 3, 4, 5]);
  });
});
