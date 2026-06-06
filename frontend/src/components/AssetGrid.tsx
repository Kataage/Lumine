import React, { useRef, useState, useEffect, useCallback } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { listAssets, getLocalImageUrl } from "../api/client";
import { formatFileSize } from "../utils/format";
import { useApp } from "../App";
import type { AssetDTO, AssetListRequest } from "../api/client";

const PAGE_SIZE = 100;
const GAP = 8;

interface AssetGridProps {
  onSelectAsset: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onAssetsLoaded: (ids: number[]) => void;
}

export function AssetGrid({ onSelectAsset, onAssetsLoaded }: AssetGridProps) {
  const { state } = useApp();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const [previewAsset, setPreviewAsset] = useState<AssetDTO | null>(null);
  const assetsRef = useRef<AssetDTO[]>([]);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const observer = new ResizeObserver(() => setContainerWidth(el.clientWidth));
    observer.observe(el);
    setContainerWidth(el.clientWidth);
    return () => observer.disconnect();
  }, []);

  const buildQuery = useCallback(
    (offset: number): AssetListRequest => ({
      libraryId: state.selectedLibraryId ?? 0,
      search: state.searchQuery || undefined,
      sortBy: state.sortBy || undefined,
      sortDesc: state.sortDesc,
      statusLabel: state.filterStatusLabel || undefined,
      rating: state.filterRating || undefined,
      offset,
      limit: PAGE_SIZE,
    }),
    [state.selectedLibraryId, state.searchQuery, state.sortBy, state.sortDesc, state.filterStatusLabel, state.filterRating]
  );

  const {
    data,
    isLoading,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery({
		queryKey: ["assets", state.selectedLibraryId, state.searchQuery, state.sortBy, state.sortDesc, state.filterStatusLabel, state.filterRating],
    queryFn: async ({ pageParam = 0 }) => {
      const req = buildQuery(pageParam as number);
      const result = await listAssets(req);
      return result ?? { assets: [], totalCount: 0 };
    },
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) => {
      const loaded = allPages.reduce((acc, p) => acc + p.assets.length, 0);
      if (loaded >= lastPage.totalCount) return undefined;
      return loaded;
    },
    enabled: !!state.selectedLibraryId,
  });

  const assets = data?.pages.flatMap((p) => p.assets) ?? [];
  const totalCount = data?.pages[0]?.totalCount ?? 0;
  assetsRef.current = assets;

  useEffect(() => {
    onAssetsLoaded(assets.map((a) => a.id));
  }, [assets, onAssetsLoaded]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (previewAsset) {
        if (e.key === "Escape") {
          setPreviewAsset(null);
          return;
        }
        const idx = assetsRef.current.findIndex((a) => a.id === previewAsset.id);
        if (e.key === "ArrowLeft" && idx > 0) {
          setPreviewAsset(assetsRef.current[idx - 1]);
        } else if (e.key === "ArrowRight" && idx < assetsRef.current.length - 1) {
          setPreviewAsset(assetsRef.current[idx + 1]);
        }
        return;
      }

      if (e.key === "a" && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
      }
      if (e.key === "Escape") {
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [previewAsset]);

  const columns =
    containerWidth > 0
      ? Math.max(1, Math.floor((containerWidth + GAP) / (state.thumbnailSize + GAP)))
      : 4;

  const rowCount = Math.ceil(assets.length / columns);

  const virtualizer = useVirtualizer({
    count: state.viewMode === "grid" ? rowCount : assets.length,
    getScrollElement: () => containerRef.current,
    estimateSize: () => state.viewMode === "grid" ? state.thumbnailSize + 28 : 48,
    overscan: 5,
  });

  const items = virtualizer.getVirtualItems();

  const loadMore = useCallback(() => {
    if (hasNextPage && !isFetchingNextPage) fetchNextPage();
  }, [hasNextPage, isFetchingNextPage, fetchNextPage]);

  useEffect(() => {
    if (!hasNextPage || isFetchingNextPage) return;
    const lastItem = items[items.length - 1];
    const maxIndex = state.viewMode === "grid" ? rowCount : assets.length;
    if (lastItem && lastItem.index >= maxIndex - 5) loadMore();
  }, [items, rowCount, assets.length, hasNextPage, isFetchingNextPage, loadMore, state.viewMode]);

  const handleDoubleClick = useCallback((asset: AssetDTO) => {
    setPreviewAsset(asset);
  }, []);

  if (!state.selectedLibraryId) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground text-sm">
        Select a library to browse images
      </div>
    );
  }

  return (
    <>
      <div ref={containerRef} className="flex-1 overflow-auto p-3">
        {state.viewMode === "grid" ? (
          <div
            style={{ height: `${virtualizer.getTotalSize()}px`, width: "100%", position: "relative" }}
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
                    style={{
                      display: "grid",
                      gridTemplateColumns: `repeat(${columns}, 1fr)`,
                      gap: `${GAP}px`,
                    }}
                  >
                    {rowAssets.map((asset) => (
                      <AssetGridItem
                        key={asset.id}
                        asset={asset}
                        size={state.thumbnailSize}
                        selected={state.selectedAssets.has(asset.id)}
                        onSelect={onSelectAsset}
                        onDoubleClick={handleDoubleClick}
                      />
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          <div
            style={{ height: `${virtualizer.getTotalSize()}px`, width: "100%", position: "relative" }}
          >
            {items.map((virtualItem) => {
              const asset = assets[virtualItem.index];
              if (!asset) return null;
              return (
                <AssetListItem
                  key={asset.id}
                  asset={asset}
                  selected={state.selectedAssets.has(asset.id)}
                  onSelect={onSelectAsset}
                  onDoubleClick={handleDoubleClick}
                  style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    width: "100%",
                    height: `${virtualItem.size}px`,
                    transform: `translateY(${virtualItem.start}px)`,
                  }}
                />
              );
            })}
          </div>
        )}

        {isLoading && (
          <div className="flex flex-col items-center justify-center py-16 gap-3">
            <div className="w-8 h-8 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
            <p className="text-sm text-muted-foreground">Loading...</p>
          </div>
        )}

        {isFetchingNextPage && (
          <div className="flex items-center justify-center py-4 gap-2 text-muted-foreground text-sm">
            <div className="w-4 h-4 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
            <span>Loading more... ({assets.length}/{totalCount})</span>
          </div>
        )}

        {!isLoading && assets.length === 0 && (
          <div className="flex flex-col items-center justify-center py-16 gap-3">
            <svg className="w-12 h-12 text-muted-foreground/50" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 15.75l5.159-5.159a2.25 2.25 0 013.182 0l5.159 5.159m-1.5-1.5l1.409-1.409a2.25 2.25 0 013.182 0l2.909 2.909M3.75 21h16.5A2.25 2.25 0 0022.5 18.75V5.25A2.25 2.25 0 0020.25 3H3.75A2.25 2.25 0 001.5 5.25v13.5A2.25 2.25 0 003.75 21z" />
            </svg>
            <p className="text-muted-foreground text-sm">No images found</p>
          </div>
        )}
      </div>

      {previewAsset && (
        <ImagePreview
          asset={previewAsset}
          onClose={() => setPreviewAsset(null)}
          onPrev={() => {
            const idx = assets.findIndex((a) => a.id === previewAsset.id);
            if (idx > 0) setPreviewAsset(assets[idx - 1]);
          }}
          onNext={() => {
            const idx = assets.findIndex((a) => a.id === previewAsset.id);
            if (idx < assets.length - 1) setPreviewAsset(assets[idx + 1]);
          }}
          hasPrev={assets.findIndex((a) => a.id === previewAsset.id) > 0}
          hasNext={assets.findIndex((a) => a.id === previewAsset.id) < assets.length - 1}
        />
      )}
    </>
  );
}

