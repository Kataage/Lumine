import { useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useApp } from "../App";
import { listLibraries, offScanProgress, onScanProgress } from "../api/client";
import type { ScanProgress } from "../api/client";
import { FoldersPanel, LibrariesPanel, SettingsPanel, TagsPanel } from "./NavigationPanels";
import { PostRecordsPanel } from "./PostRecordsPanel";
import { MemoryCacheSettingsPanel } from "./MemoryCacheSettingsPanel";\nimport { AISettingsPanel } from "./AISettingsPanel";

const APP_ICON_URL = "/appicon.png";
const NAV_ITEMS = [
  { key: "libraries", label: "ライブラリ", description: "画像フォルダーを管理", icon: "M3.75 6.75A2.25 2.25 0 016 4.5h3.879c.621 0 1.216.257 1.641.71l1.21 1.29H18a2.25 2.25 0 012.25 2.25v8.5A2.25 2.25 0 0118 19.5H6a2.25 2.25 0 01-2.25-2.25V6.75z" },
  { key: "folders", label: "フォルダー", description: "階層から表示範囲を選ぶ", icon: "M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.25-4.5L17.25 12l-3.75 3.75M17.25 12H3" },
  { key: "tags", label: "タグ", description: "分類と絞り込み", icon: "M9.568 3H5.25A2.25 2.25 0 003 5.25v4.318c0 .597.237 1.17.659 1.591l9.581 9.581c.699.699 1.78.872 2.607.33a18.095 18.095 0 005.223-5.223c.542-.827.369-1.908-.33-2.607L11.16 3.66A2.25 2.25 0 009.568 3z" },
  { key: "posts", label: "公開履歴", description: "投稿内容と公開先を確認", icon: "M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m0 12.75h7.5m-7.5 3H12M10.5 2.25H5.625A1.125 1.125 0 004.5 3.375v17.25c0 .621.504 1.125 1.125 1.125h12.75a1.125 1.125 0 001.125-1.125V11.25a9 9 0 00-9-9z" },
  { key: "settings", label: "設定", description: "読み込みとキャッシュ", icon: "M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87l1.295.747 1.217-.456a1.125 1.125 0 011.37.49l1.296 2.247a1.125 1.125 0 01-.26 1.431l-1.003.827a1.125 1.125 0 000 1.735l1.004.828c.424.35.534.954.26 1.43l-1.298 2.247a1.125 1.125 0 01-1.369.491l-1.217-.456-1.295.748a1.125 1.125 0 00-.645.869l-.213 1.28c-.09.543-.56.941-1.11.941h-2.594c-.55 0-1.02-.398-1.11-.94l-.213-1.281a1.125 1.125 0 00-.644-.87l-1.296-.747-1.217.456a1.125 1.125 0 01-1.369-.49l-1.297-2.247a1.125 1.125 0 01.26-1.431l1.004-.827a1.125 1.125 0 000-1.735l-1.004-.828a1.125 1.125 0 01-.26-1.43l1.297-2.247a1.125 1.125 0 011.37-.491l1.216.456 1.296-.748a1.125 1.125 0 00.644-.869l.214-1.281z M15 12a3 3 0 11-6 0 3 3 0 016 0z" },
] as const;

function AppIcon({ size, className = "" }: { size: number; className?: string }) {
  return <img src={APP_ICON_URL} alt="Lumine" width={size} height={size} className={`object-contain ${className}`} draggable={false} />;
}

function RailButton({ item, active, onClick }: {
  item: (typeof NAV_ITEMS)[number];
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      className={`sidebar-rail-button ${active ? "active" : ""}`}
      onClick={onClick}
      aria-label={item.label}
      aria-current={active ? "page" : undefined}
      title={`${item.label} — ${item.description}`}
    >
      <svg className="w-[18px] h-[18px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.7} aria-hidden="true">
        <path strokeLinecap="round" strokeLinejoin="round" d={item.icon} />
      </svg>
      <span className="sidebar-rail-label">{item.label}</span>
    </button>
  );
}

export function SidebarWithCacheSettings() {
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

  const primaryItems = NAV_ITEMS.filter((item) => item.key !== "settings");
  const settingsItem = NAV_ITEMS.find((item) => item.key === "settings")!;
  const activeItem = NAV_ITEMS.find((item) => item.key === state.sidebarView) ?? NAV_ITEMS[0];

  return (
    <aside className="app-sidebar border-r border-border bg-card flex flex-shrink-0 overflow-hidden" aria-label="Lumine サイドバー">
      <div className="sidebar-rail flex-shrink-0">
        <div className="sidebar-rail-brand">
          <AppIcon size={28} />
        </div>

        <nav className="sidebar-rail-nav" aria-label="サイドバーナビゲーション">
          {primaryItems.map((item) => (
            <RailButton
              key={item.key}
              item={item}
              active={state.sidebarView === item.key}
              onClick={() => setState((current) => ({ ...current, sidebarView: item.key }))}
            />
          ))}
        </nav>

        <div className="sidebar-rail-footer">
          <RailButton
            item={settingsItem}
            active={state.sidebarView === settingsItem.key}
            onClick={() => setState((current) => ({ ...current, sidebarView: settingsItem.key }))}
          />
        </div>
      </div>

      <div className="sidebar-workspace min-w-0 flex-1 flex flex-col">
        <div className="sidebar-workspace-header px-3.5 border-b border-border flex items-center flex-shrink-0">
          <p className="text-xs font-semibold tracking-wide">{activeItem.label}</p>
        </div>

        <div className="flex-1 min-h-0 overflow-auto">
          {state.sidebarView === "libraries" && <LibrariesPanel scanProgress={scanProgress} />}
          {state.sidebarView === "folders" && <FoldersPanel />}
          {state.sidebarView === "tags" && <TagsPanel />}
          {state.sidebarView === "posts" && <PostRecordsPanel />}
          {state.sidebarView === "settings" && (
            <>
              <SettingsPanel />
              <AISettingsPanel />
              <MemoryCacheSettingsPanel />
            </>
          )}
        </div>
      </div>
    </aside>
  );
}
