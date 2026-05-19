import type { MouseEvent } from "react";
import { useState } from "react";
import { useAppStore } from "@/shared/hooks/useAppStore";
import type { Asset } from "@/entities/types";
import { StarIcon, ImageIcon } from "lucide-react";
import { convertFileSrc } from "@tauri-apps/api/core";

interface AssetGridItemProps {
  asset: Asset;
  size: number;
}

export function AssetGridItem({ asset, size }: AssetGridItemProps) {
  const selectedAssetIds = useAppStore((s) => s.selectedAssetIds);
  const toggleAssetSelection = useAppStore((s) => s.toggleAssetSelection);
  const setSelectedAsset = useAppStore((s) => s.setSelectedAsset);
  const isSelected = selectedAssetIds.includes(asset.id);
  const [isLoading, setIsLoading] = useState(true);
  const [hasError, setHasError] = useState(false);

  const handleClick = (e: MouseEvent<HTMLDivElement>) => {
    if (e.ctrlKey || e.metaKey) {
      toggleAssetSelection(asset.id);
    } else {
      setSelectedAsset(asset);
    }
  };

  const thumbUrl = asset.thumb_status === "ready"
    ? convertFileSrc(asset.file_path)
    : null;

  return (
    <div
      className={`relative group cursor-pointer rounded-md overflow-hidden border-2 transition-all ${
        isSelected
          ? "border-primary ring-2 ring-primary/20"
          : "border-transparent hover:border-border"
      }`}
      style={{ width: size, height: size }}
      onClick={handleClick}
      role="button"
      tabIndex={0}
    >
      {thumbUrl && !hasError ? (
        <>
          {isLoading && (
            <div className="absolute inset-0 bg-muted animate-pulse" />
          )}
          <img
            src={thumbUrl}
            alt={asset.file_name}
            className="w-full h-full object-cover bg-muted"
            loading="lazy"
            onLoad={() => setIsLoading(false)}
            onError={() => {
              setIsLoading(false);
              setHasError(true);
            }}
          />
        </>
      ) : (
        <div className="w-full h-full bg-muted flex items-center justify-center">
          <ImageIcon className="w-8 h-8 text-muted-foreground" />
        </div>
      )}
      <div className="absolute inset-0 bg-gradient-to-t from-black/50 to-transparent opacity-0 group-hover:opacity-100 transition-opacity" />
      <div className="absolute bottom-0 left-0 right-0 p-1 text-white text-xs truncate opacity-0 group-hover:opacity-100 transition-opacity">
        {asset.file_name}
      </div>
      {asset.is_favorite && (
        <StarIcon className="absolute top-1 right-1 w-4 h-4 text-yellow-400 fill-yellow-400" />
      )}
      {asset.rating > 0 && (
        <div className="absolute top-1 left-1 text-xs text-white">
          {"★".repeat(asset.rating)}
        </div>
      )}
    </div>
  );
}