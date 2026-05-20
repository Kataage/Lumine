import { useState } from "react";
import { Sidebar } from "./Sidebar";
import { Toolbar } from "./Toolbar";
import { AssetGrid } from "@/features/asset-grid/AssetGrid";
import { AssetDetailPanel } from "@/features/asset-detail/AssetDetailPanel";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { TagsView } from "@/pages/tags/TagsView";
import { PostsView } from "@/pages/posts/PostsView";
import { SettingsView } from "@/pages/settings/SettingsView";

export function MainLayout() {
  const isDetailPanelOpen = useAppStore((s) => s.isDetailPanelOpen);
  const activeView = useAppStore((s) => s.activeView);
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(false);

  return (
    <div className="flex h-screen bg-background text-foreground">
      <Sidebar
        collapsed={isSidebarCollapsed}
        onToggle={() => setIsSidebarCollapsed(!isSidebarCollapsed)}
      />
      <div className="flex flex-col flex-1 overflow-hidden">
        <Toolbar />
        <main className="flex-1 overflow-auto">
          {activeView === "assets" && <AssetGrid />}
          {activeView === "tags" && <TagsView />}
          {activeView === "posts" && <PostsView />}
          {activeView === "settings" && <SettingsView />}
        </main>
      </div>
      {isDetailPanelOpen && <AssetDetailPanel />}
    </div>
  );
}