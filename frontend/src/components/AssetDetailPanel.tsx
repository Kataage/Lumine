import { useState, useEffect, useCallback } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getAssetDetail,
  updateAssetNote,
  setAssetTags,
  updateAssetRating,
  updateAssetStatus,
  toggleAssetFavorite,
  updateAssetColorLabel,
  listTags,
  getLocalImageUrl,
  getPostsByAsset,
} from "../api/client";
import { formatFileSize } from "../utils/format";

interface AssetDetailPanelProps {
  assetId: number;
  onClose: () => void;
}

const COLOR_LABELS = [
  { value: "", label: "None", color: "bg-muted" },
  { value: "red", label: "Red", color: "bg-red-500" },
  { value: "orange", label: "Orange", color: "bg-orange-500" },
  { value: "yellow", label: "Yellow", color: "bg-yellow-500" },
  { value: "green", label: "Green", color: "bg-green-500" },
  { value: "blue", label: "Blue", color: "bg-blue-500" },
  { value: "purple", label: "Purple", color: "bg-purple-500" },
];

export function AssetDetailPanel({ assetId, onClose }: AssetDetailPanelProps) {
  const queryClient = useQueryClient();
  const { data: asset, isLoading } = useQuery({
    queryKey: ["assetDetail", assetId],
    queryFn: async () => getAssetDetail(assetId),
    enabled: !!assetId,
  });

  const { data: allTags } = useQuery({
    queryKey: ["tags"],
    queryFn: listTags,
  });

  const { data: posts } = useQuery({
    queryKey: ["assetPosts", assetId],
    queryFn: async () => getPostsByAsset(assetId),
    enabled: !!assetId,
  });

  const [noteContent, setNoteContent] = useState("");
  const [noteDirty, setNoteDirty] = useState(false);

  useEffect(() => {
    if (asset?.noteContent !== undefined) {
      setNoteContent(asset.noteContent ?? "");
      setNoteDirty(false);
    }
  }, [asset?.noteContent]);

  const handleSaveNote = useCallback(async () => {
    if (!asset) return;
    try {
      await updateAssetNote(asset.id, noteContent);
      setNoteDirty(false);
      queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] });
    } catch (err) {
      console.error("Save note failed:", err);
    }
  }, [asset, noteContent, assetId, queryClient]);

  const handleRate = useCallback(
    async (rating: number) => {
      if (!asset) return;
      try {
      await updateAssetRating(asset.id, rating);
      queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] });
      queryClient.invalidateQueries({ queryKey: ["assets"] });
      } catch (err) {
        console.error("Rate failed:", err);
      }
    },
    [asset, assetId, queryClient]
  );

  const handleStatus = useCallback(
    async (status: string) => {
      if (!asset) return;
      try {
      await updateAssetStatus(asset.id, status);
      queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] });
      queryClient.invalidateQueries({ queryKey: ["assets"] });
      } catch (err) {
        console.error("Status update failed:", err);
      }
    },
    [asset, assetId, queryClient]
  );

  const handleFavorite = useCallback(
    async (fav: boolean) => {
      if (!asset) return;
      try {
      await toggleAssetFavorite(asset.id, fav);
      queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] });
      queryClient.invalidateQueries({ queryKey: ["assets"] });
      } catch (err) {
        console.error("Favorite failed:", err);
      }
    },
    [asset, assetId, queryClient]
  );

  const handleColorLabel = useCallback(
    async (label: string) => {
      if (!asset) return;
      try {
      await updateAssetColorLabel(asset.id, label);
      queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] });
      queryClient.invalidateQueries({ queryKey: ["assets"] });
      } catch (err) {
        console.error("Color label failed:", err);
      }
    },
    [asset, assetId, queryClient]
  );

  const handleToggleTag = useCallback(
    async (tagId: number) => {
      if (!asset) return;
      const current = asset.tags?.map((t) => t.id) ?? [];
      const next = current.includes(tagId)
        ? current.filter((id) => id !== tagId)
        : [...current, tagId];
      try {
      await setAssetTags(asset.id, next);
      queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] });
      queryClient.invalidateQueries({ queryKey: ["assets"] });
      } catch (err) {
        console.error("Tag toggle failed:", err);
      }
    },
    [asset, assetId, queryClient]
  );

  if (isLoading || !asset) {
    return (
      <aside className="w-72 border-l border-border bg-card flex flex-col flex-shrink-0">
        <div className="flex items-center justify-center h-full text-muted-foreground text-sm">
          Loading...
        </div>
      </aside>
    );
  }

  const statusOptions = [
    { value: "unsorted", label: "Unsorted" },
    { value: "reviewed", label: "Reviewed" },
    { value: "candidate", label: "Candidate" },
    { value: "published", label: "Published" },
  ];

  return (
    <aside className="w-72 border-l border-border bg-card flex flex-col flex-shrink-0 overflow-hidden">
      <div className="flex items-center justify-between px-3 py-2 border-b border-border flex-shrink-0">
        <span className="text-xs font-medium text-muted-foreground uppercase">Details</span>
        <button onClick={onClose} className="p-1 rounded hover:bg-accent text-muted-foreground hover:text-foreground transition-colors">
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      <div className="flex-1 overflow-auto p-3 space-y-4">
        <div className="rounded-lg overflow-hidden bg-muted aspect-square">
          <img
            src={getLocalImageUrl(asset.filePath)}
            alt={asset.fileName}
            className="w-full h-full object-contain"
          />
        </div>

        <div className="space-y-1.5">
          <h3 className="text-sm font-medium text-foreground truncate">{asset.fileName}</h3>
          <p className="text-xs text-muted-foreground truncate">{asset.filePath}</p>
        </div>

        <div className="grid grid-cols-2 gap-2 text-xs">
          <div>
            <span className="text-muted-foreground">Size</span>
            <p className="text-foreground">{formatFileSize(asset.fileSize)}</p>
          </div>
          <div>
            <span className="text-muted-foreground">Dimensions</span>
            <p className="text-foreground">{asset.width}×{asset.height}</p>
          </div>
          <div>
            <span className="text-muted-foreground">Modified</span>
            <p className="text-foreground">{asset.modifiedAtFs ? new Date(asset.modifiedAtFs).toLocaleDateString() : "-"}</p>
          </div>
          <div>
            <span className="text-muted-foreground">Type</span>
            <p className="text-foreground">{asset.extension.toUpperCase()}</p>
          </div>
        </div>

        <div className="space-y-1.5">
          <span className="text-xs text-muted-foreground">Color Label</span>
          <div className="flex gap-1.5">
            {COLOR_LABELS.map((cl) => (
              <button
                key={cl.value}
                onClick={() => handleColorLabel(cl.value)}
                className={`w-5 h-5 rounded-full transition-transform ${cl.color} ${
                  (asset.colorLabel ?? "") === cl.value
                    ? "ring-2 ring-primary ring-offset-1 ring-offset-card scale-110"
                    : "hover:scale-110"
                }`}
                title={cl.label}
              />
            ))}
          </div>
        </div>

        <div className="space-y-1.5">
          <span className="text-xs text-muted-foreground">Rating</span>
          <div className="flex gap-1">
            {[1, 2, 3, 4, 5].map((r) => (
              <button key={r} onClick={() => handleRate(r)} className="p-0.5 transition-transform hover:scale-110">
                <svg
                  className={`w-5 h-5 ${r <= asset.rating ? "text-yellow-400" : "text-muted-foreground/30"}`}
                  viewBox="0 0 24 24"
                  fill="currentColor"
                >
                  <path d="M10.788 3.21c.448-1.077 1.978-1.077 2.425 0l2.272 5.407a1.125 1.125 0 001.01.747l5.794.494c1.135.097 1.597 1.504.747 2.306l-4.394 3.893a1.125 1.125 0 00-.34 1.058l1.347 5.627c.264 1.1-.893 2.006-1.89 1.437l-5.088-2.863a1.125 1.125 0 00-1.08 0L6.68 20.394c-.997.57-2.154-.337-1.89-1.437l1.347-5.627a1.125 1.125 0 00-.34-1.058L1.403 8.374c-.85-.802-.388-2.21.747-2.306l5.794-.494a1.125 1.125 0 001.01-.747l2.272-5.407z" />
                </svg>
              </button>
            ))}
          </div>
        </div>

        <div className="space-y-1.5">
          <span className="text-xs text-muted-foreground">Status</span>
          <div className="flex flex-wrap gap-1.5">
            {statusOptions.map((opt) => (
              <button
                key={opt.value}
                onClick={() => handleStatus(opt.value)}
                className={`text-xs px-2 py-0.5 rounded-full border transition-colors ${
                  asset.statusLabel === opt.value
                    ? "bg-primary text-primary-foreground border-primary"
                    : "bg-muted text-muted-foreground border-border hover:bg-accent"
                }`}
              >
                {opt.label}
              </button>
            ))}
          </div>
        </div>

        <div className="space-y-1.5">
          <div className="flex items-center justify-between">
            <span className="text-xs text-muted-foreground">Favorite</span>
            <button onClick={() => handleFavorite(!asset.isFavorite)} className="p-1">
              <svg
                className={`w-5 h-5 transition-colors ${asset.isFavorite ? "text-yellow-400" : "text-muted-foreground/30 hover:text-muted-foreground"}`}
                viewBox="0 0 24 24"
                fill="currentColor"
              >
                <path d="M11.645 20.91l-.007-.003-.022-.012a15.247 15.247 0 01-.383-.218 25.18 25.18 0 01-4.244-3.17C4.688 15.36 2.25 12.174 2.25 8.25 2.25 5.322 4.714 3 7.688 3A5.5 5.5 0 0112 5.052 5.5 5.5 0 0116.313 3c2.973 0 5.437 2.322 5.437 5.25 0 3.925-2.438 7.111-4.739 9.256a25.175 25.175 0 01-4.244 3.17 15.247 15.247 0 01-.383.219l-.022.012-.007.004-.003.001a.752.752 0 01-.704 0l-.003-.001z" />
              </svg>
            </button>
          </div>
        </div>

        <div className="space-y-1.5">
          <span className="text-xs text-muted-foreground">Tags</span>
          <div className="flex flex-wrap gap-1.5">
            {allTags?.map((tag) => {
              const active = asset.tags?.some((t) => t.id === tag.id) ?? false;
              return (
                <button
                  key={tag.id}
                  onClick={() => handleToggleTag(tag.id)}
                  className={`text-xs px-2 py-0.5 rounded-full border transition-colors ${
                    active
                      ? "bg-primary/20 text-primary border-primary/30"
                      : "bg-muted text-muted-foreground border-border hover:bg-accent"
                  }`}
                >
                  <span className="inline-block w-2 h-2 rounded-full mr-1" style={{ backgroundColor: tag.color }} />
                  {tag.name}
                </button>
              );
            })}
            {(!allTags || allTags.length === 0) && (
              <span className="text-xs text-muted-foreground/60">No tags created yet</span>
            )}
          </div>
        </div>

        <div className="space-y-1.5">
          <div className="flex items-center justify-between">
            <span className="text-xs text-muted-foreground">Memo</span>
            {noteDirty && (
              <button
                onClick={handleSaveNote}
                className="text-xs px-2 py-0.5 bg-primary text-primary-foreground rounded hover:bg-primary/90 transition-colors"
              >
                Save
              </button>
            )}
          </div>
          <textarea
            value={noteContent}
            onChange={(e) => { setNoteContent(e.target.value); setNoteDirty(true); }}
            onBlur={handleSaveNote}
            placeholder="Add a memo..."
            className="w-full text-xs p-2 bg-muted rounded border border-border focus:border-primary focus:outline-none resize-none h-20 placeholder:text-muted-foreground/50"
          />
        </div>

        {posts && posts.length > 0 && (
          <div className="space-y-1.5">
            <span className="text-xs text-muted-foreground">Posts</span>
            <div className="space-y-1">
              {posts.map((post) => (
                <div key={post.id} className="text-xs px-2 py-1 bg-muted rounded border border-border">
                  <span className="font-medium text-foreground">{post.title}</span>
                  <span className="ml-1.5 text-muted-foreground">{post.status}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </aside>
  );
}
