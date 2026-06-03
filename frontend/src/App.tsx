import { useState, useCallback, createContext, useContext } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Sidebar, Toolbar, WelcomeScreen } from "./components/Sidebar";
import { AssetGrid } from "./components/AssetGrid";
import { AssetDetailPanel } from "./components/AssetDetailPanel";
import type { LibraryDTO, AssetDTO } from "./api/client";
import {
  selectFolder,
  addLibrary,
  listLibraries,
} from "./api/client";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 300_000,
      refetchOnWindowFocus: false,
    },
  },
});

type ViewMode = "grid" | "list";
type SidebarView = "libraries" | "tags" | "posts" | "settings";

interface AppState {
  libraries: LibraryDTO[];
  selectedLibraryId: number | null;
  selectedAssets: number[];
  detailAsset: AssetDTO | null;
  detailOpen: boolean;
  viewMode: ViewMode;
  sidebarView: SidebarView;
  sidebarOpen: boolean;
  searchQuery: string;
  sortBy: string;
  sortDesc: boolean;
  thumbnailSize: number;
  filterStatusLabel: string;
  filterRating: number;
}

const defaultState: AppState = {
  libraries: [],
  selectedLibraryId: null,
  selectedAssets: [],
  detailAsset: null,
  detailOpen: false,
  viewMode: "grid",
  sidebarView: "libraries",
  sidebarOpen: true,
  searchQuery: "",
  sortBy: "modifiedAtFs",
  sortDesc: true,
  thumbnailSize: 180,
  filterStatusLabel: "",
  filterRating: 0,
};

const AppContext = createContext<{
  state: AppState;
  setState: React.Dispatch<React.SetStateAction<AppState>>;
}>({ state: defaultState, setState: () => {} });

export const useApp = () => useContext(AppContext);

export default function App() {
  const [state, setState] = useState<AppState>(defaultState);

  const handleSelectFolder = useCallback(async () => {
    try {
      const path = await selectFolder();
      if (!path) return;
      const name = path.split(/[/\\]/).pop() || "Library";
      const lib = await addLibrary(name, path);
      if (lib) {
        const libs = await listLibraries();
        setState((s) => ({
          ...s,
          libraries: libs,
          selectedLibraryId: lib.id,
        }));
      }
    } catch (err) {
      console.error("Failed to select folder:", err);
    }
  }, []);

  const handleSelectAsset = useCallback(
    (asset: AssetDTO, multi: boolean) => {
      setState((s) => {
        const sel = multi
          ? s.selectedAssets.includes(asset.id)
            ? s.selectedAssets.filter((id) => id !== asset.id)
            : [...s.selectedAssets, asset.id]
          : [asset.id];
        return {
          ...s,
          selectedAssets: sel,
          detailAsset: asset,
          detailOpen: true,
        };
      });
    },
    []
  );

  const handleCloseDetail = useCallback(() => {
    setState((s) => ({ ...s, detailOpen: false, detailAsset: null }));
  }, []);

  if (state.libraries.length === 0 && !state.selectedLibraryId) {
    return (
      <QueryClientProvider client={queryClient}>
        <WelcomeScreen onSelectFolder={handleSelectFolder} />
      </QueryClientProvider>
    );
  }

  return (
    <QueryClientProvider client={queryClient}>
      <AppContext.Provider value={{ state, setState }}>
        <div className="flex h-screen bg-background text-foreground">
          {state.sidebarOpen && <Sidebar />}

          <div className="flex flex-col flex-1 min-w-0">
            <Toolbar />

            <div className="flex flex-1 min-h-0">
              <div className="flex-1 min-w-0">
                <AssetGrid onSelectAsset={handleSelectAsset} />
              </div>

              {state.detailOpen && state.detailAsset && (
                <AssetDetailPanel
                  assetId={state.detailAsset.id}
                  onClose={handleCloseDetail}
                />
              )}
            </div>
          </div>
        </div>
      </AppContext.Provider>
    </QueryClientProvider>
  );
}