function ImagePreview({
  asset,
  onClose,
  onPrev,
  onNext,
  hasPrev,
  hasNext,
}: {
  asset: AssetDTO;
  onClose: () => void;
  onPrev: () => void;
  onNext: () => void;
  hasPrev: boolean;
  hasNext: boolean;
}) {
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
      if (e.key === "ArrowLeft" && hasPrev) onPrev();
      if (e.key === "ArrowRight" && hasNext) onNext();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [onClose, onPrev, onNext, hasPrev, hasNext]);

  return (
    <div
      className="fixed inset-0 z-50 bg-black/90 flex items-center justify-center"
      onClick={onClose}
    >
      <img
        src={getLocalImageUrl(asset.filePath)}
        alt={asset.fileName}
        className="max-w-[90vw] max-h-[90vh] object-contain"
        onClick={(e) => e.stopPropagation()}
      />

      <div className="absolute bottom-4 left-1/2 -translate-x-1/2 flex items-center gap-4 text-white/80 text-sm">
        <button
          onClick={(e) => { e.stopPropagation(); onPrev(); }}
          disabled={!hasPrev}
          className="p-2 rounded-full bg-white/10 hover:bg-white/20 disabled:opacity-30 transition-colors"
        >
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5L8.25 12l7.5-7.5" />
          </svg>
        </button>
        <span className="text-white/60 text-xs">{asset.fileName}</span>
        <button
          onClick={(e) => { e.stopPropagation(); onNext(); }}
          disabled={!hasNext}
          className="p-2 rounded-full bg-white/10 hover:bg-white/20 disabled:opacity-30 transition-colors"
        >
          <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
          </svg>
        </button>
      </div>

      <button
        onClick={onClose}
        className="absolute top-4 right-4 p-2 rounded-full bg-white/10 hover:bg-white/20 text-white/80 transition-colors"
      >
        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
        </svg>
      </button>
    </div>
  );
}

