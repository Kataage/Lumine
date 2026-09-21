import { useCallback, useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import {
  EventsOn,
  cancelLegacyStorageMigration,
  enqueueLightweightVisionBackfill,
  getAdvancedVisionStatus,
  getAIHealthSnapshot,
  getAISettings,
  getSemanticPipelineDiagnostics,
  getAIStorageInfo,
  getDefaultLightweightVisionModelInfo,
  getDefaultSemanticModelInfo,
  getPromptEngineStatus,
  installDefaultLightweightVisionModel,
  installDefaultSemanticModel,
  installLightweightVisionRuntime,
  loadDefaultLightweightVisionModel,
  loadDefaultSemanticModel,
  removeDefaultLightweightVisionModel,
  removeLightweightVisionRuntime,
  patchAISettings,
  requestLegacyStorageMigration,
  resetSemanticPipelineDiagnostics,
  type AdvancedVisionStatusInfo,
  type AIStorageInfo,
  type LightweightVisionModelInfo,
  type PromptEngineStatusInfo,
  type SemanticModelInfo,
  type SemanticPipelineDiagnosticsSnapshot,
} from "../api/client";
import { formatFileSize } from "../utils/format";
import {
  DEFAULT_AI_SETTINGS,
  type AIModelFeatureKey,
  type AISettings,
} from "../utils/aiSettings";
import { AdvancedVisionSettingsCard } from "./AdvancedVisionSettingsCard";
import { useAppDialog } from "./AppDialogProvider";
import { PromptEngineSettingsCard } from "./PromptEngineSettingsCard";
import { TaggerThresholdSettingsCard } from "./TaggerThresholdSettingsCard";

type FeatureStatus = "off" | "setup" | "unloaded" | "ready" | "running" | "error" | "preview";

const FEATURE_META: Record<AIModelFeatureKey, {
  title: string;
  shortTitle: string;
  description: string;
  icon: "search" | "tag" | "sparkles" | "vision" | "prompt";
}> = {
  semanticSearch: {
    title: "Semantic Search",
    shortTitle: "意味検索",
    description: "自然文で画像を探し、選択画像に近い画像も見つけます。",
    icon: "search",
  },
  tagger: {
    title: "Anime / Danbooru Tagger",
    shortTitle: "Tagger",
    description: "タグ・キャラクター・rating候補を画像から推定します。",
    icon: "tag",
  },
  lightweightVision: {
    title: "Lightweight Vision",
    shortTitle: "軽量Vision",
    description: "軽量モデルで画像の内容・構図・背景を自動解析します。",
    icon: "sparkles",
  },
  advancedVision: {
    title: "Advanced Vision",
    shortTitle: "高精度Vision",
    description: "必要な画像だけを、より詳しく理解・比較・逆プロンプト解析します。",
    icon: "vision",
  },
  promptEngine: {
    title: "Prompt Engine",
    shortTitle: "Prompt AI",
    description: "Prompt Studioで生成・改善・変換・部分編集を行います。",
    icon: "prompt",
  },
};

const FEATURE_KEYS: AIModelFeatureKey[] = [
  "semanticSearch",
  "tagger",
  "lightweightVision",
  "advancedVision",
  "promptEngine",
];

function FeatureIcon({ kind }: { kind: (typeof FEATURE_META)[AIModelFeatureKey]["icon"] }) {
  const paths: Record<typeof kind, string> = {
    search: "M11 4a7 7 0 105.18 11.71L21 20.5M8.5 11h5M11 8.5v5",
    tag: "M4 5.5A1.5 1.5 0 015.5 4h5.1a2 2 0 011.4.58l6.42 6.42a2 2 0 010 2.83l-4.59 4.59a2 2 0 01-2.83 0L4.58 12A2 2 0 014 10.6V5.5zM8 8h.01",
    sparkles: "M12 3l1.1 3.18A4.5 4.5 0 0015.82 8.9L19 10l-3.18 1.1a4.5 4.5 0 00-2.72 2.72L12 17l-1.1-3.18a4.5 4.5 0 00-2.72-2.72L5 10l3.18-1.1a4.5 4.5 0 002.72-2.72L12 3zM19 16l.5 1.5L21 18l-1.5.5L19 20l-.5-1.5L17 18l1.5-.5L19 16z",
    vision: "M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6S2.5 12 2.5 12zM12 9a3 3 0 100 6 3 3 0 000-6z",
    prompt: "M6 4.5h9.5A2.5 2.5 0 0118 7v10a2.5 2.5 0 01-2.5 2.5H6A2.5 2.5 0 013.5 17V7A2.5 2.5 0 016 4.5zM7 9h7M7 12h5M19 4v4M17 6h4",
  };
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.65} aria-hidden="true">
      <path strokeLinecap="round" strokeLinejoin="round" d={paths[kind]} />
    </svg>
  );
}

function Switch({
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
      className={`relative h-7 w-12 shrink-0 rounded-full border transition-all duration-150 ${
        checked
          ? "border-primary/30 bg-primary"
          : "border-border bg-muted"
      }`}
    >
      <span
        className={`absolute top-1 h-[18px] w-[18px] rounded-full shadow-sm transition-all duration-150 ${
          checked ? "left-[25px] bg-primary-foreground" : "left-1 bg-muted-foreground/60"
        }`}
      />
    </button>
  );
}

