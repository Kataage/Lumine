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
    <div ref={containerRef} className="flex-1 overflow-auto p-2">
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
        <div className="flex justify-center py-8 text-zinc-500 text-sm">
          Loading...
        </div>
      )}

      {isFetchingNextPage && (
        <div className="flex justify-center py-4 text-zinc-500 text-sm">
          Loading more... ({images.length}/{totalCount})
        </div>
      )}

      {!isLoading && images.length === 0 && (
        <div className="flex justify-center py-8 text-zinc-500">
          No images found
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
      className="relative rounded overflow-hidden bg-zinc-900"
    >
      {!loaded && !error && (
        <div className="absolute inset-0 bg-zinc-800 animate-pulse" />
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
        <div className="w-full h-full flex items-center justify-center text-zinc-600 text-xs">
          Error
        </div>
      ) : null}
      <div className="absolute bottom-0 left-0 right-0 px-1 py-0.5 bg-black/50 text-white text-[10px] truncate opacity-0 hover:opacity-100 transition-opacity">
        {image.fileName}
      </div>
    </div>
  );
}
