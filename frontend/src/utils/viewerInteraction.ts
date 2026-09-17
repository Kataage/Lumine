export interface ViewerSelectionResult {
  selectedIds: Set<number>;
  lastSelectedIndex: number | null;
}

export function computeViewerSelection(
  currentSelectedIds: Set<number>,
  allAssetIds: number[],
  lastSelectedIndex: number | null,
  assetId: number,
  multi: boolean,
  range: boolean
): ViewerSelectionResult {
  const currentIndex = allAssetIds.indexOf(assetId);

  if (range && lastSelectedIndex !== null && currentIndex >= 0) {
    const start = Math.min(lastSelectedIndex, currentIndex);
    const end = Math.max(lastSelectedIndex, currentIndex);
    const rangeIds = allAssetIds.slice(start, end + 1);
    return {
      selectedIds: multi ? new Set([...currentSelectedIds, ...rangeIds]) : new Set(rangeIds),
      lastSelectedIndex: currentIndex,
    };
  }

  if (multi) {
    const selectedIds = new Set(currentSelectedIds);
    if (selectedIds.has(assetId)) selectedIds.delete(assetId);
    else selectedIds.add(assetId);
    return {
      selectedIds,
      lastSelectedIndex: currentIndex >= 0 ? currentIndex : lastSelectedIndex,
    };
  }

  return {
    selectedIds: new Set([assetId]),
    lastSelectedIndex: currentIndex >= 0 ? currentIndex : null,
  };
}

export function isEditableShortcutTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;
  return target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.tagName === "SELECT";
}
