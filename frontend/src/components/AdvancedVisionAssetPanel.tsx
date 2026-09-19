import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  listAdvancedVisionRunsForAsset,
  runAdvancedVision,
  type AdvancedVisionRun,
} from "../api/client";
import { AdvancedVisionResultView } from "./AdvancedVisionResultView";

const OP_LABELS: Record<AdvancedVisionRun["operation"], string> = {
  analyze_deep: "深掘り解析",
  compare_images: "画像比較",
  reverse_prompt_support: "Reverse Prompt補助",
};

export function AdvancedVisionAssetPanel({ assetId }: { assetId: number }) {
  const [instruction, setInstruction] = useState("");
  const [busy, setBusy] = useState<AdvancedVisionRun["operation"] | "">("");
  const [error, setError] = useState<string | null>(null);
  const { data: runs = [], refetch } = useQuery({
    queryKey: ["advancedVisionRuns", assetId],
    queryFn: () => listAdvancedVisionRunsForAsset(assetId, 20),
    enabled: assetId > 0,
    staleTime: 0,
  });

  const execute = async (operation: "analyze_deep" | "reverse_prompt_support") => {
    if (busy || assetId <= 0) return;
    setBusy(operation);
    setError(null);
    try {
      await runAdvancedVision(operation, [assetId], instruction);
      await refetch();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
      await refetch();
    } finally {
      setBusy("");
    }
  };

  return (
    <div className="space-y-3">
      <div className="rounded-xl border border-border bg-muted/20 p-3 space-y-2">
        <div>
          <p className="text-[11px] font-medium">オンデマンド解析</p>
          <p className="mt-0.5 text-[9px] leading-relaxed text-muted-foreground">
            Advanced Visionは自動実行されません。必要な画像だけ明示的に解析します。
          </p>
        </div>
        <textarea
          value={instruction}
          onChange={(event) => setInstruction(event.target.value)}
          placeholder="任意の追加指示（例: 構図とキャラクターの動作を特に詳しく）"
          className="ui-input min-h-20 w-full resize-y text-[10px]"
          maxLength={4000}
        />
        <div className="flex flex-wrap gap-1.5">
          <button
            type="button"
            className="ui-primary-button"
            disabled={!!busy}
            onClick={() => void execute("analyze_deep")}
          >
            {busy === "analyze_deep" ? "解析中…" : "深掘り解析"}
          </button>
          <button
            type="button"
            className="ui-secondary-button"
            disabled={!!busy}
            onClick={() => void execute("reverse_prompt_support")}
          >
            {busy === "reverse_prompt_support" ? "解析中…" : "Reverse Prompt補助"}
          </button>
        </div>
        {error && <p className="text-[9px] text-destructive break-all">{error}</p>}
      </div>

      <div className="space-y-2">
        <p className="text-[10px] font-medium text-muted-foreground">解析履歴</p>
        {runs.length === 0 ? (
          <p className="text-[10px] text-muted-foreground">Advanced Visionの実行履歴はまだありません。</p>
        ) : runs.map((run) => (
          <div key={run.id} className="rounded-xl border border-border bg-background/35 p-3 space-y-2">
            <div className="flex items-start justify-between gap-2">
              <div>
                <p className="text-[10px] font-medium">{OP_LABELS[run.operation] ?? run.operation}</p>
                <p className="text-[9px] text-muted-foreground break-all">{run.modelId} · {run.modelVersion}</p>
              </div>
              <span className="text-[9px] text-muted-foreground">
                {run.completedAt ? new Date(run.completedAt).toLocaleString("ja-JP") : new Date(run.createdAt).toLocaleString("ja-JP")}
              </span>
            </div>
            {run.instruction && <p className="text-[9px] text-muted-foreground">指示: {run.instruction}</p>}
            {run.state === "failed" && <p className="text-[9px] text-destructive break-all">{run.errorMessage || "解析に失敗しました。"}</p>}
            {run.result && <AdvancedVisionResultView result={run.result} />}
          </div>
        ))}
      </div>
    </div>
  );
}
