import { useCallback, useEffect, useState } from "react";
import {
  EventsOff,
  EventsOn,
  getPromptEngineStatus,
  installPromptEngineModel,
  installPromptEngineRuntime,
  loadPromptEngineModel,
  removePromptEngineModel,
  type PromptEngineStatusInfo,
} from "../api/client";
import { formatFileSize } from "../utils/format";

export function PromptEngineSettingsCard({
  enabled,
  onEnable,
}: {
  enabled: boolean;
  onEnable: () => Promise<void>;
}) {
  const [status, setStatus] = useState<PromptEngineStatusInfo | null>(null);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<{ label: string; downloaded: number; total: number } | null>(null);

  const refresh = useCallback(async () => {
    try {
      setStatus(await getPromptEngineStatus());
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
      if (!modelId.includes("qwen3.5")) return;
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

  const setup = async (modelId: string) => {
    if (busy) return;
    setBusy(`setup:${modelId}`);
    setError(null);
    try {
      await onEnable();
      let current = await getPromptEngineStatus();
      if (!current.llamaRuntime.installed) {
        await installPromptEngineRuntime();
        current = await getPromptEngineStatus();
      }
      const model = current.models.find((candidate) => candidate.id === modelId);
      if (!model?.installed) {
        await installPromptEngineModel(modelId);
      }
      await loadPromptEngineModel(modelId);
      await refresh();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy("");
    }
  };

  const removeModel = async (modelId: string) => {
    if (busy) return;
    setBusy(`remove:${modelId}`);
    setError(null);
    try {
      await removePromptEngineModel(modelId);
      await refresh();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy("");
    }
  };

  if (!status) {
    return <p className="mt-2 border-t border-border/60 pt-2 text-[9px] text-muted-foreground">Prompt Engineの状態を読み込んでいます…</p>;
  }

  const ready = status.runtime.state === "ready" || status.runtime.state === "running";

  return (
    <div className="mt-2 border-t border-border/60 pt-2 space-y-2">
      <div className="grid grid-cols-2 gap-2 text-[9px]">
        <div className="rounded-md border border-border/60 p-2">
          <p className="font-medium text-foreground">共有 llama.cpp runtime</p>
          <p className="mt-0.5 text-muted-foreground">
            {status.llamaRuntime.installed ? "導入済み" : "未導入"} · {formatFileSize(status.llamaRuntime.sizeBytes)}
          </p>
        </div>
        <div className="rounded-md border border-border/60 p-2">
          <p className="font-medium text-foreground">実行状態</p>
          <p className="mt-0.5 text-muted-foreground">
            {ready ? "利用可能" : enabled ? "セットアップが必要" : "機能OFF"}
          </p>
        </div>
      </div>

      {progress && progress.total > 0 && (
        <div className="space-y-1">
          <div className="h-1.5 overflow-hidden rounded-full bg-muted">
            <div
              className="h-full bg-primary transition-[width]"
              style={{ width: `${Math.min(100, (progress.downloaded / progress.total) * 100)}%` }}
            />
          </div>
          <p className="text-[9px] text-muted-foreground">
            {progress.label}: {formatFileSize(progress.downloaded)} / {formatFileSize(progress.total)}
          </p>
        </div>
      )}

      <div className="space-y-1.5">
        {status.models.map((model) => {
          const active = status.activeModelId === model.id && ready;
          return (
            <div
              key={model.id}
              className={`rounded-md border p-2 text-[9px] ${active ? "border-emerald-500/35 bg-emerald-500/5" : "border-border/60"}`}
            >
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <p className="font-medium text-foreground">{model.displayName}</p>
                    {model.reference && (
                      <span className="rounded-full border border-amber-500/30 px-1.5 py-0.5 text-amber-300">reference</span>
                    )}
                    {active && (
                      <span className="rounded-full border border-emerald-500/30 px-1.5 py-0.5 text-emerald-300">使用中</span>
                    )}
                  </div>
                  <p className="mt-0.5 text-muted-foreground">
                    {formatFileSize(model.sizeBytes)} · {model.license} · {model.installed ? "導入済み" : "未導入"}
                  </p>
                </div>
                <div className="flex flex-wrap justify-end gap-1">
                  {!active && (
                    <button
                      type="button"
                      className="ui-primary-button"
                      disabled={!!busy}
                      onClick={() => void setup(model.id)}
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
                        void removeModel(model.id);
                      }}
                    >
                      削除
                    </button>
                  )}
                </div>
              </div>
            </div>
          );
        })}
      </div>

      <p className="rounded-md border border-amber-500/20 bg-amber-500/5 px-2 py-1.5 text-[9px] leading-relaxed text-muted-foreground">
        {status.selectionNote}
      </p>
      {!enabled && (
        <p className="text-[9px] text-muted-foreground">
          「導入して使用」を押すとAI全体とPrompt Engineを自動でONにします。
        </p>
      )}
      {status.runtime.error && <p className="text-[9px] text-destructive break-all">{status.runtime.error}</p>}
      {error && <p className="text-[9px] text-destructive break-all">{error}</p>}
    </div>
  );
}