interface AssetGridItemProps {
  asset: AssetDTO;
  size: number;
  selected: boolean;
  onSelect: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onDoubleClick: (asset: AssetDTO) => void;
}

const AssetGridItem = React.memo(function AssetGridItem({ asset, size, selected, onSelect, onDoubleClick }: AssetGridItemProps) {
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState(false);
  const src = getLocalImageUrl(asset.filePath);

  return (
    <div
      style={{ width: size, height: size }}
      onClick={(e) => onSelect(asset, e.ctrlKey || e.metaKey, e.shiftKey)}
      onDoubleClick={() => onDoubleClick(asset)}
      className={`relative rounded-lg overflow-hidden bg-muted group cursor-pointer border transition-colors ${
        selected ? "border-primary ring-1 ring-primary/30" : "border-border/50 hover:border-border"
      }`}
    >
      {!loaded && !error && <div className="absolute inset-0 bg-muted animate-pulse" />}
      {src && !error ? (
        <img
          src={src}
          alt={asset.fileName}
          loading="lazy"
          className="w-full h-full object-cover"
          onLoad={() => setLoaded(true)}
          onError={() => setError(true)}
        />
      ) : error ? (
        <div className="w-full h-full flex flex-col items-center justify-center gap-2 text-muted-foreground/50">
          <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 15.75l5.159-5.159a2.25 2.25 0 013.182 0l5.159 5.159m-1.5-1.5l1.409-1.409a2.25 2.25 0 013.182 0l2.909 2.909M3.75 21h16.5A2.25 2.25 0 0022.5 18.75V5.25A2.25 2.25 0 0020.25 3H3.75A2.25 2.25 0 001.5 5.25v13.5A2.25 2.25 0 003.75 21z" />
          </svg>
        </div>
      ) : null}

      <div className="absolute inset-0 bg-gradient-to-t from-black/60 via-transparent to-transparent opacity-0 group-hover:opacity-100 transition-opacity" />
      <div className="absolute bottom-0 left-0 right-0 px-2 py-1.5 opacity-0 group-hover:opacity-100 transition-opacity">
        <p className="text-white text-xs truncate font-medium">{asset.fileName}</p>
        <p className="text-white/60 text-[10px]">{formatFileSize(asset.fileSize)}</p>
      </div>

      {selected && (
        <div className="absolute top-1.5 right-1.5 w-5 h-5 bg-primary rounded-full flex items-center justify-center">
          <svg className="w-3 h-3 text-primary-foreground" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
          </svg>
        </div>
      )}

      {asset.isFavorite && !selected && (
        <div className="absolute top-1.5 right-1.5">
          <svg className="w-4 h-4 text-yellow-400 drop-shadow" viewBox="0 0 24 24" fill="currentColor">
            <path d="M11.645 20.91l-.007-.003-.022-.012a15.247 15.247 0 01-.383-.218 25.18 25.18 0 01-4.244-3.17C4.688 15.36 2.25 12.174 2.25 8.25 2.25 5.322 4.714 3 7.688 3A5.5 5.5 0 0112 5.052 5.5 5.5 0 0116.313 3c2.973 0 5.437 2.322 5.437 5.25 0 3.925-2.438 7.111-4.739 9.256a25.175 25.175 0 01-4.244 3.17 15.247 15.247 0 01-.383.219l-.022.012-.007.004-.003.001a.752.752 0 01-.704 0l-.003-.001z" />
          </svg>
        </div>
      )}

      {asset.colorLabel && asset.colorLabel !== "" && (
        <div className={`absolute top-1.5 left-1.5 w-3 h-3 rounded-full ${colorLabelClass(asset.colorLabel)}`} />
      )}

      {asset.rating > 0 && (
        <div className="absolute top-1.5 left-1.5 flex gap-0.5">
          {Array.from({ length: asset.rating }).map((_, i) => (
            <svg key={i} className="w-2.5 h-2.5 text-yellow-400" viewBox="0 0 24 24" fill="currentColor">
              <path d="M10.788 3.21c.448-1.077 1.978-1.077 2.425 0l2.272 5.407a1.125 1.125 0 001.01.747l5.794.494c1.135.097 1.597 1.504.747 2.306l-4.394 3.893a1.125 1.125 0 00-.34 1.058l1.347 5.627c.264 1.1-.893 2.006-1.89 1.437l-5.088-2.863a1.125 1.125 0 00-1.08 0L6.68 20.394c-.997.57-2.154-.337-1.89-1.437l1.347-5.627a1.125 1.125 0 00-.34-1.058L1.403 8.374c-.85-.802-.388-2.21.747-2.306l5.794-.494a1.125 1.125 0 001.01-.747l2.272-5.407z" />
            </svg>
          ))}
        </div>
      )}
    </div>
  );
});

