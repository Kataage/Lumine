import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useApp } from "../App";
import {
  addLibrary,
  createPostAccount,
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
  listPostRecords,
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
import type { FolderDTO, PostAccountDTO, PostTargetDTO, ScanProgress } from "../api/client";

const NAV_ITEMS = [
  ["libraries", "ライブラリ", "画像フォルダー"],
  ["folders", "フォルダー", "階層で絞り込み"],
  ["tags", "タグ", "画像の分類"],
  ["posts", "投稿記録", "投稿先を確認"],
  ["settings", "設定", "読み込み・操作"],
] as const;

const TAG_MANAGER_VISIBLE_LIMIT = 200;
const tagNameCollator = new Intl.Collator("ja", { sensitivity: "base", numeric: true });

export function WelcomeScreenV2({ onSelectFolder, busy = false }: { onSelectFolder: () => void; busy?: boolean }) {
  return (
    <div className="h-screen bg-background flex items-center justify-center p-6">
      <div className="w-full max-w-xl rounded-3xl border border-border bg-card p-8 sm:p-10 text-center shadow-2xl">
        <div className="w-16 h-16 mx-auto rounded-2xl bg-primary text-primary-foreground flex items-center justify-center text-xl font-bold">L</div>
        <h1 className="mt-5 text-2xl font-bold">Lumine</h1>
        <p className="mt-2 text-sm text-muted-foreground">画像を軽快に探し、確認し、投稿先まで記録する画像ライブラリ</p>
        <div className="mt-7 rounded-2xl border border-border bg-muted/30 p-4 text-left">
          <p className="text-sm font-medium">最初に画像フォルダーを登録してください</p>
          <p className="mt-1 text-xs leading-relaxed text-muted-foreground">画像そのものをコピーせず、表示用サムネイルもディスクへ保存しません。</p>
        </div>
        <button onClick={onSelectFolder} disabled={busy} className="ui-primary-button mt-6 w-full justify-center h-11 text-sm">
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
  const timers = useRef<number[]>([]);

  useEffect(() => {
    onScanProgress((progress) => {
      setScanProgress((current) => ({ ...current, [progress.libraryId]: progress }));
      if (!progress.isDone) return;
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: ["assets", progress.libraryId] }),
        queryClient.invalidateQueries({ queryKey: ["folderTree", progress.libraryId] }),
        listLibraries().then((libraries) => setState((current) => ({ ...current, libraries }))),
      ]);
      const timer = window.setTimeout(() => setScanProgress((current) => {
        const next = { ...current };
        delete next[progress.libraryId];
        return next;
      }), 1500);
      timers.current.push(timer);
    });
    return () => {
      offScanProgress();
      timers.current.forEach(window.clearTimeout);
    };
  }, [queryClient, setState]);

  return (
    <aside className="app-sidebar border-r border-border bg-card flex flex-col flex-shrink-0 overflow-hidden">
      <div className="h-14 px-3.5 border-b border-border flex items-center gap-2.5 flex-shrink-0">
        <div className="w-8 h-8 rounded-lg bg-primary text-primary-foreground flex items-center justify-center font-bold">L</div>
        <div className="min-w-0">
          <p className="text-sm font-bold">Lumine</p>
          <p className="text-[11px] text-muted-foreground">画像ライブラリ</p>
        </div>
      </div>

      <nav className="p-2 border-b border-border grid grid-cols-1 gap-1 flex-shrink-0">
        {NAV_ITEMS.map(([key, label, description]) => (
          <button
            key={key}
            onClick={() => setState((current) => ({ ...current, sidebarView: key }))}
            className={`min-h-10 px-2.5 rounded-lg flex items-center gap-2 text-left ${state.sidebarView === key ? "bg-primary/10 text-foreground" : "text-muted-foreground hover:bg-accent/50 hover:text-foreground"}`}
          >
            <span className="w-2 h-2 rounded-full border border-current flex-shrink-0" />
            <span className="min-w-0">
              <span className="block text-xs font-medium">{label}</span>
              <span className="block text-[10px] truncate opacity-70">{description}</span>
            </span>
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

function PanelTitle({ title, description, action }: { title: string; description: string; action?: React.ReactNode }) {
  return (
    <div className="px-3 pt-3 pb-2 flex items-start justify-between gap-2">
      <div className="min-w-0"><p className="text-xs font-semibold">{title}</p><p className="text-[11px] text-muted-foreground leading-relaxed">{description}</p></div>
      {action}
    </div>
  );
}

function LibrariesPanel({ scanProgress }: { scanProgress: Record<number, ScanProgress> }) {
  const { state, setState } = useApp();
  const add = async () => {
    const path = await selectFolder();
    if (!path) return;
    const name = path.split(/[/\\]/).pop() || "画像フォルダー";
    const library = await addLibrary(name, path);
    if (!library) return;
    const libraries = await listLibraries();
    setState((current) => ({ ...current, libraries, selectedLibraryId: library.id, selectedFolderPath: "", searchQuery: "" }));
    await scanLibrary(library.id);
  };

  return (
    <div className="pb-3">
      <PanelTitle title="ライブラリ" description="画像フォルダーを登録して一覧化します。" action={<button className="ui-secondary-button" onClick={() => void add()}>＋ 追加</button>} />
      <div className="px-2 space-y-1.5">
        {state.libraries.map((library) => {
          const selected = state.selectedLibraryId === library.id;
          const progress = scanProgress[library.id];
          return (
            <div key={library.id} className={`rounded-xl border p-2.5 ${selected ? "border-primary/40 bg-primary/10" : "border-border bg-muted/15"}`}>
              <button className="w-full text-left min-w-0" onClick={() => setState((current) => ({ ...current, selectedLibraryId: library.id, selectedFolderPath: "", selectedAssets: new Set(), detailOpen: false, detailAsset: null }))}>
                <p className="text-xs font-medium truncate">{library.name}</p>
                <p className="mt-0.5 text-[10px] text-muted-foreground truncate">{library.rootPath}</p>
              </button>
              {progress && !progress.isDone && <p className="mt-2 text-[10px] text-muted-foreground">スキャン中… {progress.scannedCount.toLocaleString()}件</p>}
              <div className="mt-2 flex flex-wrap gap-1.5">
                <button className="ui-mini-button" onClick={() => void scanLibrary(library.id)} disabled={!library.isEnabled || !!progress}>再スキャン</button>
                <button className="ui-mini-button" onClick={async () => {
                  if (library.isEnabled) await disableLibrary(library.id); else await enableLibrary(library.id);
                  const libraries = await listLibraries(); setState((current) => ({ ...current, libraries }));
                }}>{library.isEnabled ? "無効化" : "有効化"}</button>
                <button className="ui-mini-button text-destructive" onClick={async () => {
                  if (!confirm(`「${library.name}」の登録を解除しますか？\n元画像は削除されません。`)) return;
                  await removeLibrary(library.id);
                  const libraries = await listLibraries();
                  setState((current) => ({ ...current, libraries, selectedLibraryId: current.selectedLibraryId === library.id ? (libraries[0]?.id ?? null) : current.selectedLibraryId, selectedFolderPath: "", detailOpen: false, detailAsset: null }));
                }}>登録解除</button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function FoldersPanel() {
  const { state, setState } = useApp();
  const { data: folders = [] } = useQuery({ queryKey: ["folderTree", state.selectedLibraryId], queryFn: () => getFolderTree(state.selectedLibraryId!), enabled: !!state.selectedLibraryId, staleTime: Infinity });
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const roots = folders.filter((folder) => !folder.parentPath);
  const children = (path: string) => folders.filter((folder) => folder.parentPath === path);

  const renderFolder = (folder: FolderDTO, depth: number): React.ReactNode => {
    const nested = children(folder.path);
    const open = expanded.has(folder.path);
    const selected = state.selectedFolderPath === folder.path;
    const name = folder.path.split(/[\\/]/).pop() || folder.path;
    return (
      <div key={folder.path}>
        <div className={`flex items-center rounded-lg ${selected ? "bg-primary/10" : "hover:bg-accent/50"}`} style={{ paddingLeft: depth * 10 }}>
          <button className="w-7 h-8 text-[10px] flex-shrink-0" onClick={() => setExpanded((current) => { const next = new Set(current); if (next.has(folder.path)) next.delete(folder.path); else next.add(folder.path); return next; })} disabled={!nested.length}>{nested.length ? (open ? "▼" : "▶") : ""}</button>
          <button className="min-w-0 flex-1 h-8 text-left text-[11px] truncate pr-2" title={folder.path} onClick={() => setState((current) => ({ ...current, selectedFolderPath: folder.path }))}>{name}</button>
        </div>
        {open && nested.map((item) => renderFolder(item, depth + 1))}
      </div>
    );
  };

  return (
    <div className="pb-3">
      <PanelTitle title="フォルダー" description="選択したフォルダー以下をまとめて表示します。" />
      <div className="px-2">
        <button className={`w-full h-9 px-2.5 text-left rounded-lg text-xs ${!state.selectedFolderPath ? "bg-primary/10 font-medium" : "hover:bg-accent/50"}`} onClick={() => setState((current) => ({ ...current, selectedFolderPath: "" }))}>すべての画像</button>
        <div className="mt-1">{roots.map((folder) => renderFolder(folder, 0))}</div>
      </div>
    </div>
  );
}

function TagsPanel() {
  const queryClient = useQueryClient();
  const { data: tags = [] } = useQuery({ queryKey: ["tags"], queryFn: listTags, staleTime: Infinity });
  const [name, setName] = useState("");
  const [color, setColor] = useState("#6366f1");
  const [search, setSearch] = useState("");
  const searchKey = search.trim().toLocaleLowerCase("ja-JP");
  const filteredTags = useMemo(() => {
    const next = tags.filter((tag) => !searchKey || tag.name.toLocaleLowerCase("ja-JP").includes(searchKey));
    next.sort((a, b) => tagNameCollator.compare(a.name, b.name));
    return next;
  }, [searchKey, tags]);
  const visibleTags = filteredTags.slice(0, TAG_MANAGER_VISIBLE_LIMIT);

  return (
    <div className="pb-3">
      <PanelTitle title="タグ" description={`画像の分類用タグを管理します。現在 ${tags.length}件`} />
      <div className="px-3 space-y-3">
        <div className="rounded-xl border border-border bg-muted/15 p-2.5 space-y-2">
          <p className="text-[10px] font-medium text-muted-foreground">新しいタグ</p>
          <div className="grid grid-cols-[minmax(0,1fr)_36px_auto] gap-2">
            <input className="ui-input min-w-0" value={name} onChange={(event) => setName(event.target.value)} placeholder="タグ名" />
            <input type="color" className="w-9 h-9 rounded-lg border border-border bg-transparent" value={color} onChange={(event) => setColor(event.target.value)} />
            <button className="ui-primary-button" onClick={async () => { if (!name.trim()) return; await createTag(name.trim(), color); setName(""); await queryClient.invalidateQueries({ queryKey: ["tags"] }); }}>追加</button>
          </div>
        </div>

        <div className="space-y-1.5">
          <label className="ui-label" htmlFor="tag-manager-search">タグを検索</label>
          <input id="tag-manager-search" className="ui-input w-full" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="タグ名で絞り込み…" autoComplete="off" />
          <div className="flex items-center justify-between text-[10px] text-muted-foreground">
            <span>{searchKey ? `${filteredTags.length}件ヒット` : `${tags.length}件`}</span>
            {filteredTags.length > TAG_MANAGER_VISIBLE_LIMIT && <span>上位{TAG_MANAGER_VISIBLE_LIMIT}件を表示</span>}
          </div>
        </div>

        <div className="max-h-[52vh] overflow-y-auto rounded-lg border border-border p-1 space-y-0.5">
          {visibleTags.map((tag) => (
            <div key={tag.id} className="min-h-9 px-2 rounded-lg flex items-center gap-2 hover:bg-accent/50">
              <span className="w-3 h-3 rounded-full flex-shrink-0" style={{ backgroundColor: tag.color }} />
              <span className="text-xs min-w-0 flex-1 truncate" title={tag.name}>{tag.name}</span>
              <button
                className="text-[11px] text-muted-foreground hover:text-destructive"
                onClick={async () => {
                  if (!confirm(`タグ「${tag.name}」を削除しますか？\n画像へのタグ付けも解除されます。`)) return;
                  await deleteTag(tag.id);
                  await queryClient.invalidateQueries({ queryKey: ["tags"] });
                }}
              >削除</button>
            </div>
          ))}
          {visibleTags.length === 0 && <p className="py-5 text-center text-[11px] text-muted-foreground">該当するタグはありません</p>}
        </div>
        {filteredTags.length > TAG_MANAGER_VISIBLE_LIMIT && <p className="text-[10px] leading-relaxed text-muted-foreground">タグ数が多いため先頭{TAG_MANAGER_VISIBLE_LIMIT}件だけ表示しています。検索欄へ数文字入力すると残りもすぐ探せます。</p>}
      </div>
    </div>
  );
}

function PostRecordsPanel() {
  const queryClient = useQueryClient();
  const { data: records = [] } = useQuery({ queryKey: ["postRecords"], queryFn: () => listPostRecords(0, 100), staleTime: Infinity });
  const [targets, setTargets] = useState<PostTargetDTO[]>([]);
  const [accounts, setAccounts] = useState<PostAccountDTO[]>([]);
  const [showSettings, setShowSettings] = useState(false);
  const [targetName, setTargetName] = useState("");
  const [targetKind, setTargetKind] = useState("pixiv");
  const [accountTargetId, setAccountTargetId] = useState(0);
  const [accountName, setAccountName] = useState("");
  const [accountIdentifier, setAccountIdentifier] = useState("");

  const reloadSettings = async () => {
    const [nextTargets, nextAccounts] = await Promise.all([listPostTargets(), listPostAccounts()]);
    setTargets(nextTargets ?? []); setAccounts(nextAccounts ?? []);
    setAccountTargetId((current) => current || nextTargets?.[0]?.id || 0);
  };
  useEffect(() => { void reloadSettings(); }, []);

  return (
    <div className="pb-3">
      <PanelTitle title="投稿記録" description="どの画像を、どの投稿先・アカウントへ登録したか確認します。" />
      <div className="px-3 space-y-3">
        <div className="rounded-xl border border-border bg-muted/20 p-3 text-[11px] leading-relaxed text-muted-foreground">記録の追加は、画像を選択して右の詳細パネル、または複数選択時の「＋ 投稿記録」から行えます。</div>
        <div className="space-y-2">
          {records.map((record) => (
            <div key={record.id} className="rounded-xl border border-border bg-muted/15 p-2.5">
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1">
                  <p className="text-xs font-medium truncate">{record.targetName} · {record.accountDisplay}</p>
                  <p className="mt-0.5 text-[10px] text-muted-foreground truncate">{record.title || "投稿記録"} · 画像{record.assetIds.length}件</p>
                </div>
                <button className="text-[10px] text-muted-foreground hover:text-destructive" onClick={async () => { if (!confirm("この投稿記録を削除しますか？")) return; await deletePost(record.id); await Promise.all([queryClient.invalidateQueries({ queryKey: ["postRecords"] }), queryClient.invalidateQueries({ queryKey: ["assetPostRecords"] })]); }}>削除</button>
              </div>
              {record.assets.length > 0 && (
                <div className="mt-2 space-y-1">
                  {record.assets.slice(0, 3).map((asset) => (
                    <div key={asset.id} className="h-6 px-2 rounded-md bg-background/60 flex items-center gap-1.5" title={asset.filePath}>
                      <span className="text-[10px] text-muted-foreground">画像</span>
                      <span className="min-w-0 flex-1 truncate text-[10px]">{asset.fileName}</span>
                    </div>
                  ))}
                  {record.assets.length > 3 && <p className="pl-2 text-[10px] text-muted-foreground">ほか {record.assets.length - 3}件</p>}
                </div>
              )}
              <p className="mt-1.5 text-[10px] text-muted-foreground">{record.publishedAt ? new Date(record.publishedAt).toLocaleString("ja-JP") : ""}</p>
              {record.externalPostId && <p className="mt-1 text-[10px] text-primary break-all">{record.externalPostId}</p>}
            </div>
          ))}
          {records.length === 0 && <p className="py-4 text-xs text-center text-muted-foreground">投稿記録はまだありません</p>}
        </div>

        <button className="ui-secondary-button w-full justify-between" onClick={() => setShowSettings((value) => !value)}><span>投稿先・アカウント設定</span><span>{showSettings ? "▲" : "▼"}</span></button>
        {showSettings && (
          <div className="space-y-4 rounded-xl border border-border p-3">
            <div className="space-y-2">
              <p className="ui-label">投稿先</p>
              <div className="grid grid-cols-[minmax(0,1fr)_90px_auto] gap-1.5">
                <input className="ui-input min-w-0" value={targetName} onChange={(event) => setTargetName(event.target.value)} placeholder="Pixivなど" />
                <select className="ui-input" value={targetKind} onChange={(event) => setTargetKind(event.target.value)}><option value="pixiv">Pixiv</option><option value="twitter">X</option><option value="misskey">Misskey</option><option value="bluesky">Bluesky</option><option value="other">その他</option></select>
                <button className="ui-primary-button" onClick={async () => { if (!targetName.trim()) return; await createPostTarget(targetName.trim(), targetKind); setTargetName(""); await reloadSettings(); }}>追加</button>
              </div>
              {targets.map((target) => <div key={target.id} className="flex items-center gap-2 text-[11px]"><span className="min-w-0 flex-1 truncate">{target.name} ({target.kind})</span><button className="text-muted-foreground hover:text-destructive" onClick={async () => { if (!confirm(`投稿先「${target.name}」を削除すると、その投稿先を使った既存の投稿記録との紐付けも失われます。\n\n本当に削除しますか？`)) return; await deletePostTarget(target.id); await reloadSettings(); await queryClient.invalidateQueries({ queryKey: ["postRecords"] }); }}>削除</button></div>)}
            </div>
            <div className="space-y-2 border-t border-border pt-3">
              <p className="ui-label">アカウント</p>
              <select className="ui-input w-full" value={accountTargetId} onChange={(event) => setAccountTargetId(Number(event.target.value))}>{targets.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}</select>
              <input className="ui-input w-full" value={accountName} onChange={(event) => setAccountName(event.target.value)} placeholder="表示名" />
              <div className="flex gap-1.5"><input className="ui-input min-w-0 flex-1" value={accountIdentifier} onChange={(event) => setAccountIdentifier(event.target.value)} placeholder="@username / ID" /><button className="ui-primary-button" onClick={async () => { if (!accountTargetId || !accountName.trim()) return; await createPostAccount(accountTargetId, accountName.trim(), accountIdentifier.trim()); setAccountName(""); setAccountIdentifier(""); await reloadSettings(); }}>追加</button></div>
              {accounts.map((account) => <div key={account.id} className="flex items-center gap-2 text-[11px]"><span className="min-w-0 flex-1 truncate">{account.displayName}{account.accountIdentifier ? ` · ${account.accountIdentifier}` : ""}</span><button className="text-muted-foreground hover:text-destructive" onClick={async () => { if (!confirm(`アカウント「${account.displayName}」を削除すると、そのアカウントを使った既存の投稿記録との紐付けも失われます。\n\n本当に削除しますか？`)) return; await deletePostAccount(account.id); await reloadSettings(); await queryClient.invalidateQueries({ queryKey: ["postRecords"] }); }}>削除</button></div>)}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function SettingsPanel() {
  const [extensions, setExtensions] = useState<string[]>([]);
  const [extension, setExtension] = useState("");
  const [conflictPolicy, setConflictPolicy] = useState("abort");
  useEffect(() => {
    void getSupportedExtensions().then((items) => setExtensions(items ?? []));
    void getSetting("conflictPolicy").then((value) => { if (value) setConflictPolicy(value.replace(/"/g, "")); });
  }, []);
  return (
    <div className="pb-3">
      <PanelTitle title="設定" description="読み込み対象やファイル操作を設定します。" />
      <div className="px-3 space-y-4">
        <section className="space-y-2">
          <p className="ui-label">対応拡張子</p>
          <div className="flex flex-wrap gap-1">{extensions.map((item) => <span key={item} className="h-7 px-2 rounded-full bg-muted border border-border text-[10px] inline-flex items-center gap-1">{item}<button onClick={async () => { const next = extensions.filter((value) => value !== item); await setSupportedExtensions(next); setExtensions(next); }}>×</button></span>)}</div>
          <div className="flex gap-2"><input className="ui-input min-w-0 flex-1" value={extension} onChange={(event) => setExtension(event.target.value)} placeholder="例: .raw" /><button className="ui-primary-button" onClick={async () => { const nextValue = extension.trim().toLowerCase(); if (!nextValue || extensions.includes(nextValue)) return; const next = [...extensions, nextValue]; await setSupportedExtensions(next); setExtensions(next); setExtension(""); }}>追加</button></div>
        </section>
        <section className="space-y-2 border-t border-border pt-3">
          <p className="ui-label">同名ファイルがある場合</p>
          <select className="ui-input w-full" value={conflictPolicy} onChange={async (event) => { setConflictPolicy(event.target.value); await setSetting("conflictPolicy", JSON.stringify(event.target.value)); }}><option value="abort">処理を中止</option><option value="skip">既存を残してスキップ</option><option value="rename">自動で別名</option></select>
        </section>
      </div>
    </div>
  );
}

export function ToolbarV2() {
  const { state, setState } = useApp();
  const [search, setSearch] = useState(state.searchQuery);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const library = state.libraries.find((item) => item.id === state.selectedLibraryId);
  const folderName = state.selectedFolderPath.split(/[\\/]/).pop() || "";
  useEffect(() => setSearch(state.searchQuery), [state.searchQuery]);
  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);

  const updateSearch = (value: string) => {
    setSearch(value);
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setState((current) => ({ ...current, searchQuery: value })), 250);
  };

  const hasFilters = !!(state.selectedFolderPath || state.searchQuery || state.filterStatusLabel || state.filterRating);
  return (
    <header className="toolbar-v2 border-b border-border bg-card/80 flex-shrink-0">
      <div className="toolbar-primary-row">
        <button className="ui-icon-button" onClick={() => setState((current) => ({ ...current, sidebarOpen: !current.sidebarOpen }))} aria-label="サイドバー切り替え">☰</button>
        <div className="toolbar-library min-w-0"><p className="text-[10px] text-muted-foreground">表示中</p><p className="text-xs font-medium truncate" title={library?.rootPath}>{library?.name ?? "未選択"}</p></div>
        <div className="relative min-w-0 flex-1">
          <input className="ui-input w-full pl-3" value={search} onChange={(event) => updateSearch(event.target.value)} placeholder="ファイル名・メモを検索…" />
        </div>
      </div>

      <div className="toolbar-controls-row">
        <select className="ui-input" value={state.sortBy} onChange={(event) => setState((current) => ({ ...current, sortBy: event.target.value }))}><option value="modifiedAtFs">更新日時</option><option value="created">作成日時</option><option value="name">ファイル名</option><option value="size">サイズ</option><option value="rating">評価</option><option value="status">状態</option></select>
        <button className="ui-secondary-button" onClick={() => setState((current) => ({ ...current, sortDesc: !current.sortDesc }))}>{state.sortDesc ? "降順 ↓" : "昇順 ↑"}</button>
        <select className="ui-input" value={state.filterStatusLabel} onChange={(event) => setState((current) => ({ ...current, filterStatusLabel: event.target.value }))}><option value="">状態: すべて</option><option value="unsorted">未整理</option><option value="reviewed">確認済み</option><option value="candidate">候補</option><option value="published">公開済み</option></select>
        <select className="ui-input" value={state.filterRating} onChange={(event) => setState((current) => ({ ...current, filterRating: Number(event.target.value) }))}><option value={0}>評価: すべて</option>{[1,2,3,4,5].map((rating) => <option key={rating} value={rating}>{"★".repeat(rating)}</option>)}</select>
        <div className="ui-segmented">{[[120,"小"],[180,"中"],[260,"大"]].map(([value,label]) => <button key={value} onClick={() => setState((current) => ({ ...current, thumbnailSize: Number(value) }))} className={state.thumbnailSize === Number(value) ? "active" : ""}>{label}</button>)}</div>
        <div className="ui-segmented"><button className={state.viewMode === "grid" ? "active" : ""} onClick={() => setState((current) => ({ ...current, viewMode: "grid" }))}>グリッド</button><button className={state.viewMode === "list" ? "active" : ""} onClick={() => setState((current) => ({ ...current, viewMode: "list" }))}>リスト</button></div>
        {state.selectedAssets.size > 0 && <span className="ml-auto text-[11px] font-medium text-primary whitespace-nowrap">{state.selectedAssets.size}件選択</span>}
      </div>

      {hasFilters && (
        <div className="toolbar-filter-row">
          <span className="text-[10px] text-muted-foreground">絞り込み</span>
          {state.selectedFolderPath && <button className="filter-chip" onClick={() => setState((current) => ({ ...current, selectedFolderPath: "" }))}>📁 {folderName} ×</button>}
          {state.searchQuery && <button className="filter-chip" onClick={() => setState((current) => ({ ...current, searchQuery: "" }))}>検索: {state.searchQuery} ×</button>}
          {state.filterStatusLabel && <button className="filter-chip" onClick={() => setState((current) => ({ ...current, filterStatusLabel: "" }))}>状態 ×</button>}
          {state.filterRating > 0 && <button className="filter-chip" onClick={() => setState((current) => ({ ...current, filterRating: 0 }))}>★{state.filterRating} ×</button>}
          <button className="text-[10px] text-muted-foreground hover:text-foreground" onClick={() => setState((current) => ({ ...current, selectedFolderPath: "", searchQuery: "", filterStatusLabel: "", filterRating: 0 }))}>すべて解除</button>
        </div>
      )}
    </header>
  );
}
