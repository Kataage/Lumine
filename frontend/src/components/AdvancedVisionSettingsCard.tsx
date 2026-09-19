import { useCallback, useEffect, useState } from "react";
import {
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
import { useAppDialog } from "./AppDialogProvider";

function Progress({ downloaded, total, label }: { downloaded: number; total: number; label: string }) {
  const percent = total > 0 ? Math.min(100, (downloaded / total) * 100) : 0;
  return (
    <div className="mt-3 rounded-xl border border-border/70 bg-background/45 p-3">
      <div className="mb-2 flex items-center justify-between gap-3 text-[11px]">
        <span className="font-medium">{label}</span>
        <span className="tabular-nums text-muted-foreground">{formatFileSize(downloaded)} / {formatFileSize(total)}</span>
      </div>
      <div className="h-1.5 overflow-hidden rounded-full bg-muted">
        <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${percent}%` }} />
      </div>
    </div>
  );
}

export function AdvancedVisionSettingsCard({
  enabled,
  onEnable,
  onStatusChange,
}: {
  enabled: boolean;
  onEnable: () => Promise<void>;
  onStatusChange?: (status: AdvancedVisionStatusInfo) => void;
}) {
  const dialog = useAppDialog();
  const [status, setStatus] = useState<AdvancedVisionStatusInfo | null>(null);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<{ label: string; downloaded: number; total: number } | null>(null);

  const refresh = useCallback(async () => {
    try {
      const next = await getAdvancedVisionStatus();
      setStatus(next);
      onStatusChange?.(next);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [onStatusChange]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    const offModelDownload = EventsOn("ai:model-download", (raw: unknown) => {
      const value = raw as { modelId?: string; bytesDownloaded?: number; bytesTotal?: number; done?: boolean };
      const modelID = String(value.modelId ?? "");
      if (!modelID.includes("qwen3-vl") && !modelID.includes("minicpm-v")) return;
      setProgress({
        label: "モデルをダウンロード",
        downloaded: Math.max(0, Number(value.bytesDownloaded ?? 0)),
        total: Math.max(0, Number(value.bytesTotal ?? 0)),
      });
      if (value.done) {
        setProgress(null);
        void refresh();
      }
    });
    const offRuntimeDownload = EventsOn("ai:runtime-download", (raw: unknown) => {
      const value = raw as { bytesDownloaded?: number; bytesTotal?: number; done?: boolean };
      setProgress({
        label: "llama.cpp runtimeをダウンロード",
        downloaded: Math.max(0, Number(value.bytesDownloaded ?? 0)),
        total: Math.max(0, Number(value.bytesTotal ?? 0)),
      });
      if (value.done) {
        setProgress(null);
        void refresh();
      }
    });
    return () => {
      offModelDownload();
      offRuntimeDownload();
    };
  }, [refresh]);

  const setup = async (modelId: string) => {
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
      const candidate = current.models.find((model) => model.id === modelId);
      if (!candidate?.installed) await installAdvancedVisionModel(modelId);
      await loadAdvancedVisionModel(modelId);
      await refresh();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy("");
    }
  };

  const removeModel = async (modelId: string, displayName: string) => {
    if (busy) return;
    const approved = await dialog.confirm({
      title: "Advanced Visionモデルを削除しますか？",
      description: `${displayName} をローカルから削除します。`,
      confirmLabel: "モデルを削除",
      tone: "danger",
    });
    if (!approved) return;
    setBusy(`remove:${modelId}`);
    setError(null);
    try {
      await removeAdvancedVisionModel(modelId);
      await refresh();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy("");
    }
  };

  if (!status) {
    return <p className="pt-4 text-[11px] text-muted-foreground">Advanced Visionの状態を確認しています…</p>;
  }

  const ready = status.runtime.state === "ready" || status.runtime.state === "running";

  return (
    <div className="pt-4">
      <div className="mb-3 flex flex-wrap items-center gap-2 text-[11px] text-muted-foreground">
        <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 ${
          status.llamaRuntime.installed ? "bg-emerald-500/[0.08] text-emerald-200" : "bg-muted/55"
        }`}>
          <span className={`h-1.5 w-1.5 rounded-full ${status.llamaRuntime.installed ? "bg-emerald-400" : "bg-muted-foreground/40"}`} />
          llama.cpp {status.llamaRuntime.installed ? "導入済み" : "セットアップ時に導入"}
        </span>
        <span>{formatFileSize(status.llamaRuntime.sizeBytes)}</span>
        {status.llamaRuntime.installed && (
          <button
            type="button"
            className="ml-auto text-[11px] text-muted-foreground underline decoration-border underline-offset-4 hover:text-foreground"
            disabled={!!busy}
            onClick={() => {
              void (async () => {
                const approved = await dialog.confirm({
                  title: "共有runtimeを削除しますか？",
                  description: "llama.cpp runtimeを削除します。Lightweight Vision / Advanced Vision / Prompt Engineの実行中runtimeは停止します。",
                  confirmLabel: "runtimeを削除",
                  tone: "danger",
                });
                if (!approved) return;
                setBusy("runtime-remove");
                try {
                  await removeAdvancedVisionRuntime();
                  await refresh();
                } catch (cause) {
                  setError(cause instanceof Error ? cause.message : String(cause));
                } finally {
                  setBusy("");
                }
              })();
            }}
          >
            runtimeを管理
          </button>
        )}
      </div>

      {progress && progress.total > 0 && <Progress {...progress} />}

      <div className="space-y-2">
        {status.models.map((model) => {
          const active = status.activeModelId === model.id && ready;
          return (
            <div
              key={model.id}
              className={`grid gap-3 rounded-xl border px-3.5 py-3 sm:grid-cols-[1fr_auto] sm:items-center ${
                active ? "border-primary/25 bg-primary/[0.05]" : "border-border/70 bg-background/25"
              }`}
            >
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-[12px] font-semibold">{model.displayName}</p>
                  {active && <span className="rounded-full bg-emerald-500/10 px-2 py-0.5 text-[10px] font-medium text-emerald-300">使用中</span>}
                </div>
                <p className="mt-1 text-[11px] text-muted-foreground">
                  {formatFileSize(model.sizeBytes)} · {model.license} · {model.installed ? "インストール済み" : "未インストール"}
                </p>
              </div>
              <div className="flex items-center gap-2 sm:justify-end">
                {!active && (
                  <button type="button" className="ui-primary-button min-w-[126px]" disabled={!!busy} onClick={() => void setup(model.id)}>
                    {busy === `setup:${model.id}` ? "準備しています…" : model.installed ? "このモデルを使用" : "セットアップ"}
                  </button>
                )}
                {model.installed && (
                  <button
                    type="button"
                    className="ui-secondary-button"
                    disabled={!!busy}
                    onClick={() => void removeModel(model.id, model.displayName)}
                  >
                    削除
                  </button>
                )}
              </div>
            </div>
          );
        })}
      </div>

      {!enabled && (
        <p className="mt-3 text-[11px] leading-relaxed text-muted-foreground">
          セットアップを開始すると、ローカルAI全体とAdvanced Visionを自動で有効にします。
        </p>
      )}
      {status.runtime.error && <p className="mt-3 rounded-xl bg-destructive/[0.08] px-3 py-2.5 text-[11px] leading-relaxed text-red-200">{status.runtime.error}</p>}
      {error && <p className="mt-3 rounded-xl bg-destructive/[0.08] px-3 py-2.5 text-[11px] leading-relaxed text-red-200">{error}</p>}
    </div>
  );
}
