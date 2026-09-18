import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useApp } from "../App";
import { listLibraries, listTags, offScanProgress, onScanProgress, setSetting } from "../api/client";
import type { ScanProgress } from "../api/client";
import { FoldersPanel, LibrariesPanel, SettingsPanel, TagsPanel } from "./NavigationPanels";
import { PostRecordsPanel } from "./PostRecordsPanel";
import { ToolbarSelect } from "./ToolbarSelect";

const APP_ICON_URL = "/appicon.png";

const SORT_OPTIONS = [
  { value: "modifiedAtFs", label: "更新日時" },
  { value: "created", label: "作成日時" },
  { value: "name", label: "ファイル名" },
  { value: "size", label: "サイズ" },
  { value: "rating", label: "評価" },
  { value: "status", label: "状態" },
] as const;

const STATUS_FILTER_OPTIONS = [
  { value: "", label: "状態: すべて" },
  { value: "unsorted", label: "未整理" },
  { value: "reviewed", label: "確認済み" },
  { value: "candidate", label: "候補" },
  { value: "published", label: "公開済み" },
] as const;

const RATING_FILTER_OPTIONS = [
  { value: 0, label: "評価: すべて" },
  { value: 1, label: "★" },
  { value: 2, label: "★★" },
  { value: 3, label: "★★★" },
  { value: 4, label: "★★★★" },
  { value: 5, label: "★★★★★" },
] as const;
const NAV_ITEMS = [
  { key: "libraries", label: "ライブラリ", description: "画像フォルダー", icon: "M3.75 6.75A2.25 2.25 0 016 4.5h3.879c.621 0 1.216.257 1.641.71l1.21 1.29H18a2.25 2.25 0 012.25 2.25v8.5A2.25 2.25 0 0118 19.5H6a2.25 2.25 0 01-2.25-2.25V6.75z" },
  { key: "folders", label: "フォルダー", description: "階層で絞り込み", icon: "M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.25-4.5L17.25 12l-3.75 3.75M17.25 12H3" },
  { key: "tags", label: "タグ", description: "画像の分類", icon: "M9.568 3H5.25A2.25 2.25 0 003 5.25v4.318c0 .597.237 1.17.659 1.591l9.581 9.581c.699.699 1.78.872 2.607.33a18.095 18.095 0 005.223-5.223c.542-.827.369-1.908-.33-2.607L11.16 3.66A2.25 2.25 0 009.568 3z" },
  { key: "posts", label: "公開履歴", description: "投稿内容を確認", icon: "M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625A1.125 1.125 0 004.5 3.375v17.25c0 .621.504 1.125 1.125 1.125h12.75a1.125 1.125 0 001.125-1.125V11.25a9 9 0 00-9-9z" },
  { key: "settings", label: "設定", description: "読み込み・操作", icon: "M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87l1.295.747 1.217-.456a1.125 1.125 0 011.37.49l1.296 2.247a1.125 1.125 0 01-.26 1.431l-1.003.827a1.125 1.125 0 000 1.735l1.004.828c.424.35.534.954.26 1.43l-1.298 2.247a1.125 1.125 0 01-1.369.491l-1.217-.456-1.295.748a1.125 1.125 0 00-.645.869l-.213 1.28c-.09.543-.56.941-1.11.941h-2.594c-.55 0-1.02-.398-1.11-.94l-.213-1.281a1.125 1.125 0 00-.644-.87l-1.296-.747-1.217.456a1.125 1.125 0 01-1.369-.49l-1.297-2.247a1.125 1.125 0 01.26-1.431l1.004-.827a1.125 1.125 0 000-1.735l-1.004-.828a1.125 1.125 0 01-.26-1.43l1.297-2.247a1.125 1.125 0 011.37-.491l1.216.456 1.296-.748a1.125 1.125 0 00.644-.869l.214-1.281z M15 12a3 3 0 11-6 0 3 3 0 016 0z" },
] as const;

