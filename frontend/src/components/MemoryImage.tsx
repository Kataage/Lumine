import { useEffect, useMemo, useRef, useState } from "react";
import { getLocalImageUrl } from "../api/client";
import { loadMemoryBitmap, type ImageFit } from "../utils/imagePipeline";

interface MemoryImageProps {
  filePath: string;
  modifiedAtFs?: string;
  sourceWidth?: number;
  sourceHeight?: number;
  width: number;
  height: number;
  fit?: ImageFit;
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
  alt = "",
  className = "",
}: MemoryImageProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [loading, setLoading] = useState(true);
  const [fallback, setFallback] = useState(false);
  const [fallbackError, setFallbackError] = useState(false);

  const pixelRatio = useMemo(() => {
    if (typeof window === "undefined") return 1;
    return Math.min(2, Math.max(1, window.devicePixelRatio || 1));
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setFallback(false);
    setFallbackError(false);

    const canvas = canvasRef.current;
    if (!canvas || typeof createImageBitmap !== "function") {
      setFallback(true);
      setLoading(false);
      return () => {
        cancelled = true;
      };
    }

    const targetWidth = Math.max(1, Math.round(width * pixelRatio));
    const targetHeight = Math.max(1, Math.round(height * pixelRatio));
    canvas.width = targetWidth;
    canvas.height = targetHeight;

    loadMemoryBitmap({
      filePath,
      modifiedAtFs,
      sourceWidth,
      sourceHeight,
      targetWidth,
      targetHeight,
      fit,
    })
      .then((bitmap) => {
        if (cancelled) return;
        const ctx = canvas.getContext("2d", { alpha: false });
        if (!ctx) throw new Error("2D canvas unavailable");
        ctx.clearRect(0, 0, targetWidth, targetHeight);
        const x = Math.floor((targetWidth - bitmap.width) / 2);
        const y = Math.floor((targetHeight - bitmap.height) / 2);
        ctx.drawImage(bitmap, x, y);
        setLoading(false);
      })
      .catch((error) => {
        if (cancelled) return;
        console.debug("Memory image decode fallback", filePath, error);
        setFallback(true);
        setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [filePath, modifiedAtFs, sourceWidth, sourceHeight, width, height, fit, pixelRatio]);

  return (
    <div
      className={`relative overflow-hidden bg-muted ${className}`}
      style={{ width, height }}
      role="img"
      aria-label={alt || undefined}
    >
      {loading && <div className="absolute inset-0 bg-muted animate-pulse" />}
      <canvas
        ref={canvasRef}
        className={`w-full h-full ${fallback ? "hidden" : "block"}`}
        style={{ width, height }}
      />
      {fallback && !fallbackError && (
        <img
          src={getLocalImageUrl(filePath)}
          alt={alt}
          className={`w-full h-full ${fit === "cover" ? "object-cover" : "object-contain"}`}
          decoding="async"
          draggable={false}
          onLoad={() => setLoading(false)}
          onError={() => setFallbackError(true)}
        />
      )}
      {fallbackError && (
        <div className="absolute inset-0 flex items-center justify-center text-muted-foreground/50">
          <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 15.75l5.159-5.159a2.25 2.25 0 013.182 0l5.159 5.159m-1.5-1.5l1.409-1.409a2.25 2.25 0 013.182 0l2.909 2.909M3.75 21h16.5A2.25 2.25 0 0022.5 18.75V5.25A2.25 2.25 0 0020.25 3H3.75A2.25 2.25 0 001.5 5.25v13.5A2.25 2.25 0 003.75 21z" />
          </svg>
        </div>
      )}
    </div>
  );
}
