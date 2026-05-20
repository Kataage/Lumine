import { useState, useEffect, useCallback } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { getAssetNote, updateAssetNote, setAssetRating, setAssetStatus, setAssetFavorite, setAssetColorLabel } from "@/shared/api/client";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { XIcon, StarIcon, ImageIcon, Maximize2, ChevronLeft, ChevronRight } from "lucide-react";
import { convertFileSrc } from "@tauri-apps/api/core";
import { useToast } from "@/components/ui/toast";
import { FullscreenViewer } from "./FullscreenViewer";
import { TagInput } from "@/features/tags/TagInput";

export function AssetDetailPanel() {
  const selectedAsset = useAppStore((s) => s.selectedAsset);
  const setSelectedAsset = useAppStore((s) => s.setSelectedAsset);
  const setDetailPanelOpen = useAppStore((s) => s.setDetailPanelOpen);
  const queryClient = useQueryClient();
  const { addToast } = useToast();

  const [noteContent, setNoteContent] = useState("");
  const [imageError, setImageError] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);

  useEffect(() => {
    if (selectedAsset) {
      setImageError(false);
      getAssetNote(selectedAsset.id).then((note) => {
        setNoteContent(note ?? "");
      }).catch(() => {
        setNoteContent("");
      });
    }
  }, [selectedAsset?.id]);

  const noteMutation = useMutation({
    mutationFn: ({ assetId, content }: { assetId: number; content: string }) =>
      updateAssetNote(assetId, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      const msg = error instanceof Error ? error.message : String(error);
      addToast(`Failed to save note: ${msg}`, "error");
    },
  });

  const ratingMutation = useMutation({
    mutationFn: ({ assetId, rating }: { assetId: number; rating: number }) =>
      setAssetRating(assetId, rating),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      const msg = error instanceof Error ? error.message : String(error);
      addToast(`Failed to update rating: ${msg}`, "error");
    },
  });

  const statusMutation = useMutation({
    mutationFn: ({ assetId, status }: { assetId: number; status: string }) =>
      setAssetStatus(assetId, status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      const msg = error instanceof Error ? error.message : String(error);
      addToast(`Failed to update status: ${msg}`, "error");
    },
  });

  const favoriteMutation = useMutation({
    mutationFn: ({ assetId, isFavorite }: { assetId: number; isFavorite: boolean }) =>
      setAssetFavorite(assetId, isFavorite),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      const msg = error instanceof Error ? error.message : String(error);
      addToast(`Failed to update favorite: ${msg}`, "error");
    },
  });

  const colorLabelMutation = useMutation({
    mutationFn: ({ assetId, colorLabel }: { assetId: number; colorLabel: string | null }) =>
      setAssetColorLabel(assetId, colorLabel),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      const msg = error instanceof Error ? error.message : String(error);
      addToast(`Failed to update color label: ${msg}`, "error");
    },
  });

  const handlePrevAsset = useCallback(() => {
    const assets = queryClient.getQueryData<any[]>(["folder-assets", useAppStore.getState().selectedLibraryId]) ?? [];
    if (!selectedAsset || assets.length === 0) return;
    const currentIndex = assets.findIndex((a) => a.id === selectedAsset.id);
    if (currentIndex > 0) {
      const prevAsset = assets[currentIndex - 1];
      setSelectedAsset({
        id: prevAsset.id,
        library_id: 0,
        folder_path: prevAsset.folder_path,
        file_name: prevAsset.file_name,
        file_path: prevAsset.file_path,
        extension: prevAsset.extension,
        file_size: prevAsset.file_size,
        created_at_fs: null,
        modified_at_fs: prevAsset.modified_at,
        width: null,
        height: null,
        mime_type: null,
        hash_blake3: null,
        thumb_status: prevAsset.thumb_status as "none" | "queued" | "ready" | "failed",
        thumb_path: prevAsset.thumb_path,
        rating: 0,
        status_label: "unorganized",
        is_favorite: false,
        color_label: null,
        indexed_at: "",
        updated_at: "",
      });
    }
  }, [selectedAsset, queryClient, setSelectedAsset]);

  const handleNextAsset = useCallback(() => {
    const assets = queryClient.getQueryData<any[]>(["folder-assets", useAppStore.getState().selectedLibraryId]) ?? [];
    if (!selectedAsset || assets.length === 0) return;
    const currentIndex = assets.findIndex((a) => a.id === selectedAsset.id);
    if (currentIndex < assets.length - 1) {
      const nextAsset = assets[currentIndex + 1];
      setSelectedAsset({
        id: nextAsset.id,
        library_id: 0,
        folder_path: nextAsset.folder_path,
        file_name: nextAsset.file_name,
        file_path: nextAsset.file_path,
        extension: nextAsset.extension,
        file_size: nextAsset.file_size,
        created_at_fs: null,
        modified_at_fs: nextAsset.modified_at,
        width: null,
        height: null,
        mime_type: null,
        hash_blake3: null,
        thumb_status: nextAsset.thumb_status as "none" | "queued" | "ready" | "failed",
        thumb_path: nextAsset.thumb_path,
        rating: 0,
        status_label: "unorganized",
        is_favorite: false,
        color_label: null,
        indexed_at: "",
        updated_at: "",
      });
    }
  }, [selectedAsset, queryClient, setSelectedAsset]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (!selectedAsset) return;
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;

      switch (e.key) {
        case "ArrowLeft":
          e.preventDefault();
          handlePrevAsset();
          break;
        case "ArrowRight":
          e.preventDefault();
          handleNextAsset();
          break;
        case "Enter":
          e.preventDefault();
          setIsFullscreen(true);
          break;
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [selectedAsset, handlePrevAsset, handleNextAsset]);

  if (!selectedAsset) {
    return null;
  }

  const handleClose = () => {
    setSelectedAsset(null);
    setDetailPanelOpen(false);
  };

  const handleSaveNote = () => {
    noteMutation.mutate({ assetId: selectedAsset.id, content: noteContent });
  };

  const imageUrl = selectedAsset.thumb_status === "ready" && selectedAsset.thumb_path
    ? convertFileSrc(selectedAsset.thumb_path)
    : convertFileSrc(selectedAsset.file_path);

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  return (
    <>
      <aside className="w-80 border-l border-border bg-card flex flex-col">
        <div className="flex items-center justify-between p-4 border-b border-border">
          <h2 className="font-semibold truncate">{selectedAsset.file_name}</h2>
          <div className="flex items-center gap-1">
            <Button
              variant="ghost"
              size="icon"
              onClick={() => setIsFullscreen(true)}
              title="Fullscreen (Enter)"
            >
              <Maximize2 className="w-4 h-4" />
            </Button>
            <Button variant="ghost" size="icon" onClick={handleClose}>
              <XIcon className="w-4 h-4" />
            </Button>
          </div>
        </div>

        <div className="flex-1 overflow-auto p-4 space-y-4">
          <div className="relative group">
            {imageError ? (
              <div className="w-full aspect-square bg-muted rounded-md flex items-center justify-center">
                <ImageIcon className="w-12 h-12 text-muted-foreground" />
              </div>
            ) : (
              <div className="relative">
                <img
                  src={imageUrl}
                  alt={selectedAsset.file_name}
                  className="w-full rounded-md bg-muted cursor-pointer"
                  onClick={() => setIsFullscreen(true)}
                  onError={() => setImageError(true)}
                />
                <div className="absolute inset-0 bg-black/0 group-hover:bg-black/20 transition-colors rounded-md flex items-center justify-center">
                  <Maximize2 className="w-6 h-6 text-white opacity-0 group-hover:opacity-100 transition-opacity" />
                </div>
              </div>
            )}
            <div className="flex items-center justify-between mt-2">
              <Button
                variant="outline"
                size="sm"
                onClick={handlePrevAsset}
                className="flex-1 mr-1"
              >
                <ChevronLeft className="w-4 h-4 mr-1" />
                Prev
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={handleNextAsset}
                className="flex-1 ml-1"
              >
                Next
                <ChevronRight className="w-4 h-4 ml-1" />
              </Button>
            </div>
          </div>

          <div className="space-y-1 text-sm">
            <p>
              <span className="text-muted-foreground">Path:</span>{" "}
              <span className="truncate block">{selectedAsset.file_path}</span>
            </p>
            <p>
              <span className="text-muted-foreground">Size:</span>{" "}
              {formatFileSize(selectedAsset.file_size)}
            </p>
            {selectedAsset.width && selectedAsset.height && (
              <p>
                <span className="text-muted-foreground">Dimensions:</span>{" "}
                {selectedAsset.width} x {selectedAsset.height}
              </p>
            )}
            <p>
              <span className="text-muted-foreground">Modified:</span>{" "}
              {selectedAsset.modified_at_fs || "Unknown"}
            </p>
          </div>

          <div>
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-medium">Rating</span>
              <div className="flex gap-1">
                {[1, 2, 3, 4, 5].map((star) => (
                  <button
                    key={star}
                    onClick={() =>
                      ratingMutation.mutate({
                        assetId: selectedAsset.id,
                        rating: star,
                      })
                    }
                    className="p-1"
                  >
                    <StarIcon
                      className={`w-4 h-4 ${
                        star <= selectedAsset.rating
                          ? "text-yellow-400 fill-yellow-400"
                          : "text-muted-foreground"
                      }`}
                    />
                  </button>
                ))}
              </div>
            </div>
          </div>

          <div>
            <span className="text-sm font-medium">Status</span>
            <div className="flex flex-wrap gap-1 mt-1">
              {["unorganized", "selected", "posting_candidate", "posted"].map(
                (status) => (
                  <button
                    key={status}
                    onClick={() =>
                      statusMutation.mutate({
                        assetId: selectedAsset.id,
                        status,
                      })
                    }
                    className={`px-2 py-1 text-xs rounded ${
                      selectedAsset.status_label === status
                        ? "bg-primary text-primary-foreground"
                        : "bg-secondary hover:bg-secondary/80"
                    }`}
                  >
                    {status.replace("_", " ")}
                  </button>
                )
              )}
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-medium">Favorite</span>
              <Button
                variant="ghost"
                size="sm"
                onClick={() =>
                  favoriteMutation.mutate({
                    assetId: selectedAsset.id,
                    isFavorite: !selectedAsset.is_favorite,
                  })
                }
              >
                <StarIcon
                  className={`w-4 h-4 mr-1 ${
                    selectedAsset.is_favorite
                      ? "text-yellow-400 fill-yellow-400"
                      : ""
                  }`}
                />
                {selectedAsset.is_favorite ? "Unfavorite" : "Favorite"}
              </Button>
            </div>
          </div>

          <div>
            <span className="text-sm font-medium">Tags</span>
            <div className="mt-1">
              <TagInput assetId={selectedAsset.id} />
            </div>
          </div>

          <div>
            <span className="text-sm font-medium">Color Label</span>
            <div className="flex flex-wrap gap-1.5 mt-1">
              <button
                onClick={() =>
                  colorLabelMutation.mutate({
                    assetId: selectedAsset.id,
                    colorLabel: null,
                  })
                }
                className={`w-6 h-6 rounded-full border-2 ${
                  !selectedAsset.color_label
                    ? "border-primary"
                    : "border-border"
                }`}
                title="Clear"
              >
                <XIcon className="w-3 h-3 mx-auto text-muted-foreground" />
              </button>
              {["#ef4444", "#f97316", "#eab308", "#22c55e", "#3b82f6", "#8b5cf6", "#ec4899"].map(
                (color) => (
                  <button
                    key={color}
                    onClick={() =>
                      colorLabelMutation.mutate({
                        assetId: selectedAsset.id,
                        colorLabel: color,
                      })
                    }
                    className={`w-6 h-6 rounded-full border-2 ${
                      selectedAsset.color_label === color
                        ? "border-primary"
                        : "border-transparent"
                    }`}
                    style={{ backgroundColor: color }}
                    title={color}
                  />
                )
              )}
            </div>
          </div>

          <div>
            <span className="text-sm font-medium">Note</span>
            <Textarea
              value={noteContent}
              onChange={(e) => setNoteContent(e.target.value)}
              placeholder="Add a note..."
              className="mt-1 min-h-[100px]"
            />
            <Button
              size="sm"
              className="mt-2"
              onClick={handleSaveNote}
              disabled={noteMutation.isPending}
            >
              Save Note
            </Button>
          </div>
        </div>
      </aside>

      {isFullscreen && (
        <FullscreenViewer
          assets={[selectedAsset]}
          currentIndex={0}
          onClose={() => setIsFullscreen(false)}
          onNavigate={() => {}}
        />
      )}
    </>
  );
}
