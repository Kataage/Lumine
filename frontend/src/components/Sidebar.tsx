import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useApp } from "../App";
import {
  addLibrary,
  createPostAccount,
  createPostDraft,
  createPostTarget,
  createTag,
  deletePost,
  deletePostAccount,
  deletePostTarget,
  deleteTag,
  disableLibrary,
  enableLibrary,
  getFolderTree,
  getSetting,
  getSupportedExtensions,
  listLibraries,
  listPostAccounts,
  listPosts,
  listPostTargets,
  listTags,
  offScanProgress,
  onScanProgress,
  removeLibrary,
  scanLibrary,
  selectFolder,
  setSetting,
  setSupportedExtensions,
} from "../api/client";
import type { FolderDTO, PostAccountDTO, PostDTO, PostTargetDTO, ScanProgress } from "../api/client";

const NAV_ITEMS = [
  {
    key: "libraries" as const,
    label: "ライブラリ",
    description: "画像フォルダー",
    icon: "M3.75 6.75A2.25 2.25 0 016 4.5h3.879c.621 0 1.216.257 1.641.71l1.21 1.29h5.27A2.25 2.25 0 0120.25 8.75v8.5A2.25 2.25 0 0118 19.5H6a2.25 2.25 0 01-2.25-2.25V6.75z",
  },
  {
    key: "folders" as const,
    label: "フォルダー",
    description: "階層で絞り込み",
    icon: "M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.25-4.5l3.75 3.75-3.75 3.75m3.75-3.75H3",
  },
  {
    key: "tags" as const,
    label: "タグ",
    description: "タグを管理",
    icon: "M9.568 3H5.25A2.25 2.25 0 003 5.25v4.318c0 .597.237 1.17.659 1.591l9.581 9.581c.699.699 1.78.872 2.607.33a18.095 18.095 0 005.223-5.223c.542-.827.369-1.908-.33-2.607L11.16 3.66A2.25 2.25 0 009.568 3z",
  },
  {
    key: "posts" as const,
    label: "投稿管理",
    description: "投稿下書き・連携先",
    icon: "M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z",
  },
  {
    key: "settings" as const,
    label: "設定",
    description: "読み込み・ファイル操作",
    icon: "M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.324.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 011.37.49l1.296 2.247a1.125 1.125 0 01-.26 1.431l-1.003.827c-.293.24-.438.613-.431.992a6.759 6.759 0 010 .255c-.007.378.138.75.43.99l1.005.828c.424.35.534.954.26 1.43l-1.298 2.247a1.125 1.125 0 01-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.57 6.57 0 01-.22.128c-.331.183-.581.495-.644.869l-.213 1.28c-.09.543-.56.941-1.11.941h-2.594c-.55 0-1.02-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 01-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 01-1.369-.49l-1.297-2.247a1.125 1.125 0 01.26-1.431l1.004-.827c.292-.24.437-.613.43-.992a6.932 6.932 0 010-.255c.007-.378-.138-.75-.43-.99l-1.004-.828a1.125 1.125 0 01-.26-1.43l1.297-2.247a1.125 1.125 0 011.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.087.22-.128.332-.183.582-.495.644-.869l.214-1.281z",
  },
];

export function WelcomeScreen({
  onSelectFolder,
  busy = false,
}: {
  onSelectFolder: () => void;
  busy?: boolean;
}) {
  return (
    <div className="flex items-center justify-center h-screen bg-background p-6">
      <div className="w-full max-w-xl rounded-3xl border border-border bg-card/70 p-8 sm:p-10 text-center shadow-2xl shadow-black/20">
        <div className="w-16 h-16 mx-auto mb-6 rounded-2xl bg-primary text-primary-foreground flex items-center justify-center shadow-lg">
          <svg className="w-8 h-8" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.7}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 15.75l5.159-5.159a2.25 2.25 0 013.182 0l5.159 5.159m-1.5-1.5l1.409-1.409a2.25 2.25 0 013.182 0l2.909 2.909M3.75 21h16.5A2.25 2.25 0 0022.5 18.75V5.25A2.25 2.25 0 0020.25 3H3.75A2.25 2.25 0 001.5 5.25v13.5A2.25 2.25 0 003.75 21z" />
          </svg>
        </div>
        <h1 className="text-3xl font-bold tracking-tight text-foreground">Lumine</h1>
        <p className="mt-2 text-base text-muted-foreground">大量の画像を、軽快に探して見るための画像ライブラリ</p>

        <div className="mt-7 rounded-2xl bg-muted/50 border border-border p-4 text-left space-y-2">
          <p className="text-sm font-medium text-foreground">最初に画像フォルダーを登録してください</p>
          <p className="text-xs leading-relaxed text-muted-foreground">
            登録後、自動で画像を一覧化します。画像ファイルをLumineへコピーしたり、表示用サムネイルをディスクへ保存したりはしません。
          </p>
        </div>

        <button
          onClick={onSelectFolder}
          disabled={busy}
          className="mt-7 w-full inline-flex items-center justify-center gap-2 px-5 py-3 bg-primary text-primary-foreground rounded-xl hover:opacity-90 disabled:opacity-60 disabled:cursor-wait transition-opacity text-sm font-semibold"
        >
          {busy ? (
            <div className="w-4 h-4 border-2 border-primary-foreground/30 border-t-primary-foreground rounded-full animate-spin" />
          ) : (
            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 10.5v6m3-3h-6M3.75 6.75A2.25 2.25 0 016 4.5h3.879c.621 0 1.216.257 1.641.71l1.21 1.29H18A2.25 2.25 0 0120.25 8.75v8.5A2.25 2.25 0 0118 19.5H6a2.25 2.25 0 01-2.25-2.25V6.75z" />
            </svg>
          )}
          {busy ? "画像を読み込んでいます…" : "画像フォルダーを追加"}
        </button>

        <p className="mt-4 text-[11px] text-muted-foreground/70">
          JPG / PNG / GIF / WebP / BMP / TIFF / SVG / AVIF などに対応
        </p>
      </div>
    </div>
  );
}

