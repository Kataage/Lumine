import { useQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useRef, useState, useEffect } from "react";
import { listAssets, scanLibrary } from "@/shared/api/client";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { AssetGridItem } from "./AssetGridItem";
import { Skeleton } from "@/components/ui/skeleton";
import { useToast } from "@/components/ui/toast";
import type { Asset } from "@/entities/types";

const PAGE_SIZE = 200;

export function AssetGrid() {
  const containerRef = useRef<HTMLDivElement>(null);
  const selectedLibraryId = useAppStore((s) => s.selectedLibraryId);
  const thumbnailSize = useAppStore((s) => s.thumbnailSize);
  const { addToast } = useToast();
  const [containerWidth, setContainerWidth] = useState(0);
  const [isScanning, setIsScanning] = useState(false);

  useEffect(() => {
    const updateWidth = () => {
      if (containerRef.current) {
        setContainerWidth(containerRef.current.clientWidth);
      }
    };
    updateWidth();
    const observer = new ResizeObserver(updateWidth);
    if (containerRef.current) {
      observer.observe(containerRef.current);
    }
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (selectedLibraryId !== null) {
      setIsScanning(true);
      scanLibrary(selectedLibraryId)
        .then((result) => {
          addToast(`Scan complete: +${result.added} assets`, "info");
        })
        .catch((err) => {
          const msg = err instanceof Error ? err.message : String(err);
          addToast(`Scan failed: ${msg}`, "error");
        })
        .finally(() => {
          setIsScanning(false);
        });
    }
  }, [selectedLibraryId, addToast]);

  const { data: assets = [], isLoading, isError, error, refetch } = useQuery<Asset[]>({
    queryKey: ["assets", selectedLibraryId],
    queryFn: async () => {
      if (!selectedLibraryId) return [];
      const allAssets: Asset[] = [];
      let offset = 0;
      while (true) {
        const batch = await listAssets({
          library_id: selectedLibraryId,
          offset,
          limit: PAGE_SIZE,
        });
        if (batch.length === 0) break;
        allAssets.push(...batch);
        offset += PAGE_SIZE;
        if (batch.length < PAGE_SIZE) break;
      }
      return allAssets;
    },
    enabled: selectedLibraryId !== null,
    staleTime: 30000,
  });

  useEffect(() => {
    if (isError && error) {
      const msg = error instanceof Error ? error.message : String(error);
      addToast(`Failed to load assets: ${msg}`, "error");
    }
  }, [isError, error, addToast]);

  useEffect(() => {
    if (!isScanning && selectedLibraryId !== null) {
      refetch();
    }
  }, [isScanning, selectedLibraryId, refetch]);

  const columns = containerWidth > 0
    ? Math.max(1, Math.floor(containerWidth / (thumbnailSize + 16)))
    : 4;

  const rowCount = Math.ceil(assets.length / columns);

  const virtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => containerRef.current,
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

  if (isError) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground">
        <p>Failed to load assets. Please try again.</p>
      </div>
    );
  }

  if (assets.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground">
        <p>No images found. Try scanning the library.</p>
      </div>
    );
  }

  return (
    <div ref={containerRef} className="h-full overflow-auto p-4">
      {isScanning && (
        <div className="sticky top-0 z-10 mb-2 px-3 py-2 bg-card border border-border rounded-md text-sm text-muted-foreground">
          Scanning library...
        </div>
      )}
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
                  <AssetGridItem
                    key={asset.id}
                    asset={asset}
                    size={thumbnailSize}
                  />
                ))}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
