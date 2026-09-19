import { useCallback, useEffect, useMemo, useState } from "react";
import { useApp } from "../App";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { AssetDTO } from "../api/client";
import {
  deleteAssetFiles,
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
import { openAssetInViewer } from "../utils/viewerSession";
import { useAppDialog } from "./AppDialogProvider";
import { CreativeContextPanel } from "./CreativeContextPanel";
import { MemoryImage } from "./MemoryImage";
import { PostRecordModal } from "./PostRecordModal";
import { TagPicker } from "./TagPicker";
import { AdvancedVisionAssetPanel } from "./AdvancedVisionAssetPanel";

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

type DetailView = "organize" | "creative" | "ai" | "publication" | "info";

const DETAIL_VIEWS: Array<{ key: DetailView; label: string }> = [
  { key: "organize", label: "整理" },
  { key: "creative", label: "制作" },
  { key: "ai", label: "AI解析" },
  { key: "publication", label: "公開" },
  { key: "info", label: "情報" },
];

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

function parsePlatformMetadata(value?: string): Record<string, unknown> {
  if (!value) return {};
  try {
    const parsed = JSON.parse(value);
    return parsed && typeof parsed === "object" ? parsed as Record<string, unknown> : {};
  } catch {
    return {};
  }
}

export function AssetDetailPanel({ asset: listAsset, onClose }: AssetDetailPanelProps) {
  const { setState } = useApp();
  const queryClient = useQueryClient();
  const dialog = useAppDialog();
  const baseAsset = useMemo(() => normalizeAsset(listAsset), [listAsset]);
  const assetId = baseAsset.id;
  const [asset, setAsset] = useState<AssetDTO>(baseAsset);
  const [showPostRecord, setShowPostRecord] = useState(false);
  const [note, setNote] = useState(baseAsset.noteContent ?? "");
  const [noteDirty, setNoteDirty] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [activeView, setActiveView] = useState<DetailView>("organize");

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

  const { data: tags = [] } = useQuery({ queryKey: ["tags"], queryFn: listTags, staleTime: Infinity });
  const { data: postRecords = [] } = useQuery({
    queryKey: ["assetPostRecords", assetId],
    queryFn: () => getPostRecordsByAsset(assetId),
    enabled: assetId > 0,
    staleTime: 30_000,
  });

  useEffect(() => {
    setAsset(baseAsset);
    setShowPostRecord(false);
    setNote(baseAsset.noteContent ?? "");
    setNoteDirty(false);
    setDeleting(false);
    setActiveView("organize");
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

  const deleteCurrentFile = async () => {
    if (assetId <= 0 || deleting) return;
    const approved = await dialog.confirm({
      title: "元画像ファイルを削除しますか？",
      description: `「${asset.fileName}」をディスクから削除し、Lumineの登録情報も削除します。\nこの操作は元に戻せません。`,
      confirmLabel: "画像を削除",
      tone: "danger",
    });
    if (!approved) return;
    setDeleting(true);
    try {
      const result = await deleteAssetFiles([assetId]);
      if (result.deletedCount > 0) {
        onClose();
        await queryClient.invalidateQueries({ queryKey: ["assets"], refetchType: "active" });
        return;
      }
      await dialog.notify({
        title: "画像ファイルを削除できませんでした",
        description: "ファイルが使用中か、アクセス権限が不足している可能性があります。",
        detail: (result.errors ?? []).slice(0, 3).join("\n"),
        tone: "danger",
      });
    } catch (error) {
      console.error("image file delete failed:", error);
      await dialog.notify({
        title: "画像ファイルの削除に失敗しました",
        description: "元画像は削除されていません。",
        detail: error instanceof Error ? error.message : String(error),
        tone: "danger",
      });
    } finally {
      setDeleting(false);
    }
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
          </div>
          <button className="ui-icon-button text-lg" onClick={onClose} aria-label="詳細パネルを閉じる">×</button>
        </div>

        <div className="px-3 py-3 border-b border-border bg-muted/10 flex-shrink-0">
          <div className="flex gap-3 min-w-0">
            <button
              type="button"
              onClick={() => openAssetInViewer(asset)}
              className="detail-summary-thumb relative rounded-xl border border-border bg-background/50 overflow-hidden group flex items-center justify-center"
              title="大きく表示"
              disabled={!asset.filePath}
            >
              {asset.filePath
                ? <MemoryImage filePath={asset.filePath} modifiedAtFs={asset.modifiedAtFs} sourceWidth={asset.width} sourceHeight={asset.height} width={104} height={84} fit="contain" priority="high" alt={asset.fileName} />
                : <span className="text-[10px] text-muted-foreground">画像なし</span>}
              {asset.filePath && <span className="absolute right-1.5 bottom-1.5 rounded-md border border-white/10 bg-black/70 px-1.5 py-0.5 text-[9px] text-white opacity-0 group-hover:opacity-100 transition-opacity">⛶ 拡大</span>}
            </button>

            <div className="min-w-0 flex-1 py-0.5">
              <h3 className="text-sm font-semibold leading-snug break-words">{asset.fileName}</h3>
              <p className="mt-1 text-[10px] leading-relaxed text-muted-foreground break-all line-clamp-2">{asset.filePath || "ファイルパス不明"}</p>
              <div className="mt-2 flex flex-wrap gap-1.5">
                <SummaryChip>{extensionLabel}</SummaryChip>
                <SummaryChip>{formatFileSize(asset.fileSize)}</SummaryChip>
                {asset.width > 0 && asset.height > 0 && <SummaryChip>{asset.width} × {asset.height}</SummaryChip>}
                {asset.isFavorite && <SummaryChip>★ お気に入り</SummaryChip>}
                <button
                  type="button"
                  className="ui-mini-button"
                  onClick={() => {
                    setState((current) => ({
                      ...current,
                      searchMode: "semantic",
                      searchQuery: "",
                      similarAssetId: asset.id,
                      similarAssetName: asset.fileName,
                      selectedAssets: new Set(),
                      lastSelectedIndex: null,
                    }));
                    onClose();
                  }}
                  disabled={asset.id <= 0}
                  title="Semantic embeddingから近い画像を表示"
                >
                  ≈ 類似画像
                </button>
              </div>
            </div>
          </div>
        </div>

        <div className="detail-tab-strip" role="tablist" aria-label="画像詳細のカテゴリ">
          {DETAIL_VIEWS.map((view) => (
            <button
              key={view.key}
              type="button"
              role="tab"
              aria-selected={activeView === view.key}
              className={`detail-tab-button ${activeView === view.key ? "active" : ""}`}
              onClick={() => setActiveView(view.key)}
            >
              {view.label}
              {view.key === "publication" && postRecords.length > 0 && <span className="ml-1 text-[9px] opacity-65">{postRecords.length}</span>}
            </button>
          ))}
        </div>

        <div className="detail-scroll-region flex-1">
          {detailError && (
            <div className="mx-3 mt-3 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-[11px] leading-relaxed text-amber-200">
              基本情報は表示できています。EXIF・タグなど追加情報の取得だけ失敗しました。
              {detailQueryError ? <span className="block mt-1 text-amber-200/70 break-all">{String(detailQueryError)}</span> : null}
              <button className="mt-2 underline underline-offset-2" onClick={() => void refreshDetail()}>追加情報を再読み込み</button>
            </div>
          )}

          {activeView === "organize" && (
            <div className="p-3 space-y-3">
              <Section title="整理ステータス">
                <div className="space-y-3">
                  <div>
                    <p className="ui-label mb-1.5">評価</p>
                    <div className="flex gap-1">
                      {[1, 2, 3, 4, 5].map((rating) => (
                        <button key={rating} className={`w-8 h-8 rounded-lg text-lg hover:bg-accent ${rating <= asset.rating ? "text-yellow-400" : "text-muted-foreground/25"}`} onClick={() => void updateRating(rating)} aria-label={`評価 ${rating}`}>★</button>
                      ))}
                    </div>
                  </div>

                  <div>
                    <p className="ui-label mb-1.5">状態</p>
                    <div className="flex flex-wrap gap-1.5">
                      {STATUS_OPTIONS.map((option) => (
                        <button key={option.value} onClick={() => void updateStatus(option.value)} className={`h-8 px-2.5 rounded-lg border text-[11px] ${asset.statusLabel === option.value ? "bg-primary border-primary text-primary-foreground" : "bg-muted border-border text-muted-foreground hover:text-foreground"}`}>{option.label}</button>
                      ))}
                    </div>
                  </div>

                  <div className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3 rounded-lg border border-border bg-background/35 px-2.5 py-2">
                    <div>
                      <p className="text-[11px] font-medium">お気に入り</p>
                    </div>
                    <button className={`ui-secondary-button ${asset.isFavorite ? "text-yellow-400 border-yellow-400/30" : ""}`} onClick={() => void updateFavorite(!asset.isFavorite)}>★ {asset.isFavorite ? "登録済み" : "登録"}</button>
                  </div>

                  <div>
                    <p className="ui-label mb-1.5">カラーラベル</p>
                    <div className="flex gap-2">
                      {COLORS.map((color) => <button key={color || "none"} className={`w-6 h-6 rounded-full border border-border ${asset.colorLabel === color ? "ring-2 ring-primary ring-offset-2 ring-offset-card" : ""}`} style={{ backgroundColor: color || "transparent" }} onClick={() => void updateColor(color)} aria-label={color || "カラーラベルなし"} />)}
                    </div>
                  </div>
                </div>
              </Section>

              <Section title="タグ">
                <TagPicker assetId={asset.id} tags={tags} assignedTags={asset.tags ?? []} onAssignedTagsChange={(nextTags) => setAsset((current) => ({ ...current, tags: nextTags } as AssetDTO))} onChanged={refreshAll} />
              </Section>

              <Section title="メモ">
                <textarea value={note} onChange={(event) => { setNote(event.target.value); setNoteDirty(true); }} onBlur={() => void saveNote()} placeholder="この画像についてメモを残す…" className="ui-input w-full min-h-28 resize-y leading-relaxed" />
                {noteDirty && <div className="mt-1.5 flex justify-end"><button className="ui-primary-button" onClick={() => void saveNote()}>保存</button></div>}
              </Section>
            </div>
          )}

          {activeView === "creative" && (
            <div className="p-3 space-y-3">
              <PanelIntro title="制作コンテキスト" />
              <CreativeContextPanel assetId={assetId} />
            </div>
          )}

          {activeView === "ai" && (
            <div className="p-3 space-y-3">
              <PanelIntro title="Advanced Vision" />
              <AdvancedVisionAssetPanel assetId={assetId} />
            </div>
          )}

          {activeView === "publication" && (
            <div className="p-3 space-y-3">
              <PanelIntro title="公開履歴" />
              <button className="ui-primary-button w-full justify-center" onClick={() => setShowPostRecord(true)} disabled={asset.id <= 0}>＋ この画像の公開記録を追加</button>
              <div className="space-y-2">
                {postRecords.map((record) => {
                  const metadata = parsePlatformMetadata(record.platformMetadataJson);
                  const aiGenerated = metadata.aiGenerated === true;
                  const ageRestriction = typeof metadata.ageRestriction === "string" ? metadata.ageRestriction : "";
                  const tagsLabel = record.hashtags.split(/[\n,]+/).map((item) => item.trim()).filter(Boolean).slice(0, 5);
                  return (
                    <div key={record.id} className="rounded-xl border border-border bg-muted/20 p-3 space-y-1.5">
                      <div className="flex items-start gap-2">
                        <div className="min-w-0 flex-1">
                          <p className="text-xs font-medium truncate">{record.targetName} · {record.accountDisplay}</p>
                          <p className="text-[10px] text-muted-foreground truncate">{record.title}</p>
                        </div>
                        <span className="text-[10px] text-muted-foreground whitespace-nowrap">{record.publishedAt ? new Date(record.publishedAt).toLocaleDateString("ja-JP") : ""}</span>
                      </div>
                      {record.body && <p className="text-[10px] leading-relaxed text-muted-foreground line-clamp-3 whitespace-pre-wrap">{record.body}</p>}
                      {tagsLabel.length > 0 && <div className="flex flex-wrap gap-1">{tagsLabel.map((tag) => <span key={tag} className="rounded-md bg-background/70 px-1.5 py-0.5 text-[9px] text-muted-foreground">#{tag.replace(/^#/, "")}</span>)}</div>}
                      {(aiGenerated || ageRestriction) && <p className="text-[9px] text-muted-foreground">{aiGenerated ? "AI生成" : ""}{aiGenerated && ageRestriction ? " · " : ""}{ageRestriction}</p>}
                      {(record.externalUrl || record.externalPostId) && <p className="text-[10px] text-primary break-all select-text">{record.externalUrl || record.externalPostId}</p>}
                    </div>
                  );
                })}
                {postRecords.length === 0 && <div className="rounded-xl border border-dashed border-border p-5 text-center"><p className="text-[11px] text-muted-foreground">まだ公開履歴はありません</p></div>}
              </div>
            </div>
          )}

          {activeView === "info" && (
            <div className="p-3 space-y-3">
              <Section title="ファイル情報">
                <div className="grid grid-cols-2 gap-2">
                  <Info label="ファイルサイズ" value={formatFileSize(asset.fileSize)} />
                  <Info label="形式" value={extensionLabel} />
                  <Info label="解像度" value={asset.width > 0 && asset.height > 0 ? `${asset.width} × ${asset.height}` : "取得中 / 不明"} />
                  <Info label="更新日" value={modifiedLabel} />
                </div>
                {asset.hashBlake3 && <div className="mt-3 border-t border-border pt-2"><Meta label="BLAKE3" value={asset.hashBlake3} /></div>}
              </Section>

              {(asset.cameraModel || asset.lensModel || asset.focalLength || asset.aperture || asset.shutterSpeed || asset.iso || asset.exifDate) && (
                <Section title="撮影情報 (EXIF)">
                  <div className="space-y-1.5">{asset.cameraModel && <Meta label="カメラ" value={asset.cameraModel} />}{asset.lensModel && <Meta label="レンズ" value={asset.lensModel} />}{asset.focalLength && <Meta label="焦点距離" value={asset.focalLength} />}{asset.aperture && <Meta label="絞り" value={asset.aperture} />}{asset.shutterSpeed && <Meta label="シャッター" value={asset.shutterSpeed} />}{asset.iso ? <Meta label="ISO" value={String(asset.iso)} /> : null}{asset.exifDate && <Meta label="撮影日時" value={asset.exifDate} />}</div>
                </Section>
              )}

              {assetId > 0 && (
                <Section title="ファイル操作">
                  <button type="button" disabled={deleting} className="w-full h-9 px-3 rounded-lg border border-destructive/40 bg-destructive/10 text-[11px] font-medium text-destructive hover:bg-destructive hover:text-destructive-foreground" onClick={() => void deleteCurrentFile()}>{deleting ? "削除しています…" : "元画像ファイルを削除…"}</button>
                </Section>
              )}
            </div>
          )}
        </div>
      </aside>

      {showPostRecord && asset.id > 0 && <PostRecordModal assetIds={[asset.id]} defaultTitle={asset.fileName} onClose={() => setShowPostRecord(false)} onSaved={() => void queryClient.invalidateQueries({ queryKey: ["assetPostRecords", asset.id] })} />}
    </>
  );
}

function Section({ title, description, children }: { title: string; description?: string; children: React.ReactNode }) {
  return (
    <section className="rounded-xl border border-border bg-muted/10 p-3">
      <div className="mb-2.5">
        <h4 className="text-[11px] font-semibold">{title}</h4>
        {description && <p className="mt-0.5 text-[10px] leading-relaxed text-muted-foreground">{description}</p>}
      </div>
      {children}
    </section>
  );
}

function PanelIntro({ title }: { title: string }) {
  return <div className="px-0.5"><p className="text-xs font-semibold">{title}</p></div>;
}

function SummaryChip({ children }: { children: React.ReactNode }) {
  return <span className="rounded-md border border-border bg-background/50 px-1.5 py-0.5 text-[9px] text-muted-foreground whitespace-nowrap">{children}</span>;
}

function Info({ label, value }: { label: string; value: string }) {
  return <div className="rounded-lg border border-border/70 bg-background/35 p-2"><p className="text-[10px] text-muted-foreground">{label}</p><p className="mt-0.5 text-xs break-words">{value}</p></div>;
}

function Meta({ label, value }: { label: string; value: string }) {
  return <div className="flex justify-between gap-3 text-[11px]"><span className="text-muted-foreground flex-shrink-0">{label}</span><span className="text-right break-all">{value}</span></div>;
}

