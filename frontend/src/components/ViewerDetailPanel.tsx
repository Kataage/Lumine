import { useCallback, useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getAssetDetail,
  getPostsByAsset,
  listTags,
  setAssetTags,
  toggleAssetFavorite,
  updateAssetColorLabel,
  updateAssetNote,
  updateAssetRating,
  updateAssetStatus,
} from "../api/client";
import { formatFileSize } from "../utils/format";
import { MemoryImage } from "./MemoryImage";

interface ViewerDetailPanelProps {
  assetId: number;
  onClose: () => void;
}

const COLOR_LABELS = [
  { value: "", label: "None", color: "transparent" },
  { value: "red", label: "Red", color: "#ef4444" },
  { value: "orange", label: "Orange", color: "#f97316" },
  { value: "yellow", label: "Yellow", color: "#eab308" },
  { value: "green", label: "Green", color: "#22c55e" },
  { value: "blue", label: "Blue", color: "#3b82f6" },
  { value: "purple", label: "Purple", color: "#a855f7" },
];

const STATUS_OPTIONS = [
  { value: "unsorted", label: "Unsorted" },
  { value: "reviewed", label: "Reviewed" },
  { value: "candidate", label: "Candidate" },
  { value: "published", label: "Published" },
];

export function ViewerDetailPanel({ assetId, onClose }: ViewerDetailPanelProps) {
  const queryClient = useQueryClient();
  const { data: asset, isLoading, isError, error } = useQuery({
    queryKey: ["assetDetail", assetId],
    queryFn: () => getAssetDetail(assetId),
    enabled: assetId > 0,
    staleTime: Infinity,
  });
  const { data: allTags } = useQuery({ queryKey: ["tags"], queryFn: listTags, staleTime: Infinity });
  const { data: posts } = useQuery({
    queryKey: ["assetPosts", assetId],
    queryFn: () => getPostsByAsset(assetId),
    enabled: assetId > 0,
    staleTime: Infinity,
  });
  const [noteContent, setNoteContent] = useState("");
  const [noteDirty, setNoteDirty] = useState(false);

  useEffect(() => {
    if (asset?.noteContent !== undefined) {
      setNoteContent(asset.noteContent ?? "");
      setNoteDirty(false);
    }
  }, [asset?.noteContent, asset?.id]);

  const refreshDetail = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] });
  }, [assetId, queryClient]);

  const refreshDetailAndGrid = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] }),
      queryClient.invalidateQueries({ queryKey: ["assets"] }),
    ]);
  }, [assetId, queryClient]);

  const saveNote = useCallback(async () => {
    if (!asset || !noteDirty) return;
    try {
      await updateAssetNote(asset.id, noteContent);
      setNoteDirty(false);
      await refreshDetail();
    } catch (saveError) {
      console.error("Save note failed", saveError);
    }
  }, [asset, noteContent, noteDirty, refreshDetail]);

  if (isLoading) {
    return (
      <aside className="w-72 border-l border-border bg-card flex items-center justify-center flex-shrink-0">
        <div className="w-5 h-5 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
      </aside>
    );
  }

  if (isError || !asset) {
    return (
      <aside className="w-72 border-l border-border bg-card flex flex-col items-center justify-center gap-3 p-4 flex-shrink-0">
        <p className="text-xs text-destructive text-center">Failed to open image details.</p>
        {error && <p className="text-[10px] text-muted-foreground break-all">{String(error)}</p>}
        <button onClick={onClose} className="text-xs px-3 py-1.5 bg-muted rounded">Close</button>
      </aside>
    );
  }

  return (
    <aside className="w-72 border-l border-border bg-card flex flex-col flex-shrink-0 overflow-hidden">
      <div className="flex items-center justify-between px-3 py-2 border-b border-border flex-shrink-0">
        <span className="text-xs font-medium text-muted-foreground uppercase">Details</span>
        <button onClick={onClose} className="p-1 rounded hover:bg-accent text-muted-foreground hover:text-foreground">×</button>
      </div>

      <div className="flex-1 overflow-auto p-3 space-y-4">
        <MemoryImage
          filePath={asset.filePath}
          modifiedAtFs={asset.modifiedAtFs}
          sourceWidth={asset.width}
          sourceHeight={asset.height}
          width={264}
          height={264}
          fit="contain"
          alt={asset.fileName}
          className="rounded-lg"
        />

        <div className="space-y-1.5">
          <h3 className="text-sm font-medium text-foreground break-words">{asset.fileName}</h3>
          <p className="text-[10px] text-muted-foreground break-all">{asset.filePath}</p>
        </div>

        <div className="grid grid-cols-2 gap-2 text-xs">
          <Info label="Size" value={formatFileSize(asset.fileSize)} />
          {asset.width > 0 && asset.height > 0 && <Info label="Dimensions" value={`${asset.width} × ${asset.height}`} />}
          <Info label="Type" value={asset.extension.toUpperCase()} />
          <Info
            label="Modified"
            value={asset.modifiedAtFs ? new Date(asset.modifiedAtFs).toLocaleDateString() : "-"}
          />
        </div>

        {(asset.cameraModel || asset.lensModel || asset.focalLength || asset.aperture || asset.shutterSpeed || asset.iso || asset.exifDate || asset.gpsLatitude) && (
          <section className="border-t border-border pt-3 space-y-1 text-xs">
            <h4 className="text-[10px] uppercase tracking-wide text-muted-foreground mb-2">EXIF</h4>
            {asset.cameraModel && <MetaRow label="Camera" value={asset.cameraModel} />}
            {asset.lensModel && <MetaRow label="Lens" value={asset.lensModel} />}
            {asset.focalLength && <MetaRow label="Focal" value={asset.focalLength} />}
            {asset.aperture && <MetaRow label="Aperture" value={asset.aperture} />}
            {asset.shutterSpeed && <MetaRow label="Shutter" value={asset.shutterSpeed} />}
            {asset.iso ? <MetaRow label="ISO" value={String(asset.iso)} /> : null}
            {asset.exifDate && <MetaRow label="Date" value={asset.exifDate} />}
            {asset.gpsLatitude && <MetaRow label="GPS" value={`${asset.gpsLatitude}, ${asset.gpsLongitude}`} />}
          </section>
        )}

        <section className="space-y-1.5">
          <span className="text-xs text-muted-foreground">Color Label</span>
          <div className="flex gap-1.5">
            {COLOR_LABELS.map((item) => (
              <button
                key={item.value || "none"}
                onClick={async () => {
                  await updateAssetColorLabel(asset.id, item.value);
                  await refreshDetailAndGrid();
                }}
                className={`w-5 h-5 rounded-full border transition-transform ${
                  (asset.colorLabel ?? "") === item.value ? "ring-2 ring-primary scale-110" : "border-border hover:scale-110"
                }`}
                style={{ backgroundColor: item.color }}
                title={item.label}
              />
            ))}
          </div>
        </section>

        <section className="space-y-1.5">
          <span className="text-xs text-muted-foreground">Rating</span>
          <div className="flex gap-1">
            {[1, 2, 3, 4, 5].map((rating) => (
              <button
                key={rating}
                onClick={async () => {
                  await updateAssetRating(asset.id, rating);
                  await refreshDetailAndGrid();
                }}
                className={`text-lg leading-none ${rating <= asset.rating ? "text-yellow-400" : "text-muted-foreground/30"}`}
              >
                ★
              </button>
            ))}
          </div>
        </section>

        <section className="space-y-1.5">
          <span className="text-xs text-muted-foreground">Status</span>
          <div className="flex flex-wrap gap-1.5">
            {STATUS_OPTIONS.map((option) => (
              <button
                key={option.value}
                onClick={async () => {
                  await updateAssetStatus(asset.id, option.value);
                  await refreshDetailAndGrid();
                }}
                className={`text-xs px-2 py-0.5 rounded-full border ${
                  asset.statusLabel === option.value
                    ? "bg-primary text-primary-foreground border-primary"
                    : "bg-muted text-muted-foreground border-border hover:bg-accent"
                }`}
              >
                {option.label}
              </button>
            ))}
          </div>
        </section>

        <section className="flex items-center justify-between">
          <span className="text-xs text-muted-foreground">Favorite</span>
          <button
            onClick={async () => {
              await toggleAssetFavorite(asset.id, !asset.isFavorite);
              await refreshDetailAndGrid();
            }}
            className={`text-xl ${asset.isFavorite ? "text-yellow-400" : "text-muted-foreground/30"}`}
          >
            ★
          </button>
        </section>

        <section className="space-y-1.5">
          <span className="text-xs text-muted-foreground">Tags</span>
          <div className="flex flex-wrap gap-1.5">
            {allTags?.map((tag) => {
              const active = asset.tags?.some((candidate) => candidate.id === tag.id) ?? false;
              return (
                <button
                  key={tag.id}
                  onClick={async () => {
                    const current = asset.tags?.map((candidate) => candidate.id) ?? [];
                    const next = active
                      ? current.filter((id) => id !== tag.id)
                      : [...current, tag.id];
                    await setAssetTags(asset.id, next);
                    await refreshDetailAndGrid();
                  }}
                  className={`text-xs px-2 py-0.5 rounded-full border ${
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
          </div>
        </section>

        <section className="space-y-1.5">
          <div className="flex items-center justify-between">
            <span className="text-xs text-muted-foreground">Memo</span>
            {noteDirty && (
              <button onClick={() => void saveNote()} className="text-xs px-2 py-0.5 bg-primary text-primary-foreground rounded">
                Save
              </button>
            )}
          </div>
          <textarea
            value={noteContent}
            onChange={(event) => {
              setNoteContent(event.target.value);
              setNoteDirty(true);
            }}
            onBlur={() => void saveNote()}
            placeholder="Add a memo..."
            className="w-full text-xs p-2 bg-muted rounded border border-border focus:border-primary focus:outline-none resize-none h-20"
          />
        </section>

        {posts && posts.length > 0 && (
          <section className="space-y-1.5">
            <span className="text-xs text-muted-foreground">Posts</span>
            <div className="space-y-1">
              {posts.map((post) => (
                <div key={post.id} className="text-xs px-2 py-1 bg-muted rounded border border-border">
                  <span className="font-medium text-foreground">{post.title}</span>
                  <span className="ml-1.5 text-muted-foreground">{post.status}</span>
                </div>
              ))}
            </div>
          </section>
        )}
      </div>
    </aside>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span className="text-muted-foreground">{label}</span>
      <p className="text-foreground break-words">{value}</p>
    </div>
  );
}

function MetaRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-2">
      <span className="text-muted-foreground flex-shrink-0">{label}</span>
      <span className="text-foreground text-right break-all">{value}</span>
    </div>
  );
}
