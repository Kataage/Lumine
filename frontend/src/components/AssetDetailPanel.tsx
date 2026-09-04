import { useCallback, useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getAssetDetail,
  getPostRecordsByAsset,
  listTags,
  setAssetTags,
  toggleAssetFavorite,
  updateAssetColorLabel,
  updateAssetNote,
  updateAssetRating,
  updateAssetStatus,
} from "../api/client";
import { formatFileSize } from "../utils/format";
import { ImageViewerModal } from "./ImageViewerModal";
import { MemoryImage } from "./MemoryImage";
import { PostRecordModal } from "./PostRecordModal";

interface AssetDetailPanelProps {
  assetId: number;
  onClose: () => void;
}

const STATUS_OPTIONS = [
  { value: "unsorted", label: "未整理" },
  { value: "reviewed", label: "確認済み" },
  { value: "candidate", label: "候補" },
  { value: "published", label: "公開済み" },
];

const COLORS = ["", "red", "orange", "yellow", "green", "blue", "purple"];

export function AssetDetailPanel({ assetId, onClose }: AssetDetailPanelProps) {
  const queryClient = useQueryClient();
  const [showViewer, setShowViewer] = useState(false);
  const [showPostRecord, setShowPostRecord] = useState(false);
  const [note, setNote] = useState("");
  const [noteDirty, setNoteDirty] = useState(false);

  const { data: asset, isLoading, isError, error } = useQuery({
    queryKey: ["assetDetail", assetId],
    queryFn: () => getAssetDetail(assetId),
    enabled: assetId > 0,
    staleTime: Infinity,
  });
  const { data: tags = [] } = useQuery({ queryKey: ["tags"], queryFn: listTags, staleTime: Infinity });
  const { data: postRecords = [] } = useQuery({
    queryKey: ["assetPostRecords", assetId],
    queryFn: () => getPostRecordsByAsset(assetId),
    enabled: assetId > 0,
    staleTime: Infinity,
  });

  useEffect(() => {
    if (!asset) return;
    setNote(asset.noteContent ?? "");
    setNoteDirty(false);
  }, [asset?.id, asset?.noteContent]);

  useEffect(() => {
    setShowViewer(false);
    setShowPostRecord(false);
  }, [assetId]);

  const refreshDetail = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] });
  }, [assetId, queryClient]);

  const refreshAll = useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] }),
      queryClient.invalidateQueries({ queryKey: ["assets"] }),
    ]);
  }, [assetId, queryClient]);

  const saveNote = useCallback(async () => {
    if (!asset || !noteDirty) return;
    await updateAssetNote(asset.id, note);
    setNoteDirty(false);
    await refreshDetail();
  }, [asset, note, noteDirty, refreshDetail]);

  if (isLoading) {
    return (
      <aside className="app-detail-panel border-l border-border bg-card flex items-center justify-center">
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <div className="w-4 h-4 border-2 border-muted-foreground/30 border-t-primary rounded-full animate-spin" />
          詳細を読み込み中…
        </div>
      </aside>
    );
  }

  if (isError || !asset) {
    return (
      <aside className="app-detail-panel border-l border-border bg-card flex flex-col items-center justify-center gap-3 p-5">
        <p className="text-xs font-medium text-destructive">画像の詳細を開けませんでした</p>
        {error && <p className="text-[11px] text-muted-foreground text-center break-all">{String(error)}</p>}
        <button className="ui-secondary-button" onClick={onClose}>閉じる</button>
      </aside>
    );
  }

  return (
    <>
      <aside className="app-detail-panel border-l border-border bg-card flex flex-col overflow-hidden shadow-[-8px_0_24px_rgba(0,0,0,0.08)]">
        <div className="min-h-14 px-3.5 py-2 border-b border-border flex items-center justify-between gap-3 flex-shrink-0">
          <div className="min-w-0">
            <p className="text-xs font-semibold">画像の詳細</p>
            <p className="text-[11px] text-muted-foreground truncate">確認・整理・投稿記録</p>
          </div>
          <button className="ui-icon-button text-lg" onClick={onClose} aria-label="詳細パネルを閉じる">×</button>
        </div>

        <div className="flex-1 overflow-auto p-3 space-y-4">
          <section className="space-y-2">
            <button
              onClick={() => setShowViewer(true)}
              className="relative block w-full rounded-xl border border-border bg-muted/25 overflow-hidden group focus:outline-none focus:ring-2 focus:ring-primary"
              title="大きく表示"
            >
              <div className="flex justify-center">
                <MemoryImage
                  filePath={asset.filePath}
                  modifiedAtFs={asset.modifiedAtFs}
                  sourceWidth={asset.width}
                  sourceHeight={asset.height}
                  width={300}
                  height={240}
                  fit="contain"
                  alt={asset.fileName}
                />
              </div>
              <div className="absolute inset-x-0 bottom-0 h-14 bg-gradient-to-t from-black/75 to-transparent" />
              <span className="absolute bottom-2 right-2 h-8 px-3 inline-flex items-center rounded-lg bg-black/70 border border-white/15 text-[11px] font-medium text-white">
                ⛶ 大きく表示
              </span>
            </button>
            <div className="min-w-0">
              <h3 className="text-sm font-semibold leading-snug break-words">{asset.fileName}</h3>
              <p className="mt-1 text-[11px] leading-relaxed text-muted-foreground break-all">{asset.filePath}</p>
            </div>
          </section>

          <section className="grid grid-cols-2 gap-2 rounded-xl border border-border bg-muted/20 p-3">
            <Info label="ファイルサイズ" value={formatFileSize(asset.fileSize)} />
            <Info label="形式" value={asset.extension.toUpperCase()} />
            {asset.width > 0 && asset.height > 0 && <Info label="解像度" value={`${asset.width} × ${asset.height}`} />}
            <Info label="更新日" value={asset.modifiedAtFs ? new Date(asset.modifiedAtFs).toLocaleDateString("ja-JP") : "-"} />
          </section>

          <Section title="整理">
            <div className="space-y-3">
              <div>
                <p className="ui-label mb-1.5">評価</p>
                <div className="flex gap-1">
                  {[1, 2, 3, 4, 5].map((rating) => (
                    <button
                      key={rating}
                      className={`w-8 h-8 rounded-lg text-lg hover:bg-accent ${rating <= asset.rating ? "text-yellow-400" : "text-muted-foreground/25"}`}
                      onClick={async () => { await updateAssetRating(asset.id, rating); await refreshAll(); }}
                      aria-label={`評価 ${rating}`}
                    >★</button>
                  ))}
                </div>
              </div>

              <div>
                <p className="ui-label mb-1.5">状態</p>
                <div className="flex flex-wrap gap-1.5">
                  {STATUS_OPTIONS.map((option) => (
                    <button
                      key={option.value}
                      onClick={async () => { await updateAssetStatus(asset.id, option.value); await refreshAll(); }}
                      className={`h-8 px-2.5 rounded-lg border text-[11px] ${asset.statusLabel === option.value ? "bg-primary border-primary text-primary-foreground" : "bg-muted border-border text-muted-foreground hover:text-foreground"}`}
                    >{option.label}</button>
                  ))}
                </div>
              </div>

              <div className="flex items-center justify-between gap-3">
                <span className="ui-label">お気に入り</span>
                <button
                  className={`ui-secondary-button ${asset.isFavorite ? "text-yellow-400 border-yellow-400/30" : ""}`}
                  onClick={async () => { await toggleAssetFavorite(asset.id, !asset.isFavorite); await refreshAll(); }}
                >★ {asset.isFavorite ? "登録済み" : "登録する"}</button>
              </div>

              <div>
                <p className="ui-label mb-1.5">カラーラベル</p>
                <div className="flex gap-2">
                  {COLORS.map((color) => (
                    <button
                      key={color || "none"}
                      className={`w-6 h-6 rounded-full border border-border ${asset.colorLabel === color ? "ring-2 ring-primary ring-offset-2 ring-offset-card" : ""}`}
                      style={{ backgroundColor: color || "transparent" }}
                      onClick={async () => { await updateAssetColorLabel(asset.id, color); await refreshAll(); }}
                      aria-label={color || "カラーラベルなし"}
                    />
                  ))}
                </div>
              </div>
            </div>
          </Section>

          <Section title="タグ">
            <div className="flex flex-wrap gap-1.5">
              {tags.length > 0 ? tags.map((tag) => {
                const active = asset.tags?.some((item) => item.id === tag.id) ?? false;
                return (
                  <button
                    key={tag.id}
                    onClick={async () => {
                      const current = asset.tags?.map((item) => item.id) ?? [];
                      const next = active ? current.filter((id) => id !== tag.id) : [...current, tag.id];
                      await setAssetTags(asset.id, next);
                      await refreshAll();
                    }}
                    className={`h-7 px-2.5 rounded-full border text-[11px] ${active ? "bg-primary/15 border-primary/40 text-foreground" : "bg-muted border-border text-muted-foreground"}`}
                  >
                    <span className="inline-block w-2 h-2 rounded-full mr-1" style={{ backgroundColor: tag.color }} />{tag.name}
                  </button>
                );
              }) : <p className="text-[11px] text-muted-foreground">タグはまだありません。</p>}
            </div>
          </Section>

          <Section title="メモ">
            <textarea
              value={note}
              onChange={(event) => { setNote(event.target.value); setNoteDirty(true); }}
              onBlur={() => void saveNote()}
              placeholder="この画像についてメモを残す…"
              className="ui-input w-full min-h-24 resize-y leading-relaxed"
            />
            <div className="mt-1.5 min-h-5 flex justify-end">
              {noteDirty ? <button className="ui-primary-button" onClick={() => void saveNote()}>保存</button> : <span className="text-[11px] text-muted-foreground">変更は自動保存されます</span>}
            </div>
          </Section>

          <Section title="投稿記録">
            <button className="ui-primary-button w-full justify-center" onClick={() => setShowPostRecord(true)}>
              ＋ この画像の投稿記録を追加
            </button>
            <div className="mt-2 space-y-2">
              {postRecords.map((record) => (
                <div key={record.id} className="rounded-lg border border-border bg-muted/25 p-2.5">
                  <div className="flex items-start gap-2">
                    <div className="min-w-0 flex-1">
                      <p className="text-xs font-medium truncate">{record.targetName}</p>
                      <p className="text-[11px] text-muted-foreground truncate">{record.accountDisplay}{record.accountIdentifier ? ` · ${record.accountIdentifier}` : ""}</p>
                    </div>
                    <span className="text-[10px] text-muted-foreground whitespace-nowrap">{record.publishedAt ? new Date(record.publishedAt).toLocaleDateString("ja-JP") : ""}</span>
                  </div>
                  {record.externalPostId && <p className="mt-1.5 text-[11px] text-primary break-all">{record.externalPostId}</p>}
                </div>
              ))}
              {postRecords.length === 0 && <p className="text-[11px] text-muted-foreground text-center py-2">まだ投稿記録はありません</p>}
            </div>
          </Section>

          {(asset.cameraModel || asset.lensModel || asset.focalLength || asset.aperture || asset.shutterSpeed || asset.iso || asset.exifDate) && (
            <Section title="撮影情報 (EXIF)">
              <div className="space-y-1.5">
                {asset.cameraModel && <Meta label="カメラ" value={asset.cameraModel} />}
                {asset.lensModel && <Meta label="レンズ" value={asset.lensModel} />}
                {asset.focalLength && <Meta label="焦点距離" value={asset.focalLength} />}
                {asset.aperture && <Meta label="絞り" value={asset.aperture} />}
                {asset.shutterSpeed && <Meta label="シャッター" value={asset.shutterSpeed} />}
                {asset.iso ? <Meta label="ISO" value={String(asset.iso)} /> : null}
                {asset.exifDate && <Meta label="撮影日時" value={asset.exifDate} />}
              </div>
            </Section>
          )}
        </div>
      </aside>

      {showViewer && <ImageViewerModal asset={asset} onClose={() => setShowViewer(false)} />}
      {showPostRecord && (
        <PostRecordModal
          assetIds={[asset.id]}
          defaultTitle={asset.fileName}
          onClose={() => setShowPostRecord(false)}
          onSaved={() => void queryClient.invalidateQueries({ queryKey: ["assetPostRecords", asset.id] })}
        />
      )}
    </>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="border-t border-border pt-3">
      <h4 className="mb-2 text-[11px] font-semibold text-muted-foreground">{title}</h4>
      {children}
    </section>
  );
}

function Info({ label, value }: { label: string; value: string }) {
  return <div><p className="text-[10px] text-muted-foreground">{label}</p><p className="mt-0.5 text-xs break-words">{value}</p></div>;
}

function Meta({ label, value }: { label: string; value: string }) {
  return <div className="flex justify-between gap-3 text-[11px]"><span className="text-muted-foreground flex-shrink-0">{label}</span><span className="text-right break-all">{value}</span></div>;
}
