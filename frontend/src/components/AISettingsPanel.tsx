import { useCallback, useEffect, useState } from "react";
import {
  EventsOff,
  EventsOn,
  getAISettings,
  getDefaultSemanticModelInfo,
  getDefaultLightweightVisionModelInfo,
  enqueueLightweightVisionBackfill,
  installDefaultSemanticModel,
  installDefaultLightweightVisionModel,
  installLightweightVisionRuntime,
  loadDefaultSemanticModel,
  loadDefaultLightweightVisionModel,
  removeDefaultLightweightVisionModel,
  removeLightweightVisionRuntime,
  setAISettings,
  type LightweightVisionModelInfo,
  type SemanticModelInfo,
} from "../api/client";
import { formatFileSize } from "../utils/format";
import { AdvancedVisionSettingsCard } from "./AdvancedVisionSettingsCard";
import {
  DEFAULT_AI_SETTINGS,
  getInitialAIEngineStatus,
  type AIEngineStatus,
  type AIModelFeatureKey,
  type AISettings,
} from "../utils/aiSettings";

const MODEL_FEATURES: Array<{
  key: AIModelFeatureKey;
  label: string;
  description: string;
}> = [
  {
    key: "semanticSearch",
    label: "Semantic Search / Embedding",
    description: "画像の意味検索と類似画像検索。",
  },
  {
    key: "tagger",
    label: "Anime / Danbooru Tagger",
    description: "タグ・キャラクター・rating候補の解析。",
  },
  {
    key: "lightweightVision",
    label: "Lightweight Vision",
    description: "軽量な画像説明・構図・背景解析。",
  },
  {
    key: "advancedVision",
    label: "Advanced Vision",
    description: "必要な時だけ使う高精度な画像理解。",
  },
  {
    key: "promptEngine",
    label: "Prompt Engine / Prompt Studio AI",
    description: "画像生成Promptの生成・改善・変換。",
  },
];

const STATUS_LABELS: Record<AIEngineStatus, string> = {
  disabled: "無効",
  model_not_installed: "モデル未導入",
  ready: "準備完了",
  running: "処理中",
  error: "エラー",
};

