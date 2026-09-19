import { useCallback, useEffect, useState } from "react";
import {
  EventsOff,
  EventsOn,
  getAdvancedVisionStatus,
  installAdvancedVisionModel,
  installAdvancedVisionRuntime,
  loadAdvancedVisionModel,
  removeAdvancedVisionModel,
  removeAdvancedVisionRuntime,
  type AdvancedVisionStatusInfo,
} from "../api/client";
import { formatFileSize } from "../utils/format";

export function AdvancedVisionSettingsCard({
  enabled,
  onEnable,
}: {
  enabled: boolean;
  onEnable: () => Promise<void>;
}) {
  const [status, setStatus] = useState<AdvancedVisionStatusInfo | null>(null);
  const [busy, setBusy] = useState<string>("");
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<{ label: string; downloaded: number; total: number } | null>(null);

  const refresh = useCallback(async () => {
    try {
      setStatus(await getAdvancedVisionStatus());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    EventsOn("ai:model-download", (raw: unknown) => {
      const value = raw as {
        modelId?: string;
        bytesDownloaded?: number;
        bytesTotal?: number;
        done?: boolean;
      };
      const modelId = String(value.modelId ?? "");
      if (!modelId.includes("qwen3-vl") && !modelId.includes("minicpm-v")) return;
      setProgress({
        label: modelId,
        downloaded: Math.max(0, Number(value.bytesDownloaded ?? 0)),
        total: Math.max(0, Number(value.bytesTotal ?? 0)),
      });
      if (value.done) {
        setProgress(null);
        void refresh();
      }
    });
    EventsOn("ai:runtime-download", (raw: unknown) => {
      const value = raw as { bytesDownloaded?: number; bytesTotal?: number; done?: boolean };
      setProgress({
        label: "llama.cpp runtime",
        downloaded: Math.max(0, Number(value.bytesDownloaded ?? 0)),
        total: Math.max(0, Number(value.bytesTotal ?? 0)),
      });
      if (value.done) {
        setProgress(null);
        void refresh();
      }
    });
    return () => {
      EventsOff("ai:model-download");
      EventsOff("ai:runtime-download");
    };
  }, [refresh]);

  const run = async (key: string, action: () => Promise<void>) => {
    if (busy) return;
    setBusy(key);
    setError(null);
    try {
      await action();
      await refresh();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy("");
    }
  };
  const setupModel = async (modelId: string) => {
    if (busy) return;
    setBusy(`setup:${modelId}`);
    setError(null);
    try {
      await onEnable();
      let current = await getAdvancedVisionStatus();
      if (!current.llamaRuntime.installed) {
        await installAdvancedVisionRuntime();
        current = await getAdvancedVisionStatus();
      }
      const model = current.models.find((candidate) => candidate.id === modelId);
      if (!model?.installed) {
        await installAdvancedVisionModel(modelId);
      }
      await loadAdvancedVisionModel(modelId);
      await refresh();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy("");
    }
  };


  if (!status) {
    return <p className="mt-2 border-t border-border/60 pt-2 text-[9px] text-muted-foreground">Advanced Visionの状態を読み込んでいます…</p>;
  }

  const runtimeState = status.runtime.state;
  const runtimeLabel =
    runtimeState === "ready" ? "利用可能" :
    runtimeState === "running" ? "処理中" :
    runtimeState === "error" ? "エラー" :
    runtimeState === "disabled" ? "機能OFF" : "セットアップが必要";

  return (
    <div className="mt-2 border-t border-border/60 pt-2 space-y-2">
      <div className="flex items-center justify-between gap-2 text-[9px]">
        <span className="text-muted-foreground">Advanced runtime</span>
        <span className="rounded-full border border-border px-2 py-0.5 text-muted-foreground">{runtimeLabel}</span>
      </div>

      <div className="rounded-md border border-border/60 p-2 text-[9px]">
        <div className="flex items-start justify-between gap-2">
          <div>
            <p className="font-medium text-foreground">共有 llama.cpp runtime</p>
            <p className="mt-0.5 text-muted-foreground">{formatFileSize(status.llamaRuntime.sizeBytes)} · {status.llamaRuntime.installed ? "導入済み" : "未導入"}</p>
            <p className="mt-0.5 text-muted-foreground/70">Lightweight Visionと共有します。</p>
          </div>
          {status.llamaRuntime.installed ? (
            <button
              type="button"
              className="ui-secondary-button"
              disabled={!!busy}
              onClick={() => {
                if (!window.confirm("共有llama.cpp runtimeを削除します。Lightweight Vision / Prompt Engineも停止します。続行しますか？")) return;
                void run("runtime-remove", removeAdvancedVisionRuntime);
              }}
            >
              {busy === "runtime-remove" ? "削除中…" : "削除"}
            </button>
          ) : (
            <span className="rounded-full border border-amber-500/25 px-2 py-0.5 text-amber-300">セットアップ時に自動導入</span>
          )}
        </div>
      </div>

      {progress && progress.total > 0 && (
        <div className="space-y-1">
          <div className="h-1.5 overflow-hidden rounded-full bg-muted">
            <div className="h-full bg-primary transition-[width]" style={{ width: `${Math.min(100, (progress.downloaded / progress.total) * 100)}%` }} />
          </div>
          <p className="text-[9px] text-muted-foreground">
            {progress.label}: {formatFileSize(progress.downloaded)} / {formatFileSize(progress.total)}
          </p>
        </div>
      )}

      <div className="space-y-1.5">
        {status.models.map((model) => {
          const active = status.activeModelId === model.id && (runtimeState === "ready" || runtimeState === "running");
          return (
            <div key={model.id} className={`rounded-md border p-2 text-[9px] ${active ? "border-primary/50 bg-primary/5" : "border-border/60"}`}>
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <p className="font-medium text-foreground">{model.displayName}</p>
                    {active && <span className="rounded-full border border-primary/40 px-1.5 py-0.5 text-primary">使用中</span>}
                  </div>
                  <p className="mt-0.5 text-muted-foreground">{formatFileSize(model.sizeBytes)} · {model.license} · {model.installed ? "導入済み" : "未導入"}</p>
                </div>
                <div className="flex flex-wrap justify-end gap-1">
                  {!active && (
                    <button
                      type="button"
                      className="ui-primary-button"
                      disabled={!!busy}
                      onClick={() => void setupModel(model.id)}
                    >
                      {busy === `setup:${model.id}`
                        ? "セットアップ中…"
                        : model.installed
                          ? "このモデルを使用"
                          : "導入して使用"}
                    </button>
                  )}
                  {model.installed && (
                    <button
                      type="button"
                      className="ui-secondary-button"
                      disabled={!!busy}
                      onClick={() => {
                        if (!window.confirm(`${model.displayName} をローカルから削除しますか？`)) return;
                        void run(`remove:${model.id}`, () => removeAdvancedVisionModel(model.id));
                      }}
                    >
                      {busy === `remove:${model.id}` ? "削除中…" : "削除"}
                    </button>
                  )}
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {!enabled && (
        <p className="text-[9px] text-muted-foreground">「導入して使用」を押すとAI全体とAdvanced Visionを自動でONにします。</p>
      )}
      {status.runtime.error && <p className="text-[9px] text-destructive break-all">{status.runtime.error}</p>}
      {error && <p className="text-[9px] text-destructive break-all">{error}</p>}
      <p className="text-[9px] leading-relaxed text-muted-foreground">
        モデル導入・runtime導入は明示操作のみです。Advanced Visionはscan時に自動解析しません。
      </p>
    </div>
  );
}
