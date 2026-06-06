import { useState, useEffect, useRef } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useApp } from "../App";
import {
  selectFolder,
  addLibrary,
  listLibraries,
  scanLibrary,
  enableLibrary,
  disableLibrary,
  removeLibrary,
  getSupportedExtensions,
  setSupportedExtensions,
  listTags,
  createTag,
  deleteTag,
  listPosts,
  createPostDraft,
  deletePost,
  listPostTargets,
  createPostTarget,
  deletePostTarget,
  listPostAccounts,
  createPostAccount,
  deletePostAccount,
  onScanProgress,
  offScanProgress,
  getSetting,
  setSetting,
  getFolderTree,
} from "../api/client";
import type { ScanProgress, PostDTO, PostTargetDTO, PostAccountDTO, FolderDTO } from "../api/client";

export function WelcomeScreen({ onSelectFolder }: { onSelectFolder: () => void }) {
  return (
    <div className="flex items-center justify-center h-full bg-background">
      <div className="text-center max-w-md px-6">
        <div className="w-16 h-16 mx-auto mb-6 rounded-2xl bg-muted flex items-center justify-center">
          <svg className="w-8 h-8 text-muted-foreground" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 15.75l5.159-5.159a2.25 2.25 0 013.182 0l5.159 5.159m-1.5-1.5l1.409-1.409a2.25 2.25 0 013.182 0l2.909 2.909M3.75 21h16.5A2.25 2.25 0 0022.5 18.75V5.25A2.25 2.25 0 0020.25 3H3.75A2.25 2.25 0 001.5 5.25v13.5A2.25 2.25 0 003.75 21z" />
          </svg>
        </div>
        <h1 className="text-2xl font-semibold mb-2 text-foreground">Lumine</h1>
        <p className="text-muted-foreground mb-8 text-sm leading-relaxed">
          Select a folder to browse your image collection.
          <br />
          Supports JPG, PNG, GIF, WebP, BMP, TIFF, SVG, AVIF.
        </p>
        <button
          onClick={onSelectFolder}
          className="inline-flex items-center gap-2 px-5 py-2.5 bg-primary text-primary-foreground rounded-lg hover:bg-primary/90 transition-colors text-sm font-medium"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M3 7v4a1 1 0 001 1h3m6-6l3.75 3.75m-3.75-3.75V3h3.75m-3.75 15.75l-3.75-3.75 3.75-3.75m3.75 3.75V21h-3.75" />
          </svg>
          Open Folder
        </button>
      </div>
    </div>
  );
}

