import { useCallback, useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { AssetDTO } from "../api/client";
import {
  getAssetDetail,
  getPostRecordsByAsset,
  listTags,
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
import { TagPicker } from "./TagPicker";

interface AssetDetailPanelProps {
  asset: AssetDTO;
  onClose: () => void;
}

const STATUS_OPTIONS = [
  { value: "unsorted", label: "未整理" },
  { value: "reviewed", label: "確認済み" },
  { value: "candidate", label: "候補" },
  { value: "published", label: "公開済み" },
];

const COLORS = ["", "red", "orange", "yellow", "green", "blue", "purple"];

function asString(value: unknown): string {
  return typeof value === "string" ? value : "";
}

function asNumber(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function normalizeAsset(source: AssetDTO): AssetDTO {
  const filePath = asString(source?.filePath);
  const pathParts = filePath.split(/[\\/]/);
  const fallbackName = pathParts[pathParts.length - 1] || "画像";
  const fileName = asString(source?.fileName) || fallbackName;
  const rawExtension = asString(source?.extension);
  const dot = fileName.lastIndexOf(".");
  const extension = rawExtension || (dot >= 0 ? fileName.slice(dot) : "");

  return {
    ...source,
    id: asNumber(source?.id),
    libraryId: asNumber(source?.libraryId),
    folderPath: asString(source?.folderPath),
    fileName,
    filePath,
    extension,
    fileSize: Math.max(0, asNumber(source?.fileSize)),
    width: Math.max(0, asNumber(source?.width)),
    height: Math.max(0, asNumber(source?.height)),
    thumbStatus: asString(source?.thumbStatus) || "none",
    rating: Math.max(0, Math.min(5, asNumber(source?.rating))),
    statusLabel: asString(source?.statusLabel) || "unsorted",
    isFavorite: Boolean(source?.isFavorite),
    colorLabel: asString(source?.colorLabel),
    noteContent: asString(source?.noteContent),
    tags: Array.isArray(source?.tags) ? source.tags : [],
    cameraModel: asString(source?.cameraModel),
    lensModel: asString(source?.lensModel),
    focalLength: asString(source?.focalLength),
    aperture: asString(source?.aperture),
    shutterSpeed: asString(source?.shutterSpeed),
    iso: Math.max(0, asNumber(source?.iso)),
    exifDate: asString(source?.exifDate),
    gpsLatitude: asString(source?.gpsLatitude),
    gpsLongitude: asString(source?.gpsLongitude),
    hashBlake3: asString(source?.hashBlake3),
  } as AssetDTO;
}

function mergeAsset(base: AssetDTO, detail: AssetDTO): AssetDTO {
  return normalizeAsset({
    ...base,
    ...detail,
    tags: detail.tags ?? base.tags,
    noteContent: detail.noteContent ?? base.noteContent,
  } as AssetDTO);
}

export function AssetDetailPanel({ asset: listAsset, onClose }: AssetDetailPanelProps) {
  const queryClient = useQueryClient();
  const baseAsset = useMemo(() => normalizeAsset(listAsset), [listAsset]);
  const assetId = baseAsset.id;
  const [asset, setAsset] = useState<AssetDTO>(baseAsset);
  const [showViewer, setShowViewer] = useState(false);
  const [showPostRecord, setShowPostRecord] = useState(false);
  const [note, setNote] = useState(baseAsset.noteContent ?? "");
  const [noteDirty, setNoteDirty] = useState(false);

  const {
    data: detailedAsset,
    isFetching: detailFetching,
    isError: detailError,
    error: detailQueryError,
  } = useQuery({
    queryKey: ["assetDetail", assetId],
    queryFn: async () => {
      const detail = await getAssetDetail(assetId);
      if (!detail) throw new Error("画像の詳細情報を取得できませんでした");
      return normalizeAsset(detail);
    },
    enabled: assetId > 0,
    staleTime: 0,
    retry: 1,
    refetchOnMount: "always",
  });

  const { data: tags = [] } = useQuery({
    queryKey: ["tags"],
    queryFn: listTags,
    staleTime: Infinity,
  });

  const { data: postRecords = [] } = useQuery({
    queryKey: ["assetPostRecords", assetId],
    queryFn: () => getPostRecordsByAsset(assetId),
    enabled: assetId > 0,
    staleTime: 30_000,
  });

  useEffect(() => {
    setAsset(baseAsset);
    setShowViewer(false);
    setShowPostRecord(false);
    setNote(baseAsset.noteContent ?? "");
    setNoteDirty(false);
  }, [assetId, baseAsset]);

  useEffect(() => {
    if (!detailedAsset || detailedAsset.id !== assetId) return;
    setAsset((current) => mergeAsset(current.id === assetId ? current : baseAsset, detailedAsset));
    if (!noteDirty) setNote(detailedAsset.noteContent ?? "");
  }, [assetId, baseAsset, detailedAsset, noteDirty]);

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
    if (!noteDirty || assetId <= 0) return;
    await updateAssetNote(assetId, note);
    setAsset((current) => ({ ...current, noteContent: note } as AssetDTO));
    setNoteDirty(false);
    await refreshDetail();
  }, [assetId, note, noteDirty, refreshDetail]);

  const updateRating = async (rating: number) => {
    setAsset((current) => ({ ...current, rating } as AssetDTO));
    await updateAssetRating(assetId, rating);
    await refreshAll();
  };

  const updateStatus = async (statusLabel: string) => {
    setAsset((current) => ({ ...current, statusLabel } as AssetDTO));
    await updateAssetStatus(assetId, statusLabel);
    await refreshAll();
  };

  const updateFavorite = async (isFavorite: boolean) => {
    setAsset((current) => ({ ...current, isFavorite } as AssetDTO));
    await toggleAssetFavorite(assetId, isFavorite);
    await refreshAll();
  };

  const updateColor = async (colorLabel: string) => {
    setAsset((current) => ({ ...current, colorLabel } as AssetDTO));
    await updateAssetColorLabel(assetId, colorLabel);
    await refreshAll();
  };

  const extensionLabel = asset.extension ? asset.extension.toUpperCase() : "不明";
  const modifiedLabel = asset.modifiedAtFs
    ? (() => {
        const parsed = new Date(asset.modifiedAtFs);
        return Number.isNaN(parsed.getTime()) ? "不明" : parsed.toLocaleDateString("ja-JP");
      })()
    : "不明";

  return (
    <>
      <aside className="app-detail-panel border-l border-border bg-card flex flex-col overflow-hidden shadow-[-8px_0_24px_rgba(0,0,0,0.08)]" aria-label="画像の詳細パネル">
        <div className="min-h-14 px-3.5 py-2 border-b border-border flex items-center justify-between gap-3 flex-shrink-0">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <p className="text-xs font-semibold">画像の詳細</p>
              {detailFetching && <span className="w-3 h-3 border-2 border-muted-foreground/25 border-t-primary rounded-full animate-spin" title="詳細情報を読み込み中" />}
            </div>
            <p className="text-[11px] text-muted-foreground truncate">基本情報は即時表示・追加情報は後から読み込み</p>
          </div>
          <button className="ui-icon-button text-lg" onClick={onClose} aria-label="詳細パネルを閉じる">×</button>
        </div>

        <div className="flex-1 overflow-auto p-3 space-y-4">
          {detailError && (
            <div className="rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-[11px] leading-relaxed text-amber-200">
              基本情報は表示できています。EXIF・タグなど追加情報の取得だけ失敗しました。
              {detailQueryError ? <span className="block mt-1 text-amber-200/70 break-all">{String(detailQueryError)}</span> : null}
              <button className="mt-2 underline underline-offset-2" onClick={() => void refreshDetail()}>追加情報を再読み込み</button>
            </div>
          )}

          <section className="space-y-2">
            <button
              onClick={() => setShowViewer(true)}
              className="relative block w-full rounded-xl border border-border bg-muted/25 overflow-hidden group focus:outline-none focus:ring-2 focus:ring-primary"
              title="大きく表示"
              disabled={!asset.filePath}
            >
              <div className="flex justify-center min-h-40 items-center">
                {asset.filePath ? (
                  <MemoryImage
                    filePath={asset.filePath}
                    modifiedAtFs={asset.modifiedAtFs}
                    sourceWidth={asset.width}
                    sourceHeight={asset.height}
                    width={300}
                    height={240}
                    fit="contain"
                    priority="high"
                    alt={asset.fileName}
                  />
                ) : (
                  <div className="w-full h-40 flex items-center justify-center text-xs text-muted-foreground">画像パスを取得できません</div>
                )}
              </div>
              <div className="absolute inset-x-0 bottom-0 h-14 bg-gradient-to-t from-black/75 to-transparent pointer-events-none" />
              <span className="absolute bottom-2 right-2 h-8 px-3 inline-flex items-center rounded-lg bg-black/70 border border-white/15 text-[11px] font-medium text-white pointer-events-none">
                ⛶ 大きく表示
              </span>
            </button>
            <div className="min-w-0">
              <h3 className="text-sm font-semibold leading-snug break-words">{asset.fileName}</h3>
              <p className="mt-1 text-[11px] leading-relaxed text-muted-foreground break-all">{asset.filePath || "ファイルパス不明"}</p>
            </div>
          </section>

          <section className="grid grid-cols-2 gap-2 rounded-xl border border-border bg-muted/20 p-3">
            <Info label="ファイルサイズ" value={formatFileSize(asset.fileSize)} />
            <Info label="形式" value={extensionLabel} />
            <Info label="解像度" value={asset.width > 0 && asset.height > 0 ? `${asset.width} × ${asset.height}` : "取得中 / 不明"} />
            <Info label="更新日" value={modifiedLabel} />
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
                      onClick={() => void updateRating(rating)}
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
                      onClick={() => void updateStatus(option.value)}
                      className={`h-8 px-2.5 rounded-lg border text-[11px] ${asset.statusLabel === option.value ? "bg-primary border-primary text-primary-foreground" : "bg-muted border-border text-muted-foreground hover:text-foreground"}`}
                    >{option.label}</button>
                  ))}
                </div>
              </div>

              <div className="flex items-center justify-between gap-3">
                <span className="ui-label">お気に入り</span>
                <button
                  className={`ui-secondary-button ${asset.isFavorite ? "text-yellow-400 border-yellow-400/30" : ""}`}
                  onClick={() => void updateFavorite(!asset.isFavorite)}
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
                      onClick={() => void updateColor(color)}
                      aria-label={color || "カラーラベルなし"}
                    />
                  ))}
                </div>
              </div>
            </div>
          </Section>

          <Section title="タグ">
            <TagPicker
              assetId={asset.id}
              tags={tags}
              assignedTags={asset.tags ?? []}
              onAssignedTagsChange={(nextTags) => setAsset((current) => ({ ...current, tags: nextTags } as AssetDTO))}
              onChanged={refreshAll}
            />
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
            <button className="ui-primary-button w-full justify-center" onClick={() => setShowPostRecord(true)} disabled={asset.id <= 0}>
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

      {showViewer && asset.filePath && <ImageViewerModal asset={asset} onClose={() => setShowViewer(false)} />}
      {showPostRecord && asset.id > 0 && (
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
