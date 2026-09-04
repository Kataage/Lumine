import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { AssetDTO, AssetListRequest } from "../api/client";
import { listAssets } from "../api/client";
import { useApp } from "../App";
import { formatFileSize } from "../utils/format";
import { MemoryImage } from "./MemoryImage";
import { ImageViewerModal } from "./ImageViewerModal";

const PAGE_SIZE = 100;
const GAP = 10;
const GRID_PADDING = 24;

interface ViewerGridV2Props {
  onSelectAsset: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onAssetsLoaded: (ids: number[]) => void;
}

export function ViewerGridV2({ onSelectAsset, onAssetsLoaded }: ViewerGridV2Props) {
  const { state } = useApp();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const [previewAsset, setPreviewAsset] = useState<AssetDTO | null>(null);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;
    const updateWidth = () => setContainerWidth(Math.max(0, element.clientWidth - GRID_PADDING));
    const observer = new ResizeObserver(updateWidth);
    observer.observe(element);
    updateWidth();
    return () => observer.disconnect();
  }, []);

  const buildQuery = useCallback((offset: number): AssetListRequest => ({
    libraryId: state.selectedLibraryId ?? 0,
    search: state.searchQuery || undefined,
    folderPath: state.selectedFolderPath || undefined,
    recurse: !!state.selectedFolderPath,
    sortBy: state.sortBy || undefined,
    sortDesc: state.sortDesc,
    statusLabel: state.filterStatusLabel || undefined,
    rating: state.filterRating || undefined,
    offset,
    limit: PAGE_SIZE,
  }), [
    state.filterRating,
    state.filterStatusLabel,
    state.searchQuery,
    state.selectedFolderPath,
    state.selectedLibraryId,
    state.sortBy,
    state.sortDesc,
  ]);

  const {
    data,
    isLoading,
    isError,
    error,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery({
    queryKey: [
      "assets",
      state.selectedLibraryId,
      state.selectedFolderPath,
      state.searchQuery,
      state.sortBy,
      state.sortDesc,
      state.filterStatusLabel,
      state.filterRating,
    ],
    queryFn: async ({ pageParam = 0 }) => {
      const result = await listAssets(buildQuery(Number(pageParam)));
      return result ?? { assets: [], totalCount: 0 };
    },
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) => {
      const loaded = allPages.reduce((sum, page) => sum + page.assets.length, 0);
      const total = allPages[0]?.totalCount ?? 0;
      return lastPage.assets.length === 0 || loaded >= total ? undefined : loaded;
    },
    enabled: !!state.selectedLibraryId,
    staleTime: Infinity,
    gcTime: Infinity,
    refetchOnWindowFocus: false,
    refetchOnMount: false,
  });

  const assets = useMemo(() => data?.pages.flatMap((page) => page.assets) ?? [], [data]);
  const totalCount = data?.pages[0]?.totalCount ?? 0;

  useEffect(() => onAssetsLoaded(assets.map((asset) => asset.id)), [assets, onAssetsLoaded]);

  const columns = containerWidth > 0
    ? Math.max(1, Math.floor((containerWidth + GAP) / (state.thumbnailSize + GAP)))
    : 4;
  const rowCount = Math.ceil(assets.length / columns);

  const virtualizer = useVirtualizer({
    count: state.viewMode === "grid" ? rowCount : assets.length,
    getScrollElement: () => containerRef.current,
    estimateSize: () => state.viewMode === "grid" ? state.thumbnailSize + GAP : 60,
    overscan: 1,
  });
  const virtualItems = virtualizer.getVirtualItems();

  useEffect(() => {
    if (!hasNextPage || isFetchingNextPage) return;
    const last = virtualItems[virtualItems.length - 1];
    const maximum = state.viewMode === "grid" ? rowCount : assets.length;
    if (last && last.index >= maximum - 3) void fetchNextPage();
  }, [assets.length, fetchNextPage, hasNextPage, isFetchingNextPage, rowCount, state.viewMode, virtualItems]);

  const previewIndex = previewAsset ? assets.findIndex((asset) => asset.id === previewAsset.id) : -1;

  if (!state.selectedLibraryId) {
    return (
      <div className="flex-1 flex items-center justify-center p-8">
        <div className="text-center space-y-2">
          <p className="text-sm font-semibold">表示するライブラリを選択してください</p>
          <p className="text-xs text-muted-foreground">左側のライブラリから画像フォルダーを選べます。</p>
        </div>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center gap-2 p-8">
        <p className="text-sm font-semibold text-destructive">画像一覧を読み込めませんでした</p>
        <p className="max-w-lg text-center text-xs text-muted-foreground break-all">{String(error)}</p>
      </div>
    );
  }

  const hasFilters = !!(state.selectedFolderPath || state.searchQuery || state.filterStatusLabel || state.filterRating > 0);

  return (
    <>
      <div ref={containerRef} className="flex-1 min-w-0 overflow-auto bg-background p-3">
        {state.viewMode === "grid" ? (
          <div style={{ height: virtualizer.getTotalSize(), width: "100%", position: "relative" }}>
            {virtualItems.map((virtualRow) => {
              const startIndex = virtualRow.index * columns;
              const rowAssets = assets.slice(startIndex, startIndex + columns);
              return (
                <div
                  key={virtualRow.key}
                  style={{
                    position: "absolute",
                    insetInline: 0,
                    top: 0,
                    height: virtualRow.size,
                    transform: `translateY(${virtualRow.start}px)`,
                  }}
                >
                  <div style={{ display: "grid", gridTemplateColumns: `repeat(${columns}, ${state.thumbnailSize}px)`, gap: GAP }}>
                    {rowAssets.map((asset) => (
                      <GridCard
                        key={asset.id}
                        asset={asset}
                        size={state.thumbnailSize}
                        selected={state.selectedAssets.has(asset.id)}
                        onSelect={onSelectAsset}
                        onPreview={() => setPreviewAsset(asset)}
                      />
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          <div style={{ height: virtualizer.getTotalSize(), width: "100%", position: "relative" }}>
            {virtualItems.map((item) => {
              const asset = assets[item.index];
              if (!asset) return null;
              return (
                <ListRow
                  key={asset.id}
                  asset={asset}
                  selected={state.selectedAssets.has(asset.id)}
                  onSelect={onSelectAsset}
                  onPreview={() => setPreviewAsset(asset)}
                  style={{
                    position: "absolute",
                    insetInline: 0,
                    top: 0,
                    height: item.size,
                    transform: `translateY(${item.start}px)`,
                  }}
                />
              );
            })}
          </div>
        )}

        {isLoading && (
          <div className="flex flex-col items-center justify-center py-20 gap-3 text-sm text-muted-foreground">
            <div className="w-7 h-7 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
            画像一覧を読み込んでいます…
          </div>
        )}

        {isFetchingNextPage && (
          <div className="flex items-center justify-center py-4 gap-2 text-xs text-muted-foreground">
            <div className="w-4 h-4 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
            続きを読み込み中… {assets.length.toLocaleString()} / {totalCount.toLocaleString()}件
          </div>
        )}

        {!isLoading && assets.length === 0 && (
          <div className="min-h-[55vh] flex items-center justify-center">
            <div className="max-w-sm rounded-2xl border border-dashed border-border p-8 text-center">
              <p className="text-sm font-semibold">{hasFilters ? "条件に一致する画像がありません" : "画像が見つかりません"}</p>
              <p className="mt-2 text-xs leading-relaxed text-muted-foreground">
                {hasFilters ? "上部の絞り込み条件を解除して確認してください。" : "対象フォルダーを再スキャンしてください。"}
              </p>
            </div>
          </div>
        )}
      </div>

      {previewAsset && (
        <ImageViewerModal
          asset={previewAsset}
          onClose={() => setPreviewAsset(null)}
          onPrev={() => previewIndex > 0 && setPreviewAsset(assets[previewIndex - 1])}
          onNext={() => previewIndex >= 0 && previewIndex < assets.length - 1 && setPreviewAsset(assets[previewIndex + 1])}
          hasPrev={previewIndex > 0}
          hasNext={previewIndex >= 0 && previewIndex < assets.length - 1}
        />
      )}
    </>
  );
}

function GridCard({
  asset,
  size,
  selected,
  onSelect,
  onPreview,
}: {
  asset: AssetDTO;
  size: number;
  selected: boolean;
  onSelect: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onPreview: () => void;
}) {
  return (
    <div
      className={`relative overflow-hidden rounded-xl border bg-muted group transition-all ${selected ? "border-primary ring-2 ring-primary/30 shadow-lg" : "border-border/60 hover:border-border hover:shadow-lg"}`}
      style={{ width: size, height: size }}
      onClick={(event) => onSelect(asset, event.ctrlKey || event.metaKey, event.shiftKey)}
      onDoubleClick={(event) => { event.preventDefault(); onPreview(); }}
      tabIndex={0}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          onPreview();
        }
      }}
      title={`${asset.fileName}\nダブルクリックまたはEnterで大きく表示`}
    >
      <MemoryImage
        filePath={asset.filePath}
        modifiedAtFs={asset.modifiedAtFs}
        sourceWidth={asset.width}
        sourceHeight={asset.height}
        width={size}
        height={size}
        fit="cover"
        alt={asset.fileName}
      />
      <div className="absolute inset-x-0 bottom-0 h-20 bg-gradient-to-t from-black/85 via-black/25 to-transparent pointer-events-none" />
      <div className="absolute inset-x-0 bottom-0 px-2.5 py-2 pointer-events-none">
        <p className="truncate text-[11px] font-medium text-white drop-shadow">{asset.fileName}</p>
        <p className="mt-0.5 text-[10px] text-white/60">{formatFileSize(asset.fileSize)}</p>
      </div>

      <button
        onClick={(event) => { event.stopPropagation(); onPreview(); }}
        className="absolute top-2 right-2 min-w-16 h-8 px-2 rounded-lg border border-white/20 bg-black/65 text-[11px] font-medium text-white opacity-90 hover:bg-black/80 focus:opacity-100"
        aria-label={`${asset.fileName} を大きく表示`}
      >
        ⛶ 拡大
      </button>

      {selected && (
        <div className="absolute top-2 left-2 w-5 h-5 rounded-full bg-primary text-primary-foreground flex items-center justify-center text-[11px] shadow">✓</div>
      )}
      {asset.isFavorite && !selected && <div className="absolute top-2 left-2 text-yellow-400 drop-shadow">★</div>}
      {asset.rating > 0 && <div className="absolute bottom-10 right-2 text-[10px] text-yellow-400 drop-shadow">{"★".repeat(asset.rating)}</div>}
    </div>
  );
}

function ListRow({
  asset,
  selected,
  onSelect,
  onPreview,
  style,
}: {
  asset: AssetDTO;
  selected: boolean;
  onSelect: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onPreview: () => void;
  style: React.CSSProperties;
}) {
  return (
    <div
      style={style}
      className={`flex items-center gap-3 px-2.5 rounded-lg border border-transparent ${selected ? "bg-primary/10 border-primary/25" : "hover:bg-accent/50"}`}
      onClick={(event) => onSelect(asset, event.ctrlKey || event.metaKey, event.shiftKey)}
      onDoubleClick={onPreview}
    >
      <MemoryImage
        filePath={asset.filePath}
        modifiedAtFs={asset.modifiedAtFs}
        sourceWidth={asset.width}
        sourceHeight={asset.height}
        width={44}
        height={44}
        fit="cover"
        alt={asset.fileName}
        className="rounded-lg flex-shrink-0"
      />
      <div className="min-w-0 flex-1">
        <p className="truncate text-xs font-medium">{asset.fileName}</p>
        <p className="truncate text-[11px] text-muted-foreground">{asset.folderPath}</p>
      </div>
      <span className="text-[11px] text-muted-foreground tabular-nums flex-shrink-0">{formatFileSize(asset.fileSize)}</span>
      <button
        onClick={(event) => { event.stopPropagation(); onPreview(); }}
        className="ui-secondary-button flex-shrink-0"
      >
        ⛶ 大きく表示
      </button>
    </div>
  );
}
