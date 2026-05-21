import { create } from "zustand";
import type { Asset, Library, Tag, Post, PostTarget, PostAccount } from "../../entities/types";

interface AppState {
  libraries: Library[];
  selectedLibraryId: number | null;
  selectedAssetIds: number[];
  selectedAsset: Asset | null;
  tags: Tag[];
  posts: Post[];
  postTargets: PostTarget[];
  postAccounts: PostAccount[];
  viewMode: "grid" | "list";
  thumbnailSize: number;
  isDetailPanelOpen: boolean;
  isScanning: boolean;
  scanProgress: { added: number; unchanged: number; errors: number } | null;
  searchQuery: string;
  activeView: "assets" | "tags" | "posts" | "settings";

  setLibraries: (libraries: Library[]) => void;
  setSearchQuery: (query: string) => void;
  setActiveView: (view: "assets" | "tags" | "posts" | "settings") => void;
  selectLibrary: (id: number | null) => void;
  addLibrary: (library: Library) => void;
  removeLibrary: (id: number) => void;
  setSelectedAssetIds: (ids: number[]) => void;
  toggleAssetSelection: (id: number) => void;
  setSelectedAsset: (asset: Asset | null) => void;
  setTags: (tags: Tag[]) => void;
  addTag: (tag: Tag) => void;
  setPosts: (posts: Post[]) => void;
  addPost: (post: Post) => void;
  setPostTargets: (targets: PostTarget[]) => void;
  addPostTarget: (target: PostTarget) => void;
  setPostAccounts: (accounts: PostAccount[]) => void;
  addPostAccount: (account: PostAccount) => void;
  setViewMode: (mode: "grid" | "list") => void;
  setThumbnailSize: (size: number) => void;
  toggleDetailPanel: () => void;
  setDetailPanelOpen: (open: boolean) => void;
  setScanning: (scanning: boolean) => void;
  setScanProgress: (progress: { added: number; unchanged: number; errors: number } | null) => void;
}

export const useAppStore = create<AppState>((set) => ({
  libraries: [],
  selectedLibraryId: null,
  selectedAssetIds: [],
  selectedAsset: null,
  tags: [],
  posts: [],
  postTargets: [],
  postAccounts: [],
  viewMode: "grid",
  thumbnailSize: 150,
  isDetailPanelOpen: false,
  isScanning: false,
  scanProgress: null,
  searchQuery: "",
  activeView: "assets",

  setLibraries: (libraries) => set({ libraries }),
  setSearchQuery: (query) => set({ searchQuery: query }),
  setActiveView: (view) => set({ activeView: view }),
  selectLibrary: (id) => set({ selectedLibraryId: id }),
  addLibrary: (library) =>
    set((state) => ({ libraries: [...state.libraries, library] })),
  removeLibrary: (id) =>
    set((state) => ({
      libraries: state.libraries.filter((l) => l.id !== id),
    })),
  setSelectedAssetIds: (ids) => set({ selectedAssetIds: ids }),
  toggleAssetSelection: (id) =>
    set((state) => {
      const isSelected = state.selectedAssetIds.includes(id);
      return {
        selectedAssetIds: isSelected
          ? state.selectedAssetIds.filter((i) => i !== id)
          : [...state.selectedAssetIds, id],
      };
    }),
  setSelectedAsset: (asset) => set({ selectedAsset: asset, isDetailPanelOpen: asset !== null }),
  setTags: (tags) => set({ tags }),
  addTag: (tag) => set((state) => ({ tags: [...state.tags, tag] })),
  setPosts: (posts) => set({ posts }),
  addPost: (post) => set((state) => ({ posts: [...state.posts, post] })),
  setPostTargets: (targets) => set({ postTargets: targets }),
  addPostTarget: (target) =>
    set((state) => ({ postTargets: [...state.postTargets, target] })),
  setPostAccounts: (accounts) => set({ postAccounts: accounts }),
  addPostAccount: (account) =>
    set((state) => ({ postAccounts: [...state.postAccounts, account] })),
  setViewMode: (mode) => set({ viewMode: mode }),
  setThumbnailSize: (size) => set({ thumbnailSize: size }),
  toggleDetailPanel: () =>
    set((state) => ({ isDetailPanelOpen: !state.isDetailPanelOpen })),
  setDetailPanelOpen: (open) => set({ isDetailPanelOpen: open }),
  setScanning: (scanning) => set({ isScanning: scanning }),
  setScanProgress: (progress) => set({ scanProgress: progress }),
}));