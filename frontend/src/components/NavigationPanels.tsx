import { useEffect, useMemo, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useApp } from "../App";
import {
  addLibrary, createTag, deleteTag, disableLibrary, enableLibrary, getFolderTree, getSetting, getSupportedExtensions,
  listLibraries, removeLibrary, scanLibrary, selectFolder, setSetting, setSupportedExtensions,
} from "../api/client";
import type { FolderDTO, ScanProgress } from "../api/client";

const TAG_MANAGER_VISIBLE_LIMIT = 200;
const tagNameCollator = new Intl.Collator("ja", { sensitivity: "base", numeric: true });

function PanelTitle({ title, description, action }: { title: string; description: string; action?: ReactNode }) {
  return (
    <div className="px-3 pt-3 pb-2 flex items-start justify-between gap-2">
      <div className="min-w-0">
        <p className="text-xs font-semibold">{title}</p>
        <p className="text-[11px] text-muted-foreground leading-relaxed">{description}</p>
      </div>
      {action}
    </div>
  );
}

export function LibrariesPanel({ scanProgress }: { scanProgress: Record<number, ScanProgress> }) {
  const { state, setState } = useApp();
  const [error, setError] = useState<string | null>(null);

  const add = async () => {
    setError(null);
    try {
      const path = await selectFolder();
      if (!path) return;
      const name = path.split(/[/\\]/).pop() || "画像フォルダー";
      const library = await addLibrary(name, path);
      if (!library) throw new Error("画像フォルダーを登録できませんでした");
      const libraries = await listLibraries();
      setState((current) => ({ ...current, libraries, selectedLibraryId: library.id, selectedFolderPath: "", searchQuery: "", filterTagIds: [] }));
      await scanLibrary(library.id);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  };

  return (
    <div className="pb-3">
      <PanelTitle title="ライブラリ" description="画像フォルダーを登録して一覧化します。" action={<button type="button" className="ui-secondary-button" onClick={() => void add()}>＋ 追加</button>} />
      {error && <div role="alert" className="mx-3 mb-2 rounded-lg border border-destructive/40 bg-destructive/10 px-2.5 py-2 text-[11px] text-destructive">{error}</div>}
      <div className="px-2 space-y-1.5">
        {state.libraries.map((library) => {
          const selected = state.selectedLibraryId === library.id;
          const progress = scanProgress[library.id];
          return (
            <div key={library.id} className={`rounded-xl border p-2.5 ${selected ? "border-primary/40 bg-primary/10" : "border-border bg-muted/15"}`}>
              <button type="button" className="w-full text-left min-w-0" onClick={() => setState((current) => ({ ...current, selectedLibraryId: library.id, selectedFolderPath: "", selectedAssets: new Set(), detailOpen: false, detailAsset: null, filterTagIds: [] }))}>
                <p className="text-xs font-medium truncate">{library.name}</p>
                <p className="mt-0.5 text-[10px] text-muted-foreground truncate">{library.rootPath}</p>
              </button>
              {progress && !progress.isDone && <p className="mt-2 text-[10px] text-muted-foreground">スキャン中… {progress.scannedCount.toLocaleString()}件</p>}
              <div className="mt-2 flex flex-wrap gap-1.5">
                <button type="button" className="ui-mini-button" onClick={() => void scanLibrary(library.id)} disabled={!library.isEnabled || !!progress}>再スキャン</button>
                <button type="button" className="ui-mini-button" onClick={async () => {
                  if (library.isEnabled) await disableLibrary(library.id); else await enableLibrary(library.id);
                  const libraries = await listLibraries();
                  setState((current) => ({ ...current, libraries }));
                }}>{library.isEnabled ? "無効化" : "有効化"}</button>
                <button type="button" className="ui-mini-button text-destructive" onClick={async () => {
                  if (!confirm(`「${library.name}」の登録を解除しますか？\n元画像は削除されません。`)) return;
                  await removeLibrary(library.id);
                  const libraries = await listLibraries();
                  setState((current) => ({ ...current, libraries, selectedLibraryId: current.selectedLibraryId === library.id ? (libraries[0]?.id ?? null) : current.selectedLibraryId, selectedFolderPath: "", detailOpen: false, detailAsset: null, filterTagIds: [] }));
                }}>登録解除</button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export function FoldersPanel() {
  const { state, setState } = useApp();
  const { data: folders = [] } = useQuery({ queryKey: ["folderTree", state.selectedLibraryId], queryFn: () => getFolderTree(state.selectedLibraryId!), enabled: !!state.selectedLibraryId, staleTime: Infinity });
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const roots = folders.filter((folder) => !folder.parentPath);
  const children = (path: string) => folders.filter((folder) => folder.parentPath === path);

  const renderFolder = (folder: FolderDTO, depth: number): ReactNode => {
    const nested = children(folder.path);
    const open = expanded.has(folder.path);
    const selected = state.selectedFolderPath === folder.path;
    const name = folder.path.split(/[\\/]/).pop() || folder.path;
    return (
      <div key={folder.path}>
        <div className={`flex items-center rounded-lg ${selected ? "bg-primary/10" : "hover:bg-accent/50"}`} style={{ paddingLeft: depth * 10 }}>
          <button type="button" className="w-7 h-8 text-[10px] flex-shrink-0" onClick={() => setExpanded((current) => { const next = new Set(current); if (next.has(folder.path)) next.delete(folder.path); else next.add(folder.path); return next; })} disabled={!nested.length}>{nested.length ? (open ? "▼" : "▶") : ""}</button>
          <button type="button" className="min-w-0 flex-1 h-8 text-left text-[11px] truncate pr-2" title={folder.path} onClick={() => setState((current) => ({ ...current, selectedFolderPath: folder.path }))}>{name}</button>
        </div>
        {open && nested.map((item) => renderFolder(item, depth + 1))}
      </div>
    );
  };

  return (
    <div className="pb-3">
      <PanelTitle title="フォルダー" description="選択したフォルダー以下をまとめて表示します。" />
      <div className="px-2">
        <button type="button" className={`w-full h-9 px-2.5 text-left rounded-lg text-xs ${!state.selectedFolderPath ? "bg-primary/10 font-medium" : "hover:bg-accent/50"}`} onClick={() => setState((current) => ({ ...current, selectedFolderPath: "" }))}>すべての画像</button>
        <div className="mt-1">{roots.map((folder) => renderFolder(folder, 0))}</div>
      </div>
    </div>
  );
}

export function TagsPanel() {
  const queryClient = useQueryClient();
  const { state, setState } = useApp();
  const { data: tags = [] } = useQuery({ queryKey: ["tags"], queryFn: listTags, staleTime: Infinity });
  const [name, setName] = useState("");
  const [color, setColor] = useState("#6366f1");
  const [search, setSearch] = useState("");
  const searchKey = search.trim().toLocaleLowerCase("ja-JP");
  const filteredTags = useMemo(() => {
    const next = tags.filter((tag) => !searchKey || tag.name.toLocaleLowerCase("ja-JP").includes(searchKey));
    next.sort((a, b) => {
      const aActive = state.filterTagIds.includes(a.id);
      const bActive = state.filterTagIds.includes(b.id);
      if (aActive !== bActive) return aActive ? -1 : 1;
      return tagNameCollator.compare(a.name, b.name);
    });
    return next;
  }, [searchKey, state.filterTagIds, tags]);
  const visibleTags = filteredTags.slice(0, TAG_MANAGER_VISIBLE_LIMIT);

  const create = async (event: FormEvent) => {
    event.preventDefault();
    if (!name.trim()) return;
    await createTag(name.trim(), color);
    setName("");
    await queryClient.invalidateQueries({ queryKey: ["tags"] });
  };

  const toggleFilterTag = (tagId: number) => setState((current) => ({
    ...current,
    filterTagIds: current.filterTagIds.includes(tagId) ? current.filterTagIds.filter((id) => id !== tagId) : [...current.filterTagIds, tagId],
    selectedAssets: new Set(),
    lastSelectedIndex: null,
    detailOpen: false,
    detailAsset: null,
  }));

  return (
    <div className="pb-3">
      <PanelTitle title="タグ" description={`クリックで画像を絞り込み。現在 ${tags.length}件`} />
      <div className="px-3 space-y-3">
        <form className="rounded-xl border border-border bg-muted/15 p-2.5 space-y-2" onSubmit={create}>
          <p className="text-[10px] font-medium text-muted-foreground">新しいタグ</p>
          <div className="grid grid-cols-[minmax(0,1fr)_36px_auto] gap-2">
            <input className="ui-input min-w-0" value={name} onChange={(event) => setName(event.target.value)} placeholder="タグ名" />
            <input type="color" className="w-9 h-9 rounded-lg border border-border bg-transparent" value={color} onChange={(event) => setColor(event.target.value)} />
            <button type="submit" className="ui-primary-button">追加</button>
          </div>
        </form>
        <div className="space-y-1.5">
          <label className="ui-label" htmlFor="tag-manager-search">タグを検索</label>
          <input id="tag-manager-search" className="ui-input w-full" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="タグ名で絞り込み…" autoComplete="off" />
          <div className="flex items-center justify-between text-[10px] text-muted-foreground"><span>{searchKey ? `${filteredTags.length}件ヒット` : `${tags.length}件`}{state.filterTagIds.length > 0 ? ` · ${state.filterTagIds.length}件で絞り込み中` : ""}</span>{filteredTags.length > TAG_MANAGER_VISIBLE_LIMIT && <span>上位{TAG_MANAGER_VISIBLE_LIMIT}件を表示</span>}</div>
        </div>
        <div className="max-h-[52vh] overflow-y-auto rounded-lg border border-border p-1 space-y-0.5">
          {visibleTags.map((tag) => {
            const active = state.filterTagIds.includes(tag.id);
            return (
              <div key={tag.id} className={`min-h-9 rounded-lg flex items-center gap-1 ${active ? "bg-primary/12 ring-1 ring-primary/25" : "hover:bg-accent/50"}`}>
                <button type="button" className="min-w-0 flex-1 h-9 px-2 flex items-center gap-2 text-left" onClick={() => toggleFilterTag(tag.id)}>
                  <span className="w-3 h-3 rounded-full flex-shrink-0" style={{ backgroundColor: tag.color }} />
                  <span className="text-xs min-w-0 flex-1 truncate">{tag.name}</span>
                  <span className={`text-[10px] ${active ? "text-primary font-medium" : "text-muted-foreground"}`}>{active ? "✓ 絞込中" : "絞込"}</span>
                </button>
                <button type="button" className="h-9 px-2 text-[11px] text-muted-foreground hover:text-destructive flex-shrink-0" onClick={async () => {
                  if (!confirm(`タグ「${tag.name}」を削除しますか？\n画像へのタグ付けも解除されます。`)) return;
                  await deleteTag(tag.id);
                  setState((current) => ({ ...current, filterTagIds: current.filterTagIds.filter((id) => id !== tag.id) }));
                  await Promise.all([queryClient.invalidateQueries({ queryKey: ["tags"] }), queryClient.invalidateQueries({ queryKey: ["assets"] })]);
                }}>削除</button>
              </div>
            );
          })}
          {visibleTags.length === 0 && <p className="py-5 text-center text-[11px] text-muted-foreground">該当するタグはありません</p>}
        </div>
      </div>
    </div>
  );
}

export function SettingsPanel() {
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
          <div className="flex flex-wrap gap-1">{extensions.map((item) => <span key={item} className="h-7 px-2 rounded-full bg-muted border border-border text-[10px] inline-flex items-center gap-1">{item}<button type="button" onClick={async () => { const next = extensions.filter((value) => value !== item); await setSupportedExtensions(next); setExtensions(next); }}>×</button></span>)}</div>
          <div className="flex gap-2"><input className="ui-input min-w-0 flex-1" value={extension} onChange={(event) => setExtension(event.target.value)} placeholder="例: .raw" /><button type="button" className="ui-primary-button" onClick={async () => { const nextValue = extension.trim().toLowerCase(); if (!nextValue || extensions.includes(nextValue)) return; const next = [...extensions, nextValue]; await setSupportedExtensions(next); setExtensions(next); setExtension(""); }}>追加</button></div>
        </section>
        <section className="space-y-2 border-t border-border pt-3">
          <p className="ui-label">同名ファイルがある場合</p>
          <select className="ui-input w-full" value={conflictPolicy} onChange={async (event) => { setConflictPolicy(event.target.value); await setSetting("conflictPolicy", JSON.stringify(event.target.value)); }}><option value="abort">処理を中止</option><option value="skip">既存を残してスキップ</option><option value="rename">自動で別名</option></select>
        </section>
      </div>
    </div>
  );
}