function StatusChip({ status }: { status: FeatureStatus }) {
  const meta: Record<FeatureStatus, { label: string; className: string; dot: string }> = {
    off: {
      label: "OFF",
      className: "border-border bg-muted/45 text-muted-foreground",
      dot: "bg-muted-foreground/45",
    },
    setup: {
      label: "セットアップが必要",
      className: "border-amber-500/25 bg-amber-500/[0.07] text-amber-200",
      dot: "bg-amber-400",
    },
    unloaded: {
      label: "インストール済み・未ロード",
      className: "border-sky-500/25 bg-sky-500/[0.07] text-sky-200",
      dot: "bg-sky-400",
    },
    ready: {
      label: "利用可能",
      className: "border-emerald-500/25 bg-emerald-500/[0.07] text-emerald-200",
      dot: "bg-emerald-400",
    },
    running: {
      label: "処理中",
      className: "border-sky-500/25 bg-sky-500/[0.07] text-sky-200",
      dot: "bg-sky-400 animate-pulse",
    },
    error: {
      label: "エラー",
      className: "border-destructive/35 bg-destructive/10 text-red-200",
      dot: "bg-red-400",
    },
    preview: {
      label: "準備中",
      className: "border-border bg-muted/45 text-muted-foreground",
      dot: "bg-muted-foreground/45",
    },
  };
  const item = meta[status];
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-medium ${item.className}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${item.dot}`} />
      {item.label}
    </span>
  );
}

function ProgressBar({ downloaded, total, label }: { downloaded: number; total: number; label: string }) {
  const percentage = total > 0 ? Math.min(100, (downloaded / total) * 100) : 0;
  return (
    <div className="rounded-xl border border-border/70 bg-background/45 p-3">
      <div className="mb-2 flex items-center justify-between gap-3 text-[11px]">
        <span className="font-medium">{label}</span>
        <span className="tabular-nums text-muted-foreground">{formatFileSize(downloaded)} / {formatFileSize(total)}</span>
      </div>
      <div className="h-1.5 overflow-hidden rounded-full bg-muted">
        <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${percentage}%` }} />
      </div>
    </div>
  );
}

function SettingsRow({
  title,
  description,
  checked,
  disabled = false,
  onChange,
}: {
  title: string;
  description: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <div className="flex items-center gap-4 rounded-xl border border-border/70 bg-background/35 px-4 py-3.5">
      <div className="min-w-0 flex-1">
        <p className="text-[13px] font-semibold">{title}</p>
        <p className="mt-0.5 text-[11px] leading-relaxed text-muted-foreground">{description}</p>
      </div>
      <Switch checked={checked} disabled={disabled} label={title} onChange={onChange} />
    </div>
  );
}

