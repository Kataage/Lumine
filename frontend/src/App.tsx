import React, { useState, useCallback, createContext, useContext } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Sidebar, Toolbar, WelcomeScreen } from "./components/Sidebar";
import { AssetGrid } from "./components/AssetGrid";
import { AssetDetailPanel } from "./components/AssetDetailPanel";
import type { LibraryDTO, AssetDTO } from "./api/client";
import {
  selectFolder,
  addLibrary,
  listLibraries,
  bulkUpdateRating,
  bulkUpdateStatus,
  bulkUpdateFavorite,
  bulkUpdateColorLabel,
  bulkDeleteAssets,
} from "./api/client";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 300_000,
      refetchOnWindowFocus: false,
    },
  },
});

type ViewMode = "grid" | "list";
type SidebarView = "libraries" | "folders" | "tags" | "posts" | "settings";

interface AppState {
  libraries: LibraryDTO[];
  selectedLibraryId: number | null;
  selectedAssets: Set<number>;
  lastSelectedIndex: number | null;
  detailAsset: AssetDTO | null;
  detailOpen: boolean;
  viewMode: ViewMode;
  sidebarView: SidebarView;
  sidebarOpen: boolean;
  searchQuery: string;
  sortBy: string;
  sortDesc: boolean;
  thumbnailSize: number;
  filterStatusLabel: string;
  filterRating: number;
  allAssetIds: number[];
}

const defaultState: AppState = {
  libraries: [],
  selectedLibraryId: null,
  selectedAssets: new Set<number>(),
  lastSelectedIndex: null,
  detailAsset: null,
  detailOpen: false,
  viewMode: "grid",
  sidebarView: "libraries",
  sidebarOpen: true,
  searchQuery: "",
  sortBy: "modifiedAtFs",
  sortDesc: true,
  thumbnailSize: 180,
  filterStatusLabel: "",
  filterRating: 0,
  allAssetIds: [],
};

const AppContext = createContext<{
  state: AppState;
  setState: React.Dispatch<React.SetStateAction<AppState>>;
}>({ state: defaultState, setState: () => {} });

export const useApp = () => useContext(AppContext);

export default function App() {
  const [state, setState] = useState<AppState>(defaultState);

  const handleSelectFolder = useCallback(async () => {
    try {
      const path = await selectFolder();
      if (!path) return;
      const name = path.split(/[/\\]/).pop() || "Library";
      const lib = await addLibrary(name, path);
      if (lib) {
        const libs = await listLibraries();
        setState((s) => ({
          ...s,
          libraries: libs,
          selectedLibraryId: lib.id,
        }));
      }
    } catch (err) {
      console.error("Failed to select folder:", err);
    }
  }, []);

  const handleSelectAsset = useCallback(
    (asset: AssetDTO, multi: boolean, range: boolean) => {
      setState((s) => {
        let sel: Set<number>;
        let lastIdx: number | null = null;

        const currentIdx = s.allAssetIds.indexOf(asset.id);

        if (range && s.lastSelectedIndex !== null && currentIdx >= 0) {
          const start = Math.min(s.lastSelectedIndex, currentIdx);
          const end = Math.max(s.lastSelectedIndex, currentIdx);
          const rangeIds = s.allAssetIds.slice(start, end + 1);
          sel = multi ? new Set([...s.selectedAssets, ...rangeIds]) : new Set(rangeIds);
          lastIdx = currentIdx;
        } else if (multi) {
          sel = new Set(s.selectedAssets);
          if (sel.has(asset.id)) {
            sel.delete(asset.id);
          } else {
            sel.add(asset.id);
          }
          lastIdx = currentIdx >= 0 ? currentIdx : s.lastSelectedIndex;
        } else {
          sel = new Set([asset.id]);
          lastIdx = currentIdx >= 0 ? currentIdx : null;
        }

        return {
          ...s,
          selectedAssets: sel,
          lastSelectedIndex: lastIdx,
          detailAsset: asset,
          detailOpen: true,
        };
      });
    },
    []
  );

  const handleCloseDetail = useCallback(() => {
    setState((s) => ({ ...s, detailOpen: false, detailAsset: null }));
  }, []);

  const handleAssetsLoaded = useCallback((ids: number[]) => {
    setState((s) => ({ ...s, allAssetIds: ids }));
  }, []);

  const handleBulkRate = useCallback(async (rating: number) => {
    if (state.selectedAssets.size === 0) return;
    try {
      await bulkUpdateRating(Array.from(state.selectedAssets), rating);
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    } catch (err) {
      console.error("Bulk rate failed:", err);
    }
  }, [state.selectedAssets]);

  const handleBulkStatus = useCallback(async (status: string) => {
    if (state.selectedAssets.size === 0) return;
    try {
      await bulkUpdateStatus(Array.from(state.selectedAssets), status);
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    } catch (err) {
      console.error("Bulk status failed:", err);
    }
  }, [state.selectedAssets]);

  const handleBulkFavorite = useCallback(async (favorite: boolean) => {
    if (state.selectedAssets.size === 0) return;
    try {
      await bulkUpdateFavorite(Array.from(state.selectedAssets), favorite);
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    } catch (err) {
      console.error("Bulk favorite failed:", err);
    }
  }, [state.selectedAssets]);

  const handleBulkColorLabel = useCallback(async (label: string) => {
    if (state.selectedAssets.size === 0) return;
    try {
      await bulkUpdateColorLabel(Array.from(state.selectedAssets), label);
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    } catch (err) {
      console.error("Bulk color label failed:", err);
    }
  }, [state.selectedAssets]);

  const handleBulkDelete = useCallback(async () => {
    const ids = Array.from(state.selectedAssets);
    if (ids.length === 0) return;
    if (!confirm(`${ids.length}件のアセットをデータベースから削除しますか？`)) return;
    try {
      await bulkDeleteAssets(ids);
      setState(s => ({
        ...s,
        selectedAssets: new Set<number>(),
        detailOpen: false,
        detailAsset: null,
      }));
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    } catch (e) {
      console.error("bulk delete failed:", e);
    }
  }, [state.selectedAssets, setState, queryClient]);

  if (state.libraries.length === 0 && !state.selectedLibraryId) {
    return (
      <QueryClientProvider client={queryClient}>
        <WelcomeScreen onSelectFolder={handleSelectFolder} />
      </QueryClientProvider>
    );
  }

  return (
    <QueryClientProvider client={queryClient}>
      <AppContext.Provider value={{ state, setState }}>
        <div className="flex h-screen bg-background text-foreground">
          {state.sidebarOpen && <Sidebar />}

          <div className="flex flex-col flex-1 min-w-0">
            <Toolbar />

            <div className="flex flex-1 min-h-0">
              <div className="flex-1 min-w-0">
                <AssetGrid onSelectAsset={handleSelectAsset} onAssetsLoaded={handleAssetsLoaded} />
              </div>

              {state.detailOpen && state.detailAsset && (
                <AssetDetailPanel
                  assetId={state.detailAsset.id}
                  onClose={handleCloseDetail}
                />
              )}
            </div>

        {state.selectedAssets.size > 1 && (
        <BulkActionsBar
          count={state.selectedAssets.size}
          onRate={handleBulkRate}
          onStatus={handleBulkStatus}
          onFavorite={handleBulkFavorite}
          onColorLabel={handleBulkColorLabel}
          onDelete={handleBulkDelete}
          onClear={() => setState((s) => ({ ...s, selectedAssets: new Set(), lastSelectedIndex: null }))}
        />
            )}
          </div>
        </div>
      </AppContext.Provider>
    </QueryClientProvider>
  );
}

