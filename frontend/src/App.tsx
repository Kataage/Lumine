import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import { QueryClientProvider } from "@tanstack/react-query";
import { SidebarV2, ToolbarV2, WelcomeScreenV2 } from "./components/NavigationV2";
import { ViewerGridV2 } from "./components/ViewerGridV2";
import { AssetDetailPanel } from "./components/AssetDetailPanel";
import { PostRecordModal } from "./components/PostRecordModal";
import type { AssetDTO, LibraryDTO } from "./api/client";
import {
  addLibrary,
  bulkUpdateColorLabel,
  bulkUpdateFavorite,
  bulkUpdateRating,
  bulkUpdateStatus,
  deleteAssetFiles,
  getAppBootstrap,
  listLibraries,
  scanLibrary,
  selectFolder,
  syncLibrary,
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

const AUTO_SYNC_INTERVAL_MS = 15_000;

export default function App() {
  const [state, setState] = useState<AppState>(defaultState);
  const [booting, setBooting] = useState(true);
  const [bootstrapError, setBootstrapError] = useState<string | null>(null);
  const [addingLibrary, setAddingLibrary] = useState(false);
  const [bulkPostRecordOpen, setBulkPostRecordOpen] = useState(false);
  const autoSyncRunning = useRef(false);

  const loadBootstrap = useCallback(async () => {
    setBooting(true);
    setBootstrapError(null);
    try {
      const bootstrap = (await getAppBootstrap()) as unknown as BootstrapPayload;
      const libraries = Array.isArray(bootstrap?.libraries) ? bootstrap.libraries : await listLibraries();
      const preferredLibrary = libraries.find((library) => library.isEnabled) ?? libraries[0];
      const savedThumbnailSize = bootstrap?.settings?.thumbnailSize;
      const thumbnailSize = typeof savedThumbnailSize === "number" && savedThumbnailSize >= 80 && savedThumbnailSize <= 420
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

  useEffect(() => { void loadBootstrap(); }, [loadBootstrap]);

  useEffect(() => {
    const libraryId = state.selectedLibraryId;
    const library = state.libraries.find((item) => item.id === libraryId);
    if (!libraryId || !library?.isEnabled) return;

    let disposed = false;

    const run = async () => {
      if (disposed || document.hidden || autoSyncRunning.current) return;
      autoSyncRunning.current = true;
      try {
        const result = await syncLibrary(libraryId);
        if (disposed || !result?.changed) return;
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: ["assets", libraryId], refetchType: "active" }),
          queryClient.invalidateQueries({ queryKey: ["folderTree", libraryId], refetchType: "active" }),
        ]);
      } catch (error) {
        // A manual scan can legitimately own the scanner at the same time.
        // Silent sync is best-effort and will retry on the next interval/focus.
        console.debug("background library sync skipped", error);
      } finally {
        autoSyncRunning.current = false;
      }
    };

    const initialTimer = window.setTimeout(() => void run(), 1200);
    const interval = window.setInterval(() => void run(), AUTO_SYNC_INTERVAL_MS);
    const onFocus = () => void run();
    const onVisibility = () => { if (!document.hidden) void run(); };
    window.addEventListener("focus", onFocus);
    document.addEventListener("visibilitychange", onVisibility);

    return () => {
      disposed = true;
      window.clearTimeout(initialTimer);
      window.clearInterval(interval);
      window.removeEventListener("focus", onFocus);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [state.libraries, state.selectedLibraryId]);

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
      setState((current) => ({ ...current, libraries, selectedLibraryId: library.id, selectedFolderPath: "", searchQuery: "" }));
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

  const handleSelectAsset = useCallback((asset: AssetDTO, multi: boolean, range: boolean) => {
    setState((current) => {
      let selection: Set<number>;
      let lastIndex: number | null = null;
      const currentIndex = current.allAssetIds.indexOf(asset.id);
      if (range && current.lastSelectedIndex !== null && currentIndex >= 0) {
        const start = Math.min(current.lastSelectedIndex, currentIndex);
        const end = Math.max(current.lastSelectedIndex, currentIndex);
        const rangeIds = current.allAssetIds.slice(start, end + 1);
        selection = multi ? new Set([...current.selectedAssets, ...rangeIds]) : new Set(rangeIds);
        lastIndex = currentIndex;
      } else if (multi) {
        selection = new Set(current.selectedAssets);
        if (selection.has(asset.id)) selection.delete(asset.id); else selection.add(asset.id);
        lastIndex = currentIndex >= 0 ? currentIndex : current.lastSelectedIndex;
      } else {
        selection = new Set([asset.id]);
        lastIndex = currentIndex >= 0 ? currentIndex : null;
      }
      return { ...current, selectedAssets: selection, lastSelectedIndex: lastIndex, detailAsset: asset, detailOpen: true };
    });
  }, []);

  const handleCloseDetail = useCallback(() => setState((current) => ({ ...current, detailOpen: false, detailAsset: null })), []);
  const handleAssetsLoaded = useCallback((ids: number[]) => {
    setState((current) => {
      const sameIds = current.allAssetIds.length === ids.length && current.allAssetIds.every((id, index) => id === ids[index]);
      const available = new Set(ids);
      const selectedAssets = new Set(Array.from(current.selectedAssets).filter((id) => available.has(id)));
      const detailStillExists = !current.detailAsset || available.has(current.detailAsset.id);
      if (sameIds && selectedAssets.size === current.selectedAssets.size && detailStillExists) return current;
      return {
        ...current,
        allAssetIds: ids,
        selectedAssets,
        detailOpen: detailStillExists ? current.detailOpen : false,
        detailAsset: detailStillExists ? current.detailAsset : null,
      };
    });
  }, []);

  const selectedIDs = Array.from(state.selectedAssets);

  const handleBulkRate = useCallback(async (rating: number) => {
    if (!state.selectedAssets.size) return;
    await bulkUpdateRating(Array.from(state.selectedAssets), rating);
    await queryClient.invalidateQueries({ queryKey: ["assets"] });
  }, [state.selectedAssets]);

  const handleBulkStatus = useCallback(async (status: string) => {
    if (!state.selectedAssets.size) return;
    await bulkUpdateStatus(Array.from(state.selectedAssets), status);
    await queryClient.invalidateQueries({ queryKey: ["assets"] });
  }, [state.selectedAssets]);

  const handleBulkFavorite = useCallback(async (favorite: boolean) => {
    if (!state.selectedAssets.size) return;
    await bulkUpdateFavorite(Array.from(state.selectedAssets), favorite);
    await queryClient.invalidateQueries({ queryKey: ["assets"] });
  }, [state.selectedAssets]);

  const handleBulkColorLabel = useCallback(async (label: string) => {
    if (!state.selectedAssets.size) return;
    await bulkUpdateColorLabel(Array.from(state.selectedAssets), label);
    await queryClient.invalidateQueries({ queryKey: ["assets"] });
  }, [state.selectedAssets]);

  const handleDeleteFiles = useCallback(async () => {
    const ids = Array.from(state.selectedAssets);
    if (!ids.length) return;
    const label = ids.length === 1 ? "選択した画像" : `選択した${ids.length}件の画像`;
    if (!confirm(`${label}の元画像ファイルを削除します。\n\nこの操作は元に戻せません。Lumineの登録情報も同時に削除されます。\n\n本当に削除しますか？`)) return;

    try {
      const result = await deleteAssetFiles(ids);
      const deleted = new Set(result.deletedIds);
      setState((current) => ({
        ...current,
        selectedAssets: new Set(Array.from(current.selectedAssets).filter((id) => !deleted.has(id))),
        detailOpen: current.detailAsset && deleted.has(current.detailAsset.id) ? false : current.detailOpen,
        detailAsset: current.detailAsset && deleted.has(current.detailAsset.id) ? null : current.detailAsset,
        lastSelectedIndex: null,
      }));
      await queryClient.invalidateQueries({ queryKey: ["assets", state.selectedLibraryId], refetchType: "active" });

      if (result.failedCount > 0) {
        const details = (result.errors ?? []).slice(0, 5).join("\n");
        alert(`${result.deletedCount}件を削除しましたが、${result.failedCount}件は削除できませんでした。${details ? `\n\n${details}` : ""}`);
      }
    } catch (error) {
      console.error("image file delete failed:", error);
      alert("画像ファイルの削除に失敗しました。\n" + (error instanceof Error ? error.message : String(error)));
    }
  }, [state.selectedAssets, state.selectedLibraryId]);

  if (booting) {
    return <div className="h-screen bg-background text-foreground flex items-center justify-center"><div className="flex flex-col items-center gap-3 text-sm text-muted-foreground"><div className="w-6 h-6 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" /><span>Lumineを起動しています…</span></div></div>;
  }

  if (bootstrapError) {
    return <div className="h-screen bg-background text-foreground flex items-center justify-center p-8"><div className="max-w-lg text-center space-y-4 rounded-2xl border border-border bg-card p-8"><h1 className="text-lg font-semibold">ライブラリ情報を読み込めませんでした</h1><p className="text-sm text-muted-foreground">Lumineのデータベースを開く際にエラーが発生しました。</p><p className="text-xs text-muted-foreground/70 break-words font-mono">{bootstrapError}</p><button onClick={() => void loadBootstrap()} className="ui-primary-button">再試行</button></div></div>;
  }

  if (state.libraries.length === 0 && !state.selectedLibraryId) {
    return <QueryClientProvider client={queryClient}><WelcomeScreenV2 onSelectFolder={handleSelectFolder} busy={addingLibrary} /></QueryClientProvider>;
  }

  return (
    <QueryClientProvider client={queryClient}>
      <AppContext.Provider value={{ state, setState }}>
        <div className="app-shell flex h-screen bg-background text-foreground overflow-hidden">
          {state.sidebarOpen && <SidebarV2 />}
          <div className="flex flex-col flex-1 min-w-0">
            <ToolbarV2 />
            <div className="app-main-region relative flex flex-1 min-h-0 min-w-0">
              <div className="flex-1 min-w-0 flex"><ViewerGridV2 onSelectAsset={handleSelectAsset} onAssetsLoaded={handleAssetsLoaded} /></div>
              {state.detailOpen && state.detailAsset && <AssetDetailPanel asset={state.detailAsset} onClose={handleCloseDetail} />}
            </div>
            {state.selectedAssets.size > 0 && (
              <BulkActionsBar
                count={state.selectedAssets.size}
                onRate={handleBulkRate}
                onStatus={handleBulkStatus}
                onFavorite={handleBulkFavorite}
                onColorLabel={handleBulkColorLabel}
                onPostRecord={() => setBulkPostRecordOpen(true)}
                onDelete={handleDeleteFiles}
                onClear={() => setState((current) => ({ ...current, selectedAssets: new Set(), lastSelectedIndex: null }))}
              />
            )}
          </div>
        </div>
        {bulkPostRecordOpen && selectedIDs.length > 0 && <PostRecordModal assetIds={selectedIDs} defaultTitle={`${selectedIDs.length}件の画像`} onClose={() => setBulkPostRecordOpen(false)} />}
      </AppContext.Provider>
    </QueryClientProvider>
  );
}

export function BulkActionsBar({ count, onRate, onStatus, onFavorite, onColorLabel, onPostRecord, onDelete, onClear }: {
  count: number;
  onRate: (rating: number) => void;
  onStatus: (status: string) => void;
  onFavorite: (favorite: boolean) => void;
  onColorLabel: (label: string) => void;
  onPostRecord: () => void;
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
    <div className="min-h-14 flex items-center gap-2 px-3 py-2 border-t border-border bg-card shadow-[0_-8px_24px_rgba(0,0,0,0.15)] flex-shrink-0 overflow-x-auto">
      <span className="text-xs font-semibold whitespace-nowrap">{count}件を選択中</span>
      <div className="h-5 w-px bg-border" />
      <div className="flex items-center gap-1 whitespace-nowrap"><span className="text-[11px] text-muted-foreground mr-1">評価</span>{[1,2,3,4,5].map((rating) => <button key={rating} onClick={() => onRate(rating)} className="w-8 h-8 rounded-lg hover:bg-accent text-yellow-400" title={`評価を${rating}に設定`}>★</button>)}</div>
      <div className="h-5 w-px bg-border" />
      <div className="flex items-center gap-1 whitespace-nowrap"><span className="text-[11px] text-muted-foreground mr-1">状態</span>{statuses.map((status) => <button key={status.value} onClick={() => onStatus(status.value)} className="ui-secondary-button">{status.label}</button>)}</div>
      <div className="h-5 w-px bg-border" />
      <div className="flex items-center gap-1 whitespace-nowrap"><span className="text-[11px] text-muted-foreground mr-1">色</span>{["","red","orange","yellow","green","blue","purple"].map((label) => <button key={label || "none"} onClick={() => onColorLabel(label)} className="w-5 h-5 rounded-full border border-border" style={{ backgroundColor: label || "transparent" }} title={label || "なし"} />)}</div>
      <div className="h-5 w-px bg-border" />
      <button onClick={() => onFavorite(true)} className="ui-secondary-button">★ お気に入り</button>
      <button onClick={onPostRecord} className="ui-primary-button">＋ 投稿記録</button>
      <button onClick={onDelete} className="h-8 px-3 rounded-lg bg-destructive text-destructive-foreground text-[11px] font-medium whitespace-nowrap">画像ファイルを削除</button>
      <div className="flex-1 min-w-3" />
      <button onClick={onClear} className="ui-secondary-button">選択解除</button>
    </div>
  );
}
