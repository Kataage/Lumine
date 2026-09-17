import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import {
  createAssetRelation,
  createGenerationGroup,
  createWork,
  getAssetDetail,
} from "../api/client";
import type { AssetDTO } from "../api/client";
import { useAppDialog } from "./AppDialogProvider";

type Mode = "group" | "work" | "relation";

const RELATION_OPTIONS = [
  ["derived", "派生"], ["variation", "バリエーション"], ["img2img", "img2img"], ["inpaint", "Inpaint"],
  ["outpaint", "Outpaint"], ["upscale", "アップスケール"], ["crop", "クロップ"], ["edit", "編集"],
  ["animation", "動画化"], ["reference", "参照"],
] as const;

export function CreativeOrganizeModal({ assetIds, onClose }: { assetIds: number[]; onClose: () => void }) {
  const dialog = useAppDialog();
  const [mode, setMode] = useState<Mode>(assetIds.length === 2 ? "relation" : "group");
  const [assets, setAssets] = useState<AssetDTO[]>([]);
  const [busy, setBusy] = useState(false);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [prompt, setPrompt] = useState("");
  const [negativePrompt, setNegativePrompt] = useState("");
  const [modelName, setModelName] = useState("");
  const [sampler, setSampler] = useState("");
  const [scheduler, setScheduler] = useState("");
  const [steps, setSteps] = useState(0);
  const [cfgScale, setCfgScale] = useState(0);
  const [notes, setNotes] = useState("");
  const [relationType, setRelationType] = useState("derived");
  const [relationNote, setRelationNote] = useState("");
  const [parentFirst, setParentFirst] = useState(true);

  useEffect(() => {
    let cancelled = false;
    void Promise.all(assetIds.map((id) => getAssetDetail(id))).then((items) => {
      if (!cancelled) setAssets(items.filter((item): item is AssetDTO => Boolean(item)));
    });
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { cancelled = true; document.body.style.overflow = previous; };
  }, [assetIds]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) onClose();
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [busy, onClose]);

  const relationAssets = useMemo(() => {
    if (assetIds.length !== 2) return null;
    return parentFirst ? [assetIds[0], assetIds[1]] : [assetIds[1], assetIds[0]];
  }, [assetIds, parentFirst]);

  const save = async () => {
    if (busy) return;
    setBusy(true);
    try {
      if (mode === "work") {
        if (!name.trim()) throw new Error("作品名を入力してください");
        const result = await createWork(name.trim(), description.trim(), assetIds);
        if (!result) throw new Error("作品を作成できませんでした");
      } else if (mode === "group") {
        if (!name.trim()) throw new Error("生成グループ名を入力してください");
        const result = await createGenerationGroup({
          assetIds,
          name: name.trim(),
          prompt: prompt.trim(),
          negativePrompt: negativePrompt.trim(),
          modelName: modelName.trim(),
          sampler: sampler.trim(),
          scheduler: scheduler.trim(),
          steps: Number.isFinite(steps) ? steps : 0,
          cfgScale: Number.isFinite(cfgScale) ? cfgScale : 0,
          workflowJson: "",
          notes: notes.trim(),
        });
        if (!result) throw new Error("生成グループを作成できませんでした");
      } else {
        if (!relationAssets) throw new Error("関係性の作成には2枚の画像を選択してください");
        const result = await createAssetRelation(relationAssets[0], relationAssets[1], relationType, relationNote.trim());
        if (!result) throw new Error("関係性を作成できませんでした。同じ関係が既に存在する可能性があります。");
      }
      onClose();
    } catch (cause) {
      await dialog.notify({
        title: "制作情報を保存できませんでした",
        description: "入力内容を確認してください。",
        detail: cause instanceof Error ? cause.message : String(cause),
        tone: "danger",
      });
    } finally {
      setBusy(false);
    }
  };

  return createPortal(
    <div className="fixed inset-0 z-[120] flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm" onMouseDown={(event) => { if (event.currentTarget === event.target && !busy) onClose(); }}>
      <div className="w-full max-w-2xl max-h-[calc(100dvh-32px)] flex flex-col overflow-hidden rounded-2xl border border-border bg-card shadow-2xl" role="dialog" aria-modal="true" aria-label="制作情報を整理">
        <div className="h-14 px-4 border-b border-border flex items-center justify-between gap-3 flex-shrink-0">
          <div><h2 className="text-sm font-semibold">制作情報を整理</h2><p className="text-[10px] text-muted-foreground">{assetIds.length}枚を、ファイルではなく制作単位として整理します</p></div>
          <button className="ui-icon-button text-lg" onClick={onClose} disabled={busy} aria-label="閉じる">×</button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto p-4 space-y-4">
          <div className="grid grid-cols-3 gap-2">
            <ModeButton active={mode === "group"} title="生成グループ" description="同じ生成意図の画像群" onClick={() => setMode("group")} />
            <ModeButton active={mode === "work"} title="作品" description="人が認識する作品単位" onClick={() => setMode("work")} />
            <ModeButton active={mode === "relation"} title="派生関係" description="元画像 → 派生画像" onClick={() => setMode("relation")} disabled={assetIds.length !== 2} />
          </div>

          <div className="rounded-xl border border-border bg-muted/10 p-3">
            <p className="ui-label mb-2">対象画像</p>
            <div className="space-y-1.5">{assetIds.map((id, index) => {
              const asset = assets.find((item) => item.id === id);
              return <div key={id} className="flex items-center gap-2 text-[11px]"><span className="w-5 text-right text-muted-foreground">{index + 1}</span><span className="min-w-0 flex-1 truncate">{asset?.fileName ?? `Asset #${id}`}</span></div>;
            })}</div>
          </div>

          {mode === "work" && <div className="space-y-3"><label className="block space-y-1.5"><span className="ui-label">作品名</span><input className="ui-input w-full" value={name} onChange={(event) => setName(event.target.value)} placeholder="例: 白上フブキ - 夏祭り" autoFocus /></label><label className="block space-y-1.5"><span className="ui-label">説明 <span className="font-normal text-muted-foreground">任意</span></span><textarea className="ui-input w-full min-h-24 resize-y" value={description} onChange={(event) => setDescription(event.target.value)} placeholder="この作品のテーマ、用途、メモなど" /></label></div>}

          {mode === "group" && <div className="space-y-3">
            <label className="block space-y-1.5"><span className="ui-label">グループ名</span><input className="ui-input w-full" value={name} onChange={(event) => setName(event.target.value)} placeholder="例: 浴衣フブキ seed variations" autoFocus /></label>
            <label className="block space-y-1.5"><span className="ui-label">共通プロンプト</span><textarea className="ui-input w-full min-h-28 resize-y font-mono text-[11px]" value={prompt} onChange={(event) => setPrompt(event.target.value)} placeholder="positive prompt" /></label>
            <label className="block space-y-1.5"><span className="ui-label">ネガティブプロンプト</span><textarea className="ui-input w-full min-h-20 resize-y font-mono text-[11px]" value={negativePrompt} onChange={(event) => setNegativePrompt(event.target.value)} placeholder="negative prompt" /></label>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3"><label className="space-y-1.5"><span className="ui-label">モデル</span><input className="ui-input w-full" value={modelName} onChange={(event) => setModelName(event.target.value)} placeholder="checkpoint / model" /></label><label className="space-y-1.5"><span className="ui-label">Sampler</span><input className="ui-input w-full" value={sampler} onChange={(event) => setSampler(event.target.value)} /></label><label className="space-y-1.5"><span className="ui-label">Scheduler</span><input className="ui-input w-full" value={scheduler} onChange={(event) => setScheduler(event.target.value)} /></label><div className="grid grid-cols-2 gap-2"><label className="space-y-1.5"><span className="ui-label">Steps</span><input type="number" min={0} className="ui-input w-full" value={steps || ""} onChange={(event) => setSteps(Number(event.target.value))} /></label><label className="space-y-1.5"><span className="ui-label">CFG</span><input type="number" min={0} step="0.1" className="ui-input w-full" value={cfgScale || ""} onChange={(event) => setCfgScale(Number(event.target.value))} /></label></div></div>
            <label className="block space-y-1.5"><span className="ui-label">生成メモ <span className="font-normal text-muted-foreground">任意</span></span><textarea className="ui-input w-full min-h-20 resize-y" value={notes} onChange={(event) => setNotes(event.target.value)} placeholder="LoRA、調整点、意図など" /></label>
          </div>}

          {mode === "relation" && relationAssets && <div className="space-y-3">
            <div className="rounded-xl border border-primary/20 bg-primary/5 p-3"><p className="text-[10px] text-muted-foreground mb-2">方向</p><div className="flex items-center gap-2"><div className="min-w-0 flex-1 rounded-lg bg-background/70 p-2 text-center text-[11px] truncate">{assets.find((item) => item.id === relationAssets[0])?.fileName ?? `Asset #${relationAssets[0]}`}</div><span className="text-primary font-bold">→</span><div className="min-w-0 flex-1 rounded-lg bg-background/70 p-2 text-center text-[11px] truncate">{assets.find((item) => item.id === relationAssets[1])?.fileName ?? `Asset #${relationAssets[1]}`}</div><button type="button" className="ui-secondary-button" onClick={() => setParentFirst((value) => !value)}>入替</button></div></div>
            <label className="block space-y-1.5"><span className="ui-label">関係</span><select className="ui-input w-full" value={relationType} onChange={(event) => setRelationType(event.target.value)}>{RELATION_OPTIONS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
            <label className="block space-y-1.5"><span className="ui-label">メモ <span className="font-normal text-muted-foreground">任意</span></span><textarea className="ui-input w-full min-h-20 resize-y" value={relationNote} onChange={(event) => setRelationNote(event.target.value)} placeholder="例: 顔をinpaintして表情だけ修正" /></label>
          </div>}
        </div>

        <div className="px-4 py-3 border-t border-border flex justify-end gap-2 flex-shrink-0"><button type="button" className="ui-secondary-button" onClick={onClose} disabled={busy}>キャンセル</button><button type="button" className="ui-primary-button min-w-24" onClick={() => void save()} disabled={busy || (mode !== "relation" && !name.trim())}>{busy ? "保存中…" : "保存"}</button></div>
      </div>
    </div>, document.body
  );
}

function ModeButton({ active, title, description, onClick, disabled = false }: { active: boolean; title: string; description: string; onClick: () => void; disabled?: boolean }) {
  return <button type="button" disabled={disabled} onClick={onClick} className={`rounded-xl border p-3 text-left transition-colors disabled:opacity-35 ${active ? "border-primary/50 bg-primary/10" : "border-border bg-muted/10 hover:bg-accent/40"}`}><p className="text-xs font-semibold">{title}</p><p className="mt-1 text-[10px] leading-relaxed text-muted-foreground">{description}</p></button>;
}