export function BulkActionsBar({
  count,
  onRate,
  onStatus,
  onFavorite,
  onColorLabel,
  onDelete,
  onClear,
}: {
  count: number;
  onRate: (rating: number) => void;
  onStatus: (status: string) => void;
  onFavorite: (favorite: boolean) => void;
  onColorLabel: (label: string) => void;
  onDelete: () => void;
  onClear: () => void;
}) {
  return (
    <div className="flex items-center gap-2 px-4 py-2 border-t border-border bg-card flex-shrink-0">
      <span className="text-xs text-muted-foreground font-medium">{count} selected</span>
      <div className="h-4 w-px bg-border" />

      <div className="flex items-center gap-1">
        <span className="text-xs text-muted-foreground">Rate:</span>
        {[1, 2, 3, 4, 5].map((r) => (
          <button key={r} onClick={() => onRate(r)} className="p-0.5 hover:scale-110 transition-transform">
            <svg className="w-4 h-4 text-muted-foreground hover:text-yellow-400" viewBox="0 0 24 24" fill="currentColor">
              <path d="M10.788 3.21c.448-1.077 1.978-1.077 2.425 0l2.272 5.407a1.125 1.125 0 001.01.747l5.794.494c1.135.097 1.597 1.504.747 2.306l-4.394 3.893a1.125 1.125 0 00-.34 1.058l1.347 5.627c.264 1.1-.893 2.006-1.89 1.437l-5.088-2.863a1.125 1.125 0 00-1.08 0L6.68 20.394c-.997.57-2.154-.337-1.89-1.437l1.347-5.627a1.125 1.125 0 00-.34-1.058L1.403 8.374c-.85-.802-.388-2.21.747-2.306l5.794-.494a1.125 1.125 0 001.01-.747l2.272-5.407z" />
            </svg>
          </button>
        ))}
      </div>

      <div className="h-4 w-px bg-border" />

      <div className="flex items-center gap-1">
        <span className="text-xs text-muted-foreground">Status:</span>
        {(["unsorted", "reviewed", "candidate", "published"] as const).map((s) => (
          <button
            key={s}
            onClick={() => onStatus(s)}
            className="text-[10px] px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground border border-border hover:bg-accent transition-colors"
          >
            {s}
          </button>
        ))}
      </div>

      <div className="h-4 w-px bg-border" />

      <div className="flex items-center gap-1">
        <span className="text-xs text-muted-foreground">Label:</span>
        {["", "red", "orange", "yellow", "green", "blue", "purple"].map((c) => (
          <button
		key={c}
			onClick={() => onColorLabel(c)}
			className={`w-4 h-4 rounded-full transition-transform hover:scale-110 ${
				c === "" ? "bg-muted border border-border" : ""
			}`}
			style={c !== "" ? { backgroundColor: c === "red" ? "#ef4444" : c === "orange" ? "#f97316" : c === "yellow" ? "#eab308" : c === "green" ? "#22c55e" : c === "blue" ? "#3b82f6" : "#a855f7" } : undefined}
          />
        ))}
      </div>

      <div className="h-4 w-px bg-border" />

      <button
        onClick={() => onFavorite(true)}
        className="text-xs px-2 py-0.5 bg-muted text-muted-foreground border border-border rounded hover:bg-accent transition-colors"
      >
      Favorite
    </button>

    <div className="h-4 w-px bg-border" />

    <button
      className="px-3 py-1.5 text-xs bg-red-600 hover:bg-red-500 text-white rounded-lg transition-colors"
      onClick={onDelete}
    >
      削除
    </button>

    <div className="flex-1" />

    <button
        onClick={onClear}
        className="text-xs px-2 py-0.5 text-muted-foreground hover:text-foreground transition-colors"
      >
        Clear selection
      </button>
    </div>
  );
}
