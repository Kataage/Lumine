import React, { createContext, useCallback, useContext, useEffect, useState } from "react";
import { QueryClientProvider } from "@tanstack/react-query";
import { Sidebar, Toolbar, WelcomeScreen } from "./components/Sidebar";
import { ViewerGrid } from "./components/ViewerGrid";
import { AssetDetailPanel } from "./components/AssetDetailPanel";
import type { AssetDTO, LibraryDTO } from "./api/client";
import {
  addLibrary,
  bulkDeleteAssets,
  bulkUpdateColorLabel,
  bulkUpdateFavorite,
  bulkUpdateRating,
  bulkUpdateStatus,
  getAppBootstrap,
  listLibraries,
  scanLibrary,
  selectFolder,
} from "./api/client";
import { queryClient } from "./queryClient";

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
}>({ state: defaultState, setState: () => undefined });

export const useApp = () => useContext(AppContext);

interface BootstrapPayload {
  libraries?: LibraryDTO[];
  settings?: Record<string, unknown>;
}

export default function App() {
  const [state, setState] = useState<AppState>(defaultState);
  const [booting, setBooting] = useState(true);
  const [bootstrapError, setBootstrapError] = useState<string | null>(null);

  const loadBootstrap = useCallback(async () => {
    setBooting(true);
    setBootstrapError(null);
    try {
      const bootstrap = (await getAppBootstrap()) as unknown as BootstrapPayload;
      const libraries = Array.isArray(bootstrap?.libraries)
        ? bootstrap.libraries
        : await listLibraries();
      const preferredLibrary = libraries.find((library) => library.isEnabled) ?? libraries[0];
      const savedThumbnailSize = bootstrap?.settings?.thumbnailSize;
      const thumbnailSize =
        typeof savedThumbnailSize === "number" && savedThumbnailSize >= 80 && savedThumbnailSize <= 420
          ? savedThumbnailSize
          : defaultState.thumbnailSize;

      setState((current) => ({
        ...current,
        libraries,
        selectedLibraryId: preferredLibrary?.id ?? null,
        thumbnailSize,
      }));
    } catch (error) {
      console.error("Lumine bootstrap failed", error);
      setBootstrapError(error instanceof Error ? error.message : String(error));
    } finally {
      setBooting(false);
    }
  }, []);

  useEffect(() => {
    void loadBootstrap();
  }, [loadBootstrap]);

  const handleSelectFolder = useCallback(async () => {
    try {
      const path = await selectFolder();
      if (!path) return;
      const name = path.split(/[/\\]/).pop() || "Library";
      const library = await addLibrary(name, path);
      if (!library) throw new Error("Failed to register library");

      const libraries = await listLibraries();
      setState((current) => ({
        ...current,
        libraries,
        selectedLibraryId: library.id,
      }));

      try {
        await scanLibrary(library.id);
        const refreshedLibraries = await listLibraries();
        setState((current) => ({ ...current, libraries: refreshedLibraries }));
      } catch (scanError) {
        console.error("Initial library scan failed", scanError);
      }
    } catch (error) {
      console.error("Failed to select folder:", error);
      alert("フォルダーの選択に失敗しました: " + (error instanceof Error ? error.message : String(error)));
    }
  }, []);

  const handleSelectAsset = useCallback(
    (asset: AssetDTO, multi: boolean, range: boolean) => {
      setState((current) => {
        let selection: Set<number>;
        let lastIndex: number | null = null;
        const currentIndex = current.allAssetIds.indexOf(asset.id);

        if (range && current.lastSelectedIndex !== null && currentIndex >= 0) {
          const start = Math.min(current.lastSelectedIndex, currentIndex);
          const end = Math.max(current.lastSelectedIndex, currentIndex);
          const rangeIds = current.allAssetIds.slice(start, end + 1);
          selection = multi
            ? new Set([...current.selectedAssets, ...rangeIds])
            : new Set(rangeIds);
          lastIndex = currentIndex;
        } else if (multi) {
          selection = new Set(current.selectedAssets);
          if (selection.has(asset.id)) selection.delete(asset.id);
          else selection.add(asset.id);
          lastIndex = currentIndex >= 0 ? currentIndex : current.lastSelectedIndex;
        } else {
          selection = new Set([asset.id]);
          lastIndex = currentIndex >= 0 ? currentIndex : null;
        }

        return {
          ...current,
          selectedAssets: selection,
          lastSelectedIndex: lastIndex,
          detailAsset: asset,
          detailOpen: true,
        };
      });
    },
    []
  );

  const handleCloseDetail = useCallback(() => {
    setState((current) => ({ ...current, detailOpen: false, detailAsset: null }));
  }, []);

  const handleAssetsLoaded = useCallback((ids: number[]) => {
    setState((current) => {
      if (
        current.allAssetIds.length === ids.length &&
        current.allAssetIds.every((id, index) => id === ids[index])
      ) {
        return current;
      }
      return { ...current, allAssetIds: ids };
    });
  }, []);

  const handleBulkRate = useCallback(async (rating: number) => {
    if (state.selectedAssets.size === 0) return;
    await bulkUpdateRating(Array.from(state.selectedAssets), rating);
    await queryClient.invalidateQueries({ queryKey: ["assets"] });
  }, [state.selectedAssets]);

  const handleBulkStatus = useCallback(async (status: string) => {
    if (state.selectedAssets.size === 0) return;
    await bulkUpdateStatus(Array.from(state.selectedAssets), status);
    await queryClient.invalidateQueries({ queryKey: ["assets"] });
  }, [state.selectedAssets]);

  const handleBulkFavorite = useCallback(async (favorite: boolean) => {
    if (state.selectedAssets.size === 0) return;
    await bulkUpdateFavorite(Array.from(state.selectedAssets), favorite);
    await queryClient.invalidateQueries({ queryKey: ["assets"] });
  }, [state.selectedAssets]);

  const handleBulkColorLabel = useCallback(async (label: string) => {
    if (state.selectedAssets.size === 0) return;
    await bulkUpdateColorLabel(Array.from(state.selectedAssets), label);
    await queryClient.invalidateQueries({ queryKey: ["assets"] });
  }, [state.selectedAssets]);

  const handleBulkDelete = useCallback(async () => {
    const ids = Array.from(state.selectedAssets);
    if (ids.length === 0) return;
    if (!confirm(`${ids.length}件のアセットをデータベースから削除しますか？`)) return;
    try {
      await bulkDeleteAssets(ids);
      setState((current) => ({
        ...current,
        selectedAssets: new Set<number>(),
        detailOpen: false,
        detailAsset: null,
      }));
      await queryClient.invalidateQueries({ queryKey: ["assets"] });
    } catch (error) {
      console.error("bulk delete failed:", error);
    }
  }, [state.selectedAssets]);

  if (booting) {
    return (
      <div className="h-screen bg-background text-foreground flex items-center justify-center">
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <div className="w-5 h-5 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
          <span>Opening Lumine…</span>
        </div>
      </div>
    );
  }

  if (bootstrapError) {
    return (
      <div className="h-screen bg-background text-foreground flex items-center justify-center p-8">
        <div className="max-w-lg text-center space-y-4">
          <h1 className="text-lg font-semibold">Lumine could not open its library database</h1>
          <p className="text-sm text-muted-foreground break-words">{bootstrapError}</p>
          <button
            onClick={() => void loadBootstrap()}
            className="px-4 py-2 rounded-lg bg-primary text-primary-foreground text-sm"
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

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
              <div className="flex-1 min-w-0 flex">
                <ViewerGrid onSelectAsset={handleSelectAsset} onAssetsLoaded={handleAssetsLoaded} />
              </div>
              {state.detailOpen && state.detailAsset && (
                <AssetDetailPanel assetId={state.detailAsset.id} onClose={handleCloseDetail} />
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
                onClear={() => setState((current) => ({
                  ...current,
                  selectedAssets: new Set(),
                  lastSelectedIndex: null,
                }))}
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
        {[1, 2, 3, 4, 5].map((rating) => (
          <button key={rating} onClick={() => onRate(rating)} className="p-0.5 hover:scale-110 transition-transform text-yellow-400">
            ★
          </button>
        ))}
      </div>
      <div className="h-4 w-px bg-border" />
      <div className="flex items-center gap-1">
        <span className="text-xs text-muted-foreground">Status:</span>
        {(["unsorted", "reviewed", "candidate", "published"] as const).map((status) => (
          <button
            key={status}
            onClick={() => onStatus(status)}
            className="text-[10px] px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground border border-border hover:bg-accent"
          >
            {status}
          </button>
        ))}
      </div>
      <div className="h-4 w-px bg-border" />
      <div className="flex items-center gap-1">
        <span className="text-xs text-muted-foreground">Label:</span>
        {["", "red", "orange", "yellow", "green", "blue", "purple"].map((label) => (
          <button
            key={label || "none"}
            onClick={() => onColorLabel(label)}
            className="w-4 h-4 rounded-full border border-border hover:scale-110 transition-transform"
            style={{ backgroundColor: label || "transparent" }}
            title={label || "Clear label"}
          />
        ))}
      </div>
      <div className="h-4 w-px bg-border" />
      <button onClick={() => onFavorite(true)} className="text-xs px-2 py-1 bg-muted rounded hover:bg-accent">
        Favorite
      </button>
      <button onClick={onDelete} className="text-xs px-2 py-1 bg-red-600 text-white rounded hover:bg-red-500">
        削除
      </button>
      <div className="flex-1" />
      <button onClick={onClear} className="text-xs px-2 py-1 text-muted-foreground hover:text-foreground">
        Clear selection
      </button>
    </div>
  );
}
