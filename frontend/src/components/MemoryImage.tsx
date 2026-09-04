import { useEffect, useMemo, useRef, useState } from "react";
import { getLocalImageUrl } from "../api/client";
import { loadMemoryBitmap, type ImageDecodePriority, type ImageFit } from "../utils/imagePipeline";

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

    // Do not resize/clear the canvas before the replacement bitmap is ready.
    // Keeping the previous pixels stretched by CSS avoids the black flash on a
    // library refresh or when switching thumbnail size from medium to large.
    loadMemoryBitmap({
      filePath,
      modifiedAtFs,
      sourceWidth,
      sourceHeight,
      targetWidth,
      targetHeight,
      fit,
      priority,
    })
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