export function Sidebar() {
  const { state, setState } = useApp();
  const [scanProgress, setScanProgress] = useState<Record<number, ScanProgress>>({});

	useEffect(() => {
		onScanProgress((p) => {
			setScanProgress((prev) => ({ ...prev, [p.libraryID]: p }));
			if (p.isDone) {
				const timer = setTimeout(() => {
					setScanProgress((prev) => {
						const next = { ...prev };
						delete next[p.libraryID];
						return next;
					});
					listLibraries().then((libs) => setState((s) => ({ ...s, libraries: libs })));
				}, 2000);
				return () => clearTimeout(timer);
			}
		});
		return () => offScanProgress();
	}, [setState]);

  const handleAddLibrary = async () => {
    try {
      const path = await selectFolder();
      if (!path) return;
      const name = path.split(/[/\\]/).pop() || "Library";
      const lib = await addLibrary(name, path);
      if (lib) {
        const libs = await listLibraries();
        setState((s) => ({ ...s, libraries: libs, selectedLibraryId: lib.id }));
      }
    } catch (err) {
      console.error("Add library failed:", err);
      alert("フォルダーの選択に失敗しました: " + (err instanceof Error ? err.message : String(err)));
    }
  };

  const handleScan = async (id: number) => {
    try {
      await scanLibrary(id);
    } catch (err) {
      console.error("Scan failed:", err);
    }
  };

  const handleToggleEnabled = async (lib: { id: number; isEnabled: boolean }) => {
    try {
      if (lib.isEnabled) {
        await disableLibrary(lib.id);
      } else {
        await enableLibrary(lib.id);
      }
      const libs = await listLibraries();
      setState((s) => ({ ...s, libraries: libs }));
    } catch (err) {
      console.error("Toggle library failed:", err);
    }
  };

  const handleRemoveLibrary = async (id: number) => {
    try {
      await removeLibrary(id);
      const libs = await listLibraries();
      setState((s) => ({ ...s, libraries: libs, selectedLibraryId: s.selectedLibraryId === id ? null : s.selectedLibraryId }));
    } catch (err) {
      console.error("Remove library failed:", err);
    }
  };

  const navItems: { key: typeof state.sidebarView; label: string; icon: string }[] = [
    { key: "libraries", label: "Libraries", icon: "M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.25-4.5l3.75 3.75-3.75 3.75m3.75-3.75H3" },
    { key: "folders", label: "Folders", icon: "M2 6a2 2 0 012-2h5l2 2h5a2 2 0 012 2v6a2 2 0 01-2 2H4a2 2 0 01-2-2V6z" },
    { key: "tags", label: "Tags", icon: "M9.568 3H5.25A2.25 2.25 0 003 5.25v4.318c0 .597.237 1.17.659 1.591l9.581 9.581c.699.699 1.78.872 2.607.33a18.095 18.095 0 005.223-5.223c.542-.827.369-1.908-.33-2.607L11.16 3.66A2.25 2.25 0 009.568 3z" },
    { key: "posts", label: "Posts", icon: "M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z" },
    { key: "settings", label: "Settings", icon: "M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.324.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 011.37.49l1.296 2.247a1.125 1.125 0 01-.26 1.431l-1.003.827c-.293.24-.438.613-.431.992a6.759 6.759 0 010 .255c-.007.378.138.75.43.99l1.005.828c.424.35.534.954.26 1.43l-1.298 2.247a1.125 1.125 0 01-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.57 6.57 0 01-.22.128c-.331.183-.581.495-.644.869l-.213 1.28c-.09.543-.56.941-1.11.941h-2.594c-.55 0-1.02-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 01-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 01-1.369-.49l-1.297-2.247a1.125 1.125 0 01.26-1.431l1.004-.827c.292-.24.437-.613.43-.992a6.932 6.932 0 010-.255c.007-.378-.138-.75-.43-.99l-1.004-.828a1.125 1.125 0 01-.26-1.43l1.297-2.247a1.125 1.125 0 011.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.087.22-.128.332-.183.582-.495.644-.869l.214-1.281z" },
  ];

  return (
    <aside className="w-56 border-r border-border bg-card flex flex-col flex-shrink-0">
      <div className="px-4 py-3 border-b border-border">
        <h1 className="text-sm font-semibold text-foreground tracking-wide">LUMINE</h1>
      </div>

      <nav className="flex-1 overflow-auto py-2">
        {navItems.map((item) => (
          <button
            key={item.key}
            onClick={() => setState((s) => ({ ...s, sidebarView: item.key }))}
            className={`w-full flex items-center gap-3 px-4 py-2 text-sm transition-colors ${
              state.sidebarView === item.key
                ? "bg-accent text-accent-foreground font-medium"
                : "text-muted-foreground hover:bg-accent/50"
            }`}
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path strokeLinecap="round" strokeLinejoin="round" d={item.icon} />
            </svg>
            {item.label}
          </button>
        ))}
      </nav>

      {state.sidebarView === "libraries" && (
        <div className="border-t border-border p-3">
          <div className="flex items-center justify-between mb-2">
            <span className="text-xs font-medium text-muted-foreground uppercase">Libraries</span>
            <button
              onClick={handleAddLibrary}
              className="p-1 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors"
              title="Add library"
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
              </svg>
            </button>
          </div>
          {state.libraries.map((lib) => {
            const progress = scanProgress[lib.id];
            return (
            <div
              key={lib.id}
              onClick={() => setState((s) => ({ ...s, selectedLibraryId: lib.id }))}
              className={`group flex flex-col gap-0.5 px-2 py-1.5 rounded text-sm cursor-pointer transition-colors ${
                state.selectedLibraryId === lib.id
                  ? "bg-accent text-accent-foreground"
                  : lib.isEnabled
                  ? "text-muted-foreground hover:bg-accent/50"
                  : "text-muted-foreground/50 hover:bg-accent/30"
              }`}
            >
              <div className="flex items-center gap-2">
              <button
                onClick={(e) => { e.stopPropagation(); handleToggleEnabled(lib); }}
                className={`p-0.5 rounded transition-colors ${lib.isEnabled ? "text-green-500" : "text-muted-foreground/40"}`}
                title={lib.isEnabled ? "Disable library" : "Enable library"}
              >
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  {lib.isEnabled ? (
                    <path strokeLinecap="round" strokeLinejoin="round" d="M2.036 12.322a.75.75 0 011.05 0l3.87 3.87a.75.75 0 001.05 0l9.543-9.543a.75.75 0 011.05 1.05l-10.07 10.07a2.25 2.25 0 01-3.182 0l-3.87-3.87a.75.75 0 010-1.05z" />
                  ) : (
                    <path strokeLinecap="round" strokeLinejoin="round" d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636" />
                  )}
                </svg>
              </button>
              <span className={`truncate flex-1 ${!lib.isEnabled ? "line-through opacity-60" : ""}`}>{lib.name}</span>
              <button
                onClick={(e) => { e.stopPropagation(); handleScan(lib.id); }}
                className={`p-0.5 rounded hover:bg-accent/80 opacity-0 group-hover:opacity-100 transition-opacity ${progress ? "animate-spin opacity-100" : ""}`}
                title="Rescan"
                disabled={!lib.isEnabled || !!progress}
              >
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182" />
                </svg>
              </button>
              <button
                onClick={(e) => { e.stopPropagation(); handleRemoveLibrary(lib.id); }}
                className="p-0.5 rounded hover:bg-destructive/20 text-muted-foreground hover:text-destructive opacity-0 group-hover:opacity-100 transition-opacity"
                title="Remove library"
              >
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M14.74 9l-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 01-2.244 2.077H8.084a2.25 2.25 0 01-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 00-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 013.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a31.73 31.73 0 00-7.32 0C5.995 3.59 5.005 4.574 5.005 5.754v.916" />
                </svg>
              </button>
              </div>
              {progress && !progress.isDone && (
                <div className="pl-5 flex items-center gap-2">
                  <div className="flex-1 h-1 bg-muted rounded-full overflow-hidden">
                    <div className="h-full bg-primary rounded-full transition-all" style={{ width: `${Math.min(100, (progress.scannedCount / Math.max(1, progress.scannedCount + progress.addedCount)) * 100)}%` }} />
                  </div>
                  <span className="text-[9px] text-muted-foreground">{progress.scannedCount} scanned</span>
                </div>
              )}
            </div>
            );
          })}
          {state.libraries.length === 0 && (
            <p className="text-xs text-muted-foreground/60 px-2">No libraries yet</p>
          )}
        </div>
      )}

  {state.sidebarView === "tags" && <TagsPanel />}
  {state.sidebarView === "folders" && <FolderTreePanel />}
  {state.sidebarView === "posts" && <PostsPanel />}
      {state.sidebarView === "settings" && <SettingsPanel />}
    </aside>
  );
}