function persistViewerSetting(key: string, value: unknown): void {
  void setSetting(key, JSON.stringify(value)).catch((error) => {
    console.debug(`viewer setting ${key} could not be saved`, error);
  });
}

function AppIcon({ size, className = "" }: { size: number; className?: string }) {
  return <img src={APP_ICON_URL} alt="Lumine" width={size} height={size} className={`object-contain ${className}`} draggable={false} />;
}

export function WelcomeScreenV2({ onSelectFolder, busy = false }: { onSelectFolder: () => void; busy?: boolean }) {
  return (
    <div className="h-screen bg-background flex items-center justify-center p-6">
      <div className="w-full max-w-xl rounded-3xl border border-border bg-card p-8 sm:p-10 text-center shadow-2xl">
        <AppIcon size={72} className="mx-auto drop-shadow-xl" />
        <h1 className="mt-5 text-2xl font-bold">Lumine</h1>
        <p className="mt-2 text-sm text-muted-foreground">生成過程・作品・派生関係・公開履歴までつなぐ制作アーカイブ</p>
        <div className="mt-7 rounded-2xl border border-border bg-muted/30 p-4 text-left">
          <p className="text-sm font-medium">最初に画像フォルダーを登録してください</p>
          <p className="mt-1 text-xs leading-relaxed text-muted-foreground">画像そのものをコピーせず、表示用サムネイルもディスクへ保存しません。</p>
        </div>
        <button type="button" onClick={onSelectFolder} disabled={busy} className="ui-primary-button mt-6 w-full justify-center h-11 text-sm">
          {busy ? "画像を読み込んでいます…" : "画像フォルダーを追加"}
        </button>
      </div>
    </div>
  );
}

export function SidebarV2() {
  const { state, setState } = useApp();
  const queryClient = useQueryClient();
  const [scanProgress, setScanProgress] = useState<Record<number, ScanProgress>>({});
  const completionTimers = useRef<number[]>([]);

  useEffect(() => {
    onScanProgress((progress) => {
      setScanProgress((current) => ({ ...current, [progress.libraryId]: progress }));
      if (!progress.isDone) return;
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: ["assets", progress.libraryId] }),
        queryClient.invalidateQueries({ queryKey: ["folderTree", progress.libraryId] }),
        listLibraries().then((libraries) => setState((current) => ({ ...current, libraries }))),
      ]);
      const timer = window.setTimeout(() => {
        setScanProgress((current) => {
          const next = { ...current };
          delete next[progress.libraryId];
          return next;
        });
      }, 1500);
      completionTimers.current.push(timer);
    });
    return () => {
      offScanProgress();
      completionTimers.current.forEach(window.clearTimeout);
      completionTimers.current = [];
    };
  }, [queryClient, setState]);

  return (
    <aside className="app-sidebar border-r border-border bg-card flex flex-col flex-shrink-0 overflow-hidden">
      <div className="h-14 px-3.5 border-b border-border flex items-center gap-2.5 flex-shrink-0">
        <AppIcon size={34} className="flex-shrink-0 drop-shadow-md" />
        <div className="min-w-0"><p className="text-sm font-bold">Lumine</p><p className="text-[11px] text-muted-foreground">制作アーカイブ</p></div>
      </div>
      <nav className="p-2 border-b border-border grid grid-cols-1 gap-1 flex-shrink-0">
        {NAV_ITEMS.map((item) => (
          <button type="button" key={item.key} onClick={() => setState((current) => ({ ...current, sidebarView: item.key }))} className={`min-h-10 px-2.5 rounded-lg flex items-center gap-2.5 text-left ${state.sidebarView === item.key ? "bg-primary/10 text-foreground" : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"}`}>
            <svg className="w-4 h-4 flex-shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.7} aria-hidden="true"><path strokeLinecap="round" strokeLinejoin="round" d={item.icon} /></svg>
            <span className="min-w-0"><span className="block text-xs font-medium">{item.label}</span><span className="block text-[10px] truncate opacity-70">{item.description}</span></span>
          </button>
        ))}
      </nav>
      <div className="flex-1 min-h-0 overflow-auto">
        {state.sidebarView === "libraries" && <LibrariesPanel scanProgress={scanProgress} />}
        {state.sidebarView === "folders" && <FoldersPanel />}
        {state.sidebarView === "tags" && <TagsPanel />}
        {state.sidebarView === "posts" && <PostRecordsPanel />}
        {state.sidebarView === "settings" && <SettingsPanel />}
      </div>
    </aside>
  );
}

