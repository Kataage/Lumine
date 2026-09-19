import { useCallback, useEffect, useState } from "react";
import {
  EventsOff,
  EventsOn,
  getAISettings,
  getAIStorageInfo,
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
  type AIStorageInfo,
  type LightweightVisionModelInfo,
  type SemanticModelInfo,
} from "../api/client";
import { formatFileSize } from "../utils/format";
import { AdvancedVisionSettingsCard } from "./AdvancedVisionSettingsCard";
import { PromptEngineSettingsCard } from "./PromptEngineSettingsCard";
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
      className={`inline-flex h-7 min-w-[66px] flex-shrink-0 items-center justify-between gap-1 rounded-full border px-1.5 text-[9px] font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${
        checked
          ? "border-emerald-500/40 bg-emerald-500/15 text-emerald-200"
          : "border-border bg-muted/70 text-muted-foreground"
      }`}
    >
      <span className="pl-0.5">{checked ? "ON" : "OFF"}</span>
      <span className={`h-4 w-4 rounded-full shadow-sm transition-colors ${checked ? "bg-emerald-300" : "bg-zinc-500"}`} />
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
            : status === "ready"
              ? "border-emerald-500/35 bg-emerald-500/5 text-emerald-300"
              : status === "running"
                ? "border-primary/40 bg-primary/5 text-primary"
                : "border-amber-500/35 bg-amber-500/5 text-amber-300"
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
  const [storageInfo, setStorageInfo] = useState<AIStorageInfo | null>(null);
  const [modelBusy, setModelBusy] = useState(false);
  const [visionBusy, setVisionBusy] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState<{ downloaded: number; total: number } | null>(null);
  const [visionModelProgress, setVisionModelProgress] = useState<{ downloaded: number; total: number } | null>(null);
  const [runtimeProgress, setRuntimeProgress] = useState<{ downloaded: number; total: number } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [nextSettings, model, visionModel, storage] = await Promise.all([
        getAISettings(),
        getDefaultSemanticModelInfo().catch(() => null),
        getDefaultLightweightVisionModelInfo().catch(() => null),
        getAIStorageInfo().catch(() => null),
      ]);
      setSettings(nextSettings);
      setSemanticModel(model);
      setLightweightModel(visionModel);
      setStorageInfo(storage);
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

  const ensureFeatureEnabled = async (feature: AIModelFeatureKey) => {
    const next = { ...settings, enabled: true, [feature]: true } as AISettings;
    const saved = await setAISettings(next);
    setSettings(saved);
  };

  const installSemanticModel = async () => {
    if (modelBusy) return;
    setModelBusy(true);
    setError(null);
    setDownloadProgress({ downloaded: 0, total: semanticModel?.sizeBytes ?? 0 });
    try {
      await ensureFeatureEnabled("semanticSearch");
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
      await ensureFeatureEnabled("semanticSearch");
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

  const setupLightweightVision = async () => {
    if (visionBusy) return;
    setVisionBusy(true);
    setError(null);
    try {
      await ensureFeatureEnabled("lightweightVision");
      let current = await getDefaultLightweightVisionModelInfo();
      if (!current.llamaRuntime.installed) {
        setRuntimeProgress({ downloaded: 0, total: current.llamaRuntime.sizeBytes });
        await installLightweightVisionRuntime();
        setRuntimeProgress(null);
        current = await getDefaultLightweightVisionModelInfo();
      }
      if (!current.installed) {
        setVisionModelProgress({ downloaded: 0, total: current.sizeBytes });
        await installDefaultLightweightVisionModel();
        setVisionModelProgress(null);
      }
      await loadDefaultLightweightVisionModel();
      await refreshLightweightModel();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setRuntimeProgress(null);
      setVisionModelProgress(null);
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
      <div className="rounded-xl border border-border/80 bg-background/40 p-3">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <p className="text-[11px] font-semibold">ローカルAI</p>
              <span className={`rounded-full border px-2 py-0.5 text-[9px] font-semibold ${
                settings.enabled
                  ? "border-emerald-500/35 bg-emerald-500/10 text-emerald-300"
                  : "border-border text-muted-foreground"
              }`}>
                AI全体 {settings.enabled ? "ON" : "OFF"}
              </span>
            </div>
            <p className="mt-1 text-[9px] leading-relaxed text-muted-foreground">
              すべてローカルで動作します。個別機能の「導入して使用」から必要なモデルだけセットアップできます。
            </p>
          </div>
          <Toggle
            checked={settings.enabled}
            disabled={saving}
            label="AI機能全体"
            onChange={(enabled) => void update({ enabled })}
          />
        </div>
        {storageInfo && (
          <div className="mt-2.5 rounded-lg border border-border/60 bg-muted/20 p-2 text-[9px] leading-relaxed text-muted-foreground">
            <p className="font-medium text-foreground">ローカル保存先</p>
            <p className="mt-1 break-all"><span className="opacity-70">Models:</span> {storageInfo.modelsPath}</p>
            <p className="break-all"><span className="opacity-70">Runtime:</span> {storageInfo.runtimesPath}</p>
            <p className="mt-1 opacity-70">Portable版でもこのユーザーフォルダーを使用します。</p>
          </div>
        )}
      </div>

      <div className="space-y-1.5">
        {MODEL_FEATURES.map((feature) => {
          const taggerUnavailable = feature.key === "tagger";
          const effectiveEnabled = settings.enabled && settings[feature.key] && !taggerUnavailable;
          const status = feature.key === "semanticSearch" && semanticModel
            ? semanticModel.runtime.state
            : feature.key === "lightweightVision" && lightweightModel
              ? lightweightModel.runtime.state
              : getInitialAIEngineStatus(settings, feature.key);
          return (
            <div
              key={feature.key}
              className={`rounded-lg border p-2.5 transition-colors ${
                effectiveEnabled ? "border-primary/25 bg-primary/[0.03]" : "border-border/70 bg-background/30"
              }`}
            >
              <div className="flex items-center gap-2">
                <div className="min-w-0 flex-1">
                  <div className="flex min-w-0 flex-wrap items-center gap-1.5">
                    <p className="text-[10px] font-medium">{feature.label}</p>
                    {taggerUnavailable ? (
                      <span className="rounded-full border border-amber-500/30 bg-amber-500/5 px-2 py-0.5 text-[9px] font-medium text-amber-300">
                        準備中
                      </span>
                    ) : feature.key === "semanticSearch" || feature.key === "lightweightVision" ? (
                      <StatusBadge status={status} />
                    ) : (
                      <span className={`rounded-full border px-2 py-0.5 text-[9px] font-medium ${
                        effectiveEnabled
                          ? "border-emerald-500/30 bg-emerald-500/5 text-emerald-300"
                          : "border-border text-muted-foreground"
                      }`}>
                        機能 {effectiveEnabled ? "ON" : "OFF"}
                      </span>
                    )}
                  </div>
                  <p className="mt-0.5 text-[9px] leading-relaxed text-muted-foreground">
                    {feature.description}
                    {taggerUnavailable ? " 現在はモデル選定・製品統合前のため有効化できません。" : ""}
                  </p>
                </div>
                <Toggle
                  checked={effectiveEnabled}
                  disabled={saving || taggerUnavailable}
                  label={feature.label}
                  onChange={(checked) => {
                    if (checked) {
                      void ensureFeatureEnabled(feature.key);
                    } else {
                      void update({ [feature.key]: false } as Partial<AISettings>);
                    }
                  }}
                />
              </div>

              {feature.key === "advancedVision" && (
                <AdvancedVisionSettingsCard
                  enabled={effectiveEnabled}
                  onEnable={() => ensureFeatureEnabled("advancedVision")}
                />
              )}
              {feature.key === "promptEngine" && (
                <PromptEngineSettingsCard
                  enabled={effectiveEnabled}
                  onEnable={() => ensureFeatureEnabled("promptEngine")}
                />
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
                      {modelBusy ? "セットアップ中…" : "SigLIP 2を導入して使用"}
                    </button>
                  ) : status !== "ready" && status !== "running" ? (
                    <button
                      type="button"
                      className="ui-primary-button w-full justify-center"
                      disabled={modelBusy}
                      onClick={() => void loadSemanticModel()}
                    >
                      {modelBusy ? "読み込み中…" : "SigLIP 2を使用"}
                    </button>
                  ) : (
                    <p className="rounded-md border border-emerald-500/25 bg-emerald-500/5 px-2 py-1.5 text-[9px] text-emerald-300">
                      利用可能 · {semanticModel.displayName}
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
                    {(status !== "ready" && status !== "running") && (
                      <button
                        type="button"
                        className="ui-primary-button"
                        disabled={visionBusy}
                        onClick={() => void setupLightweightVision()}
                      >
                        {visionBusy
                          ? "セットアップ中…"
                          : lightweightModel.installed && lightweightModel.llamaRuntime.installed
                            ? "Lightweight Visionを使用"
                            : "Lightweight Visionを導入して使用"}
                      </button>
                    )}
                    {(status === "ready" || status === "running") && (
                      <button type="button" className="ui-secondary-button" disabled={visionBusy} onClick={() => void backfillVision()}>
                        既存画像を解析キューへ
                      </button>
                    )}
                    {lightweightModel.installed && (
                      <button
                        type="button"
                        className="ui-secondary-button"
                        disabled={visionBusy}
                        onClick={() => {
                          if (!window.confirm("SmolVLMモデルをローカルから削除しますか？")) return;
                          void removeVisionModel();
                        }}
                      >
                        モデルを削除
                      </button>
                    )}
                    {lightweightModel.llamaRuntime.installed && (
                      <button
                        type="button"
                        className="ui-secondary-button"
                        disabled={visionBusy}
                        onClick={() => {
                          if (!window.confirm("共有llama.cpp runtimeを削除します。Advanced Vision / Prompt Engineも停止します。続行しますか？")) return;
                          void removeVisionRuntime();
                        }}
                      >
                        runtimeを削除
                      </button>
                    )}
                  </div>
                  <p className="text-[9px] leading-relaxed text-muted-foreground">
                    「導入して使用」で必要なruntimeとモデルを順番に準備します。個別の導入順を考える必要はありません。
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
            checked={settings.enabled && settings.autoAnalyze}
            disabled={saving}
            label="インポート・スキャン後に自動解析"
            onChange={(autoAnalyze) => {
              if (autoAnalyze) {
                void (async () => {
                  const next = { ...settings, enabled: true, autoAnalyze: true };
                  setSettings(await setAISettings(next));
                })();
              } else {
                void update({ autoAnalyze: false });
              }
            }}
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
          <p>AI操作に失敗しました: {error}</p>
          <button type="button" className="mt-1 underline" onClick={() => void load()}>
            再読み込み
          </button>
        </div>
      )}
    </section>
  );
}