function FolderTreePanel() {
  const { state, setState } = useApp();
  const { data: folders = [] } = useQuery({
    queryKey: ["folderTree", state.selectedLibraryId],
    queryFn: () => getFolderTree(state.selectedLibraryId!),
    enabled: !!state.selectedLibraryId,
  });
  const [expandedPaths, setExpandedPaths] = useState<Set<string>>(new Set());

  const rootFolders = folders.filter(f => !f.parentPath || f.parentPath === "");
  const getChildren = (parentPath: string) => folders.filter(f => f.parentPath === parentPath);

  const toggleExpand = (path: string) => {
    setExpandedPaths(prev => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  };

  const renderFolder = (folder: FolderDTO, depth: number) => {
    const children = getChildren(folder.path);
    const isExpanded = expandedPaths.has(folder.path);
    const folderName = folder.path.split(/[\\/]/).pop() || folder.path;
    return (
      <div key={folder.path}>
        <button
          className="flex items-center gap-1.5 w-full px-2 py-1 text-xs text-zinc-300 hover:bg-zinc-700/50 rounded cursor-pointer"
          style={{ paddingLeft: `${depth * 16 + 8}px` }}
          onClick={() => {
            if (children.length > 0) toggleExpand(folder.path);
            setState(s => ({ ...s, searchQuery: `folder:${folder.path}` }));
          }}
        >
          {children.length > 0 && (
            <svg className={`w-3 h-3 transition-transform ${isExpanded ? 'rotate-90' : ''}`} fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clipRule="evenodd" />
            </svg>
          )}
          <svg className="w-3.5 h-3.5 text-amber-400 shrink-0" fill="currentColor" viewBox="0 0 20 20">
            <path d="M2 6a2 2 0 012-2h5l2 2h5a2 2 0 012 2v6a2 2 0 01-2 2H4a2 2 0 01-2-2V6z" />
          </svg>
          <span className="truncate">{folderName}</span>
        </button>
        {isExpanded && children.map(child => renderFolder(child, depth + 1))}
      </div>
    );
  };

  return (
    <div className="border-t border-border p-2 space-y-0.5 max-h-[50vh] overflow-auto">
      <span className="text-xs font-medium text-muted-foreground uppercase px-2">Folders</span>
      {rootFolders.map(f => renderFolder(f, 0))}
      {rootFolders.length === 0 && <p className="text-xs text-muted-foreground/60 px-2">No folders yet</p>}
    </div>
  );
}

function TagsPanel() {
  const queryClient = useQueryClient();
  const { data: tags = [] } = useQuery({
    queryKey: ["tags"],
    queryFn: listTags,
  });
  const [newTagName, setNewTagName] = useState("");
  const [newTagColor, setNewTagColor] = useState("#6366f1");

  const handleCreate = async () => {
    if (!newTagName.trim()) return;
    const t = await createTag(newTagName.trim(), newTagColor);
    if (t) {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
      setNewTagName("");
    }
  };

  const handleDelete = async (id: number) => {
    await deleteTag(id);
    queryClient.invalidateQueries({ queryKey: ["tags"] });
  };

  return (
    <div className="border-t border-border p-3 space-y-2">
      <span className="text-xs font-medium text-muted-foreground uppercase">Tags</span>
      <div className="flex gap-1.5">
        <input
          value={newTagName}
          onChange={(e) => setNewTagName(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && handleCreate()}
          placeholder="New tag..."
          className="flex-1 text-xs px-2 py-1 bg-muted rounded border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/50"
        />
        <input
          type="color"
          value={newTagColor}
          onChange={(e) => setNewTagColor(e.target.value)}
          className="w-7 h-7 rounded border border-border cursor-pointer bg-transparent"
        />
        <button
          onClick={handleCreate}
          className="p-1 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors"
          title="Add tag"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
        </button>
      </div>
      <div className="space-y-1">
        {tags.map((tag) => (
          <div key={tag.id} className="flex items-center gap-2 px-2 py-1 rounded hover:bg-accent/50 group">
            <div className="w-3 h-3 rounded-full flex-shrink-0" style={{ backgroundColor: tag.color }} />
            <span className="text-xs text-foreground truncate flex-1">{tag.name}</span>
            <button
              onClick={() => handleDelete(tag.id)}
              className="p-0.5 rounded opacity-0 group-hover:opacity-100 hover:bg-destructive/20 hover:text-destructive transition-all"
            >
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        ))}
        {tags.length === 0 && <p className="text-xs text-muted-foreground/60 px-2">No tags yet</p>}
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
  const [showNewTarget, setShowNewTarget] = useState(false);
  const [newTargetName, setNewTargetName] = useState("");
  const [newTargetKind, setNewTargetKind] = useState("twitter");
  const [showNewAccount, setShowNewAccount] = useState(false);
  const [newAccountTargetId, setNewAccountTargetId] = useState<number>(0);
  const [newAccountDisplay, setNewAccountDisplay] = useState("");
  const [newAccountIdentifier, setNewAccountIdentifier] = useState("");

  useEffect(() => {
    listPosts(0, 50).then(setPosts);
    listPostTargets().then(setTargets);
    listPostAccounts().then(setAccounts);
  }, []);

  const handleCreateDraft = async () => {
    if (!newTitle.trim()) return;
    const p = await createPostDraft(newTitle.trim(), "", "");
    if (p) {
      setPosts((prev) => [p, ...prev]);
      setNewTitle("");
      setShowNewPost(false);
    }
  };

  const handleDeletePost = async (id: number) => {
    await deletePost(id);
    setPosts((prev) => prev.filter((p) => p.id !== id));
  };

  const statusColors: Record<string, string> = {
    draft: "bg-muted text-muted-foreground",
    scheduled: "bg-blue-500/20 text-blue-400",
    published: "bg-green-500/20 text-green-400",
    failed: "bg-red-500/20 text-red-400",
  };

  return (
    <div className="border-t border-border p-3 space-y-2">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-muted-foreground uppercase">Posts</span>
        <button
          onClick={() => setShowNewPost(!showNewPost)}
          className="p-1 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors"
          title="New post"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
          </svg>
        </button>
      </div>

      {showNewPost && (
        <div className="flex gap-1.5">
          <input
            value={newTitle}
            onChange={(e) => setNewTitle(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleCreateDraft()}
            placeholder="Post title..."
            className="flex-1 text-xs px-2 py-1 bg-muted rounded border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/50"
          />
          <button onClick={handleCreateDraft} className="text-xs px-2 py-1 bg-primary text-primary-foreground rounded hover:bg-primary/90 transition-colors">Create</button>
        </div>
      )}

      <div className="space-y-1">
        {posts.map((post) => (
          <div key={post.id} className="flex items-center gap-2 px-2 py-1 rounded hover:bg-accent/50 group">
            <span className={`text-[10px] px-1.5 py-0.5 rounded-full ${statusColors[post.status] ?? statusColors.draft}`}>{post.status}</span>
            <span className="text-xs text-foreground truncate flex-1">{post.title}</span>
            <button
              onClick={() => handleDeletePost(post.id)}
              className="p-0.5 rounded opacity-0 group-hover:opacity-100 hover:bg-destructive/20 hover:text-destructive transition-all"
            >
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
        ))}
        {posts.length === 0 && <p className="text-xs text-muted-foreground/60 px-2">No posts yet</p>}
      </div>

      <div className="pt-2 border-t border-border">
        <div className="flex items-center justify-between">
          <span className="text-xs font-medium text-muted-foreground uppercase">Targets</span>
          <button onClick={() => setShowNewTarget(!showNewTarget)} className="p-0.5 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors">
            <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" /></svg>
          </button>
        </div>
        {showNewTarget && (
          <div className="flex gap-1 mt-1">
            <input value={newTargetName} onChange={(e) => setNewTargetName(e.target.value)} placeholder="Name" className="flex-1 text-xs px-2 py-1 bg-muted rounded border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/50" />
            <select value={newTargetKind} onChange={(e) => setNewTargetKind(e.target.value)} className="text-xs px-1 py-1 bg-muted rounded border border-border text-muted-foreground">
              <option value="twitter">Twitter/X</option>
              <option value="pixiv">Pixiv</option>
              <option value="misskey">Misskey</option>
              <option value="bluesky">Bluesky</option>
              <option value="other">Other</option>
            </select>
            <button onClick={async () => { const t = await createPostTarget(newTargetName.trim(), newTargetKind); if (t) { setTargets((prev) => [...prev, t]); setNewTargetName(""); setShowNewTarget(false); } }} className="text-xs px-2 py-1 bg-primary text-primary-foreground rounded hover:bg-primary/90">Add</button>
          </div>
        )}
        {targets.map((t) => (
          <div key={t.id} className="flex items-center gap-2 px-2 py-1 rounded hover:bg-accent/50 group">
            <span className="text-xs text-muted-foreground">{t.kind}</span>
            <span className="text-xs text-foreground truncate flex-1">{t.name}</span>
            <button onClick={async () => { await deletePostTarget(t.id); setTargets((prev) => prev.filter((x) => x.id !== t.id)); }} className="p-0.5 rounded opacity-0 group-hover:opacity-100 hover:bg-destructive/20 hover:text-destructive transition-all">
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /></svg>
            </button>
          </div>
        ))}
      </div>

      <div className="pt-2 border-t border-border">
        <div className="flex items-center justify-between">
          <span className="text-xs font-medium text-muted-foreground uppercase">Accounts</span>
          {targets.length > 0 && (
            <button onClick={() => { setShowNewAccount(!showNewAccount); setNewAccountTargetId(targets[0]?.id ?? 0); }} className="p-0.5 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors">
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" /></svg>
            </button>
          )}
        </div>
        {showNewAccount && (
          <div className="space-y-1 mt-1">
            <select value={newAccountTargetId} onChange={(e) => setNewAccountTargetId(Number(e.target.value))} className="w-full text-xs px-2 py-1 bg-muted rounded border border-border text-muted-foreground">
              {targets.map((t) => <option key={t.id} value={t.id}>{t.name} ({t.kind})</option>)}
            </select>
            <input value={newAccountDisplay} onChange={(e) => setNewAccountDisplay(e.target.value)} placeholder="Display name" className="w-full text-xs px-2 py-1 bg-muted rounded border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/50" />
            <input value={newAccountIdentifier} onChange={(e) => setNewAccountIdentifier(e.target.value)} placeholder="@username" className="w-full text-xs px-2 py-1 bg-muted rounded border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/50" />
            <button onClick={async () => { const a = await createPostAccount(newAccountTargetId, newAccountDisplay.trim(), newAccountIdentifier.trim()); if (a) { setAccounts((prev) => [...prev, a]); setNewAccountDisplay(""); setNewAccountIdentifier(""); setShowNewAccount(false); } }} className="text-xs px-2 py-1 bg-primary text-primary-foreground rounded hover:bg-primary/90">Add</button>
          </div>
        )}
        {accounts.map((a) => (
          <div key={a.id} className="flex items-center gap-2 px-2 py-1 rounded hover:bg-accent/50 group">
            <span className="text-xs text-foreground truncate flex-1">{a.displayName}</span>
            <button onClick={async () => { await deletePostAccount(a.id); setAccounts((prev) => prev.filter((x) => x.id !== a.id)); }} className="p-0.5 rounded opacity-0 group-hover:opacity-100 hover:bg-destructive/20 hover:text-destructive transition-all">
              <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /></svg>
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}

function SettingsPanel() {
  const [extensions, setExtensions] = useState<string[]>([]);
  const [extInput, setExtInput] = useState("");
  const [conflictPolicy, setConflictPolicy] = useState("abort");

  useEffect(() => {
    getSupportedExtensions().then(setExtensions);
    getSetting("conflictPolicy").then((v) => { if (v) setConflictPolicy(v.replace(/"/g, "")); });
  }, []);

  const handleAddExt = async () => {
    const ext = extInput.trim().toLowerCase();
    if (!ext || extensions.includes(ext)) return;
    const next = [...extensions, ext];
    await setSupportedExtensions(next);
    setExtensions(next);
    setExtInput("");
  };

  const handleRemoveExt = async (ext: string) => {
    const next = extensions.filter((e) => e !== ext);
    await setSupportedExtensions(next);
    setExtensions(next);
  };

  const handleConflictPolicy = async (policy: string) => {
    setConflictPolicy(policy);
    await setSetting("conflictPolicy", JSON.stringify(policy));
  };

  return (
    <div className="border-t border-border p-3 space-y-3">
      <span className="text-xs font-medium text-muted-foreground uppercase">Settings</span>

      <div className="space-y-1.5">
        <span className="text-xs text-muted-foreground">Supported Extensions</span>
        <div className="flex flex-wrap gap-1">
          {extensions.map((ext) => (
            <span key={ext} className="text-[10px] px-1.5 py-0.5 bg-muted rounded-full border border-border flex items-center gap-1">
              {ext}
              <button onClick={() => handleRemoveExt(ext)} className="text-muted-foreground hover:text-destructive">&times;</button>
            </span>
          ))}
        </div>
        <div className="flex gap-1.5">
          <input
            value={extInput}
            onChange={(e) => setExtInput(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleAddExt()}
            placeholder=".raw"
            className="flex-1 text-xs px-2 py-1 bg-muted rounded border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/50"
          />
          <button onClick={handleAddExt} className="text-xs px-2 py-1 bg-primary text-primary-foreground rounded hover:bg-primary/90 transition-colors">Add</button>
        </div>
      </div>

      <div className="space-y-1.5">
        <span className="text-xs text-muted-foreground">File Move Conflict Policy</span>
        <select
          value={conflictPolicy}
          onChange={(e) => handleConflictPolicy(e.target.value)}
          className="w-full text-xs px-2 py-1.5 bg-muted rounded-md border border-border text-foreground"
        >
          <option value="abort">Abort on conflict</option>
          <option value="skip">Skip existing</option>
          <option value="rename">Auto-rename</option>
        </select>
      </div>
    </div>
  );
}

export function Toolbar() {
  const { state, setState } = useApp();
  const [localSearch, setLocalSearch] = useState(state.searchQuery);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  useEffect(() => {
    setLocalSearch(state.searchQuery);
  }, [state.searchQuery]);

  const handleSearchChange = (value: string) => {
    setLocalSearch(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setState((s) => ({ ...s, searchQuery: value }));
    }, 300);
  };

  return (
    <header className="flex items-center gap-2 px-4 py-2 border-b border-border bg-card/50 backdrop-blur-sm flex-shrink-0">
      <button
        onClick={() => setState((s) => ({ ...s, sidebarOpen: !s.sidebarOpen }))}
        className="p-1.5 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors"
        title="Toggle sidebar"
      >
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5" />
        </svg>
      </button>

      <div className="flex-1 relative max-w-md">
        <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
        </svg>
        <input
          type="text"
          placeholder="Search files, memos, tags..."
        value={localSearch}
        onChange={(e) => handleSearchChange(e.target.value)}
          className="w-full pl-9 pr-3 py-1.5 text-sm bg-muted rounded-md border border-border focus:border-primary focus:outline-none placeholder:text-muted-foreground/60"
        />
      </div>

      <select
        value={state.sortBy}
        onChange={(e) => setState((s) => ({ ...s, sortBy: e.target.value }))}
        className="text-xs px-2 py-1.5 bg-muted rounded-md border border-border text-muted-foreground"
      >
        <option value="modifiedAtFs">Modified</option>
        <option value="created">Created</option>
        <option value="name">Name</option>
        <option value="size">Size</option>
        <option value="rating">Rating</option>
        <option value="status">Status</option>
      </select>

      <button
        onClick={() => setState((s) => ({ ...s, sortDesc: !s.sortDesc }))}
        className={`p-1.5 rounded transition-colors ${state.sortDesc ? "bg-accent text-accent-foreground" : "text-muted-foreground hover:bg-accent"}`}
        title={state.sortDesc ? "Descending" : "Ascending"}
      >
        <svg className={`w-4 h-4 transition-transform ${state.sortDesc ? "" : "rotate-180"}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 13.5L12 21m0 0l-7.5-7.5M12 21V3" />
        </svg>
      </button>

      <div className="h-5 w-px bg-border" />

      <select
        value={state.filterStatusLabel}
        onChange={(e) => setState((s) => ({ ...s, filterStatusLabel: e.target.value }))}
        className="text-xs px-2 py-1.5 bg-muted rounded-md border border-border text-muted-foreground"
      >
        <option value="">All Status</option>
        <option value="unsorted">Unsorted</option>
        <option value="reviewed">Reviewed</option>
        <option value="candidate">Candidate</option>
        <option value="published">Published</option>
      </select>

      <div className="h-5 w-px bg-border" />

      <div className="flex items-center gap-1">
        {[120, 180, 260].map((size) => (
          <button
            key={size}
            onClick={() => setState((s) => ({ ...s, thumbnailSize: size }))}
            className={`p-1 rounded transition-colors ${
              state.thumbnailSize === size ? "bg-accent text-accent-foreground" : "text-muted-foreground hover:bg-accent/50"
            }`}
            title={`${size}px`}
          >
            <div className={`rounded-sm bg-current ${size === 120 ? "w-2 h-2" : size === 180 ? "w-3 h-3" : "w-4 h-4"}`} />
          </button>
        ))}
      </div>

      <div className="h-5 w-px bg-border" />

      <button
        onClick={() => setState((s) => ({ ...s, viewMode: s.viewMode === "grid" ? "list" : "grid" }))}
        className="p-1.5 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors"
        title={state.viewMode === "grid" ? "List view" : "Grid view"}
      >
        {state.viewMode === "grid" ? (
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 12h16.5m-16.5 3.75h16.5M3.75 19.5h16.5M3.75 5.25h16.5" />
          </svg>
        ) : (
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 016 3.75h2.25A2.25 2.25 0 0110.5 6v2.25a2.25 2.25 0 01-2.25 2.25H6a2.25 2.25 0 01-2.25-2.25V6zM3.75 15.75A2.25 2.25 0 016 13.5h2.25a2.25 2.25 0 012.25 2.25V18a2.25 2.25 0 01-2.25 2.25H6A2.25 2.25 0 013.75 18v-2.25zM13.5 6a2.25 2.25 0 012.25-2.25H18A2.25 2.25 0 0120.25 6v2.25A2.25 2.25 0 0118 10.5h-2.25a2.25 2.25 0 01-2.25-2.25V6zM13.5 15.75a2.25 2.25 0 012.25-2.25H18a2.25 2.25 0 012.25 2.25V18A2.25 2.25 0 0118 20.25h-2.25a2.25 2.25 0 01-2.25-2.25v-2.25z" />
          </svg>
        )}
      </button>

      {state.selectedAssets.size > 0 && (
      <>
        <div className="h-5 w-px bg-border" />
        <span className="text-xs text-muted-foreground bg-accent px-2 py-1 rounded">
          {state.selectedAssets.size} selected
        </span>
      </>
      )}
    </header>
  );
}
