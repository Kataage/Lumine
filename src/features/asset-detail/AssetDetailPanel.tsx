import { useState, useEffect } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { updateAssetNote, setAssetRating, setAssetStatus, setAssetFavorite } from "@/shared/api/client";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { XIcon, StarIcon, ImageIcon } from "lucide-react";
import { convertFileSrc } from "@tauri-apps/api/core";
import { useToast } from "@/components/ui/toast";

export function AssetDetailPanel() {
  const selectedAsset = useAppStore((s) => s.selectedAsset);
  const setSelectedAsset = useAppStore((s) => s.setSelectedAsset);
  const setDetailPanelOpen = useAppStore((s) => s.setDetailPanelOpen);
  const queryClient = useQueryClient();
  const { addToast } = useToast();

  const [noteContent, setNoteContent] = useState("");
  const [imageError, setImageError] = useState(false);

  useEffect(() => {
    if (selectedAsset) {
      setNoteContent("");
      setImageError(false);
    }
  }, [selectedAsset?.id]);

  const noteMutation = useMutation({
    mutationFn: ({ assetId, content }: { assetId: number; content: string }) =>
      updateAssetNote(assetId, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      addToast(`Failed to save note: ${error.message}`, "error");
    },
  });

  const ratingMutation = useMutation({
    mutationFn: ({ assetId, rating }: { assetId: number; rating: number }) =>
      setAssetRating(assetId, rating),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      addToast(`Failed to update rating: ${error.message}`, "error");
    },
  });

  const statusMutation = useMutation({
    mutationFn: ({ assetId, status }: { assetId: number; status: string }) =>
      setAssetStatus(assetId, status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      addToast(`Failed to update status: ${error.message}`, "error");
    },
  });

  const favoriteMutation = useMutation({
    mutationFn: ({ assetId, isFavorite }: { assetId: number; isFavorite: boolean }) =>
      setAssetFavorite(assetId, isFavorite),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
    onError: (error) => {
      addToast(`Failed to update favorite: ${error.message}`, "error");
    },
  });

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

  return (
    <aside className="w-80 border-l border-border bg-card flex flex-col">
      <div className="flex items-center justify-between p-4 border-b border-border">
        <h2 className="font-semibold truncate">{selectedAsset.file_name}</h2>
        <Button variant="ghost" size="icon" onClick={handleClose}>
          <XIcon className="w-4 h-4" />
        </Button>
      </div>

      <div className="flex-1 overflow-auto p-4 space-y-4">
        <div>
          {imageError ? (
            <div className="w-full aspect-square bg-muted rounded-md flex items-center justify-center">
              <ImageIcon className="w-12 h-12 text-muted-foreground" />
            </div>
          ) : (
            <img
              src={convertFileSrc(selectedAsset.file_path)}
              alt={selectedAsset.file_name}
              className="w-full rounded-md bg-muted"
              onError={() => setImageError(true)}
            />
          )}
        </div>

        <div className="space-y-1 text-sm">
          <p>
            <span className="text-muted-foreground">Path:</span>{" "}
            <span className="truncate">{selectedAsset.file_path}</span>
          </p>
          <p>
            <span className="text-muted-foreground">Size:</span>{" "}
            {(selectedAsset.file_size / 1024).toFixed(1)} KB
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
  );
}