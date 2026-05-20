import { useQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useRef, useState, useEffect } from "react";
import { getLibraryPath, listAssetsFromFolder } from "@/shared/api/client";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { AssetGridItem } from "./AssetGridItem";
import { Skeleton } from "@/components/ui/skeleton";
import { useToast } from "@/components/ui/toast";

interface FolderAsset {
  id: number;
  file_path: string;
  file_name: string;
  folder_path: string;
  extension: string;
  file_size: number;
  modified_at: string;
  thumb_status: string;
  thumb_path: string | null;
}

export function AssetGrid() {
  const containerRef = useRef<HTMLDivElement>(null);
  const selectedLibraryId = useAppStore((s) => s.selectedLibraryId);
  const thumbnailSize = useAppStore((s) => s.thumbnailSize);
  const { addToast } = useToast();
  const [containerWidth, setContainerWidth] = useState(0);

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

  const { data: assets = [], isLoading, isError, error } = useQuery<FolderAsset[]>({
    queryKey: ["folder-assets", selectedLibraryId],
    queryFn: async () => {
      if (!selectedLibraryId) return [];
      const rootPath = await getLibraryPath(selectedLibraryId);
      return listAssetsFromFolder(selectedLibraryId, rootPath);
    },
    enabled: selectedLibraryId !== null,
  });

  useEffect(() => {
    if (isError && error) {
      const msg = error instanceof Error ? error.message : String(error);
      addToast(`Failed to load assets: ${msg}`, "error");
    }
  }, [isError, error, addToast]);

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
        <p>No images found in this folder.</p>
      </div>
    );
  }

  return (
    <div ref={containerRef} className="h-full overflow-auto p-4">
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
                    asset={{
                      id: asset.id,
                      library_id: 0,
                      folder_path: asset.folder_path,
                      file_name: asset.file_name,
                      file_path: asset.file_path,
                      extension: asset.extension,
                      file_size: asset.file_size,
                      created_at_fs: null,
                      modified_at_fs: asset.modified_at,
                      width: null,
                      height: null,
                      mime_type: null,
                      hash_blake3: null,
                      thumb_status: asset.thumb_status as "none" | "queued" | "ready" | "failed",
                      thumb_path: asset.thumb_path,
                      rating: 0,
                      status_label: "unorganized",
                      is_favorite: false,
                      color_label: null,
                      indexed_at: "",
                      updated_at: "",
                    }}
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
