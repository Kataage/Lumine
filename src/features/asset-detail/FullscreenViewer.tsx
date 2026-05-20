import { useEffect, useState, useCallback } from "react";
import { convertFileSrc } from "@tauri-apps/api/core";
import { XIcon, ChevronLeft, ChevronRight, ZoomIn, ZoomOut, Maximize2 } from "lucide-react";
import type { Asset } from "@/entities/types";

interface FullscreenViewerProps {
  assets: Asset[];
  currentIndex: number;
  onClose: () => void;
  onNavigate: (index: number) => void;
}

export function FullscreenViewer({ assets, currentIndex, onClose, onNavigate }: FullscreenViewerProps) {
  const [zoom, setZoom] = useState(1);
  const [isPanning, setIsPanning] = useState(false);
  const [panStart, setPanStart] = useState({ x: 0, y: 0 });
  const [panOffset, setPanOffset] = useState({ x: 0, y: 0 });

  const asset = assets[currentIndex];

  const handlePrev = useCallback(() => {
    if (currentIndex > 0) {
      onNavigate(currentIndex - 1);
      setZoom(1);
      setPanOffset({ x: 0, y: 0 });
    }
  }, [currentIndex, onNavigate]);

  const handleNext = useCallback(() => {
    if (currentIndex < assets.length - 1) {
      onNavigate(currentIndex + 1);
      setZoom(1);
      setPanOffset({ x: 0, y: 0 });
    }
  }, [currentIndex, assets.length, onNavigate]);

  const handleZoomIn = () => setZoom((z) => Math.min(z + 0.25, 5));
  const handleZoomOut = () => setZoom((z) => Math.max(z - 0.25, 0.25));
  const handleFitScreen = () => {
    setZoom(1);
    setPanOffset({ x: 0, y: 0 });
  };

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      switch (e.key) {
        case "ArrowLeft":
          handlePrev();
          break;
        case "ArrowRight":
          handleNext();
          break;
        case "Escape":
          onClose();
          break;
        case "+":
        case "=":
          handleZoomIn();
          break;
        case "-":
          handleZoomOut();
          break;
        case "0":
          handleFitScreen();
          break;
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [handlePrev, handleNext, onClose]);

  const handleMouseDown = (e: React.MouseEvent) => {
    if (zoom > 1) {
      setIsPanning(true);
      setPanStart({ x: e.clientX - panOffset.x, y: e.clientY - panOffset.y });
    }
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (isPanning) {
      setPanOffset({
        x: e.clientX - panStart.x,
        y: e.clientY - panStart.y,
      });
    }
  };

  const handleMouseUp = () => setIsPanning(false);

  const handleWheel = (e: React.WheelEvent) => {
    e.preventDefault();
    const delta = e.deltaY > 0 ? -0.1 : 0.1;
    setZoom((z) => Math.max(0.25, Math.min(5, z + delta)));
  };

  if (!asset) return null;

  const imageUrl = convertFileSrc(asset.file_path);

  return (
    <div
      className="fixed inset-0 z-50 bg-black/95 flex items-center justify-center"
      onClick={onClose}
    >
      <div
        className="absolute inset-0 flex items-center justify-center overflow-hidden"
        onClick={(e) => e.stopPropagation()}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        onWheel={handleWheel}
      >
        <img
          src={imageUrl}
          alt={asset.file_name}
          className="max-w-full max-h-full object-contain select-none"
          style={{
            transform: `scale(${zoom}) translate(${panOffset.x / zoom}px, ${panOffset.y / zoom}px)`,
            transition: isPanning ? "none" : "transform 0.1s ease-out",
            cursor: zoom > 1 ? (isPanning ? "grabbing" : "grab") : "default",
          }}
        />
      </div>

      <div className="absolute top-4 left-4 right-4 flex items-center justify-between">
        <div className="text-white text-sm bg-black/50 px-3 py-1.5 rounded-md backdrop-blur-sm">
          {currentIndex + 1} / {assets.length} - {asset.file_name}
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={handleZoomOut}
            className="p-2 bg-black/50 text-white rounded-md hover:bg-black/70 backdrop-blur-sm"
          >
            <ZoomOut className="w-4 h-4" />
          </button>
          <span className="text-white text-sm bg-black/50 px-2 py-1 rounded-md backdrop-blur-sm min-w-[60px] text-center">
            {Math.round(zoom * 100)}%
          </span>
          <button
            onClick={handleZoomIn}
            className="p-2 bg-black/50 text-white rounded-md hover:bg-black/70 backdrop-blur-sm"
          >
            <ZoomIn className="w-4 h-4" />
          </button>
          <button
            onClick={handleFitScreen}
            className="p-2 bg-black/50 text-white rounded-md hover:bg-black/70 backdrop-blur-sm"
          >
            <Maximize2 className="w-4 h-4" />
          </button>
          <button
            onClick={onClose}
            className="p-2 bg-black/50 text-white rounded-md hover:bg-black/70 backdrop-blur-sm"
          >
            <XIcon className="w-4 h-4" />
          </button>
        </div>
      </div>

      {currentIndex > 0 && (
        <button
          onClick={handlePrev}
          className="absolute left-4 top-1/2 -translate-y-1/2 p-3 bg-black/50 text-white rounded-full hover:bg-black/70 backdrop-blur-sm"
        >
          <ChevronLeft className="w-6 h-6" />
        </button>
      )}

      {currentIndex < assets.length - 1 && (
        <button
          onClick={handleNext}
          className="absolute right-4 top-1/2 -translate-y-1/2 p-3 bg-black/50 text-white rounded-full hover:bg-black/70 backdrop-blur-sm"
        >
          <ChevronRight className="w-6 h-6" />
        </button>
      )}
    </div>
  );
}
