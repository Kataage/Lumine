import { useRef, useState, useEffect, useCallback } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { listAssets } from "../api/client";
import { getLocalImageUrl } from "../api/client";
import { useApp } from "../App";
import type { AssetDTO, AssetListRequest } from "../api/client";

const PAGE_SIZE = 100;
const GAP = 8;

interface AssetGridProps {
  onSelectAsset: (asset: AssetDTO, multi: boolean) => void;
}

export function AssetGrid({ onSelectAsset }: AssetGridProps) {
  const { state } = useApp();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(0);

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
    queryKey: ["assets", state.selectedLibraryId, state.searchQuery, state.sortBy, state.sortDesc, state.filterStatusLabel],
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

  const columns =
    containerWidth > 0
      ? Math.max(1, Math.floor((containerWidth + GAP) / (state.thumbnailSize + GAP)))
      : 4;

  const rowCount = Math.ceil(assets.length / columns);

  const virtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => containerRef.current,
    estimateSize: () => state.thumbnailSize + 28,
    overscan: 3,
  });

  const items = virtualizer.getVirtualItems();

  const loadMore = useCallback(() => {
    if (hasNextPage && !isFetchingNextPage) fetchNextPage();
  }, [hasNextPage, isFetchingNextPage, fetchNextPage]);

  useEffect(() => {
    if (!hasNextPage || isFetchingNextPage) return;
    const lastItem = items[items.length - 1];
    if (lastItem && lastItem.index >= rowCount - 5) loadMore();
  }, [items, rowCount, hasNextPage, isFetchingNextPage, loadMore]);

  if (!state.selectedLibraryId) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground text-sm">
        Select a library to browse images
      </div>
    );
  }

  return (
    <div ref={containerRef} className="flex-1 overflow-auto p-3">
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
                  <AssetItem
                    key={asset.id}
                    asset={asset}
                    size={state.thumbnailSize}
                    selected={state.selectedAssets.includes(asset.id)}
                    onSelect={onSelectAsset}
                  />
                ))}
              </div>
            </div>
          );
        })}
      </div>

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
  );
}

interface AssetItemProps {
  asset: AssetDTO;
  size: number;
  selected: boolean;
  onSelect: (asset: AssetDTO, multi: boolean) => void;
}

function AssetItem({ asset, size, selected, onSelect }: AssetItemProps) {
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState(false);
  const src = getLocalImageUrl(asset.filePath);

  return (
    <div
      style={{ width: size, height: size }}
      onClick={(e) => onSelect(asset, e.ctrlKey || e.metaKey || e.shiftKey)}
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
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
