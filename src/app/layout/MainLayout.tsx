import { useState } from "react";
import { Sidebar } from "./Sidebar";
import { Toolbar } from "./Toolbar";
import { AssetGrid } from "@/features/asset-grid/AssetGrid";
import { AssetDetailPanel } from "@/features/asset-detail/AssetDetailPanel";
import { useAppStore } from "@/shared/hooks/useAppStore";

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
          {activeView === "tags" && (
            <div className="flex items-center justify-center h-full text-muted-foreground">
              <p>Tags view coming soon</p>
            </div>
          )}
          {activeView === "posts" && (
            <div className="flex items-center justify-center h-full text-muted-foreground">
              <p>Posts view coming soon</p>
            </div>
          )}
          {activeView === "settings" && (
            <div className="flex items-center justify-center h-full text-muted-foreground">
              <p>Settings view coming soon</p>
            </div>
          )}
        </main>
      </div>
      {isDetailPanelOpen && <AssetDetailPanel />}
    </div>
  );
}