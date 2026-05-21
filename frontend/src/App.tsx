import { useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ImageGrid } from "./ImageGrid";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: Infinity,
      gcTime: Infinity,
      refetchOnWindowFocus: false,
      refetchOnMount: false,
    },
  },
});

export default function App() {
  const [folderPath, setFolderPath] = useState<string | null>(null);

  const handleSelectFolder = async () => {
    const path = await window.go.main.App.SelectFolder();
    if (path) {
      setFolderPath(path);
    }
  };

  if (!folderPath) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-center">
          <h1 className="text-2xl font-semibold mb-4">Lumine</h1>
          <p className="text-zinc-400 mb-6">Select a folder to browse images</p>
          <button
            onClick={handleSelectFolder}
            className="px-6 py-2 bg-zinc-700 hover:bg-zinc-600 rounded-md transition-colors"
          >
            Open Folder
          </button>
        </div>
      </div>
    );
  }

  return (
    <QueryClientProvider client={queryClient}>
      <div className="flex flex-col h-full">
        <header className="flex items-center justify-between px-4 py-2 border-b border-zinc-800">
          <span className="text-sm text-zinc-400 truncate">{folderPath}</span>
          <button
            onClick={handleSelectFolder}
            className="text-xs px-3 py-1 bg-zinc-700 hover:bg-zinc-600 rounded transition-colors"
          >
            Change
          </button>
        </header>
        <ImageGrid folderPath={folderPath} />
      </div>
    </QueryClientProvider>
  );
}