export function AISettingsPanel() {
  const dialog = useAppDialog();
  const [settings, setSettings] = useState<AISettings>(DEFAULT_AI_SETTINGS);
  const [settingsLoaded, setSettingsLoaded] = useState(false);
  const [settingsError, setSettingsError] = useState<string | null>(null);
  const [semanticModel, setSemanticModel] = useState<SemanticModelInfo | null>(null);
  const [lightweightModel, setLightweightModel] = useState<LightweightVisionModelInfo | null>(null);
  const [advancedStatus, setAdvancedStatus] = useState<AdvancedVisionStatusInfo | null>(null);
  const [promptStatus, setPromptStatus] = useState<PromptEngineStatusInfo | null>(null);
  const [storageInfo, setStorageInfo] = useState<AIStorageInfo | null>(null);
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [semanticBusy, setSemanticBusy] = useState(false);
  const [visionBusy, setVisionBusy] = useState(false);
  const [storageBusy, setStorageBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [semanticProgress, setSemanticProgress] = useState<{ downloaded: number; total: number } | null>(null);
  const [visionModelProgress, setVisionModelProgress] = useState<{ downloaded: number; total: number } | null>(null);
  const [runtimeProgress, setRuntimeProgress] = useState<{ downloaded: number; total: number } | null>(null);
  const [semanticDiagnostics, setSemanticDiagnostics] = useState<SemanticPipelineDiagnosticsSnapshot | null>(null);
  const [diagnosticsBusy, setDiagnosticsBusy] = useState(false);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const health = await getAIHealthSnapshot();
      setSettings(health.settings);
      setSettingsLoaded(true);
      setSettingsError(null);
      setError(null);

      const [semantic, vision, advanced, prompt, storage] = await Promise.all([
        getDefaultSemanticModelInfo().catch(() => null),
        getDefaultLightweightVisionModelInfo().catch(() => null),
        getAdvancedVisionStatus().catch(() => null),
        getPromptEngineStatus().catch(() => null),
        getAIStorageInfo().catch(() => null),
      ]);
      setSemanticModel(semantic);
      setLightweightModel(vision);
      setAdvancedStatus(advanced);
      setPromptStatus(prompt);
      setStorageInfo(storage);
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : String(cause);
      setSettingsError(message);
      setError(message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    const offSettingsChanged = EventsOn("ai:settings-changed", () => {
      void Promise.all([
        getAISettings(),
        getDefaultSemanticModelInfo().catch(() => null),
        getAdvancedVisionStatus().catch(() => null),
        getPromptEngineStatus().catch(() => null),
      ])
        .then(([next, semantic, advanced, prompt]) => {
          setSettings(next);
          setSemanticModel(semantic);
          setAdvancedStatus(advanced);
          setPromptStatus(prompt);
          setSettingsLoaded(true);
          setSettingsError(null);
          setError(null);
        })
        .catch((cause) => {
          setSettingsError(cause instanceof Error ? cause.message : String(cause));
        });
    });
    const offModelDownload = EventsOn("ai:model-download", (raw: unknown) => {
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
      const modelID = String(progress.modelId ?? "");
      if (modelID.includes("smolvlm")) setVisionModelProgress(next);
      if (modelID.includes("siglip2")) setSemanticProgress(next);
      if (progress.done) {
        void Promise.all([
          getDefaultSemanticModelInfo().then(setSemanticModel).catch(() => undefined),
          getDefaultLightweightVisionModelInfo().then(setLightweightModel).catch(() => undefined),
        ]);
      }
    });
    const offRuntimeDownload = EventsOn("ai:runtime-download", (raw: unknown) => {
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
      offSettingsChanged();
      offModelDownload();
      offRuntimeDownload();
    };
  }, []);

  useEffect(() => {
    if (!open) return;
    void refresh();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [open, refresh]);

  const update = async (patch: Partial<AISettings>) => {
    const previous = settings;
    setSettings((current) => ({ ...current, ...patch }));
    setSaving(true);
    setError(null);
    try {
      const saved = await patchAISettings(patch);
      setSettings(saved);
      setSettingsLoaded(true);
      setSettingsError(null);
      return saved;
    } catch (cause) {
      setSettings(previous);
      setError(cause instanceof Error ? cause.message : String(cause));
      throw cause;
    } finally {
      setSaving(false);
    }
  };

  const ensureFeatureEnabled = async (feature: AIModelFeatureKey) => {
    const saved = await patchAISettings({ enabled: true, [feature]: true } as Partial<AISettings>);
    setSettings(saved);
    setSettingsLoaded(true);
    setSettingsError(null);
  };

  const settingsReadyForActions = settingsLoaded && !settingsError;

  const setupSemantic = async () => {
    if (semanticBusy) return;
    setSemanticBusy(true);
    setError(null);
    try {
      await ensureFeatureEnabled("semanticSearch");
      const current = await getDefaultSemanticModelInfo();
      if (!current.installed) {
        setSemanticProgress({ downloaded: 0, total: current.sizeBytes });
        await installDefaultSemanticModel();
      } else {
        await loadDefaultSemanticModel();
      }
      setSemanticModel(await getDefaultSemanticModelInfo());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
      setSemanticModel(await getDefaultSemanticModelInfo().catch(() => semanticModel));
    } finally {
      setSemanticProgress(null);
      setSemanticBusy(false);
    }
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
      setLightweightModel(await getDefaultLightweightVisionModelInfo());
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
    const approved = await dialog.confirm({
      title: "Lightweight Visionモデルを削除しますか？",
      description: "ローカルに保存されているモデルを削除します。必要になれば再度セットアップできます。",
      confirmLabel: "モデルを削除",
      tone: "danger",
    });
    if (!approved) return;
    setVisionBusy(true);
    try {
      await removeDefaultLightweightVisionModel();
      setLightweightModel(await getDefaultLightweightVisionModelInfo());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setVisionBusy(false);
    }
  };

  const removeVisionRuntime = async () => {
    if (visionBusy) return;
    const approved = await dialog.confirm({
      title: "共有runtimeを削除しますか？",
      description: "llama.cpp runtimeを削除します。Lightweight Vision / Advanced Vision / Prompt Engineの実行中runtimeは停止します。",
      confirmLabel: "runtimeを削除",
      tone: "danger",
    });
    if (!approved) return;
    setVisionBusy(true);
    try {
      await removeLightweightVisionRuntime();
      setLightweightModel(await getDefaultLightweightVisionModelInfo());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setVisionBusy(false);
    }
  };

  const scheduleStorageMigration = async () => {
    if (!storageInfo?.migrationAvailable || storageBusy) return;
    const approved = await dialog.confirm({
      title: storageInfo.mode === "portable"
        ? "旧LumineデータをPortableへコピーしますか？"
        : "旧Lumineデータを標準保存先へコピーしますか？",
      description: [
        "次回のLumine起動時、SQLiteを開く前にDB・モデル・runtime・検索indexをコピーします。",
        "元のデータは削除しません。現在の保存先にDBがある場合は、コピー前にbackupを作成します。",
        storageInfo.migrationSourcePath && storageInfo.migrationTargetPath
          ? `${storageInfo.migrationSourcePath} → ${storageInfo.migrationTargetPath}`
          : "",
      ].filter(Boolean).join("\n\n"),
      confirmLabel: "次回起動時にコピー",
    });
    if (!approved) return;

    setStorageBusy(true);
    setError(null);
    try {
      setStorageInfo(await requestLegacyStorageMigration());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setStorageBusy(false);
    }
  };

  const cancelStorageMigration = async () => {
    if (storageBusy) return;
    setStorageBusy(true);
    setError(null);
    try {
      setStorageInfo(await cancelLegacyStorageMigration());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setStorageBusy(false);
    }
  };

  const semanticStatus: FeatureStatus = !settings.enabled || !settings.semanticSearch
    ? "off"
    : semanticModel?.runtime.state === "error"
      ? "error"
      : semanticModel?.runtime.state === "not_loaded"
        ? "unloaded"
      : semanticModel?.runtime.state === "running"
        ? "running"
        : semanticModel?.runtime.state === "ready"
          ? "ready"
          : "setup";

  const lightweightStatus: FeatureStatus = !settings.enabled || !settings.lightweightVision
    ? "off"
    : lightweightModel?.runtime.state === "error"
      ? "error"
      : lightweightModel?.runtime.state === "not_loaded"
        ? "unloaded"
      : lightweightModel?.runtime.state === "running"
        ? "running"
        : lightweightModel?.runtime.state === "ready"
          ? "ready"
          : "setup";

  const runtimeStatus = (
    key: "advancedVision" | "promptEngine",
    runtimeState: AdvancedVisionStatusInfo["runtime"]["state"] | undefined,
  ): FeatureStatus => {
    if (!settings.enabled || !settings[key]) return "off";
    if (runtimeState === "error") return "error";
    if (runtimeState === "not_loaded") return "unloaded";
    if (runtimeState === "running") return "running";
    if (runtimeState === "ready") return "ready";
    return "setup";
  };

  const advancedFeatureStatus = runtimeStatus("advancedVision", advancedStatus?.runtime.state);
  const promptFeatureStatus = runtimeStatus("promptEngine", promptStatus?.runtime.state);

  const featureStatus = (key: AIModelFeatureKey): FeatureStatus => {
    if (!settingsLoaded) return "preview";
    if (key === "tagger") return "preview";
    if (key === "semanticSearch") return semanticStatus;
    if (key === "lightweightVision") return lightweightStatus;
    if (key === "advancedVision") return advancedFeatureStatus;
    return promptFeatureStatus;
  };

  const enabledCount = useMemo(
    () => FEATURE_KEYS.filter((key) => key !== "tagger" && settings.enabled && settings[key]).length,
    [settings],
  );
  const allRuntimeStatuses = [semanticStatus, lightweightStatus, advancedFeatureStatus, promptFeatureStatus];
  const errorCount = allRuntimeStatuses.filter((status) => status === "error").length;
  const readyCount = allRuntimeStatuses.filter((status) => status === "ready" || status === "running").length;

  const modal = open ? (
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/70 p-5 backdrop-blur-sm" role="presentation" onMouseDown={(event) => {
      if (event.currentTarget === event.target) setOpen(false);
    }}>
      <section
        role="dialog"
        aria-modal="true"
        aria-label="ローカルAI設定"
        className="flex max-h-[calc(100vh-2.5rem)] w-full max-w-[980px] flex-col overflow-hidden rounded-[22px] border border-border bg-card shadow-[0_32px_90px_rgba(0,0,0,0.7)]"
      >
        <header className="flex shrink-0 items-center gap-4 border-b border-border px-6 py-5">
          <div className="flex h-11 w-11 items-center justify-center rounded-2xl border border-primary/15 bg-primary/[0.07]">
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.6}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 3l1.2 3.5a4.5 4.5 0 002.8 2.8l3.5 1.2-3.5 1.2a4.5 4.5 0 00-2.8 2.8L12 18l-1.2-3.5A4.5 4.5 0 008 11.7l-3.5-1.2L8 9.3a4.5 4.5 0 002.8-2.8L12 3z" />
            </svg>
          </div>
          <div className="min-w-0 flex-1">
            <h2 className="text-base font-semibold tracking-tight">ローカルAI</h2>
            <p className="mt-0.5 text-xs text-muted-foreground">必要な機能だけを端末内で実行します。モデルの導入は明示操作のみです。</p>
          </div>
          <div className="flex items-center gap-3">
            <div className="text-right">
              <p className="text-xs font-medium">
                {settingsError ? "AI状態を取得できません" : !settingsLoaded ? "AI状態を確認中" : settings.enabled ? "AIを使用する" : "AIは停止中"}
              </p>
              <p className="text-[10px] text-muted-foreground">
                {settingsError ? settingsError : !settingsLoaded ? "保存済み設定を読み込んでいます" : settings.enabled ? `${enabledCount}機能が有効` : "モデルは実行されません"}
              </p>
            </div>
            <Switch checked={settingsLoaded && settings.enabled} disabled={saving || !settingsReadyForActions} label="AI機能全体" onChange={(enabled) => void update({ enabled }).catch(() => undefined)} />
            <button
              type="button"
              className="ml-1 flex h-9 w-9 items-center justify-center rounded-xl text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
              onClick={() => setOpen(false)}
              aria-label="閉じる"
            >
              <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
                <path strokeLinecap="round" d="M6 6l12 12M18 6L6 18" />
              </svg>
            </button>
          </div>
        </header>

        <div className="min-h-0 flex-1 overflow-y-auto">
          <div className="mx-auto max-w-[900px] space-y-6 px-6 py-6">
            {error && (
              <div role="alert" className="flex items-start gap-3 rounded-2xl border border-destructive/35 bg-destructive/[0.08] p-4 text-xs text-red-100">
                <svg className="mt-0.5 h-4 w-4 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v4m0 4h.01M10.3 4.6L2.7 18a1.5 1.5 0 001.3 2.25h16a1.5 1.5 0 001.3-2.25L13.7 4.6a2 2 0 00-3.4 0z" />
                </svg>
                <div className="min-w-0 flex-1">
                  <p className="font-semibold">AI機能でエラーが発生しました</p>
                  <p className="mt-1 break-words leading-relaxed text-red-100/75">{error}</p>
                </div>
                <button type="button" className="text-[11px] font-medium underline underline-offset-2" onClick={() => { setError(null); void refresh(); }}>再読み込み</button>
              </div>
            )}

            <section>
              <div className="mb-3">
                <h3 className="text-sm font-semibold">概要</h3>
                <p className="mt-0.5 text-[11px] text-muted-foreground">現在のAI環境をひと目で確認できます。</p>
              </div>
              <div className="grid grid-cols-3 gap-3">
                <div className="rounded-2xl border border-border bg-background/40 p-4">
                  <p className="text-[10px] font-medium uppercase tracking-[0.13em] text-muted-foreground">Active</p>
                  <p className="mt-2 text-xl font-semibold tabular-nums">{enabledCount}<span className="ml-1 text-xs font-normal text-muted-foreground">/ 4</span></p>
                  <p className="mt-1 text-[11px] text-muted-foreground">有効なAI機能</p>
                </div>
                <div className="rounded-2xl border border-border bg-background/40 p-4">
                  <p className="text-[10px] font-medium uppercase tracking-[0.13em] text-muted-foreground">Runtime</p>
                  <p className="mt-2 text-xl font-semibold tabular-nums">{readyCount}<span className="ml-1 text-xs font-normal text-muted-foreground">ready</span></p>
                  <p className="mt-1 text-[11px] text-muted-foreground">{errorCount ? `${errorCount}件のエラー` : "実行環境は正常"}</p>
                </div>
                <div className="rounded-2xl border border-border bg-background/40 p-4">
                  <p className="text-[10px] font-medium uppercase tracking-[0.13em] text-muted-foreground">Compute</p>
                  <p className="mt-2 text-base font-semibold">{settings.gpuAcceleration ? "GPU優先" : "CPU"}</p>
                  <p className="mt-1 text-[11px] text-muted-foreground">利用可能な範囲でローカル実行</p>
                </div>
              </div>
            </section>

            <section>
              <div className="mb-3">
                <h3 className="text-sm font-semibold">AI機能</h3>
                <p className="mt-0.5 text-[11px] text-muted-foreground">用途ごとに独立して有効化・セットアップできます。</p>
              </div>
              <div className="space-y-3">
                {FEATURE_KEYS.map((key) => {
                  const meta = FEATURE_META[key];
                  const isTagger = key === "tagger";
                  const enabled = settings.enabled && settings[key] && !isTagger;
                  const status = featureStatus(key);
                  return (
                    <article key={key} className={`overflow-hidden rounded-2xl border bg-background/35 transition-colors ${
                      status === "error" ? "border-destructive/35" : enabled ? "border-primary/20" : "border-border"
                    }`}>
                      <div className="flex items-center gap-4 px-4 py-4">
                        <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border ${
                          enabled ? "border-primary/20 bg-primary/[0.08] text-foreground" : "border-border bg-muted/35 text-muted-foreground"
                        }`}>
                          <div className="h-[18px] w-[18px]"><FeatureIcon kind={meta.icon} /></div>
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-2.5">
                            <h4 className="text-[13px] font-semibold">{meta.title}</h4>
                            <StatusChip status={status} />
                          </div>
                          <p className="mt-1 text-[11px] leading-relaxed text-muted-foreground">{meta.description}</p>
                        </div>
                        {!isTagger && (
                          <Switch
                            checked={enabled}
                            disabled={saving || !settingsReadyForActions}
                            label={meta.title}
                            onChange={(checked) => {
                              if (checked) void ensureFeatureEnabled(key).catch((cause) => setError(cause instanceof Error ? cause.message : String(cause)));
                              else void update({ [key]: false } as Partial<AISettings>).catch(() => undefined);
                            }}
                          />
                        )}
                      </div>

                      {isTagger && (
                        <>
                          <div className="border-t border-border/70 bg-muted/[0.12] px-4 py-3 text-[11px] leading-relaxed text-muted-foreground">
                            モデル比較と製品統合が完了するまでON切替は無効です。threshold overrideは先に設定できます。
                          </div>
                          <TaggerThresholdSettingsCard />
                        </>
                      )}

                      {key === "semanticSearch" && semanticModel && (
                        <div className="border-t border-border/70 px-4 py-4">
                          <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
                            <div className="min-w-0 flex-1">
                              <p className="text-[12px] font-medium">{semanticModel.displayName}</p>
                              <p className="mt-1 text-[11px] text-muted-foreground">
                                {formatFileSize(semanticModel.sizeBytes)} · {semanticModel.license} · {semanticModel.installed ? "インストール済み" : "未インストール"}
                              </p>
                            </div>
                            {status !== "ready" && status !== "running" && (
                              <button type="button" className="ui-primary-button min-w-[150px]" disabled={semanticBusy || !settingsReadyForActions} onClick={() => void setupSemantic()}>
                                {semanticBusy ? "準備しています…" : semanticModel.installed ? "再読み込み" : "セットアップ"}
                              </button>
                            )}
                            {(status === "ready" || status === "running") && (
                              <div className="flex flex-wrap items-center justify-end gap-2">
                                <div className="inline-flex items-center gap-2 text-[11px] font-medium text-emerald-300">
                                  <span className="h-2 w-2 rounded-full bg-emerald-400" />
                                  検索に使用できます
                                </div>
                                {semanticModel.runtime.executionProvider && (
                                  <span className="rounded-full border border-border bg-muted/35 px-2.5 py-1 text-[10px] font-medium text-muted-foreground">
                                    {semanticModel.runtime.executionProvider === "directml"
                                      ? "DirectML (GPU)"
                                      : semanticModel.runtime.warning
                                        ? "CPU fallback"
                                        : semanticModel.runtime.executionProvider.toUpperCase()}
                                  </span>
                                )}
                              </div>
                            )}
                          </div>
                          {semanticProgress && semanticProgress.total > 0 && (
                            <div className="mt-3"><ProgressBar downloaded={semanticProgress.downloaded} total={semanticProgress.total} label="SigLIP2をダウンロード" /></div>
                          )}
                          {semanticModel.runtime.warning && (
                            <p className="mt-3 rounded-xl border border-amber-500/20 bg-amber-500/[0.07] px-3 py-2.5 text-[11px] leading-relaxed text-amber-100">
                              GPU runtime: {semanticModel.runtime.warning}
                            </p>
                          )}
                          {semanticModel.runtime.error && (
                            <p className="mt-3 rounded-xl bg-destructive/[0.08] px-3 py-2.5 text-[11px] leading-relaxed text-red-200">{semanticModel.runtime.error}</p>
                          )}
                        </div>
                      )}

                      {key === "lightweightVision" && lightweightModel && (
                        <div className="border-t border-border/70 px-4 py-4">
                          <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-center">
                            <div>
                              <p className="text-[12px] font-medium">{lightweightModel.displayName}</p>
                              <p className="mt-1 text-[11px] text-muted-foreground">
                                モデル {formatFileSize(lightweightModel.sizeBytes)}
                                <span className="mx-1.5">·</span>
                                runtime {lightweightModel.llamaRuntime.backend === "vulkan" ? "Vulkan" : "CPU"} {formatFileSize(lightweightModel.llamaRuntime.sizeBytes)}
                                <span className="mx-1.5">·</span>
                                {lightweightModel.license}
                              </p>
                              {(status === "ready" || status === "running") && lightweightModel.runtime.executionProvider && (
                                <div className="mt-2 flex flex-wrap items-center gap-2">
                                  <span className="rounded-full border border-border bg-muted/35 px-2.5 py-1 text-[10px] font-medium text-muted-foreground">
                                    実行: {lightweightModel.runtime.executionProvider === "vulkan" ? "Vulkan (GPU)" : lightweightModel.runtime.executionProvider.toUpperCase()}
                                  </span>
                                  {!lightweightModel.llamaRuntime.installed && lightweightModel.llamaRuntime.fallbackInstalled && (
                                    <span className="rounded-full border border-amber-500/20 bg-amber-500/[0.07] px-2.5 py-1 text-[10px] font-medium text-amber-100">
                                      CPU fallbackのみ
                                    </span>
                                  )}
                                </div>
                              )}
                            </div>
                            <div className="flex flex-wrap items-center justify-end gap-2">
                              {status !== "ready" && status !== "running" ? (
                                <button type="button" className="ui-primary-button min-w-[150px]" disabled={visionBusy || !settingsReadyForActions} onClick={() => void setupLightweightVision()}>
                                  {visionBusy ? "準備しています…" : lightweightModel.installed && lightweightModel.llamaRuntime.installed ? "再読み込み" : "セットアップ"}
                                </button>
                              ) : (
                                <>
                                  {!lightweightModel.llamaRuntime.installed && (
                                    <button type="button" className="ui-secondary-button" disabled={visionBusy || !settingsReadyForActions} onClick={() => void setupLightweightVision()}>
                                      {visionBusy ? "準備しています…" : lightweightModel.llamaRuntime.backend === "vulkan" ? "GPU runtimeを追加" : "runtimeを追加"}
                                    </button>
                                  )}
                                  <button type="button" className="ui-secondary-button" disabled={visionBusy || !settingsReadyForActions} onClick={() => void enqueueLightweightVisionBackfill().catch((cause) => setError(cause instanceof Error ? cause.message : String(cause)))}>
                                    既存画像を解析
                                  </button>
                                </>
                              )}
                            </div>
                          </div>
                          {(runtimeProgress?.total ?? 0) > 0 && (
                            <div className="mt-3"><ProgressBar downloaded={runtimeProgress!.downloaded} total={runtimeProgress!.total} label="llama.cpp runtimeをダウンロード" /></div>
                          )}
                          {(visionModelProgress?.total ?? 0) > 0 && (
                            <div className="mt-3"><ProgressBar downloaded={visionModelProgress!.downloaded} total={visionModelProgress!.total} label="Lightweight Visionモデルをダウンロード" /></div>
                          )}
                          {lightweightModel.runtime.warning && (
                            <p className="mt-3 rounded-xl border border-amber-500/20 bg-amber-500/[0.07] px-3 py-2.5 text-[11px] leading-relaxed text-amber-100">
                              GPU runtime: {lightweightModel.runtime.warning}
                            </p>
                          )}
                          {lightweightModel.runtime.error && (
                            <p className="mt-3 rounded-xl bg-destructive/[0.08] px-3 py-2.5 text-[11px] leading-relaxed text-red-200">{lightweightModel.runtime.error}</p>
                          )}
                          {(lightweightModel.installed || lightweightModel.llamaRuntime.installed) && (
                            <details className="mt-3">
                              <summary className="cursor-pointer select-none text-[11px] text-muted-foreground hover:text-foreground">インストール済みデータを管理</summary>
                              <div className="mt-2 flex flex-wrap gap-2">
                                {lightweightModel.installed && <button type="button" className="ui-secondary-button" disabled={visionBusy || !settingsReadyForActions} onClick={() => void removeVisionModel()}>モデルを削除</button>}
                                {lightweightModel.llamaRuntime.installed && <button type="button" className="ui-secondary-button" disabled={visionBusy || !settingsReadyForActions} onClick={() => void removeVisionRuntime()}>共有runtimeを削除</button>}
                              </div>
                            </details>
                          )}
                        </div>
                      )}

                      {key === "advancedVision" && (
                        <div className="border-t border-border/70 px-4 pb-4">
                          {settingsReadyForActions ? (
                            <AdvancedVisionSettingsCard
                              enabled={enabled}
                              onEnable={() => ensureFeatureEnabled("advancedVision")}
                              onStatusChange={setAdvancedStatus}
                            />
                          ) : (
                            <p className="pt-4 text-[11px] text-muted-foreground">保存済みAI設定を確認してから操作できます。</p>
                          )}
                        </div>
                      )}
                      {key === "promptEngine" && (
                        <div className="border-t border-border/70 px-4 pb-4">
                          {settingsReadyForActions ? (
                            <PromptEngineSettingsCard
                              enabled={enabled}
                              onEnable={() => ensureFeatureEnabled("promptEngine")}
                              onStatusChange={setPromptStatus}
                            />
                          ) : (
                            <p className="pt-4 text-[11px] text-muted-foreground">保存済みAI設定を確認してから操作できます。</p>
                          )}
                        </div>
                      )}
                    </article>
                  );
                })}
              </div>
            </section>

            <section>
              <div className="mb-3">
                <h3 className="text-sm font-semibold">動作</h3>
                <p className="mt-0.5 text-[11px] text-muted-foreground">バックグラウンド処理と計算資源の使い方を設定します。</p>
              </div>
              <div className="space-y-2">
                <SettingsRow
                  title="インポート・スキャン後に自動解析"
                  description="有効なSemantic Search / Lightweight Visionを新しい画像に自動適用します。"
                  checked={settings.enabled && settings.autoAnalyze}
                  disabled={saving || !settingsReadyForActions}
                  onChange={(checked) => {
                    const next = checked ? { enabled: true, autoAnalyze: true } : { autoAnalyze: false };
                    void update(next).catch(() => undefined);
                  }}
                />
                <SettingsRow
                  title="GPUアクセラレーションを許可"
                  description="GPU対応runtimeではGPUを優先します。非対応機能はCPUで安全に動作します。"
                  checked={settings.gpuAcceleration}
                  disabled={saving || !settingsReadyForActions}
                  onChange={(checked) => void update({ gpuAcceleration: checked }).catch(() => undefined)}
                />
                <SettingsRow
                  title="Semantic診断を記録"
                  description="解析の待ち時間・decode・ORT・DB・indexなどをメモリ上で計測します。通常利用ではOFFを推奨します。"
                  checked={settings.diagnostics}
                  disabled={saving || !settingsReadyForActions}
                  onChange={(checked) => {
                    void update({ diagnostics: checked })
                      .then(async () => {
                        if (checked) {
                          setSemanticDiagnostics(await getSemanticPipelineDiagnostics());
                        } else {
                          setSemanticDiagnostics(null);
                        }
                      })
                      .catch(() => undefined);
                  }}
                />
                {settings.diagnostics && (
                  <div className="rounded-xl border border-border/70 bg-background/35 px-4 py-3.5">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <p className="text-[12px] font-semibold">Semantic pipeline diagnostics</p>
                        <p className="mt-0.5 text-[10px] text-muted-foreground">
                          自動pollingは行いません。必要な時だけ更新して計測負荷を抑えます。
                        </p>
                      </div>
                      <div className="flex gap-2">
                        <button
                          type="button"
                          className="ui-secondary-button"
                          disabled={diagnosticsBusy}
                          onClick={() => {
                            setDiagnosticsBusy(true);
                            void getSemanticPipelineDiagnostics()
                              .then(setSemanticDiagnostics)
                              .catch((cause) => setError(cause instanceof Error ? cause.message : String(cause)))
                              .finally(() => setDiagnosticsBusy(false));
                          }}
                        >
                          {diagnosticsBusy ? "更新中…" : "診断を更新"}
                        </button>
                        <button
                          type="button"
                          className="ui-secondary-button"
                          disabled={diagnosticsBusy}
                          onClick={() => {
                            setDiagnosticsBusy(true);
                            void resetSemanticPipelineDiagnostics()
                              .then(setSemanticDiagnostics)
                              .catch((cause) => setError(cause instanceof Error ? cause.message : String(cause)))
                              .finally(() => setDiagnosticsBusy(false));
                          }}
                        >
                          リセット
                        </button>
                      </div>
                    </div>
                    {semanticDiagnostics && (
                      <div className="mt-3 grid grid-cols-2 gap-2 text-[10px] sm:grid-cols-4">
                        <div className="rounded-lg border border-border/60 bg-muted/20 p-2">
                          <p className="text-muted-foreground">Ready / min</p>
                          <p className="mt-1 text-[13px] font-semibold tabular-nums">{semanticDiagnostics.readyLastMinute}</p>
                        </div>
                        <div className="rounded-lg border border-border/60 bg-muted/20 p-2">
                          <p className="text-muted-foreground">ORT / min</p>
                          <p className="mt-1 text-[13px] font-semibold tabular-nums">{semanticDiagnostics.ortRunsLastMinute}</p>
                        </div>
                        <div className="rounded-lg border border-border/60 bg-muted/20 p-2">
                          <p className="text-muted-foreground">Queue / Running</p>
                          <p className="mt-1 text-[13px] font-semibold tabular-nums">{semanticDiagnostics.queueDepth} / {semanticDiagnostics.runningJobs}</p>
                        </div>
                        <div className="rounded-lg border border-border/60 bg-muted/20 p-2">
                          <p className="text-muted-foreground">Workers</p>
                          <p className="mt-1 text-[13px] font-semibold tabular-nums">{semanticDiagnostics.activeWorkers} / {semanticDiagnostics.workerCount}</p>
                        </div>
                        <div className="rounded-lg border border-border/60 bg-muted/20 p-2">
                          <p className="text-muted-foreground">SQLite busy</p>
                          <p className="mt-1 text-[13px] font-semibold tabular-nums">{semanticDiagnostics.sqliteBusyCount}</p>
                        </div>
                        <div className="rounded-lg border border-border/60 bg-muted/20 p-2">
                          <p className="text-muted-foreground">BUSY_SNAPSHOT</p>
                          <p className="mt-1 text-[13px] font-semibold tabular-nums">{semanticDiagnostics.sqliteBusySnapshotCount}</p>
                        </div>
                        <div className="rounded-lg border border-border/60 bg-muted/20 p-2">
                          <p className="text-muted-foreground">Completion failures</p>
                          <p className="mt-1 text-[13px] font-semibold tabular-nums">{semanticDiagnostics.failedCompletionCount}</p>
                        </div>
                        <div className="rounded-lg border border-border/60 bg-muted/20 p-2">
                          <p className="text-muted-foreground">Long running</p>
                          <p className="mt-1 text-[13px] font-semibold tabular-nums">{semanticDiagnostics.longRunningJobs}</p>
                        </div>
                      </div>
                    )}
                    {semanticDiagnostics && (
                      <p className="mt-2 text-[10px] leading-relaxed text-muted-foreground">
                        Runtime: {semanticDiagnostics.executionProvider || "unknown"}
                        {semanticDiagnostics.adapterId != null ? ` / adapter ${semanticDiagnostics.adapterId}` : ""}
                        {" · "}retry {semanticDiagnostics.retryCount}
                        {" · "}re-inference {semanticDiagnostics.reInferenceCount}
                        {" · "}discarded {semanticDiagnostics.discardedAfterCancel}
                        {" · "}snapshot {semanticDiagnostics.snapshotSuccesses}/{semanticDiagnostics.snapshotAttempts}
                        {" · "}viewer {semanticDiagnostics.viewerActive ? "active" : "idle"}
                      </p>
                    )}
                    {semanticDiagnostics?.recentJobs?.length ? (() => {
                      const latest = semanticDiagnostics.recentJobs[semanticDiagnostics.recentJobs.length - 1];
                      return (
                        <p className="mt-2 break-all text-[10px] leading-relaxed text-muted-foreground">
                          Last job #{latest.jobId} / worker {latest.workerId} / {latest.outcome}
                          {" · "}queue {latest.queueWaitMs.toFixed(1)}ms
                          {" · "}decode {latest.stages.decodeMs.toFixed(1)}ms
                          {" · "}preprocess {latest.stages.preprocessMs.toFixed(1)}ms
                          {" · "}opMu {latest.stages.runtimeLockWaitMs.toFixed(1)}ms
                          {" · "}runMu {latest.stages.ortRunLockWaitMs.toFixed(1)}ms
                          {" · "}ORT {latest.stages.ortRunMs.toFixed(1)}ms
                          {" · "}DB {latest.stages.embeddingPersistenceMs.toFixed(1)}+{latest.stages.completionPersistenceMs.toFixed(1)}ms
                          {" · "}index {latest.stages.indexLockWaitMs.toFixed(1)}+{latest.stages.indexUpdateMs.toFixed(1)}ms
                        </p>
                      );
                    })() : null}
                  </div>
                )}
              </div>
            </section>

            {storageInfo && (
              <section>
                <div className="mb-3 flex items-start justify-between gap-3">
                  <div>
                    <h3 className="text-sm font-semibold">ストレージ</h3>
                    <p className="mt-0.5 text-[11px] text-muted-foreground">
                      DB・モデル・runtime・検索indexの実際の保存先です。
                    </p>
                  </div>
                  <span className="rounded-full border border-border bg-muted/35 px-2.5 py-1 text-[10px] font-medium text-muted-foreground">
                    {storageInfo.mode === "portable" ? "Portable" : "Installed"}
                  </span>
                </div>

                {storageInfo.usingLegacy && (
                  <div className="mb-3 rounded-xl border border-amber-500/25 bg-amber-500/[0.07] px-3.5 py-3 text-[11px] leading-relaxed text-amber-100">
                    旧バージョンの保存先を互換モードで使用しています。データはそのまま利用できます。
                    標準保存先へコピーしたい場合は下の移行操作を使用してください。
                  </div>
                )}

                {storageInfo.mode === "portable" && (
                  <div className="mb-3 rounded-xl border border-sky-500/20 bg-sky-500/[0.05] px-3.5 py-3 text-[11px] leading-relaxed text-sky-100">
                    Portable版はこのexeの配置フォルダー配下だけを標準保存先として使用します。
                    フォルダーごと移動するとDB・モデル・runtimeも一緒に移動できます。
                  </div>
                )}

                <div className="rounded-2xl border border-border bg-background/35 p-4">
                  <div className="grid gap-3 md:grid-cols-2">
                    {[
                      ["Root", storageInfo.rootPath],
                      ["Database", storageInfo.databasePath],
                      ["Logs", storageInfo.logsPath],
                      ["Models", storageInfo.modelsPath],
                      ["Runtime", storageInfo.runtimesPath],
                      ["Semantic index", storageInfo.semanticIndexPath],
                      ["WebView2 data", storageInfo.webviewDataPath],
                    ].map(([label, value]) => (
                      <div key={label} className="min-w-0">
                        <p className="text-[10px] font-medium uppercase tracking-[0.12em] text-muted-foreground">{label}</p>
                        <p className="mt-1.5 break-all text-[11px] leading-relaxed">{value}</p>
                      </div>
                    ))}
                  </div>
                </div>

                {storageInfo.migrationAvailable && (
                  <div className="mt-3 rounded-2xl border border-border bg-background/35 p-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div className="min-w-0 flex-1">
                        <p className="text-[12px] font-semibold">
                          {storageInfo.migrationPending ? "旧データのコピーを予約済み" : "旧データを再利用できます"}
                        </p>
                        <p className="mt-1 text-[11px] leading-relaxed text-muted-foreground">
                          {storageInfo.migrationPending
                            ? "次回起動時、DBを開く前に安全にコピーします。元の旧データは削除しません。"
                            : "DB・モデル・runtime・検索indexを次回起動時にコピーできます。再ダウンロードは不要です。"}
                        </p>
                        {storageInfo.migrationSourcePath && storageInfo.migrationTargetPath && (
                          <div className="mt-2 space-y-1 text-[10px] leading-relaxed text-muted-foreground">
                            <p className="break-all">コピー元: {storageInfo.migrationSourcePath}</p>
                            <p className="break-all">コピー先: {storageInfo.migrationTargetPath}</p>
                          </div>
                        )}
                      </div>

                      {storageInfo.migrationPending ? (
                        <button
                          type="button"
                          disabled={storageBusy}
                          onClick={() => void cancelStorageMigration()}
                          className="rounded-lg border border-border bg-background px-3 py-2 text-[11px] font-medium transition-colors hover:bg-accent disabled:opacity-50"
                        >
                          予約を取り消す
                        </button>
                      ) : (
                        <button
                          type="button"
                          disabled={storageBusy}
                          onClick={() => void scheduleStorageMigration()}
                          className="rounded-lg border border-primary/30 bg-primary/10 px-3 py-2 text-[11px] font-medium text-primary transition-colors hover:bg-primary/15 disabled:opacity-50"
                        >
                          {storageBusy ? "処理中…" : "次回起動時にコピー"}
                        </button>
                      )}
                    </div>
                  </div>
                )}
              </section>
            )}
          </div>
        </div>
      </section>
    </div>
  ) : null;

  return (
    <>
      <section className="mx-3 mb-3 overflow-hidden rounded-2xl border border-border bg-muted/[0.10]">
        <button
          type="button"
          className="w-full p-3.5 text-left transition-colors hover:bg-accent/25"
          onClick={() => setOpen(true)}
        >
          <div className="flex items-center gap-3">
            <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border ${
              settings.enabled ? "border-primary/20 bg-primary/[0.08]" : "border-border bg-background/45"
            }`}>
              <svg className="h-[17px] w-[17px]" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.6}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 3l1.2 3.5a4.5 4.5 0 002.8 2.8l3.5 1.2-3.5 1.2a4.5 4.5 0 00-2.8 2.8L12 18l-1.2-3.5A4.5 4.5 0 008 11.7l-3.5-1.2L8 9.3a4.5 4.5 0 002.8-2.8L12 3z" />
              </svg>
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2">
                <p className="text-[11px] font-semibold">ローカルAI</p>
                {settingsError ? (
                  <span className="rounded-full bg-destructive/15 px-1.5 py-0.5 text-[9px] font-medium text-red-200">ERR</span>
                ) : !settingsLoaded ? (
                  <span className="rounded-full bg-muted px-1.5 py-0.5 text-[9px] text-muted-foreground">確認中</span>
                ) : errorCount > 0 ? (
                  <span className="rounded-full bg-destructive/15 px-1.5 py-0.5 text-[9px] font-medium text-red-200">要確認</span>
                ) : settings.enabled ? (
                  <span className="rounded-full bg-emerald-500/10 px-1.5 py-0.5 text-[9px] font-medium text-emerald-300">ON</span>
                ) : (
                  <span className="rounded-full bg-muted px-1.5 py-0.5 text-[9px] text-muted-foreground">OFF</span>
                )}
              </div>
              <p className="mt-0.5 truncate text-[10px] text-muted-foreground">
                {settingsError
                  ? "AI状態を取得できません"
                  : !settingsLoaded
                    ? "状態を確認中…"
                    : loading
                    ? "状態を更新中…"
                    : settings.enabled
                      ? `${enabledCount}機能が有効 · ${readyCount} runtime ready`
                      : "端末内AIは停止中"}
              </p>
            </div>
            <svg className="h-4 w-4 shrink-0 text-muted-foreground" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
            </svg>
          </div>
        </button>
      </section>
      {modal && createPortal(modal, document.body)}
    </>
  );
}