interface AssetListItemProps {
  asset: AssetDTO;
  selected: boolean;
  onSelect: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onDoubleClick: (asset: AssetDTO) => void;
  style: React.CSSProperties;
}

const AssetListItem = React.memo(function AssetListItem({ asset, selected, onSelect, onDoubleClick, style }: AssetListItemProps) {
  const src = getLocalImageUrl(asset.filePath);

  return (
    <div
      style={style}
      onClick={(e) => onSelect(asset, e.ctrlKey || e.metaKey, e.shiftKey)}
      onDoubleClick={() => onDoubleClick(asset)}
      className={`flex items-center gap-3 px-3 cursor-pointer transition-colors ${
        selected ? "bg-accent text-accent-foreground" : "hover:bg-accent/50 text-foreground"
      }`}
    >
      <div className="w-8 h-8 rounded bg-muted flex-shrink-0 overflow-hidden">
        <img src={src} alt={asset.fileName} className="w-full h-full object-cover" loading="lazy" />
      </div>
      <span className="text-sm truncate flex-1">{asset.fileName}</span>
      {asset.colorLabel && asset.colorLabel !== "" && (
        <div className={`w-3 h-3 rounded-full flex-shrink-0 ${colorLabelClass(asset.colorLabel)}`} />
      )}
      <span className="text-xs text-muted-foreground flex-shrink-0 w-16 text-right">{formatFileSize(asset.fileSize)}</span>
      <span className="text-xs text-muted-foreground flex-shrink-0 w-20 text-right">
        {asset.modifiedAtFs ? new Date(asset.modifiedAtFs).toLocaleDateString() : "-"}
      </span>
      {asset.rating > 0 && (
        <div className="flex gap-0.5 flex-shrink-0">
          {Array.from({ length: asset.rating }).map((_, i) => (
            <svg key={i} className="w-3 h-3 text-yellow-400" viewBox="0 0 24 24" fill="currentColor">
              <path d="M10.788 3.21c.448-1.077 1.978-1.077 2.425 0l2.272 5.407a1.125 1.125 0 001.01.747l5.794.494c1.135.097 1.597 1.504.747 2.306l-4.394 3.893a1.125 1.125 0 00-.34 1.058l1.347 5.627c.264 1.1-.893 2.006-1.89 1.437l-5.088-2.863a1.125 1.125 0 00-1.08 0L6.68 20.394c-.997.57-2.154-.337-1.89-1.437l1.347-5.627a1.125 1.125 0 00-.34-1.058L1.403 8.374c-.85-.802-.388-2.21.747-2.306l5.794-.494a1.125 1.125 0 001.01-.747l2.272-5.407z" />
            </svg>
          ))}
        </div>
      )}
      <span className={`text-[10px] px-1.5 py-0.5 rounded-full flex-shrink-0 ${
        asset.statusLabel === "published" ? "bg-green-500/20 text-green-400"
        : asset.statusLabel === "candidate" ? "bg-blue-500/20 text-blue-400"
        : asset.statusLabel === "reviewed" ? "bg-yellow-500/20 text-yellow-400"
        : "bg-muted text-muted-foreground"
      }`}>
        {asset.statusLabel || "unsorted"}
      </span>
    </div>
  );
});

function colorLabelClass(label: string): string {
	const map: Record<string, string> = {
		red: "bg-red-500",
		orange: "bg-orange-500",
		yellow: "bg-yellow-500",
		green: "bg-green-500",
		blue: "bg-blue-500",
		purple: "bg-purple-500",
	};
	return map[label] ?? "bg-muted-foreground";
}
