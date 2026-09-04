import { useCallback, useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import { createPortal } from "react-dom";
import type { AssetDTO } from "../api/client";
import { getLocalImageUrl } from "../api/client";
import { MemoryImage } from "./MemoryImage";

interface ImageViewerModalProps {
  asset: AssetDTO;
  onClose: () => void;
  onPrev?: () => void;
  onNext?: () => void;
  hasPrev?: boolean;
  hasNext?: boolean;
}

interface DragState {
  pointerId: number;
  x: number;
  y: number;
}

export function ImageViewerModal({
  asset,
  onClose,
  onPrev,
  onNext,
  hasPrev = false,
  hasNext = false,
}: ImageViewerModalProps) {
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [dragging, setDragging] = useState(false);
  const [originalLoaded, setOriginalLoaded] = useState(false);
  const dragState = useRef<DragState | null>(null);
  const [viewport, setViewport] = useState(() => ({
    width: Math.max(320, window.innerWidth - 48),
    height: Math.max(240, window.innerHeight - 132),
  }));

  const reset = useCallback(() => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
    setDragging(false);
    setOriginalLoaded(false);
    dragState.current = null;
  }, []);

  useEffect(() => reset(), [asset.id, reset]);

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = previousOverflow;
    };
  }, []);

  useEffect(() => {
    const update = () => setViewport({
      width: Math.max(320, window.innerWidth - 48),
      height: Math.max(240, window.innerHeight - 132),
    });
    window.addEventListener("resize", update);
    return () => window.removeEventListener("resize", update);
  }, []);

  const setZoomSafe = useCallback((nextZoom: number) => {
    const clamped = Math.max(1, Math.min(8, nextZoom));
    setZoom(clamped);
    if (clamped <= 1) {
      setPan({ x: 0, y: 0 });
      setDragging(false);
      dragState.current = null;
    }
  }, []);

  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
      if (event.key === "ArrowLeft" && hasPrev && onPrev) {
        event.preventDefault();
        onPrev();
      }
      if (event.key === "ArrowRight" && hasNext && onNext) {
        event.preventDefault();
        onNext();
      }
      if (event.key === "+" || event.key === "=") {
        event.preventDefault();
        setZoom((value) => Math.min(8, value + 0.5));
      }
      if (event.key === "-") {
        event.preventDefault();
        setZoom((value) => {
          const next = Math.max(1, value - 0.5);
          if (next <= 1) setPan({ x: 0, y: 0 });
          return next;
        });
      }
      if (event.key === "0") {
        event.preventDefault();
        reset();
      }
    };
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [hasNext, hasPrev, onClose, onNext, onPrev, reset]);

  const handleWheel = useCallback((event: React.WheelEvent<HTMLDivElement>) => {
    event.preventDefault();
    const delta = -event.deltaY * 0.0016;
    setZoom((value) => {
      const next = Math.max(1, Math.min(8, value + delta));
      if (next <= 1) setPan({ x: 0, y: 0 });
      return next;
    });
  }, []);

  const beginDrag = useCallback((event: ReactPointerEvent<HTMLDivElement>) => {
    if (zoom <= 1 || event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    if (typeof event.currentTarget.setPointerCapture === "function") {
      event.currentTarget.setPointerCapture(event.pointerId);
    }
    dragState.current = {
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY,
    };
    setDragging(true);
  }, [zoom]);

  const moveDrag = useCallback((event: ReactPointerEvent<HTMLDivElement>) => {
    const current = dragState.current;
    if (!current || current.pointerId !== event.pointerId || zoom <= 1) return;
    event.preventDefault();
    const deltaX = event.clientX - current.x;
    const deltaY = event.clientY - current.y;
    if (deltaX === 0 && deltaY === 0) return;
    setPan((value) => ({ x: value.x + deltaX, y: value.y + deltaY }));
    dragState.current = { ...current, x: event.clientX, y: event.clientY };
  }, [zoom]);

  const endDrag = useCallback((event: ReactPointerEvent<HTMLDivElement>) => {
    const current = dragState.current;
    if (!current || current.pointerId !== event.pointerId) return;
    if (typeof event.currentTarget.hasPointerCapture === "function" && event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    dragState.current = null;
    setDragging(false);
  }, []);

  const needsOriginal = zoom > 1;
  const canPan = zoom > 1;

  return createPortal(
    <div
      className="fixed inset-0 z-[100] bg-black/96 text-white"
      role="dialog"
      aria-modal="true"
      aria-label={`${asset.fileName} を大きく表示`}
      onClick={onClose}
    >
      <div className="absolute inset-x-0 top-0 h-16 px-4 flex items-center gap-3 bg-gradient-to-b from-black/80 to-transparent z-20 pointer-events-none">
        <div className="min-w-0 flex-1">
          <p className="text-sm font-semibold truncate">{asset.fileName}</p>
          <p className="text-[11px] text-white/55 truncate">ホイール: 拡大縮小 / 拡大後ドラッグ: 移動 / 0: 全体表示 / Esc: 閉じる</p>
        </div>
        <div className="flex items-center gap-1.5 pointer-events-auto" onClick={(event) => event.stopPropagation()}>
          <button className="viewer-control" onClick={() => setZoomSafe(zoom - 0.5)} aria-label="縮小">−</button>
          <span className="min-w-14 text-center text-xs text-white/75 tabular-nums">{Math.round(zoom * 100)}%</span>
          <button className="viewer-control" onClick={() => setZoomSafe(zoom + 0.5)} aria-label="拡大">＋</button>
          <button className="viewer-control px-3 text-xs" onClick={reset}>全体</button>
          <button className="viewer-control text-lg" onClick={onClose} aria-label="閉じる">×</button>
        </div>
      </div>

      <div
        className="absolute inset-0 flex items-center justify-center overflow-hidden select-none"
        data-testid="viewer-stage"
        onClick={(event) => event.stopPropagation()}
        onWheel={handleWheel}
        onPointerDown={beginDrag}
        onPointerMove={moveDrag}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        onDoubleClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          if (zoom > 1) reset(); else setZoomSafe(2.5);
        }}
        style={{
          touchAction: "none",
          cursor: canPan ? (dragging ? "grabbing" : "grab") : "zoom-in",
        }}
      >
        <div
          data-testid="viewer-transform"
          className="relative flex items-center justify-center"
          style={{
            width: viewport.width,
            height: viewport.height,
            transform: `translate3d(${pan.x}px, ${pan.y}px, 0) scale(${zoom})`,
            transformOrigin: "center center",
            transition: dragging ? "none" : "transform 90ms ease-out",
            willChange: zoom > 1 ? "transform" : "auto",
          }}
        >
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

          {needsOriginal && asset.filePath && (
            <img
              src={getLocalImageUrl(asset.filePath)}
              alt={asset.fileName}
              decoding="async"
              draggable={false}
              onLoad={() => setOriginalLoaded(true)}
              onError={() => setOriginalLoaded(false)}
              className={`absolute inset-0 w-full h-full object-contain transition-opacity duration-100 ${originalLoaded ? "opacity-100" : "opacity-0"}`}
            />
          )}
        </div>
      </div>

      {onPrev && (
        <button
          onClick={(event) => { event.stopPropagation(); onPrev(); }}
          disabled={!hasPrev}
          className="viewer-nav left-4"
          aria-label="前の画像"
        >
          ‹
        </button>
      )}
      {onNext && (
        <button
          onClick={(event) => { event.stopPropagation(); onNext(); }}
          disabled={!hasNext}
          className="viewer-nav right-4"
          aria-label="次の画像"
        >
          ›
        </button>
      )}

      <div className="absolute inset-x-0 bottom-3 flex justify-center pointer-events-none">
        <div className="rounded-full bg-black/55 border border-white/10 px-3 py-1.5 text-[11px] text-white/60 backdrop-blur-sm">
          ダブルクリックで拡大 / 拡大後はドラッグで移動
        </div>
      </div>
    </div>,
    document.body
  );
}
