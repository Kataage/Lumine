import { useCallback, useEffect, useRef, useState } from "react";
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
  const dragOrigin = useRef({ x: 0, y: 0 });
  const [viewport, setViewport] = useState(() => ({
    width: Math.max(320, window.innerWidth - 48),
    height: Math.max(240, window.innerHeight - 132),
  }));

  const reset = useCallback(() => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
    setDragging(false);
    setOriginalLoaded(false);
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
        setZoom((value) => Math.max(1, value - 0.5));
      }
      if (event.key === "0") {
        event.preventDefault();
        reset();
      }
    };
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [hasNext, hasPrev, onClose, onNext, onPrev, reset]);

  useEffect(() => {
    if (zoom <= 1) {
      setOriginalLoaded(false);
      setPan({ x: 0, y: 0 });
    }
  }, [zoom]);

  const handleWheel = useCallback((event: React.WheelEvent) => {
    event.preventDefault();
    setZoom((value) => Math.max(1, Math.min(8, value - event.deltaY * 0.0016)));
  }, []);

  const beginDrag = useCallback((event: React.MouseEvent) => {
    if (zoom <= 1) return;
    setDragging(true);
    dragOrigin.current = { x: event.clientX, y: event.clientY };
  }, [zoom]);

  const moveDrag = useCallback((event: React.MouseEvent) => {
    if (!dragging || zoom <= 1) return;
    setPan((current) => ({
      x: current.x + event.clientX - dragOrigin.current.x,
      y: current.y + event.clientY - dragOrigin.current.y,
    }));
    dragOrigin.current = { x: event.clientX, y: event.clientY };
  }, [dragging, zoom]);

  const needsOriginal = zoom > 1;

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
          <p className="text-[11px] text-white/55 truncate">ホイール: 拡大縮小 / ドラッグ: 移動 / 0: 全体表示 / Esc: 閉じる</p>
        </div>
        <div className="flex items-center gap-1.5 pointer-events-auto" onClick={(event) => event.stopPropagation()}>
          <button className="viewer-control" onClick={() => setZoom((value) => Math.max(1, value - 0.5))} aria-label="縮小">−</button>
          <span className="min-w-14 text-center text-xs text-white/75 tabular-nums">{Math.round(zoom * 100)}%</span>
          <button className="viewer-control" onClick={() => setZoom((value) => Math.min(8, value + 0.5))} aria-label="拡大">＋</button>
          <button className="viewer-control px-3 text-xs" onClick={reset}>全体</button>
          <button className="viewer-control text-lg" onClick={onClose} aria-label="閉じる">×</button>
        </div>
      </div>

      <div
        className="absolute inset-0 flex items-center justify-center overflow-hidden select-none"
        onClick={(event) => event.stopPropagation()}
        onWheel={handleWheel}
        onMouseDown={beginDrag}
        onMouseMove={moveDrag}
        onMouseUp={() => setDragging(false)}
        onMouseLeave={() => setDragging(false)}
        onDoubleClick={() => (zoom > 1 ? reset() : setZoom(2.5))}
      >
        {(!needsOriginal || !originalLoaded) && (
          <MemoryImage
            filePath={asset.filePath}
            modifiedAtFs={asset.modifiedAtFs}
            sourceWidth={asset.width}
            sourceHeight={asset.height}
            width={viewport.width}
            height={viewport.height}
            fit="contain"
            alt={asset.fileName}
            className="bg-black"
          />
        )}
        {needsOriginal && (
          <img
            src={getLocalImageUrl(asset.filePath)}
            alt={asset.fileName}
            decoding="async"
            draggable={false}
            onLoad={() => setOriginalLoaded(true)}
            onError={() => setOriginalLoaded(false)}
            className={`${originalLoaded ? "block" : "absolute opacity-0 pointer-events-none"} max-w-[calc(100vw-48px)] max-h-[calc(100vh-132px)] object-contain`}
            style={{
              transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
              transformOrigin: "center center",
              cursor: zoom > 1 ? (dragging ? "grabbing" : "grab") : "zoom-in",
              transition: dragging ? "none" : "transform 80ms ease-out",
            }}
          />
        )}
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
          ダブルクリックで拡大 / もう一度ダブルクリックで全体表示
        </div>
      </div>
    </div>,
    document.body
  );
}