function Toggle({
  checked,
  disabled = false,
  label,
  onChange,
}: {
  checked: boolean;
  disabled?: boolean;
  label: string;
  onChange: (checked: boolean) => void;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full border transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
        checked ? "border-primary/50 bg-primary" : "border-border bg-muted"
      }`}
    >
      <span
        className={`h-4 w-4 rounded-full bg-white shadow-sm transition-transform ${
          checked ? "translate-x-5" : "translate-x-1"
        }`}
      />
    </button>
  );
}

function DownloadBar({ downloaded, total, label }: { downloaded: number; total: number; label: string }) {
  return (
    <div className="space-y-1">
      <div className="h-1.5 overflow-hidden rounded-full bg-muted">
        <div
          className="h-full bg-primary transition-[width]"
          style={{ width: `${Math.min(100, total > 0 ? (downloaded / total) * 100 : 0)}%` }}
        />
      </div>
      <p className="text-[9px] text-muted-foreground">
        {label}: {formatFileSize(downloaded)} / {formatFileSize(total)}
      </p>
    </div>
  );
}

function StatusBadge({ status }: { status: AIEngineStatus }) {
  return (
    <span
      className={`rounded-full border px-2 py-0.5 text-[9px] font-medium ${
        status === "disabled"
          ? "border-border text-muted-foreground"
          : status === "error"
            ? "border-destructive/40 text-destructive"
            : status === "running"
              ? "border-primary/40 text-primary"
              : "border-amber-500/35 text-amber-300"
      }`}
    >
      {STATUS_LABELS[status]}
    </span>
  );
}

export function AISettingsPanel() {
  const [settings, setSettings] = useState<AISettings>(DEFAULT_AI_SETTINGS);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [semanticModel, setSemanticModel] = useState<SemanticModelInfo | null>(null);
  const [lightweightModel, setLightweightModel] = useState<LightweightVisionModelInfo | null>(null);
  const [modelBusy, setModelBusy] = useState(false);
  const [visionBusy, setVisionBusy] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState<{ downloaded: number; total: number } | null>(null);
  const [visionModelProgress, setVisionModelProgress] = useState<{ downloaded: number; total: number } | null>(null);
  const [runtimeProgress, setRuntimeProgress] = useState<{ downloaded: number; total: number } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [nextSettings, model, visionModel] = await Promise.all([
        getAISettings(),
        getDefaultSemanticModelInfo().catch(() => null),
        getDefaultLightweightVisionModelInfo().catch(() => null),
      ]);
      setSettings(nextSettings);
      setSemanticModel(model);
      setLightweightModel(visionModel);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    EventsOn("ai:model-download", (raw: unknown) => {
      const progress = raw as {
        modelId?: string;
        bytesDownloaded?: number;
        bytesTotal?: number;
        done?: boolean;
      };
      const next = {
        downloaded: Math.max(0, Number(progress.bytesDownloaded ?? 0)),
        total: Math.max(0, Number(progress.bytesTotal ?? 0)),
      };
      if (String(progress.modelId ?? "").includes("smolvlm")) {
        setVisionModelProgress(next);
      } else {
        setDownloadProgress(next);
      }
      if (progress.done) {
        void Promise.all([
          getDefaultSemanticModelInfo().then(setSemanticModel).catch(() => undefined),
          getDefaultLightweightVisionModelInfo().then(setLightweightModel).catch(() => undefined),
        ]);
      }
    });
    EventsOn("ai:runtime-download", (raw: unknown) => {
      const progress = raw as { bytesDownloaded?: number; bytesTotal?: number; done?: boolean };
      setRuntimeProgress({
        downloaded: Math.max(0, Number(progress.bytesDownloaded ?? 0)),
        total: Math.max(0, Number(progress.bytesTotal ?? 0)),
      });
      if (progress.done) {
        void getDefaultLightweightVisionModelInfo().then(setLightweightModel).catch(() => undefined);
      }
    });
    return () => {
      EventsOff("ai:model-download");
      EventsOff("ai:runtime-download");
    };
  }, []);

  const installSemanticModel = async () => {
    if (modelBusy) return;
    setModelBusy(true);
    setError(null);
    setDownloadProgress({ downloaded: 0, total: semanticModel?.sizeBytes ?? 0 });
    try {
      await installDefaultSemanticModel();
      setSemanticModel(await getDefaultSemanticModelInfo());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setModelBusy(false);
      setDownloadProgress(null);
    }
  };

  const loadSemanticModel = async () => {
    if (modelBusy) return;
    setModelBusy(true);
    setError(null);
    try {
      await loadDefaultSemanticModel();
      setSemanticModel(await getDefaultSemanticModelInfo());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setModelBusy(false);
    }
  };

  const refreshLightweightModel = async () => {
    setLightweightModel(await getDefaultLightweightVisionModelInfo());
  };

  const installVisionRuntime = async () => {
    if (visionBusy) return;
    setVisionBusy(true);
    setError(null);
    setRuntimeProgress({ downloaded: 0, total: lightweightModel?.llamaRuntime.sizeBytes ?? 0 });
    try {
      await installLightweightVisionRuntime();
      await refreshLightweightModel();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setVisionBusy(false);
      setRuntimeProgress(null);
    }
  };

  const installVisionModel = async () => {
    if (visionBusy) return;
    setVisionBusy(true);
    setError(null);
    setVisionModelProgress({ downloaded: 0, total: lightweightModel?.sizeBytes ?? 0 });
    try {
      await installDefaultLightweightVisionModel();
      await refreshLightweightModel();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setVisionBusy(false);
      setVisionModelProgress(null);
    }
  };

  const loadVisionModel = async () => {
    if (visionBusy) return;
    setVisionBusy(true);
    setError(null);
    try {
      await loadDefaultLightweightVisionModel();
      await refreshLightweightModel();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setVisionBusy(false);
    }
  };

  const removeVisionModel = async () => {
    if (visionBusy) return;
    setVisionBusy(true);
    setError(null);
    try {
      await removeDefaultLightweightVisionModel();
      await refreshLightweightModel();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setVisionBusy(false);
    }
  };

  const removeVisionRuntime = async () => {
    if (visionBusy) return;
    setVisionBusy(true);
    setError(null);
    try {
      await removeLightweightVisionRuntime();
      await refreshLightweightModel();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setVisionBusy(false);
    }
  };

  const backfillVision = async () => {
    if (visionBusy) return;
    setVisionBusy(true);
    setError(null);
    try {
      const created = await enqueueLightweightVisionBackfill();
      window.alert(created > 0 ? `${created}件をLightweight Vision解析キューへ追加しました。` : "再解析が必要な既存画像はありません。");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setVisionBusy(false);
    }
  };

  const update = async (patch: Partial<AISettings>) => {
    const previous = settings;
    const next = { ...settings, ...patch };
    setSettings(next);
    setSaving(true);
    setError(null);
    try {
      setSettings(await setAISettings(next));
    } catch (cause) {
      setSettings(previous);
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <section className="mx-3 mb-3 rounded-xl border border-border bg-muted/10 p-3">
        <p className="text-[11px] font-semibold">ローカルAI</p>
        <p className="mt-1 text-[10px] text-muted-foreground">設定を読み込んでいます…</p>
      </section>
    );
  }

  return (
    <section className="mx-3 mb-3 rounded-xl border border-border bg-muted/10 p-3 space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-[11px] font-semibold">ローカルAI</p>
          <p className="mt-0.5 text-[9px] leading-relaxed text-muted-foreground">
            すべてローカルで動作します。ONにしてもモデルは自動ダウンロードされません。
          </p>
        </div>
        <Toggle
          checked={settings.enabled}
          disabled={saving}
          label="AI機能全体"
          onChange={(enabled) => void update({ enabled })}
        />
      </div>

      <div className="space-y-1.5">
        {MODEL_FEATURES.map((feature) => {
          const status = feature.key === "semanticSearch" && semanticModel
            ? semanticModel.runtime.state
            : feature.key === "lightweightVision" && lightweightModel
              ? lightweightModel.runtime.state
              : getInitialAIEngineStatus(settings, feature.key);
          return (
            <div key={feature.key} className="rounded-lg border border-border/70 bg-background/30 p-2.5">
              <div className="flex items-center gap-2">
                <div className="min-w-0 flex-1">
                  <div className="flex min-w-0 flex-wrap items-center gap-1.5">
                    <p className="text-[10px] font-medium">{feature.label}</p>
                    {feature.key !== "advancedVision" && <StatusBadge status={status} />}
                  </div>
                  <p className="mt-0.5 text-[9px] leading-relaxed text-muted-foreground">
                    {feature.description}
                  </p>
                </div>
                <Toggle
                  checked={settings[feature.key]}
                  disabled={saving}
                  label={feature.label}
                  onChange={(checked) => void update({ [feature.key]: checked } as Partial<AISettings>)}
                />
              </div>

              {feature.key === "advancedVision" && (
                <AdvancedVisionSettingsCard enabled={settings.enabled && settings.advancedVision} />
              )}
              {feature.key === "semanticSearch" && semanticModel && (
                <div className="mt-2 border-t border-border/60 pt-2 space-y-2">
                  <div className="flex items-center justify-between gap-2 text-[9px] text-muted-foreground">
                    <span className="truncate" title={semanticModel.displayName}>{semanticModel.displayName}</span>
                    <span className="flex-shrink-0">{formatFileSize(semanticModel.sizeBytes)}</span>
                  </div>
                  {downloadProgress && downloadProgress.total > 0 && (
                    <div className="space-y-1">
                      <div className="h-1.5 overflow-hidden rounded-full bg-muted">
                        <div
                          className="h-full bg-primary transition-[width]"
                          style={{ width: `${Math.min(100, (downloadProgress.downloaded / downloadProgress.total) * 100)}%` }}
                        />
                      </div>
                      <p className="text-[9px] text-muted-foreground">
                        {formatFileSize(downloadProgress.downloaded)} / {formatFileSize(downloadProgress.total)}
                      </p>
                    </div>
                  )}
                  {!semanticModel.installed ? (
                    <button
                      type="button"
                      className="ui-primary-button w-full justify-center"
                      disabled={modelBusy}
                      onClick={() => void installSemanticModel()}
                    >
                      {modelBusy ? "モデルを導入中…" : "SigLIP 2を導入"}
                    </button>
                  ) : status !== "ready" && status !== "running" && settings.enabled && settings.semanticSearch ? (
                    <button
                      type="button"
                      className="ui-secondary-button w-full justify-center"
                      disabled={modelBusy}
                      onClick={() => void loadSemanticModel()}
                    >
                      {modelBusy ? "モデルを読み込み中…" : "導入済みモデルを読み込む"}
                    </button>
                  ) : (
                    <p className="text-[9px] text-muted-foreground">
                      {semanticModel.installed ? "モデル導入済み · ローカル保存" : ""}
                    </p>
                  )}
                  {semanticModel.runtime.error && (
                    <p className="text-[9px] leading-relaxed text-destructive break-all">{semanticModel.runtime.error}</p>
                  )}
                </div>
              )}
              {feature.key === "lightweightVision" && lightweightModel && (
                <div className="mt-2 border-t border-border/60 pt-2 space-y-2">
                  <div className="grid grid-cols-2 gap-2 text-[9px]">
                    <div className="rounded-md border border-border/60 p-2">
                      <p className="font-medium text-foreground">llama.cpp runtime</p>
                      <p className="mt-0.5 text-muted-foreground">{formatFileSize(lightweightModel.llamaRuntime.sizeBytes)}</p>
                      <p className="mt-0.5 text-muted-foreground">
                        {lightweightModel.llamaRuntime.installed ? "導入済み" : "未導入"}
                      </p>
                    </div>
                    <div className="rounded-md border border-border/60 p-2">
                      <p className="font-medium text-foreground truncate" title={lightweightModel.displayName}>{lightweightModel.displayName}</p>
                      <p className="mt-0.5 text-muted-foreground">{formatFileSize(lightweightModel.sizeBytes)}</p>
                      <p className="mt-0.5 text-muted-foreground">
                        {lightweightModel.installed ? "導入済み" : "未導入"} · {lightweightModel.license}
                      </p>
                    </div>
                  </div>

                  {runtimeProgress && runtimeProgress.total > 0 && (
                    <DownloadBar downloaded={runtimeProgress.downloaded} total={runtimeProgress.total} label="runtime" />
                  )}
                  {visionModelProgress && visionModelProgress.total > 0 && (
                    <DownloadBar downloaded={visionModelProgress.downloaded} total={visionModelProgress.total} label="model" />
                  )}

                  <div className="flex flex-wrap gap-1.5">
                    {!lightweightModel.llamaRuntime.installed ? (
                      <button type="button" className="ui-primary-button" disabled={visionBusy} onClick={() => void installVisionRuntime()}>
                        {visionBusy ? "処理中…" : "runtimeを導入"}
                      </button>
                    ) : (
                      <button type="button" className="ui-secondary-button" disabled={visionBusy} onClick={() => void removeVisionRuntime()}>
                        runtimeを削除
                      </button>
                    )}
                    {!lightweightModel.installed ? (
                      <button type="button" className="ui-primary-button" disabled={visionBusy} onClick={() => void installVisionModel()}>
                        {visionBusy ? "処理中…" : "SmolVLMを導入"}
                      </button>
                    ) : (
                      <button type="button" className="ui-secondary-button" disabled={visionBusy} onClick={() => void removeVisionModel()}>
                        モデルを削除
                      </button>
                    )}
                    {lightweightModel.installed &&
                      lightweightModel.llamaRuntime.installed &&
                      status !== "ready" &&
                      status !== "running" &&
                      settings.enabled &&
                      settings.lightweightVision && (
                        <button type="button" className="ui-primary-button" disabled={visionBusy} onClick={() => void loadVisionModel()}>
                          {visionBusy ? "読み込み中…" : "Lightweight Visionを読み込む"}
                        </button>
                      )}
                    {lightweightModel.installed &&
                      lightweightModel.llamaRuntime.installed &&
                      (status === "ready" || status === "running") &&
                      settings.enabled &&
                      settings.lightweightVision && (
                        <button type="button" className="ui-secondary-button" disabled={visionBusy} onClick={() => void backfillVision()}>
                          既存画像を解析キューへ
                        </button>
                      )}
                  </div>
                  <p className="text-[9px] leading-relaxed text-muted-foreground">
                    設定ONだけではダウンロードしません。runtimeとモデルは明示操作でのみローカル保存されます。
                  </p>
                  {lightweightModel.runtime.error && (
                    <p className="text-[9px] leading-relaxed text-destructive break-all">{lightweightModel.runtime.error}</p>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>

      <div className="border-t border-border/70 pt-3 space-y-3">
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-[10px] font-medium">インポート・スキャン後に自動解析</p>
            <p className="mt-0.5 text-[9px] leading-relaxed text-muted-foreground">
              有効なAIエンジンだけをバックグラウンド処理対象にします。
            </p>
          </div>
          <Toggle
            checked={settings.autoAnalyze}
            disabled={saving}
            label="インポート・スキャン後に自動解析"
            onChange={(autoAnalyze) => void update({ autoAnalyze })}
          />
        </div>

        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-[10px] font-medium">GPUアクセラレーションを許可</p>
            <p className="mt-0.5 text-[9px] leading-relaxed text-muted-foreground">
              OFFではCPU-onlyを強制します。対応GPUがなくてもAI機能を利用できる設計です。
            </p>
          </div>
          <Toggle
            checked={settings.gpuAcceleration}
            disabled={saving}
            label="GPUアクセラレーションを許可"
            onChange={(gpuAcceleration) => void update({ gpuAcceleration })}
          />
        </div>
      </div>

      {!settings.enabled && (
        <p className="rounded-lg bg-muted/40 px-2.5 py-2 text-[9px] leading-relaxed text-muted-foreground">
          AI機能全体がOFFの間は、個別設定がONでもモデルロード・worker・AIジョブを許可しません。
        </p>
      )}

      {error && (
        <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 px-2.5 py-2 text-[10px] text-destructive">
          <p>AI設定を保存できませんでした: {error}</p>
          <button type="button" className="mt-1 underline" onClick={() => void load()}>
            再読み込み
          </button>
        </div>
      )}
    </section>
  );
}
