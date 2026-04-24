import { useQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useRef } from "react";
import { listAssets } from "@/shared/api/client";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { AssetGridItem } from "./AssetGridItem";
import { Skeleton } from "@/components/ui/skeleton";

export function AssetGrid() {
  const parentRef = useRef<HTMLDivElement>(null);
  const selectedLibraryId = useAppStore((s) => s.selectedLibraryId);
  const thumbnailSize = useAppStore((s) => s.thumbnailSize);
  const searchQuery = useAppStore((s) => s.searchQuery);

  const { data: assets = [], isLoading } = useQuery({
    queryKey: ["assets", selectedLibraryId, searchQuery],
    queryFn: () =>
      listAssets({
        library_id: selectedLibraryId ?? undefined,
        search: searchQuery || undefined,
        limit: 1000,
      }),
    enabled: selectedLibraryId !== null,
  });

  const columns = Math.max(1, Math.floor(window.innerWidth / (thumbnailSize + 20)));

  const rowCount = Math.ceil(assets.length / columns);

  const virtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => parentRef.current,
    estimateSize: () => thumbnailSize + 20,
    overscan: 5,
  });

  const items = virtualizer.getVirtualItems();

  if (!selectedLibraryId) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground">
        <p>Select a library to view assets</p>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="grid gap-4 p-4" style={{ gridTemplateColumns: `repeat(${columns}, 1fr)` }}>
        {Array.from({ length: 12 }).map((_, i) => (
          <Skeleton key={i} className="w-full h-full" />
        ))}
      </div>
    );
  }

  if (assets.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground">
        <p>No assets found. Try scanning the library.</p>
      </div>
    );
  }

  return (
    <div ref={parentRef} className="h-full overflow-auto p-4">
      <div
        style={{
          height: `${virtualizer.getTotalSize()}px`,
          width: "100%",
          position: "relative",
        }}
      >
        {items.map((virtualRow) => {
          const startIndex = virtualRow.index * columns;
          const rowAssets = assets.slice(startIndex, startIndex + columns);

          return (
            <div
              key={virtualRow.key}
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                height: `${virtualRow.size}px`,
                transform: `translateY(${virtualRow.start}px)`,
              }}
            >
              <div
                className="grid gap-4"
                style={{
                  gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`,
                }}
              >
                {rowAssets.map((asset) => (
                  <AssetGridItem key={asset.id} asset={asset} size={thumbnailSize} />
                ))}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}