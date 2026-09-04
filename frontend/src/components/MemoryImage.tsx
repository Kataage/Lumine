import { useEffect, useMemo, useRef, useState } from "react";
import { getLocalImageUrl } from "../api/client";
import {
  computeCoverCrop,
  getCachedMemoryBitmap,
  loadMemoryBitmap,
  type ImageDecodePriority,
  type ImageFit,
} from "../utils/imagePipeline";

interface MemoryImageProps {
  filePath: string;
  modifiedAtFs?: string;
  sourceWidth?: number;
  sourceHeight?: number;
  width: number;
  height: number;
  fit?: ImageFit;
  priority?: ImageDecodePriority;
  alt?: string;
  className?: string;
}

function paintCachedPlaceholder(
  canvas: HTMLCanvasElement,
  bitmap: ImageBitmap,
  targetWidth: number,
  targetHeight: number,
  fit: ImageFit
): boolean {
  const ctx = canvas.getContext("2d", { alpha: false });
  if (!ctx) return false;

  canvas.width = targetWidth;
  canvas.height = targetHeight;
  ctx.clearRect(0, 0, targetWidth, targetHeight);

  if (fit === "cover") {
    const crop = computeCoverCrop(bitmap.width, bitmap.height, targetWidth, targetHeight);
    ctx.drawImage(
      bitmap,
      crop.x,
      crop.y,
      crop.width,
      crop.height,
      0,
      0,
      targetWidth,
      targetHeight
    );
    return true;
  }

  const scale = Math.min(targetWidth / Math.max(1, bitmap.width), targetHeight / Math.max(1, bitmap.height));
  const drawWidth = Math.max(1, Math.round(bitmap.width * scale));
  const drawHeight = Math.max(1, Math.round(bitmap.height * scale));
  const x = Math.floor((targetWidth - drawWidth) / 2);
  const y = Math.floor((targetHeight - drawHeight) / 2);
  ctx.drawImage(bitmap, x, y, drawWidth, drawHeight);
  return true;
}

export function MemoryImage({
  filePath,
  modifiedAtFs,
  sourceWidth,
  sourceHeight,
  width,
  height,
  fit = "cover",
  priority = "normal",
  alt = "",
  className = "",
}: MemoryImageProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const hasRenderedRef = useRef(false);
  const [loading, setLoading] = useState(true);
  const [fallback, setFallback] = useState(false);
  const [fallbackError, setFallbackError] = useState(false);

  const pixelRatio = useMemo(() => {
    if (typeof window === "undefined") return 1;
    return Math.min(2, Math.max(1, window.devicePixelRatio || 1));
  }, []);

  useEffect(() => {
    let cancelled = false;
    if (!hasRenderedRef.current) setLoading(true);

    const canvas = canvasRef.current;
    if (!filePath) {
      setFallbackError(true);
      setLoading(false);
      return () => {
        cancelled = true;
      };
    }

    if (!canvas || typeof createImageBitmap !== "function") {
      setFallback(true);
      setFallbackError(false);
      return () => {
        cancelled = true;
      };
    }

    const targetWidth = Math.max(1, Math.round(width * pixelRatio));
    const targetHeight = Math.max(1, Math.round(height * pixelRatio));
    const request = {
      filePath,
      modifiedAtFs,
      sourceWidth,
      sourceHeight,
      targetWidth,
      targetHeight,
      fit,
      priority,
    } as const;

    // A thumbnail-size change can regroup virtualized rows and remount cards.
    // In that case there is no old canvas to preserve, so paint the closest
    // already-decoded bitmap from the memory LRU synchronously. The sharper
    // requested bitmap replaces it as soon as decoding finishes.
    if (!hasRenderedRef.current) {
      const cachedPlaceholder = getCachedMemoryBitmap(request);
      if (cachedPlaceholder && paintCachedPlaceholder(canvas, cachedPlaceholder, targetWidth, targetHeight, fit)) {
        hasRenderedRef.current = true;
        setLoading(false);
      }
    }

    // Do not resize/clear an already-rendered canvas before the replacement
    // bitmap is ready. CSS can stretch the existing pixels temporarily without
    // ever presenting an empty black frame.
    loadMemoryBitmap(request)
      .then((bitmap) => {
        if (cancelled) return;
        const ctx = canvas.getContext("2d", { alpha: false });
        if (!ctx) throw new Error("2D canvas unavailable");
        canvas.width = targetWidth;
        canvas.height = targetHeight;
        ctx.clearRect(0, 0, targetWidth, targetHeight);
        const x = Math.floor((targetWidth - bitmap.width) / 2);
        const y = Math.floor((targetHeight - bitmap.height) / 2);
        ctx.drawImage(bitmap, x, y);
        hasRenderedRef.current = true;
        setFallback(false);
        setFallbackError(false);
        setLoading(false);
      })
      .catch((error) => {
        if (cancelled) return;
        console.debug("Memory image decode fallback", filePath, error);
        // Keep the existing canvas under the fallback image until the browser
        // has decoded that image too. This avoids replacing a valid preview
        // with an empty frame just because the high-quality decode failed.
        setFallback(true);
        setFallbackError(false);
      });

    return () => {
      cancelled = true;
    };
  }, [filePath, modifiedAtFs, sourceWidth, sourceHeight, width, height, fit, priority, pixelRatio]);

  return (
    <div
      className={`relative overflow-hidden bg-muted ${className}`}
      style={{ width, height }}
      role="img"
      aria-label={alt || undefined}
    >
      {loading && !hasRenderedRef.current && <div className="absolute inset-0 bg-muted animate-pulse" />}
      <canvas
        ref={canvasRef}
        className="block w-full h-full"
        style={{ width, height }}
      />
      {fallback && !fallbackError && (
        <img
          src={getLocalImageUrl(filePath)}
          alt={alt}
          className={`absolute inset-0 w-full h-full ${fit === "cover" ? "object-cover" : "object-contain"}`}
          decoding="async"
          draggable={false}
          onLoad={() => {
            hasRenderedRef.current = true;
            setLoading(false);
          }}
          onError={() => {
            setFallbackError(true);
            setLoading(false);
          }}
        />
      )}
      {fallbackError && (
        <div className="absolute inset-0 flex flex-col items-center justify-center gap-1 text-muted-foreground/50 text-[10px] bg-muted/80">
          <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 15.75l5.159-5.159a2.25 2.25 0 013.182 0l5.159 5.159m-1.5-1.5l1.409-1.409a2.25 2.25 0 013.182 0l2.909 2.909M3.75 21h16.5A2.25 2.25 0 0022.5 18.75V5.25A2.25 2.25 0 0020.25 3H3.75A2.25 2.25 0 001.5 5.25v13.5A2.25 2.25 0 003.75 21z" />
          </svg>
          <span>プレビュー不可</span>
        </div>
      )}
    </div>
  );
}
