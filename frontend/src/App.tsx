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
  selectedFolderPath: string;
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
  selectedFolderPath: "",
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
  const [addingLibrary, setAddingLibrary] = useState(false);

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
        selectedFolderPath: "",
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
    if (addingLibrary) return;
    setAddingLibrary(true);
    try {
      const path = await selectFolder();
      if (!path) return;
      const name = path.split(/[/\\]/).pop() || "画像フォルダー";
      const library = await addLibrary(name, path);
      if (!library) throw new Error("ライブラリの登録に失敗しました");

      const libraries = await listLibraries();
      setState((current) => ({
        ...current,
        libraries,
        selectedLibraryId: library.id,
        selectedFolderPath: "",
        searchQuery: "",
      }));

      await scanLibrary(library.id);
      const refreshedLibraries = await listLibraries();
      setState((current) => ({ ...current, libraries: refreshedLibraries }));
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["assets", library.id] }),
        queryClient.invalidateQueries({ queryKey: ["folderTree", library.id] }),
      ]);
    } catch (error) {
      console.error("Failed to select folder:", error);
      alert("画像フォルダーの追加に失敗しました。\n" + (error instanceof Error ? error.message : String(error)));
    } finally {
      setAddingLibrary(false);
    }
  }, [addingLibrary]);

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
    if (!confirm(`選択した${ids.length}件をLumineの一覧から削除します。\n元の画像ファイルは削除されません。\n\n続行しますか？`)) return;
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
      alert("一覧からの削除に失敗しました。");
    }
  }, [state.selectedAssets]);

  if (booting) {
    return (
      <div className="h-screen bg-background text-foreground flex items-center justify-center">
        <div className="flex flex-col items-center gap-3 text-sm text-muted-foreground">
          <div className="w-6 h-6 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
          <span>Lumineを起動しています…</span>
        </div>
      </div>
    );
  }

  if (bootstrapError) {
    return (
      <div className="h-screen bg-background text-foreground flex items-center justify-center p-8">
        <div className="max-w-lg text-center space-y-4 rounded-2xl border border-border bg-card p-8">
          <h1 className="text-lg font-semibold">ライブラリ情報を読み込めませんでした</h1>
          <p className="text-sm text-muted-foreground">Lumineのデータベースを開く際にエラーが発生しました。</p>
          <p className="text-xs text-muted-foreground/70 break-words font-mono">{bootstrapError}</p>
          <button
            onClick={() => void loadBootstrap()}
            className="px-4 py-2 rounded-lg bg-primary text-primary-foreground text-sm font-medium"
          >
            再試行
          </button>
        </div>
      </div>
    );
  }

  if (state.libraries.length === 0 && !state.selectedLibraryId) {
    return (
      <QueryClientProvider client={queryClient}>
        <WelcomeScreen onSelectFolder={handleSelectFolder} busy={addingLibrary} />
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
  const statuses = [
    { value: "unsorted", label: "未整理" },
    { value: "reviewed", label: "確認済み" },
    { value: "candidate", label: "候補" },
    { value: "published", label: "公開済み" },
  ];

  return (
    <div className="flex items-center gap-3 px-4 py-2.5 border-t border-border bg-card shadow-[0_-8px_24px_rgba(0,0,0,0.15)] flex-shrink-0 overflow-x-auto">
      <span className="text-xs font-semibold whitespace-nowrap">{count}件を選択中</span>
      <div className="h-5 w-px bg-border" />

      <div className="flex items-center gap-1 whitespace-nowrap">
        <span className="text-xs text-muted-foreground mr-1">評価</span>
        {[1, 2, 3, 4, 5].map((rating) => (
          <button
            key={rating}
            onClick={() => onRate(rating)}
            className="w-7 h-7 rounded-md hover:bg-accent text-yellow-400 transition-colors"
            title={`評価を${rating}に設定`}
          >
            ★
          </button>
        ))}
      </div>

      <div className="h-5 w-px bg-border" />
      <div className="flex items-center gap-1 whitespace-nowrap">
        <span className="text-xs text-muted-foreground mr-1">状態</span>
        {statuses.map((status) => (
          <button
            key={status.value}
            onClick={() => onStatus(status.value)}
            className="text-xs px-2 py-1 rounded-md bg-muted text-muted-foreground border border-border hover:bg-accent hover:text-foreground transition-colors"
          >
            {status.label}
          </button>
        ))}
      </div>

      <div className="h-5 w-px bg-border" />
      <div className="flex items-center gap-1 whitespace-nowrap">
        <span className="text-xs text-muted-foreground mr-1">色</span>
        {["", "red", "orange", "yellow", "green", "blue", "purple"].map((label) => (
          <button
            key={label || "none"}
            onClick={() => onColorLabel(label)}
            className="w-5 h-5 rounded-full border border-border hover:scale-110 transition-transform"
            style={{ backgroundColor: label || "transparent" }}
            title={label ? `${label}ラベル` : "カラーラベルを解除"}
          />
        ))}
      </div>

      <div className="h-5 w-px bg-border" />
      <button onClick={() => onFavorite(true)} className="text-xs px-2.5 py-1.5 bg-muted rounded-md hover:bg-accent whitespace-nowrap">
        ★ お気に入り
      </button>
      <button onClick={onDelete} className="text-xs px-2.5 py-1.5 bg-destructive text-destructive-foreground rounded-md hover:opacity-90 whitespace-nowrap">
        一覧から削除
      </button>
      <div className="flex-1 min-w-4" />
      <button onClick={onClear} className="text-xs px-2.5 py-1.5 text-muted-foreground hover:text-foreground whitespace-nowrap">
        選択を解除
      </button>
    </div>
  );
}
