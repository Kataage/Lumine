import { describe, it, expect, beforeEach } from "vitest";
import { useAppStore } from "@/shared/hooks/useAppStore";

describe("useAppStore", () => {
  beforeEach(() => {
    const store = useAppStore.getState();
    useAppStore.setState({
      ...store,
      selectedLibraryId: null,
      selectedAssetIds: [],
      selectedAsset: null,
      searchQuery: "",
      activeView: "assets",
      viewMode: "grid",
      thumbnailSize: 150,
      isDetailPanelOpen: false,
    });
  });

  it("should select a library", () => {
    useAppStore.getState().selectLibrary(1);
    expect(useAppStore.getState().selectedLibraryId).toBe(1);
  });

  it("should toggle asset selection", () => {
    useAppStore.getState().toggleAssetSelection(1);
    expect(useAppStore.getState().selectedAssetIds).toContain(1);

    useAppStore.getState().toggleAssetSelection(1);
    expect(useAppStore.getState().selectedAssetIds).not.toContain(1);
  });

  it("should set search query", () => {
    useAppStore.getState().setSearchQuery("test");
    expect(useAppStore.getState().searchQuery).toBe("test");
  });

  it("should toggle detail panel", () => {
    useAppStore.getState().toggleDetailPanel();
    expect(useAppStore.getState().isDetailPanelOpen).toBe(true);

    useAppStore.getState().toggleDetailPanel();
    expect(useAppStore.getState().isDetailPanelOpen).toBe(false);
  });

  it("should set active view", () => {
    useAppStore.getState().setActiveView("tags");
    expect(useAppStore.getState().activeView).toBe("tags");
  });

  it("should set thumbnail size", () => {
    useAppStore.getState().setThumbnailSize(200);
    expect(useAppStore.getState().thumbnailSize).toBe(200);
  });
});
