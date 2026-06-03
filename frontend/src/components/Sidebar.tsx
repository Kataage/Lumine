import { useApp } from "../App";
import { selectFolder, addLibrary, listLibraries, scanLibrary } from "../api/client";

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
    }
  };

  const handleScan = async (id: number) => {
    try {
      await scanLibrary(id);
    } catch (err) {
      console.error("Scan failed:", err);
    }
  };

  const navItems: { key: typeof state.sidebarView; label: string; icon: string }[] = [
    { key: "libraries", label: "Libraries", icon: "M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.25-4.5l3.75 3.75-3.75 3.75m3.75-3.75H3" },
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
          {state.libraries.map((lib) => (
            <div
              key={lib.id}
              onClick={() => setState((s) => ({ ...s, selectedLibraryId: lib.id }))}
              className={`flex items-center gap-2 px-2 py-1.5 rounded text-sm cursor-pointer transition-colors ${
                state.selectedLibraryId === lib.id
                  ? "bg-accent text-accent-foreground"
                  : "text-muted-foreground hover:bg-accent/50"
              }`}
            >
              <svg className="w-3.5 h-3.5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75" />
              </svg>
              <span className="truncate">{lib.name}</span>
              <button
                onClick={(e) => { e.stopPropagation(); handleScan(lib.id); }}
                className="ml-auto p-0.5 rounded hover:bg-accent/80 opacity-0 group-hover:opacity-100 transition-opacity"
                title="Rescan"
              >
                <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182" />
                </svg>
              </button>
            </div>
          ))}
          {state.libraries.length === 0 && (
            <p className="text-xs text-muted-foreground/60 px-2">No libraries yet</p>
          )}
        </div>
      )}
    </aside>
  );
}

export function Toolbar() {
  const { state, setState } = useApp();

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
          value={state.searchQuery}
          onChange={(e) => setState((s) => ({ ...s, searchQuery: e.target.value }))}
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

      {state.selectedAssets.length > 0 && (
        <>
          <div className="h-5 w-px bg-border" />
          <span className="text-xs text-muted-foreground bg-accent px-2 py-1 rounded">
            {state.selectedAssets.length} selected
          </span>
        </>
      )}
    </header>
  );
}
