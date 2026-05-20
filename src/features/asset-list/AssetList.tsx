import { useQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useRef, useState, useEffect } from "react";
import { listAssets } from "@/shared/api/client";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { Skeleton } from "@/components/ui/skeleton";
import { useToast } from "@/components/ui/toast";
import { ImageIcon } from "lucide-react";
import { convertFileSrc } from "@tauri-apps/api/core";
import { memo } from "react";
import type { Asset } from "@/entities/types";

const PAGE_SIZE = 200;

interface AssetListItemProps {
  asset: Asset;
  isSelected: boolean;
  onClick: () => void;
}

const AssetListItem = memo(({ asset, isSelected, onClick }: AssetListItemProps) => {
  const [imageError, setImageError] = useState(false);

  const imageUrl = asset.thumb_status === "ready" && asset.thumb_path
    ? convertFileSrc(asset.thumb_path)
    : convertFileSrc(asset.file_path);

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <div
      className={`flex items-center gap-3 px-3 py-2 cursor-pointer border-b border-border/50 hover:bg-accent/50 transition-colors ${
        isSelected ? "bg-accent" : ""
      }`}
      onClick={onClick}
      role="button"
      tabIndex={0}
    >
      <div className="w-12 h-12 rounded-md overflow-hidden flex-shrink-0 bg-muted">
        {!imageError ? (
          <img
            src={imageUrl}
            alt={asset.file_name}
            className="w-full h-full object-cover"
            loading="lazy"
            onError={() => setImageError(true)}
          />
        ) : (
          <div className="w-full h-full flex items-center justify-center">
            <ImageIcon className="w-6 h-6 text-muted-foreground" />
          </div>
        )}
      </div>
      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium truncate">{asset.file_name}</p>
        <p className="text-xs text-muted-foreground truncate">{asset.folder_path}</p>
      </div>
      <div className="text-xs text-muted-foreground w-20 text-right">
        {formatFileSize(asset.file_size)}
      </div>
      <div className="text-xs text-muted-foreground w-24 text-right">
        {asset.extension.toUpperCase()}
      </div>
      <div className="text-xs text-muted-foreground w-32 text-right">
        {asset.modified_at_fs || ""}
      </div>
      <div className="w-16 text-center">
        {asset.thumb_status === "ready" ? (
          <span className="text-green-500 text-xs">✓</span>
        ) : (
          <span className="text-muted-foreground text-xs">-</span>
        )}
      </div>
    </div>
  );
});

export function AssetList() {
  const containerRef = useRef<HTMLDivElement>(null);
  const selectedLibraryId = useAppStore((s) => s.selectedLibraryId);
  const selectedAssetIds = useAppStore((s) => s.selectedAssetIds);
  const toggleAssetSelection = useAppStore((s) => s.toggleAssetSelection);
  const setSelectedAsset = useAppStore((s) => s.setSelectedAsset);
  const { addToast } = useToast();

  const { data: assets = [], isLoading, isError, error } = useQuery<Asset[]>({
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

  const virtualizer = useVirtualizer({
    count: assets.length,
    getScrollElement: () => containerRef.current,
    estimateSize: () => 56,
    overscan: 10,
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
      <div className="p-4 space-y-2">
        {Array.from({ length: 20 }).map((_, i) => (
          <Skeleton key={i} className="w-full h-14" />
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
    <div className="h-full flex flex-col">
      <div className="flex items-center gap-2 px-3 py-2 border-b border-border bg-muted/50 text-xs font-medium text-muted-foreground">
        <div className="w-12" />
        <div className="flex-1">Name</div>
        <div className="w-20 text-right">Size</div>
        <div className="w-24 text-right">Type</div>
        <div className="w-32 text-right">Modified</div>
        <div className="w-16 text-center">Thumb</div>
      </div>
      <div ref={containerRef} className="flex-1 overflow-auto">
        <div
          style={{
            height: `${virtualizer.getTotalSize()}px`,
            width: "100%",
            position: "relative",
          }}
        >
          {items.map((virtualRow) => {
            const asset = assets[virtualRow.index];
            const isSelected = selectedAssetIds.includes(asset.id);

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
                <AssetListItem
                  asset={asset}
                  isSelected={isSelected}
                  onClick={() => {
                    toggleAssetSelection(asset.id);
                    setSelectedAsset(asset);
                  }}
                />
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
