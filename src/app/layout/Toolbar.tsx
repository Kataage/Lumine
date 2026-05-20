import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { scanLibrary } from "@/shared/api/client";
import { SearchInput } from "./SearchInput";
import {
  GridIcon,
  ListIcon,
  SlidersHorizontalIcon,
  PanelRightIcon,
  MinusIcon,
  PlusIcon,
  FolderPlusIcon,
  ArrowRightIcon,
  RefreshCwIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { AddLibraryDialog } from "@/features/libraries/AddLibraryDialog";
import { MoveAssetsDialog } from "@/features/move-assets/MoveAssetsDialog";
import { useToast } from "@/components/ui/toast";

export function Toolbar() {
  const viewMode = useAppStore((s) => s.viewMode);
  const setViewMode = useAppStore((s) => s.setViewMode);
  const thumbnailSize = useAppStore((s) => s.thumbnailSize);
  const setThumbnailSize = useAppStore((s) => s.setThumbnailSize);
  const isDetailPanelOpen = useAppStore((s) => s.isDetailPanelOpen);
  const toggleDetailPanel = useAppStore((s) => s.toggleDetailPanel);
  const selectedLibraryId = useAppStore((s) => s.selectedLibraryId);
  const selectedAssetIds = useAppStore((s) => s.selectedAssetIds);
  const setScanning = useAppStore((s) => s.setScanning);
  const setScanProgress = useAppStore((s) => s.setScanProgress);
  const [isAddLibraryOpen, setIsAddLibraryOpen] = useState(false);
  const [isMoveAssetsOpen, setIsMoveAssetsOpen] = useState(false);
  const queryClient = useQueryClient();
  const { addToast } = useToast();

  const scanMutation = useMutation({
    mutationFn: () => {
      if (!selectedLibraryId) throw new Error("No library selected");
      return scanLibrary(selectedLibraryId);
    },
    onMutate: () => {
      setScanning(true);
      setScanProgress(null);
    },
    onSuccess: (result) => {
      setScanning(false);
      setScanProgress(result);
      queryClient.invalidateQueries({ queryKey: ["assets"] });
      addToast(`Scan complete: ${result.added} added, ${result.unchanged} unchanged, ${result.errors} errors`, "info");
    },
    onError: (error) => {
      setScanning(false);
      addToast(`Scan failed: ${error.message}`, "error");
    },
  });

  return (
    <header className="flex items-center gap-2 px-4 py-2 border-b border-border bg-card">
      <SearchInput />

      <div className="flex items-center gap-1 ml-auto">
        <Button
          variant="ghost"
          size="icon"
          onClick={() => setViewMode("grid")}
          className={viewMode === "grid" ? "bg-accent" : ""}
          aria-label="Grid view"
        >
          <GridIcon className="w-4 h-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          onClick={() => setViewMode("list")}
          className={viewMode === "list" ? "bg-accent" : ""}
          aria-label="List view"
        >
          <ListIcon className="w-4 h-4" />
        </Button>

        <div className="mx-2 h-6 w-px bg-border" />

        <Button
          variant="ghost"
          size="icon"
          onClick={() => setThumbnailSize(Math.max(80, thumbnailSize - 20))}
          aria-label="Smaller thumbnails"
        >
          <MinusIcon className="w-4 h-4" />
        </Button>
        <span className="text-xs text-muted-foreground w-8 text-center">
          {thumbnailSize}
        </span>
        <Button
          variant="ghost"
          size="icon"
          onClick={() => setThumbnailSize(Math.min(300, thumbnailSize + 20))}
          aria-label="Larger thumbnails"
        >
          <PlusIcon className="w-4 h-4" />
        </Button>

        <div className="mx-2 h-6 w-px bg-border" />

        <Button variant="ghost" size="icon" aria-label="Filters">
          <SlidersHorizontalIcon className="w-4 h-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          onClick={toggleDetailPanel}
          className={isDetailPanelOpen ? "bg-accent" : ""}
          aria-label="Toggle detail panel"
        >
          <PanelRightIcon className="w-4 h-4" />
        </Button>

        <div className="mx-2 h-6 w-px bg-border" />

        <Button
          variant="outline"
          size="sm"
          onClick={() => scanMutation.mutate()}
          disabled={!selectedLibraryId || scanMutation.isPending}
        >
          <RefreshCwIcon className={`w-4 h-4 mr-1 ${scanMutation.isPending ? "animate-spin" : ""}`} />
          {scanMutation.isPending ? "Scanning..." : "Scan"}
        </Button>

        <Button
          variant="outline"
          size="sm"
          onClick={() => setIsAddLibraryOpen(true)}
        >
          <FolderPlusIcon className="w-4 h-4 mr-1" />
          Add Library
        </Button>

        {selectedAssetIds.length > 0 && (
          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsMoveAssetsOpen(true)}
          >
            <ArrowRightIcon className="w-4 h-4 mr-1" />
            Move ({selectedAssetIds.length})
          </Button>
        )}
      </div>

      <AddLibraryDialog open={isAddLibraryOpen} onOpenChange={setIsAddLibraryOpen} />
      <MoveAssetsDialog open={isMoveAssetsOpen} onOpenChange={setIsMoveAssetsOpen} />
    </header>
  );
}
