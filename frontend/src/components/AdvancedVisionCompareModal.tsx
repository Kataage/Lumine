import { useState } from "react";
import { runAdvancedVision, type AdvancedVisionRun } from "../api/client";
import { AdvancedVisionResultView } from "./AdvancedVisionResultView";

export function AdvancedVisionCompareModal({
  assetIds,
  onClose,
}: {
  assetIds: number[];
  onClose: () => void;
}) {
  const [instruction, setInstruction] = useState("");
  const [busy, setBusy] = useState(false);
  const [run, setRun] = useState<AdvancedVisionRun | null>(null);
  const [error, setError] = useState<string | null>(null);

  const compare = async () => {
    if (busy || assetIds.length < 2 || assetIds.length > 8) return;
    setBusy(true);
    setError(null);
    try {
      setRun(await runAdvancedVision("compare_images", assetIds, instruction));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[80] flex items-center justify-center bg-black/70 p-4 backdrop-blur-sm" onMouseDown={onClose}>
      <div
        className="flex max-h-[88vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl border border-border bg-card shadow-2xl"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3 border-b border-border px-4 py-3">
          <div>
            <h2 className="text-sm font-semibold">Advanced Vision 画像比較</h2>
            <p className="mt-0.5 text-[10px] text-muted-foreground">{assetIds.length}枚を同時に比較します。最大8枚です。</p>
          </div>
          <button type="button" className="ui-secondary-button" onClick={onClose}>閉じる</button>
        </div>

        <div className="flex-1 overflow-auto p-4 space-y-3">
          {assetIds.length > 8 && (
            <p className="rounded-lg border border-destructive/30 bg-destructive/10 p-2 text-[10px] text-destructive">
              Advanced Visionの比較は一度に8枚までです。選択を8枚以下にしてください。
            </p>
          )}
          <textarea
            value={instruction}
            onChange={(event) => setInstruction(event.target.value)}
            placeholder="任意の比較指示（例: キャラクターは固定したまま、衣装・構図・背景の差を詳しく）"
            maxLength={4000}
            className="ui-input min-h-24 w-full resize-y text-[11px]"
          />
          <button
            type="button"
            className="ui-primary-button"
            disabled={busy || assetIds.length < 2 || assetIds.length > 8}
            onClick={() => void compare()}
          >
            {busy ? "比較解析中…" : "この画像を比較"}
          </button>
          {error && <p className="text-[10px] text-destructive break-all">{error}</p>}

          {run && (
            <div className="rounded-xl border border-border bg-muted/20 p-3 space-y-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <p className="text-[10px] font-medium">比較結果</p>
                  <p className="text-[9px] text-muted-foreground break-all">{run.modelId} · {run.modelVersion}</p>
                </div>
                <p className="text-[9px] text-muted-foreground">run #{run.id}</p>
              </div>
              {run.result && <AdvancedVisionResultView result={run.result} />}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
