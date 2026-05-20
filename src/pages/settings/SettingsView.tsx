import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useAppStore } from "@/shared/hooks/useAppStore";
import {
  getExcludedFolders,
  setExcludedFolders,
  getSupportedExtensions,
  setSupportedExtensions,
  listLibraries,
  startFolderWatcher,
  stopFolderWatcher,
  isFolderWatching,
} from "@/shared/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PlusIcon, XIcon } from "lucide-react";
import { useToast } from "@/components/ui/toast";

const DEFAULT_EXTENSIONS = [
  "jpg", "jpeg", "png", "gif", "bmp", "webp", "tiff", "tif", "ico", "svg", "avif", "apng",
];

export function SettingsView() {
  const thumbnailSize = useAppStore((s) => s.thumbnailSize);
  const setThumbnailSize = useAppStore((s) => s.setThumbnailSize);
  const selectedLibraryId = useAppStore((s) => s.selectedLibraryId);
  const queryClient = useQueryClient();
  const { addToast } = useToast();

  const [newFolder, setNewFolder] = useState("");
  const [newExtension, setNewExtension] = useState("");

  const { data: libraries = [] } = useQuery({
    queryKey: ["libraries"],
    queryFn: listLibraries,
  });

  const activeLibraryId = selectedLibraryId ?? libraries[0]?.id ?? null;

  const { data: isWatching = false } = useQuery<boolean>({
    queryKey: ["folder-watcher", activeLibraryId],
    queryFn: () => isFolderWatching(activeLibraryId!),
    enabled: activeLibraryId !== null,
  });

  const { data: excludedFolders = [] } = useQuery<string[]>({
    queryKey: ["excluded-folders", activeLibraryId],
    queryFn: () => getExcludedFolders(activeLibraryId!),
    enabled: activeLibraryId !== null,
  });

  const { data: supportedExtensions = [] } = useQuery<string[]>({
    queryKey: ["supported-extensions", activeLibraryId],
    queryFn: () => getSupportedExtensions(activeLibraryId!),
    enabled: activeLibraryId !== null,
  });

  const startWatcherMutation = useMutation({
    mutationFn: async ({ libraryId, rootPath }: { libraryId: number; rootPath: string }) => {
      await startFolderWatcher(libraryId, rootPath);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["folder-watcher"] });
      addToast("Folder watcher started", "info");
    },
  });

  const stopWatcherMutation = useMutation({
    mutationFn: (libraryId: number) => stopFolderWatcher(libraryId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["folder-watcher"] });
      addToast("Folder watcher stopped", "info");
    },
  });

  const setFoldersMutation = useMutation({
    mutationFn: ({ libraryId, folders }: { libraryId: number; folders: string[] }) =>
      setExcludedFolders(libraryId, folders),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["excluded-folders"] });
      addToast("Excluded folders updated", "info");
    },
  });

  const setExtensionsMutation = useMutation({
    mutationFn: ({ libraryId, extensions }: { libraryId: number; extensions: string[] }) =>
      setSupportedExtensions(libraryId, extensions),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["supported-extensions"] });
      addToast("Supported extensions updated", "info");
    },
  });

  const handleToggleWatcher = () => {
    if (!activeLibraryId) return;
    const library = libraries.find((l) => l.id === activeLibraryId);
    if (!library) return;
    if (isWatching) {
      stopWatcherMutation.mutate(activeLibraryId);
    } else {
      startWatcherMutation.mutate({ libraryId: activeLibraryId, rootPath: library.root_path });
    }
  };

  const handleAddFolder = () => {
    if (!newFolder.trim() || !activeLibraryId) return;
    const updated = [...excludedFolders, newFolder.trim()];
    setFoldersMutation.mutate({ libraryId: activeLibraryId, folders: updated });
    setNewFolder("");
  };

  const handleRemoveFolder = (index: number) => {
    if (!activeLibraryId) return;
    const updated = excludedFolders.filter((_, i) => i !== index);
    setFoldersMutation.mutate({ libraryId: activeLibraryId, folders: updated });
  };

  const handleAddExtension = () => {
    if (!newExtension.trim() || !activeLibraryId) return;
    const ext = newExtension.trim().toLowerCase().replace(/^\.*/, "");
    if (supportedExtensions.includes(ext)) return;
    const updated = [...supportedExtensions, ext];
    setExtensionsMutation.mutate({ libraryId: activeLibraryId, extensions: updated });
    setNewExtension("");
  };

  const handleRemoveExtension = (ext: string) => {
    if (!activeLibraryId) return;
    const updated = supportedExtensions.filter((e) => e !== ext);
    setExtensionsMutation.mutate({ libraryId: activeLibraryId, extensions: updated });
  };

  const handleResetExtensions = () => {
    if (!activeLibraryId) return;
    setExtensionsMutation.mutate({ libraryId: activeLibraryId, extensions: DEFAULT_EXTENSIONS });
  };

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <h1 className="text-2xl font-bold mb-6">Settings</h1>

      <div className="space-y-6">
        <div>
          <h2 className="text-lg font-medium mb-2">Display</h2>
          <div className="space-y-4">
            <div>
              <label className="text-sm font-medium">Default thumbnail size</label>
              <div className="flex items-center gap-2 mt-1">
                <input
                  type="range"
                  min="80"
                  max="300"
                  step="20"
                  value={thumbnailSize}
                  onChange={(e) => setThumbnailSize(Number(e.target.value))}
                  className="flex-1"
                />
                <span className="text-sm text-muted-foreground w-12">{thumbnailSize}px</span>
              </div>
            </div>
          </div>
        </div>

        {activeLibraryId && (
          <>
            <div>
              <h2 className="text-lg font-medium mb-2">Folder Watcher</h2>
              <p className="text-sm text-muted-foreground mb-2">
                Automatically detect file changes and rescan the library.
              </p>
              <Button
                onClick={handleToggleWatcher}
                variant={isWatching ? "destructive" : "default"}
                size="sm"
              >
                {isWatching ? "Stop Watching" : "Start Watching"}
              </Button>
              {isWatching && (
                <span className="ml-2 text-xs text-green-500">● Active</span>
              )}
            </div>

            <div>
              <h2 className="text-lg font-medium mb-2">Excluded Folders</h2>
              <p className="text-sm text-muted-foreground mb-2">
                Folders relative to library root that will be skipped during scanning.
              </p>
              <div className="flex gap-2 mb-2">
                <Input
                  value={newFolder}
                  onChange={(e) => setNewFolder(e.target.value)}
                  placeholder="e.g., .hidden, temp, backup"
                  onKeyDown={(e) => e.key === "Enter" && handleAddFolder()}
                  className="flex-1"
                />
                <Button onClick={handleAddFolder} size="sm">
                  <PlusIcon className="w-4 h-4 mr-1" />
                  Add
                </Button>
              </div>
              <div className="space-y-1">
                {excludedFolders.map((folder, index) => (
                  <div
                    key={index}
                    className="flex items-center justify-between px-3 py-1.5 bg-muted rounded-md text-sm"
                  >
                    <span className="font-mono">{folder}</span>
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleRemoveFolder(index)}
                    >
                      <XIcon className="w-4 h-4" />
                    </Button>
                  </div>
                ))}
              </div>
            </div>

            <div>
              <h2 className="text-lg font-medium mb-2">Supported Extensions</h2>
              <p className="text-sm text-muted-foreground mb-2">
                Only files with these extensions will be scanned. Leave empty for defaults.
              </p>
              <div className="flex gap-2 mb-2">
                <Input
                  value={newExtension}
                  onChange={(e) => setNewExtension(e.target.value)}
                  placeholder="e.g., psd, cr2"
                  onKeyDown={(e) => e.key === "Enter" && handleAddExtension()}
                  className="flex-1"
                />
                <Button onClick={handleAddExtension} size="sm">
                  <PlusIcon className="w-4 h-4 mr-1" />
                  Add
                </Button>
                <Button onClick={handleResetExtensions} variant="outline" size="sm">
                  Reset
                </Button>
              </div>
              <div className="flex flex-wrap gap-1.5">
                {supportedExtensions.map((ext) => (
                  <span
                    key={ext}
                    className="inline-flex items-center gap-1 px-2 py-0.5 bg-muted rounded-md text-xs font-mono"
                  >
                    {ext}
                    <button
                      onClick={() => handleRemoveExtension(ext)}
                      className="hover:text-destructive"
                    >
                      <XIcon className="w-3 h-3" />
                    </button>
                  </span>
                ))}
              </div>
            </div>
          </>
        )}

        <div>
          <h2 className="text-lg font-medium mb-2">Storage</h2>
          <p className="text-sm text-muted-foreground">
            Database and thumbnails are stored in the app data directory.
          </p>
        </div>
      </div>
    </div>
  );
}