export function ToolbarV2() {
  const { state, setState } = useApp();
  const [search, setSearch] = useState(state.searchQuery);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const library = state.libraries.find((item) => item.id === state.selectedLibraryId);
  const folderName = state.selectedFolderPath.split(/[\\/]/).pop() || "";
  const { data: tags = [] } = useQuery({ queryKey: ["tags"], queryFn: listTags, staleTime: Infinity });
  const selectedTagNames = state.filterTagIds.map((id) => tags.find((tag) => tag.id === id)?.name).filter((name): name is string => Boolean(name));
  useEffect(() => setSearch(state.searchQuery), [state.searchQuery]);
  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);
  const updateSearch = (value: string) => {
    setSearch(value);
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setState((current) => ({
      ...current,
      searchQuery: value,
      similarAssetId: null,
      similarAssetName: "",
    })), 250);
  };
  const setSearchMode = (searchMode: "normal" | "semantic") => {
    setState((current) => ({
      ...current,
      searchMode,
      similarAssetId: null,
      similarAssetName: "",
    }));
  };
  const hasFilters = !!(state.selectedFolderPath || state.searchQuery || state.similarAssetId || state.filterStatusLabel || state.filterRating || state.filterTagIds.length);

  const updateSort = (value: string) => {
    setState((current) => ({ ...current, sortBy: value }));
    persistViewerSetting("sortBy", value);
  };
  const toggleSortDirection = () => {
    const next = !state.sortDesc;
    setState((current) => ({ ...current, sortDesc: next }));
    persistViewerSetting("sortDesc", next);
  };
  const updateThumbnailSize = (value: number) => {
    setState((current) => ({ ...current, thumbnailSize: value }));
    persistViewerSetting("thumbnailSize", value);
  };
  const updateViewMode = (value: "grid" | "list") => {
    setState((current) => ({ ...current, viewMode: value }));
    persistViewerSetting("viewMode", value);
  };

  return (
    <header className="toolbar-v2 border-b border-border bg-card/80 flex-shrink-0">
      <div className="toolbar-primary-row">
        <button type="button" className="ui-icon-button" onClick={() => setState((current) => ({ ...current, sidebarOpen: !current.sidebarOpen }))} aria-label="サイドバー切り替え">☰</button>
        <div className="toolbar-library min-w-0"><p className="text-[10px] text-muted-foreground">表示中</p><p className="text-xs font-medium truncate" title={library?.rootPath}>{library?.name ?? "未選択"}</p></div>
        <div className="ui-segmented flex-shrink-0">
          <button type="button" className={state.searchMode === "normal" && !state.similarAssetId ? "active" : ""} onClick={() => setSearchMode("normal")}>通常</button>
          <button type="button" className={state.searchMode === "semantic" && !state.similarAssetId ? "active" : ""} onClick={() => setSearchMode("semantic")}>意味</button>
        </div>
        <div className="relative min-w-0 flex-1">
          <input
            className="ui-input w-full pl-3"
            value={search}
            onChange={(event) => updateSearch(event.target.value)}
            placeholder={state.similarAssetId ? "類似画像を表示中…" : state.searchMode === "semantic" ? "画像内容を自然文で検索…" : "ファイル名・メモを検索…"}
          />
        </div>
      </div>
      <div className="toolbar-controls-row">
        <ToolbarSelect value={state.sortBy} options={SORT_OPTIONS} onChange={updateSort} ariaLabel="並び順" />
        <button type="button" className="ui-secondary-button" onClick={toggleSortDirection}>{state.sortDesc ? "降順 ↓" : "昇順 ↑"}</button>
        <ToolbarSelect value={state.filterStatusLabel} options={STATUS_FILTER_OPTIONS} onChange={(value) => setState((current) => ({ ...current, filterStatusLabel: value }))} ariaLabel="状態で絞り込み" />
        <ToolbarSelect value={state.filterRating} options={RATING_FILTER_OPTIONS} onChange={(value) => setState((current) => ({ ...current, filterRating: value }))} ariaLabel="評価で絞り込み" />
        <div className="ui-segmented">{[[120,"小"],[180,"中"],[260,"大"]].map(([value,label]) => <button type="button" key={value} onClick={() => updateThumbnailSize(Number(value))} className={state.thumbnailSize === Number(value) ? "active" : ""}>{label}</button>)}</div>
        <div className="ui-segmented"><button type="button" className={state.viewMode === "grid" ? "active" : ""} onClick={() => updateViewMode("grid")}>グリッド</button><button type="button" className={state.viewMode === "list" ? "active" : ""} onClick={() => updateViewMode("list")}>リスト</button></div>
        {state.selectedAssets.size > 0 && (
          <div className="ml-auto flex items-center gap-1.5 whitespace-nowrap">
            <span className="text-[11px] font-medium text-primary" title="1-5: 評価 / F: お気に入り / I: 詳細">{state.selectedAssets.size}件選択</span>
            {state.selectedAssets.size === 1 && state.detailAsset && (
              <button type="button" className="ui-secondary-button" onClick={() => setState((current) => ({ ...current, detailOpen: true }))} title="詳細を表示 (I)">ⓘ 詳細</button>
            )}
          </div>
        )}
      </div>
      {hasFilters && <div className="toolbar-filter-row">
        <span className="text-[10px] text-muted-foreground">絞り込み</span>
        {state.selectedFolderPath && <button type="button" className="filter-chip" onClick={() => setState((current) => ({ ...current, selectedFolderPath: "" }))}>📁 {folderName} ×</button>}
        {state.similarAssetId && <button type="button" className="filter-chip" onClick={() => setState((current) => ({ ...current, similarAssetId: null, similarAssetName: "" }))}>類似: {state.similarAssetName || `#${state.similarAssetId}`} ×</button>}
        {state.searchQuery && <button type="button" className="filter-chip" onClick={() => setState((current) => ({ ...current, searchQuery: "" }))}>{state.searchMode === "semantic" ? "意味検索" : "検索"}: {state.searchQuery} ×</button>}
        {state.filterStatusLabel && <button type="button" className="filter-chip" onClick={() => setState((current) => ({ ...current, filterStatusLabel: "" }))}>状態 ×</button>}
        {state.filterRating > 0 && <button type="button" className="filter-chip" onClick={() => setState((current) => ({ ...current, filterRating: 0 }))}>★{state.filterRating} ×</button>}
        {state.filterTagIds.length > 0 && <button type="button" className="filter-chip" onClick={() => setState((current) => ({ ...current, filterTagIds: [] }))} title={selectedTagNames.join(", ")}>タグ: {state.filterTagIds.length === 1 && selectedTagNames[0] ? selectedTagNames[0] : `${state.filterTagIds.length}件`} ×</button>}
        <button type="button" className="text-[10px] text-muted-foreground hover:text-foreground" onClick={() => setState((current) => ({ ...current, selectedFolderPath: "", searchQuery: "", similarAssetId: null, similarAssetName: "", filterStatusLabel: "", filterRating: 0, filterTagIds: [] }))}>すべて解除</button>
      </div>}
    </header>
  );
}