export function Sidebar() {
  const { state, setState } = useApp();
  const queryClient = useQueryClient();
  const [scanProgress, setScanProgress] = useState<Record<number, ScanProgress>>({});
  const completionTimers = useRef<number[]>([]);

  useEffect(() => {
    onScanProgress((progress) => {
      setScanProgress((previous) => ({ ...previous, [progress.libraryId]: progress }));
      if (!progress.isDone) return;

      void Promise.all([
        queryClient.invalidateQueries({ queryKey: ["assets", progress.libraryId] }),
        queryClient.invalidateQueries({ queryKey: ["folderTree", progress.libraryId] }),
        listLibraries().then((libraries) => setState((current) => ({ ...current, libraries }))),
      ]);

      const timer = window.setTimeout(() => {
        setScanProgress((previous) => {
          const next = { ...previous };
          delete next[progress.libraryId];
          return next;
        });
      }, 1800);
      completionTimers.current.push(timer);
    });

    return () => {
      offScanProgress();
      completionTimers.current.forEach((timer) => window.clearTimeout(timer));
      completionTimers.current = [];
    };
  }, [queryClient, setState]);

  const selectLibrary = (libraryId: number) => {
    setState((current) => ({
      ...current,
      selectedLibraryId: libraryId,
      selectedFolderPath: "",
      selectedAssets: new Set<number>(),
      lastSelectedIndex: null,
      detailAsset: null,
      detailOpen: false,
    }));
  };

  const handleAddLibrary = async () => {
    try {
      const path = await selectFolder();
      if (!path) return;
      const name = path.split(/[/\\]/).pop() || "画像フォルダー";
      const library = await addLibrary(name, path);
      if (!library) throw new Error("ライブラリを登録できませんでした");

      const libraries = await listLibraries();
      setState((current) => ({
        ...current,
        libraries,
        selectedLibraryId: library.id,
        selectedFolderPath: "",
        searchQuery: "",
      }));
      await scanLibrary(library.id);
      const refreshed = await listLibraries();
      setState((current) => ({ ...current, libraries: refreshed }));
    } catch (error) {
      console.error("Add library failed:", error);
      alert("画像フォルダーの追加に失敗しました。\n" + (error instanceof Error ? error.message : String(error)));
    }
  };

  const handleScan = async (id: number) => {
    try {
      await scanLibrary(id);
      const libraries = await listLibraries();
      setState((current) => ({ ...current, libraries }));
    } catch (error) {
      console.error("Scan failed:", error);
      alert("再スキャンに失敗しました。\n" + (error instanceof Error ? error.message : String(error)));
    }
  };

  const handleToggleEnabled = async (library: { id: number; isEnabled: boolean }) => {
    try {
      if (library.isEnabled) await disableLibrary(library.id);
      else await enableLibrary(library.id);
      const libraries = await listLibraries();
      setState((current) => ({ ...current, libraries }));
    } catch (error) {
      console.error("Toggle library failed:", error);
      alert("ライブラリの状態を変更できませんでした。");
    }
  };

  const handleRemoveLibrary = async (id: number, name: string) => {
    if (!confirm(`「${name}」をLumineから登録解除します。\n元の画像ファイルは削除されません。\n\n続行しますか？`)) return;
    try {
      await removeLibrary(id);
      const libraries = await listLibraries();
      setState((current) => ({
        ...current,
        libraries,
        selectedLibraryId: current.selectedLibraryId === id ? (libraries[0]?.id ?? null) : current.selectedLibraryId,
        selectedFolderPath: current.selectedLibraryId === id ? "" : current.selectedFolderPath,
        selectedAssets: current.selectedLibraryId === id ? new Set<number>() : current.selectedAssets,
        detailOpen: current.selectedLibraryId === id ? false : current.detailOpen,
        detailAsset: current.selectedLibraryId === id ? null : current.detailAsset,
      }));
    } catch (error) {
      console.error("Remove library failed:", error);
      alert("ライブラリの登録解除に失敗しました。");
    }
  };

  return (
    <aside className="w-64 border-r border-border bg-card/80 flex flex-col flex-shrink-0 backdrop-blur-sm">
      <div className="px-4 py-3.5 border-b border-border">
        <div className="flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-lg bg-primary text-primary-foreground flex items-center justify-center font-bold text-sm">L</div>
          <div className="min-w-0">
            <h1 className="text-sm font-bold text-foreground tracking-wide">Lumine</h1>
            <p className="text-[10px] text-muted-foreground">画像ライブラリ</p>
          </div>
        </div>
      </div>

      <nav className="p-2 space-y-1 border-b border-border">
        {NAV_ITEMS.map((item) => (
          <button
            key={item.key}
            onClick={() => setState((current) => ({ ...current, sidebarView: item.key }))}
            className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-left transition-colors ${
              state.sidebarView === item.key
                ? "bg-accent text-accent-foreground"
                : "text-muted-foreground hover:bg-accent/60 hover:text-foreground"
            }`}
          >
            <svg className="w-4 h-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.7}>
              <path strokeLinecap="round" strokeLinejoin="round" d={item.icon} />
            </svg>
            <div className="min-w-0">
              <div className="text-xs font-medium">{item.label}</div>
              <div className="text-[9px] opacity-60 truncate">{item.description}</div>
            </div>
          </button>
        ))}
      </nav>

      <div className="flex-1 min-h-0 overflow-auto">
        {state.sidebarView === "libraries" && (
          <LibrariesPanel
            scanProgress={scanProgress}
            onAdd={handleAddLibrary}
            onSelect={selectLibrary}
            onScan={handleScan}
            onToggleEnabled={handleToggleEnabled}
            onRemove={handleRemoveLibrary}
          />
        )}
        {state.sidebarView === "folders" && <FolderTreePanel />}
        {state.sidebarView === "tags" && <TagsPanel />}
        {state.sidebarView === "posts" && <PostsPanel />}
        {state.sidebarView === "settings" && <SettingsPanel />}
      </div>
    </aside>
  );
}

function PanelHeader({ title, description, action }: { title: string; description?: string; action?: React.ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-2 px-3 pt-3 pb-2">
      <div className="min-w-0">
        <h2 className="text-xs font-semibold text-foreground">{title}</h2>
        {description && <p className="text-[10px] text-muted-foreground mt-0.5 leading-relaxed">{description}</p>}
      </div>
      {action}
    </div>
  );
}

function LibrariesPanel({
  scanProgress,
  onAdd,
  onSelect,
  onScan,
  onToggleEnabled,
  onRemove,
}: {
  scanProgress: Record<number, ScanProgress>;
  onAdd: () => void;
  onSelect: (id: number) => void;
  onScan: (id: number) => void;
  onToggleEnabled: (library: { id: number; isEnabled: boolean }) => void;
  onRemove: (id: number, name: string) => void;
}) {
  const { state } = useApp();

  return (
    <div className="pb-3">
      <PanelHeader
        title="画像ライブラリ"
        description="Lumineで表示する画像フォルダーを管理します。"
        action={(
          <button
            onClick={onAdd}
            className="px-2.5 py-1.5 rounded-md bg-primary text-primary-foreground text-[10px] font-medium hover:opacity-90 whitespace-nowrap"
          >
            ＋ 追加
          </button>
        )}
      />

      <div className="px-2 space-y-1.5">
        {state.libraries.map((library) => {
          const progress = scanProgress[library.id];
          const selected = state.selectedLibraryId === library.id;
          return (
            <div
              key={library.id}
              onClick={() => onSelect(library.id)}
              className={`group rounded-xl border cursor-pointer transition-colors ${
                selected
                  ? "border-primary/50 bg-primary/10"
                  : "border-transparent hover:border-border hover:bg-accent/40"
              } ${library.isEnabled ? "" : "opacity-60"}`}
            >
              <div className="p-2.5">
                <div className="flex items-center gap-2">
                  <div className={`w-2 h-2 rounded-full flex-shrink-0 ${library.isEnabled ? "bg-emerald-500" : "bg-muted-foreground/40"}`} />
                  <span className="text-xs font-medium truncate flex-1">{library.name}</span>
                  {selected && <span className="text-[9px] px-1.5 py-0.5 rounded bg-primary text-primary-foreground">表示中</span>}
                </div>
                <p className="mt-1 pl-4 text-[9px] text-muted-foreground truncate" title={library.rootPath}>{library.rootPath}</p>

                {progress && !progress.isDone && (
                  <div className="mt-2 pl-4 space-y-1">
                    <div className="h-1 bg-muted rounded-full overflow-hidden">
                      <div className="h-full w-2/3 bg-primary/70 rounded-full animate-pulse" />
                    </div>
                    <p className="text-[9px] text-muted-foreground">
                      読み込み中: {progress.scannedCount.toLocaleString()}件
                      {progress.failedCount > 0 ? ` / エラー ${progress.failedCount}件` : ""}
                    </p>
                  </div>
                )}

                <div className={`mt-2 pl-4 flex items-center gap-1 transition-opacity ${selected ? "opacity-100" : "opacity-0 group-hover:opacity-100"}`}>
                  <button
                    onClick={(event) => { event.stopPropagation(); void onToggleEnabled(library); }}
                    className="text-[9px] px-2 py-1 rounded-md bg-muted hover:bg-accent"
                  >
                    {library.isEnabled ? "無効にする" : "有効にする"}
                  </button>
                  <button
                    onClick={(event) => { event.stopPropagation(); void onScan(library.id); }}
                    disabled={!library.isEnabled || !!progress}
                    className="text-[9px] px-2 py-1 rounded-md bg-muted hover:bg-accent disabled:opacity-40"
                  >
                    再スキャン
                  </button>
                  <button
                    onClick={(event) => { event.stopPropagation(); void onRemove(library.id, library.name); }}
                    className="text-[9px] px-2 py-1 rounded-md text-destructive hover:bg-destructive/10"
                  >
                    登録解除
                  </button>
                </div>
              </div>
            </div>
          );
        })}

        {state.libraries.length === 0 && (
          <div className="mx-1 mt-3 rounded-xl border border-dashed border-border p-4 text-center">
            <p className="text-xs text-muted-foreground">画像フォルダーが登録されていません</p>
            <button onClick={onAdd} className="mt-2 text-xs text-primary hover:underline">フォルダーを追加</button>
          </div>
        )}
      </div>
    </div>
  );
}

function FolderTreePanel() {
  const { state, setState } = useApp();
  const { data: folders = [], isLoading } = useQuery({
    queryKey: ["folderTree", state.selectedLibraryId],
    queryFn: () => getFolderTree(state.selectedLibraryId!),
    enabled: !!state.selectedLibraryId,
    staleTime: Infinity,
  });
  const [expandedPaths, setExpandedPaths] = useState<Set<string>>(new Set());

  const rootFolders = folders.filter((folder) => !folder.parentPath || folder.parentPath === "");
  const getChildren = (parentPath: string) => folders.filter((folder) => folder.parentPath === parentPath);

  const toggleExpand = (path: string) => {
    setExpandedPaths((previous) => {
      const next = new Set(previous);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  };

  const renderFolder = (folder: FolderDTO, depth: number) => {
    const children = getChildren(folder.path);
    const expanded = expandedPaths.has(folder.path);
    const selected = state.selectedFolderPath === folder.path;
    const folderName = folder.path.split(/[\\/]/).pop() || folder.path;

    return (
      <div key={folder.path}>
        <div
          className={`flex items-center rounded-lg transition-colors ${selected ? "bg-primary/10 text-foreground" : "hover:bg-accent/50 text-muted-foreground"}`}
          style={{ paddingLeft: `${depth * 12 + 4}px` }}
        >
          <button
            onClick={() => children.length > 0 && toggleExpand(folder.path)}
            className={`w-6 h-7 flex items-center justify-center flex-shrink-0 ${children.length === 0 ? "opacity-0 pointer-events-none" : ""}`}
            aria-label={expanded ? "フォルダーを閉じる" : "フォルダーを開く"}
          >
            <svg className={`w-3 h-3 transition-transform ${expanded ? "rotate-90" : ""}`} fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clipRule="evenodd" />
            </svg>
          </button>
          <button
            onClick={() => setState((current) => ({ ...current, selectedFolderPath: folder.path }))}
            className="min-w-0 flex-1 h-8 flex items-center gap-1.5 pr-2 text-left"
            title={folder.path}
          >
            <svg className="w-3.5 h-3.5 text-amber-400 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
              <path d="M2 6a2 2 0 012-2h5l2 2h5a2 2 0 012 2v6a2 2 0 01-2 2H4a2 2 0 01-2-2V6z" />
            </svg>
            <span className="text-[11px] truncate">{folderName}</span>
          </button>
        </div>
        {expanded && children.map((child) => renderFolder(child, depth + 1))}
      </div>
    );
  };

  return (
    <div className="pb-3">
      <PanelHeader title="フォルダー" description="フォルダー階層で表示画像を絞り込みます。" />
      <div className="px-2 space-y-1">
        <button
          onClick={() => setState((current) => ({ ...current, selectedFolderPath: "" }))}
          className={`w-full text-left px-3 py-2 rounded-lg text-xs transition-colors ${
            !state.selectedFolderPath ? "bg-primary/10 text-foreground font-medium" : "text-muted-foreground hover:bg-accent/50"
          }`}
        >
          すべての画像
        </button>

        {!state.selectedLibraryId && <p className="px-3 py-4 text-xs text-muted-foreground text-center">先にライブラリを選択してください</p>}
        {state.selectedLibraryId && isLoading && <p className="px-3 py-4 text-xs text-muted-foreground text-center">フォルダーを読み込み中…</p>}
        {state.selectedLibraryId && !isLoading && rootFolders.map((folder) => renderFolder(folder, 0))}
        {state.selectedLibraryId && !isLoading && rootFolders.length === 0 && (
          <p className="px-3 py-4 text-xs text-muted-foreground text-center">フォルダー情報がありません。再スキャンしてください。</p>
        )}
      </div>
    </div>
  );
}

function TagsPanel() {
  const queryClient = useQueryClient();
  const { data: tags = [] } = useQuery({ queryKey: ["tags"], queryFn: listTags, staleTime: Infinity });
  const [newTagName, setNewTagName] = useState("");
  const [newTagColor, setNewTagColor] = useState("#6366f1");

  const handleCreate = async () => {
    if (!newTagName.trim()) return;
    const tag = await createTag(newTagName.trim(), newTagColor);
    if (tag) {
      await queryClient.invalidateQueries({ queryKey: ["tags"] });
      setNewTagName("");
    }
  };

  const handleDelete = async (id: number, name: string) => {
    if (!confirm(`タグ「${name}」を削除しますか？`)) return;
    await deleteTag(id);
    await queryClient.invalidateQueries({ queryKey: ["tags"] });
  };

  return (
    <div className="pb-3">
      <PanelHeader title="タグ管理" description="画像に付けるタグを作成・削除します。" />
      <div className="px-3 space-y-3">
        <div className="flex gap-1.5">
          <input
            value={newTagName}
            onChange={(event) => setNewTagName(event.target.value)}
            onKeyDown={(event) => event.key === "Enter" && void handleCreate()}
            placeholder="新しいタグ名"
            className="min-w-0 flex-1 text-xs px-2.5 py-2 bg-muted rounded-lg border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/50"
          />
          <input
            type="color"
            value={newTagColor}
            onChange={(event) => setNewTagColor(event.target.value)}
            className="w-8 h-8 rounded-lg border border-border cursor-pointer bg-transparent"
            title="タグの色"
          />
          <button onClick={() => void handleCreate()} className="px-2.5 py-1 bg-primary text-primary-foreground rounded-lg text-xs font-medium">追加</button>
        </div>

        <div className="space-y-1">
          {tags.map((tag) => (
            <div key={tag.id} className="flex items-center gap-2 px-2.5 py-2 rounded-lg hover:bg-accent/50 group">
              <div className="w-3 h-3 rounded-full flex-shrink-0" style={{ backgroundColor: tag.color }} />
              <span className="text-xs text-foreground truncate flex-1">{tag.name}</span>
              <button
                onClick={() => void handleDelete(tag.id, tag.name)}
                className="text-[10px] text-muted-foreground opacity-0 group-hover:opacity-100 hover:text-destructive transition-all"
              >
                削除
              </button>
            </div>
          ))}
          {tags.length === 0 && <p className="text-xs text-muted-foreground text-center py-5">タグはまだありません</p>}
        </div>
      </div>
    </div>
  );
}

function PostsPanel() {
  const [posts, setPosts] = useState<PostDTO[]>([]);
  const [targets, setTargets] = useState<PostTargetDTO[]>([]);
  const [accounts, setAccounts] = useState<PostAccountDTO[]>([]);
  const [showNewPost, setShowNewPost] = useState(false);
  const [newTitle, setNewTitle] = useState("");
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [newTargetName, setNewTargetName] = useState("");
  const [newTargetKind, setNewTargetKind] = useState("twitter");
  const [newAccountTargetId, setNewAccountTargetId] = useState<number>(0);
  const [newAccountDisplay, setNewAccountDisplay] = useState("");
  const [newAccountIdentifier, setNewAccountIdentifier] = useState("");

  useEffect(() => {
    void Promise.all([
      listPosts(0, 50).then(setPosts),
      listPostTargets().then((items) => {
        setTargets(items);
        setNewAccountTargetId((current) => current || items[0]?.id || 0);
      }),
      listPostAccounts().then(setAccounts),
    ]);
  }, []);

  const createDraft = async () => {
    if (!newTitle.trim()) return;
    const post = await createPostDraft(newTitle.trim(), "", "");
    if (post) {
      setPosts((previous) => [post, ...previous]);
      setNewTitle("");
      setShowNewPost(false);
    }
  };

  const statusLabel: Record<string, string> = {
    draft: "下書き",
    scheduled: "予約済み",
    published: "公開済み",
    failed: "失敗",
  };

  return (
    <div className="pb-3">
      <PanelHeader
        title="投稿管理"
        description="画像を投稿するための下書きや連携先を管理します。"
        action={<button onClick={() => setShowNewPost((value) => !value)} className="px-2.5 py-1.5 rounded-md bg-primary text-primary-foreground text-[10px]">＋ 下書き</button>}
      />

      <div className="px-3 space-y-3">
        {showNewPost && (
          <div className="flex gap-1.5">
            <input
              value={newTitle}
              onChange={(event) => setNewTitle(event.target.value)}
              onKeyDown={(event) => event.key === "Enter" && void createDraft()}
              placeholder="投稿タイトル"
              className="min-w-0 flex-1 text-xs px-2.5 py-2 bg-muted rounded-lg border border-border focus:border-primary focus:outline-none"
            />
            <button onClick={() => void createDraft()} className="px-2.5 py-1 bg-primary text-primary-foreground rounded-lg text-xs">作成</button>
          </div>
        )}

        <div className="space-y-1">
          {posts.map((post) => (
            <div key={post.id} className="flex items-center gap-2 px-2.5 py-2 rounded-lg hover:bg-accent/50 group">
              <span className="text-[9px] px-1.5 py-0.5 rounded bg-muted text-muted-foreground">{statusLabel[post.status] ?? post.status}</span>
              <span className="text-xs truncate flex-1">{post.title}</span>
              <button
                onClick={async () => {
                  if (!confirm(`投稿「${post.title}」を削除しますか？`)) return;
                  await deletePost(post.id);
                  setPosts((previous) => previous.filter((item) => item.id !== post.id));
                }}
                className="text-[10px] text-muted-foreground opacity-0 group-hover:opacity-100 hover:text-destructive"
              >
                削除
              </button>
            </div>
          ))}
          {posts.length === 0 && <p className="text-xs text-muted-foreground text-center py-4">投稿下書きはありません</p>}
        </div>

        <button onClick={() => setShowAdvanced((value) => !value)} className="w-full flex items-center justify-between px-2.5 py-2 rounded-lg bg-muted/50 text-xs text-muted-foreground hover:text-foreground">
          <span>連携先・アカウント設定</span>
          <span>{showAdvanced ? "▲" : "▼"}</span>
        </button>

        {showAdvanced && (
          <div className="space-y-4 rounded-xl border border-border p-2.5">
            <section className="space-y-2">
              <p className="text-[10px] font-semibold text-muted-foreground">投稿先</p>
              <div className="flex gap-1">
                <input value={newTargetName} onChange={(event) => setNewTargetName(event.target.value)} placeholder="名前" className="min-w-0 flex-1 text-[10px] px-2 py-1.5 bg-muted rounded border border-border" />
                <select value={newTargetKind} onChange={(event) => setNewTargetKind(event.target.value)} className="text-[10px] px-1 bg-muted rounded border border-border">
                  <option value="twitter">X</option>
                  <option value="pixiv">Pixiv</option>
                  <option value="misskey">Misskey</option>
                  <option value="bluesky">Bluesky</option>
                  <option value="other">その他</option>
                </select>
                <button
                  onClick={async () => {
                    if (!newTargetName.trim()) return;
                    const target = await createPostTarget(newTargetName.trim(), newTargetKind);
                    if (target) {
                      setTargets((previous) => [...previous, target]);
                      setNewTargetName("");
                      if (!newAccountTargetId) setNewAccountTargetId(target.id);
                    }
                  }}
                  className="text-[10px] px-2 rounded bg-primary text-primary-foreground"
                >
                  追加
                </button>
              </div>
              {targets.map((target) => (
                <div key={target.id} className="flex items-center gap-2 text-[10px] py-1">
                  <span className="text-muted-foreground">{target.kind}</span>
                  <span className="truncate flex-1">{target.name}</span>
                  <button onClick={async () => { await deletePostTarget(target.id); setTargets((previous) => previous.filter((item) => item.id !== target.id)); }} className="text-muted-foreground hover:text-destructive">削除</button>
                </div>
              ))}
            </section>

            {targets.length > 0 && (
              <section className="space-y-2 border-t border-border pt-3">
                <p className="text-[10px] font-semibold text-muted-foreground">アカウント</p>
                <select value={newAccountTargetId} onChange={(event) => setNewAccountTargetId(Number(event.target.value))} className="w-full text-[10px] px-2 py-1.5 bg-muted rounded border border-border">
                  {targets.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}
                </select>
                <input value={newAccountDisplay} onChange={(event) => setNewAccountDisplay(event.target.value)} placeholder="表示名" className="w-full text-[10px] px-2 py-1.5 bg-muted rounded border border-border" />
                <div className="flex gap-1">
                  <input value={newAccountIdentifier} onChange={(event) => setNewAccountIdentifier(event.target.value)} placeholder="@username など" className="min-w-0 flex-1 text-[10px] px-2 py-1.5 bg-muted rounded border border-border" />
                  <button
                    onClick={async () => {
                      if (!newAccountTargetId || !newAccountDisplay.trim()) return;
                      const account = await createPostAccount(newAccountTargetId, newAccountDisplay.trim(), newAccountIdentifier.trim());
                      if (account) {
                        setAccounts((previous) => [...previous, account]);
                        setNewAccountDisplay("");
                        setNewAccountIdentifier("");
                      }
                    }}
                    className="text-[10px] px-2 rounded bg-primary text-primary-foreground"
                  >
                    追加
                  </button>
                </div>
                {accounts.map((account) => (
                  <div key={account.id} className="flex items-center gap-2 text-[10px] py-1">
                    <span className="truncate flex-1">{account.displayName}</span>
                    <button onClick={async () => { await deletePostAccount(account.id); setAccounts((previous) => previous.filter((item) => item.id !== account.id)); }} className="text-muted-foreground hover:text-destructive">削除</button>
                  </div>
                ))}
              </section>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

function SettingsPanel() {
  const [extensions, setExtensions] = useState<string[]>([]);
  const [extInput, setExtInput] = useState("");
  const [conflictPolicy, setConflictPolicy] = useState("abort");

  useEffect(() => {
    void getSupportedExtensions().then(setExtensions);
    void getSetting("conflictPolicy").then((value) => {
      if (value) setConflictPolicy(value.replace(/"/g, ""));
    });
  }, []);

  const handleAddExt = async () => {
    let ext = extInput.trim().toLowerCase();
    if (ext && !ext.startsWith(".")) ext = `.${ext}`;
    if (!ext || extensions.includes(ext)) return;
    const next = [...extensions, ext];
    await setSupportedExtensions(next);
    setExtensions(next);
    setExtInput("");
  };

  const handleRemoveExt = async (ext: string) => {
    const next = extensions.filter((item) => item !== ext);
    await setSupportedExtensions(next);
    setExtensions(next);
  };

  const handleConflictPolicy = async (policy: string) => {
    setConflictPolicy(policy);
    await setSetting("conflictPolicy", JSON.stringify(policy));
  };

  return (
    <div className="pb-3">
      <PanelHeader title="設定" description="画像の読み込み形式やファイル操作の設定です。" />
      <div className="px-3 space-y-4">
        <div className="rounded-xl border border-border bg-muted/30 p-3">
          <p className="text-xs font-medium">ストレージについて</p>
          <p className="mt-1 text-[10px] leading-relaxed text-muted-foreground">
            Lumineは表示用サムネイル画像をディスクへ保存しません。プレビューは必要な分だけメモリ上で生成します。
          </p>
        </div>

        <section className="space-y-2">
          <div>
            <p className="text-xs font-medium">読み込む拡張子</p>
            <p className="text-[10px] text-muted-foreground">ライブラリのスキャン対象にする画像形式です。</p>
          </div>
          <div className="flex flex-wrap gap-1">
            {extensions.map((ext) => (
              <span key={ext} className="text-[10px] px-2 py-1 bg-muted rounded-full border border-border flex items-center gap-1.5">
                {ext}
                <button onClick={() => void handleRemoveExt(ext)} className="text-muted-foreground hover:text-destructive" aria-label={`${ext}を削除`}>×</button>
              </span>
            ))}
          </div>
          <div className="flex gap-1.5">
            <input
              value={extInput}
              onChange={(event) => setExtInput(event.target.value)}
              onKeyDown={(event) => event.key === "Enter" && void handleAddExt()}
              placeholder="例: .raw"
              className="min-w-0 flex-1 text-xs px-2.5 py-2 bg-muted rounded-lg border border-border focus:border-primary focus:outline-none"
            />
            <button onClick={() => void handleAddExt()} className="text-xs px-3 py-1 bg-primary text-primary-foreground rounded-lg">追加</button>
          </div>
        </section>

        <section className="space-y-2">
          <div>
            <p className="text-xs font-medium">同名ファイルがある場合</p>
            <p className="text-[10px] text-muted-foreground">画像を移動するときの競合処理です。</p>
          </div>
          <select
            value={conflictPolicy}
            onChange={(event) => void handleConflictPolicy(event.target.value)}
            className="w-full text-xs px-2.5 py-2 bg-muted rounded-lg border border-border text-foreground"
          >
            <option value="abort">処理を中止する</option>
            <option value="skip">既存ファイルを残してスキップ</option>
            <option value="rename">自動で別名を付ける</option>
          </select>
        </section>
      </div>
    </div>
  );
}

export function Toolbar() {
  const { state, setState } = useApp();
  const [localSearch, setLocalSearch] = useState(state.searchQuery);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const selectedLibrary = state.libraries.find((library) => library.id === state.selectedLibraryId);
  const selectedFolderName = state.selectedFolderPath.split(/[\\/]/).pop() || state.selectedFolderPath;

  useEffect(() => {
    setLocalSearch(state.searchQuery);
  }, [state.searchQuery]);

  useEffect(() => () => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
  }, []);

  const handleSearchChange = (value: string) => {
    setLocalSearch(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setState((current) => ({ ...current, searchQuery: value }));
    }, 250);
  };

  return (
    <header className="border-b border-border bg-card/70 backdrop-blur-sm flex-shrink-0">
      <div className="h-12 flex items-center gap-2 px-3 overflow-x-auto">
        <button
          onClick={() => setState((current) => ({ ...current, sidebarOpen: !current.sidebarOpen }))}
          className="h-8 w-8 flex items-center justify-center rounded-lg hover:bg-accent text-muted-foreground hover:text-foreground transition-colors flex-shrink-0"
          title={state.sidebarOpen ? "サイドバーを閉じる" : "サイドバーを開く"}
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5" />
          </svg>
        </button>

        <div className="min-w-[100px] max-w-[180px] flex-shrink-0">
          <p className="text-[9px] text-muted-foreground">表示中</p>
          <p className="text-xs font-medium truncate" title={selectedLibrary?.rootPath}>{selectedLibrary?.name ?? "ライブラリ未選択"}</p>
        </div>

        <div className="h-6 w-px bg-border flex-shrink-0" />

        <div className="relative min-w-[220px] flex-1 max-w-xl">
          <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
          </svg>
          <input
            type="text"
            placeholder="ファイル名・メモを検索…"
            value={localSearch}
            onChange={(event) => handleSearchChange(event.target.value)}
            className="w-full pl-9 pr-3 py-2 text-xs bg-muted rounded-lg border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/60"
          />
        </div>

        <select
          value={state.sortBy}
          onChange={(event) => setState((current) => ({ ...current, sortBy: event.target.value }))}
          className="h-8 text-[11px] px-2 bg-muted rounded-lg border border-border text-foreground flex-shrink-0"
          title="並び順"
        >
          <option value="modifiedAtFs">更新日時</option>
          <option value="created">作成日時</option>
          <option value="name">ファイル名</option>
          <option value="size">ファイルサイズ</option>
          <option value="rating">評価</option>
          <option value="status">状態</option>
        </select>

        <button
          onClick={() => setState((current) => ({ ...current, sortDesc: !current.sortDesc }))}
          className="h-8 px-2 rounded-lg bg-muted border border-border text-[10px] text-muted-foreground hover:text-foreground flex-shrink-0"
          title={state.sortDesc ? "降順" : "昇順"}
        >
          {state.sortDesc ? "降順 ↓" : "昇順 ↑"}
        </button>

        <select
          value={state.filterStatusLabel}
          onChange={(event) => setState((current) => ({ ...current, filterStatusLabel: event.target.value }))}
          className="h-8 text-[11px] px-2 bg-muted rounded-lg border border-border text-foreground flex-shrink-0"
          title="状態で絞り込み"
        >
          <option value="">状態: すべて</option>
          <option value="unsorted">未整理</option>
          <option value="reviewed">確認済み</option>
          <option value="candidate">候補</option>
          <option value="published">公開済み</option>
        </select>

        <select
          value={state.filterRating}
          onChange={(event) => setState((current) => ({ ...current, filterRating: Number(event.target.value) }))}
          className="h-8 text-[11px] px-2 bg-muted rounded-lg border border-border text-foreground flex-shrink-0"
          title="評価で絞り込み"
        >
          <option value={0}>評価: すべて</option>
          {[1, 2, 3, 4, 5].map((rating) => <option key={rating} value={rating}>{"★".repeat(rating)}</option>)}
        </select>

        <div className="flex items-center rounded-lg border border-border bg-muted p-0.5 flex-shrink-0" title="画像の大きさ">
          {[
            { value: 120, label: "小" },
            { value: 180, label: "中" },
            { value: 260, label: "大" },
          ].map((option) => (
            <button
              key={option.value}
              onClick={() => setState((current) => ({ ...current, thumbnailSize: option.value }))}
              className={`h-6 min-w-7 px-1.5 rounded-md text-[10px] transition-colors ${
                state.thumbnailSize === option.value ? "bg-background text-foreground shadow-sm" : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {option.label}
            </button>
          ))}
        </div>

        <div className="flex items-center rounded-lg border border-border bg-muted p-0.5 flex-shrink-0">
          <button
            onClick={() => setState((current) => ({ ...current, viewMode: "grid" }))}
            className={`h-6 px-2 rounded-md text-[10px] ${state.viewMode === "grid" ? "bg-background text-foreground shadow-sm" : "text-muted-foreground"}`}
          >
            グリッド
          </button>
          <button
            onClick={() => setState((current) => ({ ...current, viewMode: "list" }))}
            className={`h-6 px-2 rounded-md text-[10px] ${state.viewMode === "list" ? "bg-background text-foreground shadow-sm" : "text-muted-foreground"}`}
          >
            リスト
          </button>
        </div>
      </div>

      {(state.selectedFolderPath || state.searchQuery || state.filterStatusLabel || state.filterRating > 0) && (
        <div className="min-h-8 px-3 pb-2 flex items-center gap-1.5 flex-wrap">
          <span className="text-[10px] text-muted-foreground mr-1">絞り込み:</span>
          {state.selectedFolderPath && (
            <button
              onClick={() => setState((current) => ({ ...current, selectedFolderPath: "" }))}
              className="text-[10px] px-2 py-1 rounded-full bg-accent text-accent-foreground hover:opacity-80 max-w-[240px] truncate"
              title={`${state.selectedFolderPath} の絞り込みを解除`}
            >
              📁 {selectedFolderName} ×
            </button>
          )}
          {state.searchQuery && (
            <button
              onClick={() => setState((current) => ({ ...current, searchQuery: "" }))}
              className="text-[10px] px-2 py-1 rounded-full bg-accent text-accent-foreground hover:opacity-80 max-w-[240px] truncate"
            >
              検索: {state.searchQuery} ×
            </button>
          )}
          {state.filterStatusLabel && (
            <button onClick={() => setState((current) => ({ ...current, filterStatusLabel: "" }))} className="text-[10px] px-2 py-1 rounded-full bg-accent text-accent-foreground hover:opacity-80">状態フィルター ×</button>
          )}
          {state.filterRating > 0 && (
            <button onClick={() => setState((current) => ({ ...current, filterRating: 0 }))} className="text-[10px] px-2 py-1 rounded-full bg-accent text-accent-foreground hover:opacity-80">評価 ★{state.filterRating} ×</button>
          )}
          <button
            onClick={() => setState((current) => ({ ...current, selectedFolderPath: "", searchQuery: "", filterStatusLabel: "", filterRating: 0 }))}
            className="text-[10px] px-2 py-1 text-muted-foreground hover:text-foreground"
          >
            すべて解除
          </button>
        </div>
      )}

      {state.selectedAssets.size > 0 && (
        <div className="px-3 pb-2 flex items-center">
          <span className="text-[10px] text-primary font-medium">{state.selectedAssets.size}件を選択中</span>
          <span className="ml-2 text-[9px] text-muted-foreground">Ctrl: 複数選択 / Shift: 範囲選択 / ダブルクリック: 全画面表示</span>
        </div>
      )}
    </header>
  );
}
