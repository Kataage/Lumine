import { useRef, useState, useEffect, useCallback } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useVirtualizer } from "@tanstack/react-virtual";
import { scanFolder } from "./api/client";
import type { ImageInfo } from "./api/client";

const PAGE_SIZE = 100;
const THUMBNAIL_SIZE = 180;
const GAP = 8;

interface ImageGridProps {
  folderPath: string;
}

export function ImageGrid({ folderPath }: ImageGridProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [containerWidth, setContainerWidth] = useState(0);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const observer = new ResizeObserver(() => {
      setContainerWidth(el.clientWidth);
    });
    observer.observe(el);
    setContainerWidth(el.clientWidth);
    return () => observer.disconnect();
  }, []);

  const {
    data,
    isLoading,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery({
    queryKey: ["images", folderPath],
    queryFn: async ({ pageParam = 0 }) => {
      return scanFolder(folderPath, pageParam as number, PAGE_SIZE);
    },
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) => {
      if (!lastPage.hasMore) return undefined;
      return allPages.length * PAGE_SIZE;
    },
  });

  const images = data?.pages.flatMap((p) => p.images) ?? [];
  const totalCount = data?.pages[0]?.totalCount ?? 0;

  const columns = containerWidth > 0
    ? Math.max(1, Math.floor((containerWidth + GAP) / (THUMBNAIL_SIZE + GAP)))
    : 4;

  const rowCount = Math.ceil(images.length / columns);

  const virtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => containerRef.current,
    estimateSize: () => THUMBNAIL_SIZE + 24,
    overscan: 3,
  });

  const items = virtualizer.getVirtualItems();

  const loadMore = useCallback(() => {
    if (hasNextPage && !isFetchingNextPage) {
      fetchNextPage();
    }
  }, [hasNextPage, isFetchingNextPage, fetchNextPage]);

  useEffect(() => {
    if (!hasNextPage || isFetchingNextPage) return;
    const lastItem = items[items.length - 1];
    if (lastItem && lastItem.index >= rowCount - 5) {
      loadMore();
    }
  }, [items, rowCount, hasNextPage, isFetchingNextPage, loadMore]);

  const range = virtualizer.range;

  return (
    <div ref={containerRef} className="flex-1 overflow-auto p-3">
      <div
        style={{
          height: `${virtualizer.getTotalSize()}px`,
          width: "100%",
          position: "relative",
        }}
      >
        {items.map((virtualRow) => {
          const startIndex = virtualRow.index * columns;
          const rowImages = images.slice(startIndex, startIndex + columns);
          const isInRange = range
            ? virtualRow.index >= range.startIndex && virtualRow.index <= range.endIndex
            : false;

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
                {rowImages.map((image) => (
                  <ImageItem
                    key={image.filePath}
                    image={image}
                    size={THUMBNAIL_SIZE}
                    shouldLoad={isInRange}
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
          <p className="text-sm text-muted-foreground">Scanning folder...</p>
        </div>
      )}

      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 gap-2 text-muted-foreground text-sm">
          <div className="w-4 h-4 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
          <span>Loading more... ({images.length}/{totalCount})</span>
        </div>
      )}

      {!isLoading && images.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 gap-3">
          <svg className="w-12 h-12 text-muted-foreground/50" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 15.75l5.159-5.159a2.25 2.25 0 013.182 0l5.159 5.159m-1.5-1.5l1.409-1.409a2.25 2.25 0 013.182 0l2.909 2.909M3.75 21h16.5A2.25 2.25 0 0022.5 18.75V5.25A2.25 2.25 0 0020.25 3H3.75A2.25 2.25 0 001.5 5.25v13.5A2.25 2.25 0 003.75 21z" />
          </svg>
          <p className="text-muted-foreground text-sm">No images found in this folder</p>
        </div>
      )}
    </div>
  );
}

interface ImageItemProps {
  image: ImageInfo;
  size: number;
  shouldLoad: boolean;
}

function ImageItem({ image, size, shouldLoad }: ImageItemProps) {
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState(false);
  const [src, setSrc] = useState<string | null>(null);

  useEffect(() => {
    if (shouldLoad && !src) {
      const normalizedPath = image.filePath.replace(/\\/g, "/");
      setSrc(`/local/${normalizedPath}`);
    }
  }, [shouldLoad, image.filePath, src]);

  return (
    <div
      style={{ width: size, height: size }}
      className="relative rounded-lg overflow-hidden bg-muted group cursor-pointer border border-border/50 hover:border-border transition-colors"
    >
      {!loaded && !error && (
        <div className="absolute inset-0 bg-muted animate-pulse" />
      )}
      {src && !error ? (
        <img
          src={src}
          alt={image.fileName}
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
        <p className="text-white text-xs truncate font-medium">{image.fileName}</p>
        <p className="text-white/60 text-[10px]">{formatFileSize(image.fileSize)}</p>
      </div>
    </div>
  );
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
