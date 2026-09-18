import { useEffect, useMemo, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useApp } from "../App";
import {
  addLibrary, createTag, deleteTag, disableLibrary, enableLibrary, getFolderTree, getSetting, getSupportedExtensions,
  listLibraries, listTags, removeLibrary, scanLibrary, selectFolder, setSetting, setSupportedExtensions,
} from "../api/client";
import type { FolderDTO, ScanProgress } from "../api/client";
import { useAppDialog } from "./AppDialogProvider";

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
  const dialog = useAppDialog();
  const [error, setError] = useState<string | null>(null);
  const [managingId, setManagingId] = useState<number | null>(null);

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

  const refreshLibraries = async () => {
    const libraries = await listLibraries();
    setState((current) => ({ ...current, libraries }));
    return libraries;
  };

  return (
    <div className="pb-3">
      <PanelTitle
        title="ライブラリ"
        description="普段は選択だけ。管理操作は必要な時だけ開けます。"
        action={<button type="button" className="ui-primary-button" onClick={() => void add()}>＋ 追加</button>}
      />
      {error && <div role="alert" className="mx-3 mb-2 rounded-lg border border-destructive/40 bg-destructive/10 px-2.5 py-2 text-[11px] text-destructive">{error}</div>}

      <div className="px-2 space-y-1.5">
        {state.libraries.map((library) => {
          const selected = state.selectedLibraryId === library.id;
          const progress = scanProgress[library.id];
          const managing = managingId === library.id;

          return (
            <div key={library.id} className={`rounded-xl border overflow-hidden ${selected ? "border-primary/35 bg-primary/8" : "border-border bg-muted/10"}`}>
              <button
                type="button"
                className="w-full px-2.5 py-2.5 text-left min-w-0 hover:bg-accent/35"
                onClick={() => {
                  setManagingId(null);
                  setState((current) => ({ ...current, selectedLibraryId: library.id, selectedFolderPath: "", selectedAssets: new Set(), detailOpen: false, detailAsset: null, filterTagIds: [] }));
                }}
              >
                <div className="flex items-center gap-2">
                  <span className={`w-2 h-2 rounded-full flex-shrink-0 ${library.isEnabled ? "bg-emerald-400" : "bg-muted-foreground/35"}`} />
                  <p className="text-xs font-medium truncate flex-1">{library.name}</p>
                  {selected && <span className="text-[9px] text-primary font-semibold">表示中</span>}
                </div>
                <p className="mt-1 pl-4 text-[10px] text-muted-foreground truncate" title={library.rootPath}>{library.rootPath}</p>
                {progress && !progress.isDone && <p className="mt-1.5 pl-4 text-[10px] text-muted-foreground">スキャン中… {progress.scannedCount.toLocaleString()}件</p>}
              </button>

              {selected && (
                <div className="border-t border-border/70 px-2 py-1.5 flex items-center gap-1.5">
                  <button type="button" className="ui-mini-button flex-1" onClick={() => void scanLibrary(library.id)} disabled={!library.isEnabled || !!progress}>再スキャン</button>
                  <button type="button" className={`ui-mini-button flex-1 ${managing ? "bg-accent text-foreground" : ""}`} onClick={() => setManagingId(managing ? null : library.id)} aria-expanded={managing}>管理</button>
                </div>
              )}

              {selected && managing && (
                <div className="border-t border-border/70 bg-background/30 px-2.5 py-2.5 space-y-2">
                  <p className="text-[10px] leading-relaxed text-muted-foreground">日常操作では使わないライブラリ設定です。</p>
                  <button type="button" className="ui-secondary-button w-full justify-center" onClick={async () => {
                    try {
                      if (library.isEnabled) await disableLibrary(library.id); else await enableLibrary(library.id);
                      await refreshLibraries();
                    } catch (cause) {
                      await dialog.notify({ title: "ライブラリの状態を変更できませんでした", description: "有効/無効の切り替えに失敗しました。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
                    }
                  }}>{library.isEnabled ? "ライブラリを無効化" : "ライブラリを有効化"}</button>
                  <button type="button" className="w-full h-8 rounded-lg border border-destructive/35 bg-destructive/8 text-[10px] font-medium text-destructive hover:bg-destructive hover:text-destructive-foreground" onClick={async () => {
                    const approved = await dialog.confirm({
                      title: "ライブラリの登録を解除しますか？",
                      description: `「${library.name}」をLumineのライブラリから外します。\n元の画像ファイルは削除されません。`,
                      confirmLabel: "登録を解除",
                      tone: "danger",
                    });
                    if (!approved) return;
                    try {
                      await removeLibrary(library.id);
                      const libraries = await listLibraries();
                      setManagingId(null);
                      setState((current) => ({ ...current, libraries, selectedLibraryId: current.selectedLibraryId === library.id ? (libraries[0]?.id ?? null) : current.selectedLibraryId, selectedFolderPath: "", detailOpen: false, detailAsset: null, filterTagIds: [] }));
                    } catch (cause) {
                      await dialog.notify({ title: "ライブラリの登録を解除できませんでした", description: "データベースからライブラリ情報を削除できませんでした。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
                    }
                  }}>登録を解除…</button>
                </div>
              )}
            </div>
          );
        })}
        {state.libraries.length === 0 && <div className="rounded-xl border border-dashed border-border px-3 py-5 text-center text-[11px] text-muted-foreground">画像フォルダーを追加すると、ここから切り替えられます。</div>}
      </div>
    </div>
  );
}: { scanProgress: Record<number, ScanProgress> }) {
  const { state, setState } = useApp();
  const dialog = useAppDialog();
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
                  try {
                    if (library.isEnabled) await disableLibrary(library.id); else await enableLibrary(library.id);
                    const libraries = await listLibraries();
                    setState((current) => ({ ...current, libraries }));
                  } catch (cause) {
                    await dialog.notify({ title: "ライブラリの状態を変更できませんでした", description: "有効/無効の切り替えに失敗しました。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
                  }
                }}>{library.isEnabled ? "無効化" : "有効化"}</button>
                <button type="button" className="ui-mini-button text-destructive" onClick={async () => {
                  const approved = await dialog.confirm({
                    title: "ライブラリの登録を解除しますか？",
                    description: `「${library.name}」をLumineのライブラリから外します。\n元の画像ファイルは削除されません。`,
                    confirmLabel: "登録を解除",
                    tone: "danger",
                  });
                  if (!approved) return;
                  try {
                    await removeLibrary(library.id);
                    const libraries = await listLibraries();
                    setState((current) => ({ ...current, libraries, selectedLibraryId: current.selectedLibraryId === library.id ? (libraries[0]?.id ?? null) : current.selectedLibraryId, selectedFolderPath: "", detailOpen: false, detailAsset: null, filterTagIds: [] }));
                  } catch (cause) {
                    await dialog.notify({ title: "ライブラリの登録を解除できませんでした", description: "データベースからライブラリ情報を削除できませんでした。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
                  }
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
  const dialog = useAppDialog();
  const { state, setState } = useApp();
  const { data: tags = [] } = useQuery({ queryKey: ["tags"], queryFn: listTags, staleTime: Infinity });
  const [name, setName] = useState("");
  const [color, setColor] = useState("#6366f1");
  const [search, setSearch] = useState("");
  const [showCreate, setShowCreate] = useState(false);
  const [manageMode, setManageMode] = useState(false);
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
    setShowCreate(false);
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
      <PanelTitle
        title="タグ"
        description={`画像の絞り込みが中心です。現在 ${tags.length}件`}
        action={
          <div className="flex gap-1">
            <button type="button" className={`ui-mini-button ${showCreate ? "bg-accent text-foreground" : ""}`} onClick={() => setShowCreate((value) => !value)} aria-expanded={showCreate}>＋ 新規</button>
            <button type="button" className={`ui-mini-button ${manageMode ? "bg-accent text-foreground" : ""}`} onClick={() => setManageMode((value) => !value)} aria-pressed={manageMode}>管理</button>
          </div>
        }
      />

      <div className="px-3 space-y-2.5">
        {showCreate && (
          <form className="rounded-xl border border-border bg-muted/15 p-2.5 space-y-2" onSubmit={create}>
            <p className="text-[10px] font-medium">新しいタグを作成</p>
            <div className="grid grid-cols-[minmax(0,1fr)_34px] gap-2">
              <input className="ui-input min-w-0" value={name} onChange={(event) => setName(event.target.value)} placeholder="タグ名" autoFocus />
              <input type="color" className="w-[34px] h-8 rounded-lg border border-border bg-transparent" value={color} onChange={(event) => setColor(event.target.value)} aria-label="新しいタグの色" />
            </div>
            <div className="flex justify-end gap-1.5">
              <button type="button" className="ui-mini-button" onClick={() => { setShowCreate(false); setName(""); }}>キャンセル</button>
              <button type="submit" className="ui-primary-button" disabled={!name.trim()}>作成</button>
            </div>
          </form>
        )}

        <div className="space-y-1.5">
          <input id="tag-manager-search" aria-label="タグを検索" className="ui-input w-full" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="タグを検索…" autoComplete="off" />
          <div className="flex items-center justify-between text-[10px] text-muted-foreground">
            <span>{searchKey ? `${filteredTags.length}件ヒット` : `${tags.length}件`}{state.filterTagIds.length > 0 ? ` · ${state.filterTagIds.length}件で絞り込み中` : ""}</span>
            {manageMode && <span>管理モード</span>}
          </div>
        </div>

        {state.filterTagIds.length > 0 && (
          <button type="button" className="w-full min-h-8 rounded-lg border border-primary/20 bg-primary/8 px-2.5 text-[10px] font-medium text-primary hover:bg-primary/12" onClick={() => setState((current) => ({ ...current, filterTagIds: [] }))}>
            選択中のタグ絞り込みを解除 ({state.filterTagIds.length})
          </button>
        )}

        <div className="rounded-lg border border-border p-1 space-y-0.5">
          {visibleTags.map((tag) => {
            const active = state.filterTagIds.includes(tag.id);
            return (
              <div key={tag.id} className={`min-h-9 rounded-lg flex items-center gap-1 ${active ? "bg-primary/12 ring-1 ring-primary/25" : "hover:bg-accent/50"}`}>
                <button type="button" className="min-w-0 flex-1 h-9 px-2 flex items-center gap-2 text-left" onClick={() => toggleFilterTag(tag.id)}>
                  <span className="w-3 h-3 rounded-full flex-shrink-0" style={{ backgroundColor: tag.color }} />
                  <span className="text-xs min-w-0 flex-1 truncate">{tag.name}</span>
                  <span className={`text-[10px] ${active ? "text-primary font-medium" : "text-muted-foreground"}`}>{active ? "✓" : "＋"}</span>
                </button>
                {manageMode && (
                  <button type="button" className="h-9 px-2 text-[10px] text-muted-foreground hover:text-destructive flex-shrink-0" onClick={async () => {
                    const approved = await dialog.confirm({
                      title: "タグを削除しますか？",
                      description: `「${tag.name}」を削除します。\nこのタグが付いている画像からもタグ付けが解除されます。`,
                      confirmLabel: "タグを削除",
                      tone: "danger",
                    });
                    if (!approved) return;
                    try {
                      await deleteTag(tag.id);
                      setState((current) => ({ ...current, filterTagIds: current.filterTagIds.filter((id) => id !== tag.id) }));
                      await Promise.all([queryClient.invalidateQueries({ queryKey: ["tags"] }), queryClient.invalidateQueries({ queryKey: ["assets"] })]);
                    } catch (cause) {
                      await dialog.notify({ title: "タグを削除できませんでした", description: "タグ情報を更新できませんでした。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
                    }
                  }}>削除</button>
                )}
              </div>
            );
          })}
          {visibleTags.length === 0 && <p className="py-5 text-center text-[11px] text-muted-foreground">該当するタグはありません</p>}
        </div>

        {filteredTags.length > TAG_MANAGER_VISIBLE_LIMIT && <p className="text-[10px] leading-relaxed text-muted-foreground">上位{TAG_MANAGER_VISIBLE_LIMIT}件を表示しています。検索すると残りもすぐ探せます。</p>}
      </div>
    </div>
  );
}

export function SettingsPanel() {
  const [extensions, setExtensions] = useState<string[]>([]);
  const [extension, setExtension] = useState("");
  const [conflictPolicy, setConflictPolicy] = useState("abort");
  const [editingExtensions, setEditingExtensions] = useState(false);

  useEffect(() => {
    void getSupportedExtensions().then((items) => setExtensions(items ?? []));
    void getSetting("conflictPolicy").then((value) => { if (value) setConflictPolicy(value.replace(/"/g, "")); });
  }, []);

  return (
    <div className="pb-3">
      <PanelTitle title="設定" description="普段変更しない項目は必要な時だけ編集できます。" />
      <div className="px-3 space-y-3">
        <section className="rounded-xl border border-border bg-muted/10 p-3 space-y-2.5">
          <div className="flex items-start justify-between gap-2">
            <div>
              <p className="text-[11px] font-semibold">読み込み対象</p>
              <p className="mt-0.5 text-[10px] leading-relaxed text-muted-foreground">{extensions.length}種類の拡張子を読み込みます。</p>
            </div>
            <button type="button" className="ui-mini-button" onClick={() => setEditingExtensions((value) => !value)} aria-expanded={editingExtensions}>{editingExtensions ? "閉じる" : "編集"}</button>
          </div>
          <div className="flex flex-wrap gap-1">
            {extensions.map((item) => <span key={item} className="h-6 px-2 rounded-full bg-background/60 border border-border text-[9px] inline-flex items-center">{item}</span>)}
          </div>

          {editingExtensions && (
            <div className="border-t border-border pt-2.5 space-y-2">
              <div className="flex flex-wrap gap-1">
                {extensions.map((item) => (
                  <button key={item} type="button" className="h-7 px-2 rounded-full bg-muted border border-border text-[10px] inline-flex items-center gap-1 hover:border-destructive/40 hover:text-destructive" onClick={async () => {
                    const next = extensions.filter((value) => value !== item);
                    await setSupportedExtensions(next);
                    setExtensions(next);
                  }}>{item}<span>×</span></button>
                ))}
              </div>
              <div className="flex gap-2">
                <input className="ui-input min-w-0 flex-1" value={extension} onChange={(event) => setExtension(event.target.value)} placeholder="例: .raw" />
                <button type="button" className="ui-primary-button" onClick={async () => {
                  const raw = extension.trim().toLowerCase();
                  if (!raw) return;
                  const nextValue = raw.startsWith(".") ? raw : `.${raw}`;
                  if (extensions.includes(nextValue)) return;
                  const next = [...extensions, nextValue];
                  await setSupportedExtensions(next);
                  setExtensions(next);
                  setExtension("");
                }}>追加</button>
              </div>
            </div>
          )}
        </section>

        <section className="rounded-xl border border-border bg-muted/10 p-3 space-y-2">
          <div>
            <p className="text-[11px] font-semibold">同名ファイル</p>
            <p className="mt-0.5 text-[10px] leading-relaxed text-muted-foreground">移動・コピー時に同じ名前が存在した場合の動作です。</p>
          </div>
          <select className="ui-input w-full" value={conflictPolicy} onChange={async (event) => { setConflictPolicy(event.target.value); await setSetting("conflictPolicy", JSON.stringify(event.target.value)); }}>
            <option value="abort">処理を中止</option>
            <option value="skip">既存を残してスキップ</option>
            <option value="rename">自動で別名</option>
          </select>
        </section>
      </div>
    </div>
  );
}
