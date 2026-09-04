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
  { value: "", label: "なし", color: "transparent" },
  { value: "red", label: "赤", color: "#ef4444" },
  { value: "orange", label: "オレンジ", color: "#f97316" },
  { value: "yellow", label: "黄", color: "#eab308" },
  { value: "green", label: "緑", color: "#22c55e" },
  { value: "blue", label: "青", color: "#3b82f6" },
  { value: "purple", label: "紫", color: "#a855f7" },
];

const STATUS_OPTIONS = [
  { value: "unsorted", label: "未整理" },
  { value: "reviewed", label: "確認済み" },
  { value: "candidate", label: "候補" },
  { value: "published", label: "公開済み" },
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
  const [pathCopied, setPathCopied] = useState(false);

  useEffect(() => {
    if (asset?.noteContent !== undefined) {
      setNoteContent(asset.noteContent ?? "");
      setNoteDirty(false);
    }
  }, [asset?.noteContent, asset?.id]);

  useEffect(() => {
    setPathCopied(false);
  }, [assetId]);

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

  const copyPath = async () => {
    if (!asset) return;
    try {
      await navigator.clipboard.writeText(asset.filePath);
      setPathCopied(true);
      window.setTimeout(() => setPathCopied(false), 1500);
    } catch (copyError) {
      console.error("Copy path failed", copyError);
    }
  };

  if (isLoading) {
    return (
      <aside className="w-80 border-l border-border bg-card flex items-center justify-center flex-shrink-0">
        <div className="flex flex-col items-center gap-2 text-xs text-muted-foreground">
          <div className="w-5 h-5 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
          <span>詳細情報を読み込み中…</span>
        </div>
      </aside>
    );
  }

  if (isError || !asset) {
    return (
      <aside className="w-80 border-l border-border bg-card flex flex-col items-center justify-center gap-3 p-5 flex-shrink-0">
        <p className="text-xs text-destructive text-center font-medium">画像の詳細情報を開けませんでした</p>
        {error && <p className="text-[10px] text-muted-foreground break-all text-center">{String(error)}</p>}
        <button onClick={onClose} className="text-xs px-3 py-1.5 bg-muted rounded-lg hover:bg-accent">閉じる</button>
      </aside>
    );
  }

  return (
    <aside className="w-80 border-l border-border bg-card flex flex-col flex-shrink-0 overflow-hidden shadow-[-8px_0_24px_rgba(0,0,0,0.08)]">
      <div className="flex items-center justify-between px-3.5 py-2.5 border-b border-border flex-shrink-0">
        <div>
          <span className="text-xs font-semibold">画像の詳細</span>
          <p className="text-[9px] text-muted-foreground mt-0.5">選択した画像の情報・整理</p>
        </div>
        <button onClick={onClose} className="w-7 h-7 rounded-lg hover:bg-accent text-muted-foreground hover:text-foreground" title="詳細パネルを閉じる">×</button>
      </div>

      <div className="flex-1 overflow-auto p-3 space-y-4">
        <div className="flex justify-center rounded-xl bg-muted/30 border border-border overflow-hidden">
          <MemoryImage
            filePath={asset.filePath}
            modifiedAtFs={asset.modifiedAtFs}
            sourceWidth={asset.width}
            sourceHeight={asset.height}
            width={294}
            height={260}
            fit="contain"
            alt={asset.fileName}
          />
        </div>

        <section className="space-y-2">
          <h3 className="text-sm font-semibold text-foreground break-words leading-snug">{asset.fileName}</h3>
          <div className="flex items-start gap-2">
            <p className="text-[9px] text-muted-foreground break-all flex-1 leading-relaxed">{asset.filePath}</p>
            <button
              onClick={() => void copyPath()}
              className="text-[9px] px-2 py-1 rounded-md bg-muted hover:bg-accent text-muted-foreground hover:text-foreground whitespace-nowrap"
            >
              {pathCopied ? "コピー済み" : "パスをコピー"}
            </button>
          </div>
        </section>

        <section className="rounded-xl border border-border bg-muted/20 p-3">
          <div className="grid grid-cols-2 gap-x-3 gap-y-3 text-xs">
            <Info label="ファイルサイズ" value={formatFileSize(asset.fileSize)} />
            {asset.width > 0 && asset.height > 0 && <Info label="解像度" value={`${asset.width} × ${asset.height}`} />}
            <Info label="形式" value={asset.extension.toUpperCase()} />
            <Info
              label="更新日"
              value={asset.modifiedAtFs ? new Date(asset.modifiedAtFs).toLocaleDateString("ja-JP") : "-"}
            />
          </div>
        </section>

        {(asset.cameraModel || asset.lensModel || asset.focalLength || asset.aperture || asset.shutterSpeed || asset.iso || asset.exifDate || asset.gpsLatitude) && (
          <DetailSection title="撮影情報 (EXIF)">
            <div className="space-y-1.5 text-xs">
              {asset.cameraModel && <MetaRow label="カメラ" value={asset.cameraModel} />}
              {asset.lensModel && <MetaRow label="レンズ" value={asset.lensModel} />}
              {asset.focalLength && <MetaRow label="焦点距離" value={asset.focalLength} />}
              {asset.aperture && <MetaRow label="絞り" value={asset.aperture} />}
              {asset.shutterSpeed && <MetaRow label="シャッター" value={asset.shutterSpeed} />}
              {asset.iso ? <MetaRow label="ISO" value={String(asset.iso)} /> : null}
              {asset.exifDate && <MetaRow label="撮影日時" value={asset.exifDate} />}
              {asset.gpsLatitude && <MetaRow label="GPS" value={`${asset.gpsLatitude}, ${asset.gpsLongitude}`} />}
            </div>
          </DetailSection>
        )}

        <DetailSection title="整理">
          <div className="space-y-3">
            <div>
              <p className="text-[10px] text-muted-foreground mb-1.5">評価</p>
              <div className="flex gap-0.5">
                {[1, 2, 3, 4, 5].map((rating) => (
                  <button
                    key={rating}
                    onClick={async () => {
                      await updateAssetRating(asset.id, rating);
                      await refreshDetailAndGrid();
                    }}
                    className={`w-7 h-7 rounded-md text-lg leading-none hover:bg-accent ${rating <= asset.rating ? "text-yellow-400" : "text-muted-foreground/25"}`}
                    title={`評価 ${rating}`}
                  >
                    ★
                  </button>
                ))}
              </div>
            </div>

            <div>
              <p className="text-[10px] text-muted-foreground mb-1.5">状態</p>
              <div className="flex flex-wrap gap-1.5">
                {STATUS_OPTIONS.map((option) => (
                  <button
                    key={option.value}
                    onClick={async () => {
                      await updateAssetStatus(asset.id, option.value);
                      await refreshDetailAndGrid();
                    }}
                    className={`text-[10px] px-2.5 py-1.5 rounded-lg border transition-colors ${
                      asset.statusLabel === option.value
                        ? "bg-primary text-primary-foreground border-primary"
                        : "bg-muted text-muted-foreground border-border hover:bg-accent hover:text-foreground"
                    }`}
                  >
                    {option.label}
                  </button>
                ))}
              </div>
            </div>

            <div className="flex items-center justify-between gap-3">
              <div>
                <p className="text-[10px] text-muted-foreground">お気に入り</p>
                <p className="text-[9px] text-muted-foreground/60">あとで見つけやすくします</p>
              </div>
              <button
                onClick={async () => {
                  await toggleAssetFavorite(asset.id, !asset.isFavorite);
                  await refreshDetailAndGrid();
                }}
                className={`h-8 px-3 rounded-lg border text-xs ${
                  asset.isFavorite
                    ? "bg-yellow-400/15 border-yellow-400/30 text-yellow-400"
                    : "bg-muted border-border text-muted-foreground hover:text-foreground"
                }`}
              >
                ★ {asset.isFavorite ? "登録済み" : "登録する"}
              </button>
            </div>

            <div>
              <p className="text-[10px] text-muted-foreground mb-1.5">カラーラベル</p>
              <div className="flex gap-2">
                {COLOR_LABELS.map((item) => (
                  <button
                    key={item.value || "none"}
                    onClick={async () => {
                      await updateAssetColorLabel(asset.id, item.value);
                      await refreshDetailAndGrid();
                    }}
                    className={`w-6 h-6 rounded-full border transition-transform ${
                      (asset.colorLabel ?? "") === item.value
                        ? "ring-2 ring-primary ring-offset-2 ring-offset-card scale-105"
                        : "border-border hover:scale-110"
                    }`}
                    style={{ backgroundColor: item.color }}
                    title={item.label}
                  />
                ))}
              </div>
            </div>
          </div>
        </DetailSection>

        <DetailSection title="タグ">
          <div className="flex flex-wrap gap-1.5">
            {allTags && allTags.length > 0 ? allTags.map((tag) => {
              const active = asset.tags?.some((candidate) => candidate.id === tag.id) ?? false;
              return (
                <button
                  key={tag.id}
                  onClick={async () => {
                    const current = asset.tags?.map((candidate) => candidate.id) ?? [];
                    const next = active ? current.filter((id) => id !== tag.id) : [...current, tag.id];
                    await setAssetTags(asset.id, next);
                    await refreshDetailAndGrid();
                  }}
                  className={`text-[10px] px-2.5 py-1 rounded-full border transition-colors ${
                    active
                      ? "bg-primary/15 text-foreground border-primary/40"
                      : "bg-muted text-muted-foreground border-border hover:bg-accent hover:text-foreground"
                  }`}
                >
                  <span className="inline-block w-2 h-2 rounded-full mr-1" style={{ backgroundColor: tag.color }} />
                  {tag.name}
                </button>
              );
            }) : (
              <p className="text-[10px] text-muted-foreground">タグがありません。左の「タグ」から作成できます。</p>
            )}
          </div>
        </DetailSection>

        <DetailSection title="メモ">
          <textarea
            value={noteContent}
            onChange={(event) => {
              setNoteContent(event.target.value);
              setNoteDirty(true);
            }}
            onBlur={() => void saveNote()}
            placeholder="この画像についてメモを残す…"
            className="w-full text-xs p-2.5 bg-muted rounded-lg border border-border focus:border-primary focus:outline-none resize-y min-h-24 placeholder:text-muted-foreground/50"
          />
          <div className="mt-1.5 flex justify-end min-h-5">
            {noteDirty ? (
              <button onClick={() => void saveNote()} className="text-[10px] px-2.5 py-1 bg-primary text-primary-foreground rounded-md">保存</button>
            ) : (
              <span className="text-[9px] text-muted-foreground">変更は自動保存されます</span>
            )}
          </div>
        </DetailSection>

        {posts && posts.length > 0 && (
          <DetailSection title="関連する投稿">
            <div className="space-y-1.5">
              {posts.map((post) => (
                <div key={post.id} className="text-xs px-2.5 py-2 bg-muted rounded-lg border border-border">
                  <span className="font-medium text-foreground">{post.title}</span>
                  <span className="ml-1.5 text-[9px] text-muted-foreground">{post.status}</span>
                </div>
              ))}
            </div>
          </DetailSection>
        )}
      </div>
    </aside>
  );
}

function DetailSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="border-t border-border pt-3">
      <h4 className="text-[10px] font-semibold tracking-wide text-muted-foreground mb-2">{title}</h4>
      {children}
    </section>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span className="text-[9px] text-muted-foreground">{label}</span>
      <p className="text-xs text-foreground break-words mt-0.5">{value}</p>
    </div>
  );
}

function MetaRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <span className="text-muted-foreground flex-shrink-0">{label}</span>
      <span className="text-foreground text-right break-all">{value}</span>
    </div>
  );
}
