import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useApp } from "../App";
import {
  buildImagePrompt,
  createPromptProjectFromImage,
  getAssetGenerationMetadata,
  listModelProfiles,
  type ImagePromptResult,
} from "../api/client";
import { useAppDialog } from "./AppDialogProvider";

function sourceStateLabel(state: string): string {
  switch (state) {
    case "ready": return "利用";
    case "unavailable": return "なし";
    case "skipped": return "省略";
    default: return state;
  }
}

export function ImagePromptAssetPanel({ assetId }: { assetId: number }) {
  const { setState } = useApp();
  const dialog = useAppDialog();
  const { data: profiles = [] } = useQuery({
    queryKey: ["modelProfiles"],
    queryFn: listModelProfiles,
    staleTime: 60_000,
  });
  const { data: metadata, refetch: refetchMetadata } = useQuery({
    queryKey: ["assetGenerationMetadata", assetId],
    queryFn: () => getAssetGenerationMetadata(assetId),
    enabled: assetId > 0,
    staleTime: 60_000,
  });
  const [targetProfileId, setTargetProfileId] = useState("");
  const [instruction, setInstruction] = useState("");
  const [useAdvancedVision, setUseAdvancedVision] = useState(false);
  const [result, setResult] = useState<ImagePromptResult | null>(null);
  const [busy, setBusy] = useState<"preview" | "project" | null>(null);

  useEffect(() => {
    setTargetProfileId("");
    setResult(null);
  }, [assetId]);

  const effectiveProfileLabel = targetProfileId
    ? profiles.find((profile) => profile.id === targetProfileId)?.name ?? targetProfileId
    : metadata?.suggestedProfileId
      ? `Auto: ${profiles.find((profile) => profile.id === metadata.suggestedProfileId)?.name ?? metadata.suggestedProfileId}`
      : "Auto: Illustrious / ILXL fallback";

  const request = () => ({
    assetId,
    targetProfileId,
    instruction: instruction.trim(),
    useAdvancedVision,
  });

  const preview = async () => {
    if (busy) return;
    setBusy("preview");
    try {
      setResult(await buildImagePrompt(request()));
    } catch (error) {
      await dialog.notify({
        title: "Image → Promptに失敗しました",
        description: "保存済みの解析結果または現在利用可能なAIを確認してください。",
        detail: error instanceof Error ? error.message : String(error),
        tone: "danger",
      });
    } finally {
      setBusy(null);
    }
  };

  const createProject = async () => {
    if (busy) return;
    setBusy("project");
    try {
      const created = await createPromptProjectFromImage(request());
      setResult(created.result);
      setState((current) => ({
        ...current,
        sidebarView: "prompt",
        selectedPromptProjectId: created.project.id,
        detailOpen: false,
      }));
    } catch (error) {
      await dialog.notify({
        title: "Prompt Projectを作成できませんでした",
        description: "Image → Promptの編集内容はまだ保存されていません。",
        detail: error instanceof Error ? error.message : String(error),
        tone: "danger",
      });
    } finally {
      setBusy(null);
    }
  };

  return (
    <section className="rounded-xl border border-border bg-muted/10 p-3">
      <div className="mb-2.5">
        <h4 className="text-[11px] font-semibold">Image → Prompt</h4>
        <p className="mt-0.5 text-[10px] leading-relaxed text-muted-foreground">
          保存済みタグ・Lightweight Vision・任意のAdvanced Visionを統合し、選択Profile向けPromptへ変換します。
          Prompt Engineが使えない場合も利用可能な情報だけでfallbackします。
        </p>
      </div>

      <div className="space-y-2.5">
        <div>
          <label className="ui-label mb-1">Target Model Profile</label>
          <select className="ui-input w-full" value={targetProfileId} onChange={(event) => setTargetProfileId(event.target.value)}>
            <option value="">{effectiveProfileLabel}</option>
            {profiles.map((profile) => (
              <option key={profile.id} value={profile.id}>{profile.name}{profile.builtIn ? "" : " (Custom)"}</option>
            ))}
          </select>
        </div>

        {metadata?.present && (
          <div className="rounded-lg border border-primary/20 bg-primary/5 p-2 text-[10px] text-muted-foreground">
            <div className="flex items-center justify-between gap-2">
              <span className="font-semibold text-foreground">埋め込み生成metadataを検出</span>
              <button type="button" className="ui-mini-button" onClick={() => void refetchMetadata()}>再読込</button>
            </div>
            <div className="mt-1.5 flex flex-wrap gap-1">
              {metadata.checkpoint && <span className="rounded border border-border px-1.5 py-0.5">Checkpoint: {metadata.checkpoint}</span>}
              {metadata.sampler && <span className="rounded border border-border px-1.5 py-0.5">{metadata.sampler}</span>}
              {metadata.steps > 0 && <span className="rounded border border-border px-1.5 py-0.5">{metadata.steps} steps</span>}
              {metadata.cfg > 0 && <span className="rounded border border-border px-1.5 py-0.5">CFG {metadata.cfg}</span>}
              {metadata.width > 0 && metadata.height > 0 && <span className="rounded border border-border px-1.5 py-0.5">{metadata.width}×{metadata.height}</span>}
            </div>
            {metadata.loras.length > 0 && (
              <p className="mt-1.5">
                LoRA: {metadata.loras.map((lora) => `${lora.name} @ ${lora.weight}${lora.triggerWords.length ? ` [${lora.triggerWords.join(", ")}]` : ""}`).join(" / ")}
              </p>
            )}
            {(metadata.positive || metadata.negative) && (
              <p className="mt-1 opacity-80">元Promptは「Original metadata」Variantとして保存されます。</p>
            )}
          </div>
        )}

        <div>
          <label className="ui-label mb-1">追加指示</label>
          <textarea
            className="ui-input min-h-16 w-full resize-y"
            value={instruction}
            onChange={(event) => setInstruction(event.target.value)}
            placeholder="例: 構図と衣装を優先して再現。背景情報も残す"
          />
        </div>

        <label className="flex items-start gap-2 rounded-lg border border-border bg-background/30 p-2 text-[10px] text-muted-foreground">
          <input
            type="checkbox"
            className="mt-0.5"
            checked={useAdvancedVision}
            onChange={(event) => setUseAdvancedVision(event.target.checked)}
          />
          <span>
            Advanced Visionで追加解析する
            <span className="mt-0.5 block opacity-70">OFFでも保存済み結果やLightweight Visionだけで動作します。</span>
          </span>
        </label>

        <div className="grid grid-cols-2 gap-2">
          <button type="button" className="ui-secondary-button" disabled={busy !== null} onClick={() => void preview()}>
            {busy === "preview" ? "生成中…" : "Preview"}
          </button>
          <button type="button" className="ui-primary-button" disabled={busy !== null} onClick={() => void createProject()}>
            {busy === "project" ? "作成中…" : "Prompt Project作成"}
          </button>
        </div>

        {result && (
          <div className="space-y-2 rounded-xl border border-border bg-background/35 p-2.5">
            <div className="flex flex-wrap gap-1">
              {result.sources.map((source, index) => (
                <span
                  key={`${source.kind}-${index}`}
                  className={`rounded-md border px-1.5 py-0.5 text-[9px] ${
                    source.state === "ready"
                      ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-200"
                      : "border-border text-muted-foreground"
                  }`}
                  title={source.note || [source.engine, source.modelId, source.modelVersion].filter(Boolean).join(" / ")}
                >
                  {source.label}: {sourceStateLabel(source.state)}
                </span>
              ))}
              <span className="rounded-md border border-border px-1.5 py-0.5 text-[9px] text-muted-foreground">
                {result.promptEngineUsed ? "Prompt Engine使用" : "Local fallback"}
              </span>
            </div>

            <div>
              <p className="text-[9px] font-semibold text-muted-foreground">Positive</p>
              <p className="mt-1 max-h-28 overflow-y-auto whitespace-pre-wrap break-words text-[10px] leading-relaxed">
                {result.positive}
              </p>
            </div>
            {result.negative && (
              <div>
                <p className="text-[9px] font-semibold text-muted-foreground">Negative</p>
                <p className="mt-1 max-h-20 overflow-y-auto whitespace-pre-wrap break-words text-[10px] leading-relaxed text-muted-foreground">
                  {result.negative}
                </p>
              </div>
            )}
          </div>
        )}
      </div>
    </section>
  );
}
