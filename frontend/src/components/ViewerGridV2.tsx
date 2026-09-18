import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import type { AssetDTO, AssetListRequest } from "../api/client";
import { listAssets, listSimilarAssets, semanticSearchAssets } from "../api/client";
import { useApp } from "../App";
import { formatFileSize } from "../utils/format";
import {
  computeViewerOverscan,
  getViewerVisibleRange,
  shouldFetchViewerPageAhead,
  viewerImagePriority,
  type ViewerImagePriority,
} from "../utils/viewerPreload";
import { registerViewerOpenHandler } from "../utils/viewerSession";
import { MemoryImage } from "./MemoryImage";
import { ImageViewerModal } from "./ImageViewerModal";

const PAGE_SIZE = 200;
const GAP = 8;
const GRID_PADDING = 24;
const LIST_ROW_HEIGHT = 60;

interface ViewerGridV2Props {
  onSelectAsset: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onOpenDetail: (asset: AssetDTO) => void;
  onAssetsLoaded: (ids: number[]) => void;
}

export function ViewerGridV2({ onSelectAsset, onOpenDetail, onAssetsLoaded }: ViewerGridV2Props) {
  const { state } = useApp();
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(0);
  const [containerHeight, setContainerHeight] = useState(0);
  const [previewAsset, setPreviewAsset] = useState<AssetDTO | null>(null);
  const openPreview = useCallback((asset: AssetDTO) => setPreviewAsset(asset), []);

  useEffect(() => registerViewerOpenHandler(openPreview), [openPreview]);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;
    const updateSize = () => {
      setContainerWidth(Math.max(0, element.clientWidth - GRID_PADDING));
      setContainerHeight(Math.max(0, element.clientHeight));
    };
    const observer = new ResizeObserver(updateSize);
    observer.observe(element);
    updateSize();
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
    tagIds: state.filterTagIds.length > 0 ? state.filterTagIds : undefined,
    offset,
    limit: PAGE_SIZE,
  }), [
    state.filterRating,
    state.filterStatusLabel,
    state.filterTagIds,
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
      state.searchMode,
      state.similarAssetId,
      state.sortBy,
      state.sortDesc,
      state.filterStatusLabel,
      state.filterRating,
      state.filterTagIds.join(","),
    ],
    queryFn: async ({ pageParam = 0 }) => {
      const request = buildQuery(Number(pageParam));
      if (state.similarAssetId) {
        return listSimilarAssets(state.similarAssetId, request);
      }
      if (state.searchMode === "semantic" && state.searchQuery.trim()) {
        return semanticSearchAssets(request);
      }
      const result = await listAssets(request);
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

  useEffect(() => {
    containerRef.current?.scrollTo({ top: 0, left: 0 });
  }, [
    state.selectedLibraryId,
    state.selectedFolderPath,
    state.searchQuery,
    state.searchMode,
    state.similarAssetId,
    state.sortBy,
    state.sortDesc,
    state.filterStatusLabel,
    state.filterRating,
    state.filterTagIds,
  ]);

  const columns = containerWidth > 0
    ? Math.max(1, Math.floor((containerWidth + GAP) / (state.thumbnailSize + GAP)))
    : 4;
  const rowCount = Math.ceil(assets.length / columns);
  const itemExtent = state.viewMode === "grid" ? state.thumbnailSize + GAP : LIST_ROW_HEIGHT;
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
    virtualizer.measure();
  }, [columns, state.thumbnailSize, state.viewMode, virtualizer]);

  useEffect(() => {
    if (!hasNextPage || isFetchingNextPage) return;
    const last = virtualItems[virtualItems.length - 1];
    if (last && shouldFetchViewerPageAhead(last.index, itemCount, containerHeight, itemExtent)) {
      void fetchNextPage();
    }
  }, [containerHeight, fetchNextPage, hasNextPage, isFetchingNextPage, itemCount, itemExtent, virtualItems]);

  const previewIndex = previewAsset ? assets.findIndex((asset) => asset.id === previewAsset.id) : -1;

  useEffect(() => {
    if (!previewAsset || !hasNextPage || isFetchingNextPage || previewIndex < 0) return;
    const threshold = Math.max(8, columns * 2);
    if (previewIndex >= assets.length - threshold) void fetchNextPage();
  }, [assets.length, columns, fetchNextPage, hasNextPage, isFetchingNextPage, previewAsset, previewIndex]);

  const goNext = useCallback(async () => {
    if (previewIndex < 0) return;
    if (previewIndex < assets.length - 1) {
      setPreviewAsset(assets[previewIndex + 1]);
      return;
    }
    if (!hasNextPage || isFetchingNextPage) return;

    const result = await fetchNextPage();
    const nextAssets = result.data?.pages.flatMap((page) => page.assets) ?? assets;
    const currentIndex = previewAsset ? nextAssets.findIndex((asset) => asset.id === previewAsset.id) : -1;
    if (currentIndex >= 0 && currentIndex < nextAssets.length - 1) {
      setPreviewAsset(nextAssets[currentIndex + 1]);
    }
  }, [assets, fetchNextPage, hasNextPage, isFetchingNextPage, previewAsset, previewIndex]);

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
        <p className="text-sm font-semibold text-destructive">{state.similarAssetId ? "類似画像を検索できませんでした" : state.searchMode === "semantic" ? "意味検索を実行できませんでした" : "画像一覧を読み込めませんでした"}</p>
        <p className="max-w-lg text-center text-xs text-muted-foreground break-all">{String(error)}</p>
      </div>
    );
  }

  const hasFilters = !!(
    state.selectedFolderPath ||
    state.searchQuery ||
    state.similarAssetId ||
    state.filterStatusLabel ||
    state.filterRating > 0 ||
    state.filterTagIds.length > 0
  );

  return (
    <>
      <div ref={containerRef} className="flex-1 min-w-0 overflow-auto bg-background p-3">
        {state.viewMode === "grid" ? (
          <div style={{ height: virtualizer.getTotalSize(), width: "100%", position: "relative" }}>
            {virtualItems.map((virtualRow) => {
              const startIndex = virtualRow.index * columns;
              const rowAssets = assets.slice(startIndex, startIndex + columns);
              const priority = viewerImagePriority(virtualRow.index, visibleRange.first, visibleRange.last);
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
                        priority={priority}
                        onSelect={onSelectAsset}
                        onDetail={() => onOpenDetail(asset)}
                        onPreview={() => openPreview(asset)}
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
              const priority = viewerImagePriority(item.index, visibleRange.first, visibleRange.last);
              return (
                <ListRow
                  key={asset.id}
                  asset={asset}
                  selected={state.selectedAssets.has(asset.id)}
                  priority={priority}
                  onSelect={onSelectAsset}
                  onDetail={() => onOpenDetail(asset)}
                  onPreview={() => openPreview(asset)}
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
            続きを先読み中… {assets.length.toLocaleString()} / {totalCount.toLocaleString()}件
          </div>
        )}

        {!isLoading && assets.length === 0 && (
          <div className="min-h-[55vh] flex items-center justify-center">
            <div className="max-w-sm rounded-2xl border border-dashed border-border p-8 text-center">
              <p className="text-sm font-semibold">{hasFilters ? "条件に一致する画像がありません" : "画像が見つかりません"}</p>
              <p className="mt-2 text-xs leading-relaxed text-muted-foreground">
                {state.similarAssetId
                  ? "この画像に近いembeddingを持つ画像がまだありません。Semantic Searchの解析状況を確認してください。"
                  : state.searchMode === "semantic" && state.searchQuery
                    ? "意味検索に使えるembeddingがまだないか、条件に一致する画像がありません。"
                    : hasFilters
                      ? "上部の絞り込み条件を解除して確認してください。"
                      : "画像フォルダーの変更は自動で確認されます。必要なら左側から再スキャンもできます。"}
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
          onNext={() => { void goNext(); }}
          hasPrev={previewIndex > 0}
          hasNext={previewIndex >= 0 && (previewIndex < assets.length - 1 || !!hasNextPage)}
        />
      )}
    </>
  );
}

function GridCard({
  asset,
  size,
  selected,
  priority,
  onSelect,
  onDetail,
  onPreview,
}: {
  asset: AssetDTO;
  size: number;
  selected: boolean;
  priority: ViewerImagePriority;
  onSelect: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onDetail: () => void;
  onPreview: () => void;
}) {
  return (
    <div
      className={`relative overflow-hidden rounded-xl border bg-muted group transition-[border-color,box-shadow] ${selected ? "border-primary ring-2 ring-primary/30 shadow-lg" : "border-border/60 hover:border-border hover:shadow-lg"}`}
      style={{ width: size, height: size }}
      onClick={(event) => onSelect(asset, event.ctrlKey || event.metaKey, event.shiftKey)}
      onDoubleClick={(event) => { event.preventDefault(); onPreview(); }}
      tabIndex={0}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          onPreview();
        } else if (event.key.toLowerCase() === "i") {
          event.preventDefault();
          onDetail();
        }
      }}
      title={`${asset.fileName}\nクリック: 選択 / ダブルクリック・Enter・Space: 拡大 / I: 詳細`}
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
      <div className="absolute inset-x-0 bottom-0 h-20 bg-gradient-to-t from-black/85 via-black/25 to-transparent pointer-events-none" />
      <div className="absolute inset-x-0 bottom-0 px-2.5 py-2 pointer-events-none">
        <p className="truncate text-[11px] font-medium text-white drop-shadow">{asset.fileName}</p>
        <div className="mt-0.5 flex items-center justify-between gap-2 text-[10px] text-white/60">
          <span>{formatFileSize(asset.fileSize)}</span>
          {typeof asset.semanticScore === "number" && <span>{Math.round(asset.semanticScore * 100)}%</span>}
        </div>
      </div>

      <div className={`absolute top-2 right-2 flex gap-1 transition-opacity ${selected ? "opacity-100" : "opacity-0 group-hover:opacity-100 group-focus-within:opacity-100"}`}>
        <button
          onClick={(event) => { event.stopPropagation(); onDetail(); }}
          className="w-8 h-8 rounded-lg border border-white/20 bg-black/65 text-xs font-medium text-white hover:bg-black/80"
          aria-label={`${asset.fileName} の詳細を表示`}
          title="詳細 (I)"
        >
          ⓘ
        </button>
        <button
          onClick={(event) => { event.stopPropagation(); onPreview(); }}
          className="w-8 h-8 rounded-lg border border-white/20 bg-black/65 text-xs font-medium text-white hover:bg-black/80"
          aria-label={`${asset.fileName} を大きく表示`}
          title="大きく表示 (Enter / Space)"
        >
          ⛶
        </button>
      </div>

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
  priority,
  onSelect,
  onDetail,
  onPreview,
  style,
}: {
  asset: AssetDTO;
  selected: boolean;
  priority: ViewerImagePriority;
  onSelect: (asset: AssetDTO, multi: boolean, range: boolean) => void;
  onDetail: () => void;
  onPreview: () => void;
  style: React.CSSProperties;
}) {
  return (
    <div
      style={style}
      className={`flex items-center gap-3 px-2.5 rounded-lg border border-transparent ${selected ? "bg-primary/10 border-primary/25" : "hover:bg-accent/50"}`}
      onClick={(event) => onSelect(asset, event.ctrlKey || event.metaKey, event.shiftKey)}
      onDoubleClick={onPreview}
      tabIndex={0}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          onPreview();
        } else if (event.key.toLowerCase() === "i") {
          event.preventDefault();
          onDetail();
        }
      }}
    >
      <MemoryImage
        filePath={asset.filePath}
        modifiedAtFs={asset.modifiedAtFs}
        sourceWidth={asset.width}
        sourceHeight={asset.height}
        width={44}
        height={44}
        fit="cover"
        priority={priority}
        alt={asset.fileName}
        className="rounded-lg flex-shrink-0"
      />
      <div className="min-w-0 flex-1">
        <p className="truncate text-xs font-medium">{asset.fileName}</p>
        <p className="truncate text-[11px] text-muted-foreground">{asset.folderPath}</p>
      </div>
      {typeof asset.semanticScore === "number" && <span className="text-[10px] text-primary tabular-nums flex-shrink-0">{Math.round(asset.semanticScore * 100)}%</span>}
      <span className="text-[11px] text-muted-foreground tabular-nums flex-shrink-0">{formatFileSize(asset.fileSize)}</span>
      <button
        onClick={(event) => { event.stopPropagation(); onDetail(); }}
        className="ui-secondary-button flex-shrink-0"
        title="詳細 (I)"
      >
        ⓘ 詳細
      </button>
      <button
        onClick={(event) => { event.stopPropagation(); onPreview(); }}
        className="ui-secondary-button flex-shrink-0"
        title="大きく表示 (Enter / Space)"
      >
        ⛶ 拡大
      </button>
    </div>
  );
}
