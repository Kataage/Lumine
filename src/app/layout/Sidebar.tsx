import { useQuery } from "@tanstack/react-query";
import { listLibraries } from "@/shared/api/client";
import { useAppStore } from "@/shared/hooks/useAppStore";
import {
  FolderIcon,
  ChevronLeft,
  ChevronRight,
  TagsIcon,
  FileImageIcon,
  SettingsIcon,
} from "lucide-react";

interface SidebarProps {
  collapsed: boolean;
  onToggle: () => void;
}

export function Sidebar({ collapsed, onToggle }: SidebarProps) {
  const selectedLibraryId = useAppStore((s) => s.selectedLibraryId);
  const selectLibrary = useAppStore((s) => s.selectLibrary);
  const activeView = useAppStore((s) => s.activeView);
  const setActiveView = useAppStore((s) => s.setActiveView);

  const { data: libraries = [] } = useQuery({
    queryKey: ["libraries"],
    queryFn: listLibraries,
  });

  return (
    <aside
      data-testid="sidebar"
      className={`flex flex-col border-r border-border bg-card transition-all duration-200 ${
        collapsed ? "w-16" : "w-64"
      }`}
    >
      <div className="flex items-center justify-between p-4 border-b border-border">
        {!collapsed && <span className="font-semibold text-lg">Lumine</span>}
        <button
          onClick={onToggle}
          className="p-1 hover:bg-accent rounded"
          aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
        >
          {collapsed ? (
            <ChevronRight className="w-5 h-5" />
          ) : (
            <ChevronLeft className="w-5 h-5" />
          )}
        </button>
      </div>

      <nav className="flex-1 p-2 space-y-1">
        <div className="py-2">
          {!collapsed && (
            <span className="px-3 text-xs font-medium text-muted-foreground uppercase">
              Library
            </span>
          )}
          <div className="mt-1 space-y-1">
            {libraries.map((lib) => (
              <button
                key={lib.id}
                onClick={() => selectLibrary(lib.id)}
                className={`flex items-center gap-2 w-full px-3 py-2 rounded-md text-sm transition-colors ${
                  selectedLibraryId === lib.id
                    ? "bg-accent text-accent-foreground"
                    : "hover:bg-accent/50"
                }`}
                title={lib.name}
              >
                <FolderIcon className="w-4 h-4 shrink-0" />
                {!collapsed && <span className="truncate">{lib.name}</span>}
              </button>
            ))}
          </div>
        </div>

<button
        onClick={() => setActiveView("tags")}
        className={`flex items-center gap-2 w-full px-3 py-2 rounded-md text-sm transition-colors ${
          activeView === "tags"
            ? "bg-accent text-accent-foreground"
            : "hover:bg-accent/50"
        }`}
      >
        <TagsIcon className="w-4 h-4" />
        {!collapsed && <span>Tags</span>}
      </button>

      <button
        onClick={() => setActiveView("posts")}
        className={`flex items-center gap-2 w-full px-3 py-2 rounded-md text-sm transition-colors ${
          activeView === "posts"
            ? "bg-accent text-accent-foreground"
            : "hover:bg-accent/50"
        }`}
      >
        <FileImageIcon className="w-4 h-4" />
        {!collapsed && <span>Posts</span>}
      </button>

      <button
        onClick={() => setActiveView("settings")}
        className={`flex items-center gap-2 w-full px-3 py-2 rounded-md text-sm transition-colors ${
          activeView === "settings"
            ? "bg-accent text-accent-foreground"
            : "hover:bg-accent/50"
        }`}
      >
        <SettingsIcon className="w-4 h-4" />
        {!collapsed && <span>Settings</span>}
      </button>
      </nav>
    </aside>
  );
}