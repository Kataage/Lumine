import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { getLocalImageUrl, listAssets } from "../api/client";
import type { AssetDTO, AssetListRequest } from "../api/client";
import { useApp } from "../App";
import { formatFileSize } from "../utils/format";
import {
  computeViewerOverscan,
  getViewerVisibleRange,
  shouldFetchViewerPageAhead,
  viewerImagePriority,
  type ViewerImagePriority,
} from "../utils/viewerPreload";
import { MemoryImage } from "./MemoryImage";

const PAGE_SIZE = 200;
const GAP = 8;

interface ViewerGridProps {
  onSelectAsset: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onAssetsLoaded: (ids: number[]) => void;
}

export function ViewerGrid({ onSelectAsset, onAssetsLoaded }: ViewerGridProps) {
  const { state } = useApp();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const [containerHeight, setContainerHeight] = useState(0);
  const [previewAsset, setPreviewAsset] = useState<AssetDTO | null>(null);
  const assetsRef = useRef<AssetDTO[]>([]);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;
    const update = () => {
      setContainerWidth(element.clientWidth);
      setContainerHeight(element.clientHeight);
    };
    const observer = new ResizeObserver(update);
    observer.observe(element);
    update();
    return () => observer.disconnect();
  }, []);

  const buildQuery = useCallback(
    (offset: number): AssetListRequest => ({
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
    }),
    [
      state.selectedLibraryId,
      state.selectedFolderPath,
      state.searchQuery,
      state.sortBy,
      state.sortDesc,
      state.filterStatusLabel,
      state.filterRating,
    ]
  );

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
      const result = await listAssets(buildQuery(pageParam as number));
      return result ?? { assets: [], totalCount: 0 };
    },
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) => {
      const loaded = allPages.reduce((sum, page) => sum + page.assets.length, 0);
      const total = allPages[0]?.totalCount ?? 0;
      if (lastPage.assets.length === 0 || loaded >= total) return undefined;
      return loaded;
    },
    enabled: !!state.selectedLibraryId,
    staleTime: Infinity,
    gcTime: Infinity,
    refetchOnWindowFocus: false,
    refetchOnMount: false,
  });

  const assets = useMemo(() => data?.pages.flatMap((page) => page.assets) ?? [], [data]);
  const totalCount = data?.pages[0]?.totalCount ?? 0;
  assetsRef.current = assets;

  useEffect(() => {
    onAssetsLoaded(assets.map((asset) => asset.id));
  }, [assets, onAssetsLoaded]);

  const columns =
    containerWidth > 0
      ? Math.max(1, Math.floor((containerWidth + GAP) / (state.thumbnailSize + GAP)))
      : 4;
  const rowCount = Math.ceil(assets.length / columns);
  const itemExtent = state.viewMode === "grid" ? state.thumbnailSize + GAP : 56;
  const itemCount = state.viewMode === "grid" ? rowCount : assets.length;
  const overscan = computeViewerOverscan(containerHeight, itemExtent);

  const virtualizer = useVirtualizer({
    count: itemCount,
    getScrollElement: () => containerRef.current,
    estimateSize: () => itemExtent,
    overscan,
  });
  const virtualItems = virtualizer.getVirtualItems();
  const visibleRange = getViewerVisibleRange(
    virtualizer.scrollOffset ?? 0,
    containerHeight,
    itemExtent,
    itemCount
  );

  useEffect(() => {
    if (!hasNextPage || isFetchingNextPage) return;
    const last = virtualItems[virtualItems.length - 1];
    if (
      last &&
      shouldFetchViewerPageAhead(last.index, itemCount, containerHeight, itemExtent)
    ) {
      void fetchNextPage();
    }
  }, [
    virtualItems,
    itemCount,
    containerHeight,
    itemExtent,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  ]);

  const openPreview = useCallback((asset: AssetDTO) => setPreviewAsset(asset), []);

  useEffect(() => {
    if (!previewAsset) return;
    const handler = (event: KeyboardEvent) => {
      const index = assetsRef.current.findIndex((asset) => asset.id === previewAsset.id);
      if (event.key === "Escape") {
        setPreviewAsset(null);
      } else if (event.key === "ArrowLeft" && index > 0) {
        event.preventDefault();
        setPreviewAsset(assetsRef.current[index - 1]);
      } else if (event.key === "ArrowRight" && index >= 0 && index < assetsRef.current.length - 1) {
        event.preventDefault();
        setPreviewAsset(assetsRef.current[index + 1]);
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [previewAsset]);

  if (!state.selectedLibraryId) {
    return (
      <div className="flex-1 flex items-center justify-center p-8">
        <div className="text-center space-y-2">
          <p className="text-sm font-medium">表示するライブラリを選択してください</p>
          <p className="text-xs text-muted-foreground">左の「ライブラリ」から画像フォルダーを選べます。</p>
        </div>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center gap-2 p-8 text-sm">
        <p className="text-destructive font-medium">画像一覧を読み込めませんでした</p>
        <p className="text-muted-foreground text-xs max-w-lg break-all text-center">{String(error)}</p>
      </div>
    );
  }

  const hasFilters = !!(
    state.selectedFolderPath ||
    state.searchQuery ||
    state.filterStatusLabel ||
    state.filterRating > 0
  );

  return (
    <>
      <div ref={containerRef} className="flex-1 overflow-auto p-3 bg-background">
        {state.viewMode === "grid" ? (
          <div style={{ height: virtualizer.getTotalSize(), width: "100%", position: "relative" }}>
            {virtualItems.map((virtualRow) => {
              const startIndex = virtualRow.index * columns;
              const rowAssets = assets.slice(startIndex, startIndex + columns);
              const priority = viewerImagePriority(
                virtualRow.index,
                visibleRange.first,
                visibleRange.last
              );
              return (
                <div
                  key={virtualRow.key}
                  style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    width: "100%",
                    height: virtualRow.size,
                    transform: `translateY(${virtualRow.start}px)`,
                  }}
                >
                  <div
                    style={{
                      display: "grid",
                      gridTemplateColumns: `repeat(${columns}, ${state.thumbnailSize}px)`,
                      gap: GAP,
                    }}
                  >
                    {rowAssets.map((asset) => (
                      <ViewerGridItem
                        key={asset.id}
                        asset={asset}
                        size={state.thumbnailSize}
                        priority={priority}
                        selected={state.selectedAssets.has(asset.id)}
                        onSelect={onSelectAsset}
                        onDoubleClick={openPreview}
                      />
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          <div style={{ height: virtualizer.getTotalSize(), width: "100%", position: "relative" }}>
            {virtualItems.map((virtualItem) => {
              const asset = assets[virtualItem.index];
              if (!asset) return null;
              const priority = viewerImagePriority(
                virtualItem.index,
                visibleRange.first,
                visibleRange.last
              );
              return (
                <ViewerListItem
                  key={asset.id}
                  asset={asset}
                  priority={priority}
                  selected={state.selectedAssets.has(asset.id)}
                  onSelect={onSelectAsset}
                  onDoubleClick={openPreview}
                  style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    width: "100%",
                    height: virtualItem.size,
                    transform: `translateY(${virtualItem.start}px)`,
                  }}
                />
              );
            })}
          </div>
        )}

        {isLoading && (
          <div className="flex flex-col items-center justify-center py-20 gap-3">
            <div className="w-7 h-7 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
            <p className="text-sm text-muted-foreground">画像一覧を読み込んでいます…</p>
          </div>
        )}

        {isFetchingNextPage && (
          <div className="flex items-center justify-center py-4 gap-2 text-muted-foreground text-xs">
            <div className="w-4 h-4 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
            <span>続きを読み込み中… {assets.length.toLocaleString()} / {totalCount.toLocaleString()}件</span>
          </div>
        )}

        {!isLoading && assets.length === 0 && (
          <div className="min-h-[55vh] flex items-center justify-center">
            <div className="max-w-sm text-center rounded-2xl border border-dashed border-border p-8">
              <div className="w-12 h-12 mx-auto rounded-xl bg-muted flex items-center justify-center text-muted-foreground mb-3">▧</div>
              <p className="text-sm font-medium">
                {hasFilters ? "条件に一致する画像がありません" : "画像が見つかりません"}
              </p>
              <p className="mt-1 text-xs text-muted-foreground leading-relaxed">
                {hasFilters
                  ? "上部の絞り込み条件を解除するか、別の条件で検索してください。"
                  : "ライブラリの再スキャンを行い、対象フォルダーに対応画像があるか確認してください。"}
              </p>
            </div>
          </div>
        )}
      </div>

      {previewAsset && (
        <ViewerPreview
          asset={previewAsset}
          onClose={() => setPreviewAsset(null)}
          onPrev={() => {
            const index = assets.findIndex((asset) => asset.id === previewAsset.id);
            if (index > 0) setPreviewAsset(assets[index - 1]);
          }}
          onNext={() => {
            const index = assets.findIndex((asset) => asset.id === previewAsset.id);
            if (index >= 0 && index < assets.length - 1) setPreviewAsset(assets[index + 1]);
          }}
          hasPrev={assets.findIndex((asset) => asset.id === previewAsset.id) > 0}
          hasNext={assets.findIndex((asset) => asset.id === previewAsset.id) < assets.length - 1}
        />
      )}
    </>
  );
}

interface ViewerGridItemProps {
  asset: AssetDTO;
  size: number;
  priority: ViewerImagePriority;
  selected: boolean;
  onSelect: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onDoubleClick: (asset: AssetDTO) => void;
}

const ViewerGridItem = React.memo(function ViewerGridItem({
  asset,
  size,
  priority,
  selected,
  onSelect,
  onDoubleClick,
}: ViewerGridItemProps) {
  return (
    <div
      style={{ width: size, height: size }}
      onClick={(event) => onSelect(asset, event.ctrlKey || event.metaKey, event.shiftKey)}
      onDoubleClick={() => onDoubleClick(asset)}
      title={`${asset.fileName}\nダブルクリックで全画面表示`}
      className={`relative rounded-xl overflow-hidden bg-muted group cursor-pointer border transition-all ${
        selected
          ? "border-primary ring-2 ring-primary/30 shadow-lg"
          : "border-border/50 hover:border-border hover:-translate-y-px hover:shadow-lg"
      }`}
    >
      <MemoryImage
        filePath={asset.filePath}
        modifiedAtFs={asset.modifiedAtFs}
        sourceWidth={asset.width}
        sourceHeight={asset.height}
        width={size}
        height={size}
        fit="cover"
        priority={priority}
        alt={asset.fileName}
      />

      <div className="absolute inset-x-0 bottom-0 h-16 bg-gradient-to-t from-black/80 via-black/30 to-transparent pointer-events-none" />
      <div className="absolute bottom-0 left-0 right-0 px-2 py-1.5 pointer-events-none">
        <p className="text-white text-[11px] truncate font-medium drop-shadow">{asset.fileName}</p>
        <p className="text-white/60 text-[9px] opacity-0 group-hover:opacity-100 transition-opacity">{formatFileSize(asset.fileSize)}</p>
      </div>

      {selected && (
        <div className="absolute top-1.5 right-1.5 w-5 h-5 bg-primary text-primary-foreground rounded-full flex items-center justify-center shadow-md">
          <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
          </svg>
        </div>
      )}
      {asset.isFavorite && !selected && (
        <div className="absolute top-1.5 right-1.5 text-yellow-400 drop-shadow">★</div>
      )}
      {asset.rating > 0 && (
        <div className="absolute top-1.5 left-1.5 text-[10px] text-yellow-400 drop-shadow">
          {"★".repeat(asset.rating)}
        </div>
      )}
      {asset.colorLabel && (
        <div
          className="absolute bottom-1.5 right-1.5 w-3 h-3 rounded-full border border-white/50 shadow"
          style={{ backgroundColor: labelColor(asset.colorLabel) }}
        />
      )}
    </div>
  );
});

interface ViewerListItemProps {
  asset: AssetDTO;
  priority: ViewerImagePriority;
  selected: boolean;
  onSelect: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onDoubleClick: (asset: AssetDTO) => void;
  style: React.CSSProperties;
}

const ViewerListItem = React.memo(function ViewerListItem({
  asset,
  priority,
  selected,
  onSelect,
  onDoubleClick,
  style,
}: ViewerListItemProps) {
  return (
    <div
      style={style}
      onClick={(event) => onSelect(asset, event.ctrlKey || event.metaKey, event.shiftKey)}
      onDoubleClick={() => onDoubleClick(asset)}
      title="ダブルクリックで全画面表示"
      className={`flex items-center gap-3 px-2.5 rounded-lg cursor-pointer transition-colors ${
        selected ? "bg-primary/10 ring-1 ring-inset ring-primary/30" : "hover:bg-accent/50"
      }`}
    >
      <MemoryImage
        filePath={asset.filePath}
        modifiedAtFs={asset.modifiedAtFs}
        sourceWidth={asset.width}
        sourceHeight={asset.height}
        width={42}
        height={42}
        fit="cover"
        priority={priority}
        alt={asset.fileName}
        className="rounded-lg flex-shrink-0"
      />
      <div className="min-w-0 flex-1">
        <p className="text-xs font-medium truncate">{asset.fileName}</p>
        <p className="text-[9px] text-muted-foreground truncate">{asset.folderPath}</p>
      </div>
      <span className="text-[10px] text-muted-foreground tabular-nums flex-shrink-0">{formatFileSize(asset.fileSize)}</span>
      {asset.isFavorite && <span className="text-xs text-yellow-400 flex-shrink-0">★</span>}
      {asset.rating > 0 && <span className="text-[10px] text-yellow-400 flex-shrink-0">{"★".repeat(asset.rating)}</span>}
    </div>
  );
});

function ViewerPreview({
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
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [isPanning, setIsPanning] = useState(false);
  const [fullLoaded, setFullLoaded] = useState(false);
  const lastPosition = useRef({ x: 0, y: 0 });
  const [viewport, setViewport] = useState(() => ({
    width: Math.max(320, window.innerWidth - 80),
    height: Math.max(240, window.innerHeight - 120),
  }));

  const resetView = useCallback(() => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
    setFullLoaded(false);
  }, []);

  useEffect(() => resetView(), [asset.id, resetView]);
  useEffect(() => {
    const update = () => setViewport({
      width: Math.max(320, window.innerWidth - 80),
      height: Math.max(240, window.innerHeight - 120),
    });
    window.addEventListener("resize", update);
    return () => window.removeEventListener("resize", update);
  }, []);

  useEffect(() => {
    if (zoom <= 1 && fullLoaded) setFullLoaded(false);
  }, [zoom, fullLoaded]);

  const zoomIn = () => setZoom((value) => Math.min(10, value + 0.5));
  const zoomOut = () => setZoom((value) => Math.max(1, value - 0.5));

  const handleWheel = useCallback((event: React.WheelEvent) => {
    event.preventDefault();
    setZoom((value) => Math.max(1, Math.min(10, value - event.deltaY * 0.0015)));
  }, []);

  const handleMouseDown = useCallback((event: React.MouseEvent) => {
    if (zoom <= 1) return;
    setIsPanning(true);
    lastPosition.current = { x: event.clientX, y: event.clientY };
  }, [zoom]);

  const handleMouseMove = useCallback((event: React.MouseEvent) => {
    if (!isPanning || zoom <= 1) return;
    setPan((value) => ({
      x: value.x + event.clientX - lastPosition.current.x,
      y: value.y + event.clientY - lastPosition.current.y,
    }));
    lastPosition.current = { x: event.clientX, y: event.clientY };
  }, [isPanning, zoom]);

  const handleMouseUp = useCallback(() => setIsPanning(false), []);
  const needsOriginal = zoom > 1;

  return (
    <div className="fixed inset-0 z-50 bg-black/95 flex items-center justify-center" onClick={onClose}>
      <div
        className="absolute inset-0 flex items-center justify-center overflow-hidden"
        onClick={(event) => event.stopPropagation()}
        onWheel={handleWheel}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        onDoubleClick={() => (zoom > 1 ? resetView() : setZoom(3))}
      >
        {(!needsOriginal || !fullLoaded) && (
          <MemoryImage
            filePath={asset.filePath}
            modifiedAtFs={asset.modifiedAtFs}
            sourceWidth={asset.width}
            sourceHeight={asset.height}
            width={viewport.width}
            height={viewport.height}
            fit="contain"
            priority="high"
            alt={asset.fileName}
            className="bg-black"
          />
        )}
        {needsOriginal && (
          <img
            src={getLocalImageUrl(asset.filePath)}
            alt={asset.fileName}
            className={`${fullLoaded ? "block" : "absolute opacity-0 pointer-events-none"} max-w-full max-h-full select-none`}
            style={{
              transform: `scale(${zoom}) translate(${pan.x / zoom}px, ${pan.y / zoom}px)`,
              cursor: zoom > 1 ? (isPanning ? "grabbing" : "grab") : "default",
              transition: isPanning ? "none" : "transform 0.08s ease-out",
            }}
            decoding="async"
            draggable={false}
            onLoad={() => setFullLoaded(true)}
            onError={() => setFullLoaded(false)}
          />
        )}
      </div>

      <div className="absolute top-4 left-4 right-4 flex items-center justify-between pointer-events-none">
        <div className="max-w-[55vw] truncate text-white/90 text-xs bg-black/55 px-3 py-2 rounded-lg backdrop-blur-sm">
          {asset.fileName}
        </div>
        <div
          className="flex items-center gap-1.5 pointer-events-auto bg-black/40 p-1.5 rounded-xl backdrop-blur-sm"
          onClick={(event) => event.stopPropagation()}
        >
          <button onClick={zoomOut} disabled={zoom <= 1} className="h-8 w-8 bg-zinc-800 hover:bg-zinc-700 disabled:opacity-40 text-white rounded-lg" title="縮小">−</button>
          <span className="text-[10px] text-zinc-300 min-w-[3.5rem] text-center tabular-nums">{Math.round(zoom * 100)}%</span>
          <button onClick={zoomIn} className="h-8 w-8 bg-zinc-800 hover:bg-zinc-700 text-white rounded-lg" title="拡大">+</button>
          <button onClick={resetView} className="h-8 px-2.5 bg-zinc-800 hover:bg-zinc-700 text-white rounded-lg text-[10px]">全体表示</button>
          <button onClick={onClose} className="h-8 px-2.5 bg-zinc-800 hover:bg-zinc-700 text-white rounded-lg text-[10px]">閉じる</button>
        </div>
      </div>

      <button
        onClick={(event) => { event.stopPropagation(); onPrev(); }}
        disabled={!hasPrev}
        className="absolute left-4 top-1/2 -translate-y-1/2 w-11 h-11 rounded-full bg-white/10 hover:bg-white/20 text-white text-2xl disabled:opacity-20 backdrop-blur-sm"
        title="前の画像（←）"
      >
        ‹
      </button>
      <button
        onClick={(event) => { event.stopPropagation(); onNext(); }}
        disabled={!hasNext}
        className="absolute right-4 top-1/2 -translate-y-1/2 w-11 h-11 rounded-full bg-white/10 hover:bg-white/20 text-white text-2xl disabled:opacity-20 backdrop-blur-sm"
        title="次の画像（→）"
      >
        ›
      </button>

      <div className="absolute bottom-4 left-1/2 -translate-x-1/2 text-[10px] text-white/55 bg-black/35 px-3 py-1.5 rounded-full pointer-events-none backdrop-blur-sm">
        ホイール: 拡大・縮小　ダブルクリック: 3倍 / 全体表示　← →: 前後の画像　Esc: 閉じる
      </div>
    </div>
  );
}

function labelColor(label: string): string {
  switch (label) {
    case "red": return "#ef4444";
    case "orange": return "#f97316";
    case "yellow": return "#eab308";
    case "green": return "#22c55e";
    case "blue": return "#3b82f6";
    case "purple": return "#a855f7";
    default: return label || "transparent";
  }
}