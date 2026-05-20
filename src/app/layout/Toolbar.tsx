import { useState } from "react";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { SearchInput } from "./SearchInput";
import {
  GridIcon,
  ListIcon,
  PanelRightIcon,
  MinusIcon,
  PlusIcon,
  FolderPlusIcon,
  ArrowRightIcon,
  StarIcon,
  PaletteIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { AddLibraryDialog } from "@/features/libraries/AddLibraryDialog";
import { MoveAssetsDialog } from "@/features/move-assets/MoveAssetsDialog";
import { FilterPanel, type FilterState } from "@/features/search-filter/FilterPanel";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";
import { batchUpdateAssets } from "@/shared/api/client";
import { useToast } from "@/components/ui/toast";

export function Toolbar() {
  const viewMode = useAppStore((s) => s.viewMode);
  const setViewMode = useAppStore((s) => s.setViewMode);
  const thumbnailSize = useAppStore((s) => s.thumbnailSize);
  const setThumbnailSize = useAppStore((s) => s.setThumbnailSize);
  const isDetailPanelOpen = useAppStore((s) => s.isDetailPanelOpen);
  const toggleDetailPanel = useAppStore((s) => s.toggleDetailPanel);
  const selectedAssetIds = useAppStore((s) => s.selectedAssetIds);
  const setSelectedAssetIds = useAppStore((s) => s.setSelectedAssetIds);
  const [isAddLibraryOpen, setIsAddLibraryOpen] = useState(false);
  const [isMoveAssetsOpen, setIsMoveAssetsOpen] = useState(false);
  const [filters, setFilters] = useState<FilterState>({
    sortField: "modified_at",
    sortOrder: "desc",
  });
  const { addToast } = useToast();

  const extensions = ["jpg", "png", "gif", "webp", "bmp", "tiff", "svg", "avif"];

  const handleBatchRating = async (rating: number) => {
    const result = await batchUpdateAssets(selectedAssetIds, { rating });
    addToast(`Updated ${result.updated} assets`, "info");
    setSelectedAssetIds([]);
  };

  const handleBatchStatus = async (status: string) => {
    const result = await batchUpdateAssets(selectedAssetIds, { status });
    addToast(`Updated ${result.updated} assets`, "info");
    setSelectedAssetIds([]);
  };

  const handleBatchColor = async (color: string | null) => {
    const result = await batchUpdateAssets(selectedAssetIds, { colorLabel: color });
    addToast(`Updated ${result.updated} assets`, "info");
    setSelectedAssetIds([]);
  };

  const handleBatchFavorite = async (isFavorite: boolean) => {
    const result = await batchUpdateAssets(selectedAssetIds, { isFavorite });
    addToast(`${isFavorite ? "Added" : "Removed"} ${result.updated} favorites`, "info");
    setSelectedAssetIds([]);
  };

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

        <FilterPanel
          filters={filters}
          onFilterChange={setFilters}
          extensions={extensions}
        />
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
          onClick={() => setIsAddLibraryOpen(true)}
        >
          <FolderPlusIcon className="w-4 h-4 mr-1" />
          Add Library
        </Button>

        {selectedAssetIds.length > 0 && (
          <>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="sm">
                  <PaletteIcon className="w-4 h-4 mr-1" />
                  Batch ({selectedAssetIds.length})
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <div className="px-2 py-1.5 text-sm font-medium">Rating</div>
                {[0, 1, 2, 3, 4, 5].map((r) => (
                  <DropdownMenuItem key={r} onClick={() => handleBatchRating(r)}>
                    {r === 0 ? "Clear" : "★".repeat(r)}
                  </DropdownMenuItem>
                ))}
                <DropdownMenuSeparator />
                <div className="px-2 py-1.5 text-sm font-medium">Status</div>
                {["unorganized", "selected", "posting_candidate", "posted"].map((s) => (
                  <DropdownMenuItem key={s} onClick={() => handleBatchStatus(s)}>
                    {s.replace("_", " ")}
                  </DropdownMenuItem>
                ))}
                <DropdownMenuSeparator />
                <div className="px-2 py-1.5 text-sm font-medium">Color Label</div>
                {[
                  { label: "Clear", color: null },
                  { label: "Red", color: "#ef4444" },
                  { label: "Orange", color: "#f97316" },
                  { label: "Yellow", color: "#eab308" },
                  { label: "Green", color: "#22c55e" },
                  { label: "Blue", color: "#3b82f6" },
                  { label: "Purple", color: "#8b5cf6" },
                ].map(({ label, color }) => (
                  <DropdownMenuItem key={label} onClick={() => handleBatchColor(color)}>
                    {color && (
                      <span
                        className="w-3 h-3 rounded-full mr-2"
                        style={{ backgroundColor: color }}
                      />
                    )}
                    {label}
                  </DropdownMenuItem>
                ))}
                <DropdownMenuSeparator />
                <DropdownMenuItem onClick={() => handleBatchFavorite(true)}>
                  <StarIcon className="w-4 h-4 mr-2 text-yellow-400" />
                  Add to favorites
                </DropdownMenuItem>
                <DropdownMenuItem onClick={() => handleBatchFavorite(false)}>
                  <StarIcon className="w-4 h-4 mr-2 text-muted-foreground" />
                  Remove from favorites
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setIsMoveAssetsOpen(true)}
            >
              <ArrowRightIcon className="w-4 h-4 mr-1" />
              Move ({selectedAssetIds.length})
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setSelectedAssetIds([])}
            >
              Clear selection
            </Button>
          </>
        )}
      </div>

      <AddLibraryDialog open={isAddLibraryOpen} onOpenChange={setIsAddLibraryOpen} />
      <MoveAssetsDialog open={isMoveAssetsOpen} onOpenChange={setIsMoveAssetsOpen} />
    </header>
  );
}
