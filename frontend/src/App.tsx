import { useState, useCallback } from "react";
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
  const [error, setError] = useState<string | null>(null);

  const handleSelectFolder = useCallback(async () => {
    try {
      setError(null);
      const path = await window.go.main.App.SelectFolder();
      if (path) {
        setFolderPath(path);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to select folder");
    }
  }, []);

  if (!folderPath) {
    return (
      <div className="flex items-center justify-center h-full bg-background">
        <div className="text-center max-w-md px-6">
          <div className="w-16 h-16 mx-auto mb-6 rounded-2xl bg-muted flex items-center justify-center">
            <svg className="w-8 h-8 text-muted-foreground" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.25-4.5l3.75 3.75-3.75 3.75m3.75-3.75H3" />
            </svg>
          </div>
          <h1 className="text-2xl font-semibold mb-2 text-foreground">Lumine</h1>
          <p className="text-muted-foreground mb-8 text-sm leading-relaxed">
            Select a folder to browse your image collection.
            <br />
            Supports JPG, PNG, GIF, WebP, BMP, TIFF, SVG, AVIF.
          </p>
          <button
            onClick={handleSelectFolder}
            className="inline-flex items-center gap-2 px-5 py-2.5 bg-primary text-primary-foreground rounded-lg hover:bg-primary/90 transition-colors text-sm font-medium"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M3 7v4a1 1 0 001 1h3m6-6l3.75 3.75m-3.75-3.75V3h3.75m-3.75 15.75l-3.75-3.75 3.75-3.75m3.75 3.75V21h-3.75" />
            </svg>
            Open Folder
          </button>
          {error && (
            <p className="mt-4 text-sm text-destructive">{error}</p>
          )}
        </div>
      </div>
    );
  }

  return (
    <QueryClientProvider client={queryClient}>
      <div className="flex flex-col h-full bg-background">
        <header className="flex items-center justify-between px-4 py-3 border-b border-border bg-card/50 backdrop-blur-sm">
          <div className="flex items-center gap-3 min-w-0 flex-1">
            <div className="w-7 h-7 rounded-lg bg-primary/10 flex items-center justify-center flex-shrink-0">
              <svg className="w-4 h-4 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.25-4.5l3.75 3.75-3.75 3.75m3.75-3.75H3" />
              </svg>
            </div>
            <span className="text-sm text-muted-foreground truncate font-medium">{folderPath}</span>
          </div>
          <button
            onClick={handleSelectFolder}
            className="text-xs px-3 py-1.5 bg-secondary hover:bg-secondary/80 text-secondary-foreground rounded-md transition-colors ml-2 flex-shrink-0"
          >
            Change
          </button>
        </header>
        <ImageGrid folderPath={folderPath} />
      </div>
    </QueryClientProvider>
  );
}
