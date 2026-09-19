import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getAIRuntimeStatuses,
  getTaggerReview,
  reanalyzeAssets,
  reviewTaggerSuggestions,
  type TaggerReview,
  type TaggerSuggestion,
  type TaggerSuggestionKind,
} from "../api/client";

interface TaggerSuggestionsPanelProps {
  assetId: number;
}

const KIND_META: Record<TaggerSuggestionKind, { label: string; note: string }> = {
  character: {
    label: "キャラクター",
    note: "採用すると通常タグとして付与されます。",
  },
  general: {
    label: "Danbooruタグ",
    note: "採用すると通常タグとして付与されます。",
  },
  rating: {
    label: "コンテンツrating",
    note: "safe / sensitive / questionable / explicit等のAI判定です。画像の★評価とは別管理です。",
  },
};

const STATE_LABEL: Record<TaggerReview["state"], string> = {
  queued: "待機中",
  running: "解析中",
  ready: "解析済み",
  failed: "失敗",
  stale: "再解析が必要",
};

function confidenceLabel(value: number): string {
  return \`\${Math.round(Math.max(0, Math.min(1, value)) * 100)}%\`;
}

function SuggestionRow({
  value,
  busy,
  onAction,
}: {
  value: TaggerSuggestion;
  busy: boolean;
  onAction: (value: TaggerSuggestion, action: "accept" | "reject") => Promise<void>;
}) {
  return (
    <div className="flex items-center gap-2 rounded-lg border border-border bg-background/45 px-2.5 py-2">
      <div className="min-w-0 flex-1">
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate text-[11px] font-medium">{value.name}</span>
          <span className="shrink-0 rounded-full border border-border bg-muted/35 px-1.5 py-0.5 text-[9px] tabular-nums text-muted-foreground">
            {confidenceLabel(value.confidence)}
          </span>
        </div>
        {value.threshold > 0 && (
          <p className="mt-0.5 text-[9px] text-muted-foreground/75">
            threshold {value.threshold.toFixed(2)}
          </p>
        )}
      </div>

      {value.state === "pending" ? (
        <div className="flex shrink-0 items-center gap-1">
          <button
            type="button"
            className="ui-mini-button"
            disabled={busy}
            onClick={() => void onAction(value, "accept")}
          >
            採用
          </button>
          <button
            type="button"
            className="ui-mini-button text-muted-foreground"
            disabled={busy}
            onClick={() => void onAction(value, "reject")}
          >
            却下
          </button>
        </div>
      ) : (
        <span className={\`shrink-0 rounded-full px-2 py-1 text-[9px] font-medium \${
          value.state === "accepted"
            ? "bg-emerald-500/10 text-emerald-300"
            : "bg-muted text-muted-foreground"
        }\`}>
          {value.state === "accepted" ? "採用済み" : "却下済み"}
        </span>
      )}
    </div>
  );
}

export function TaggerSuggestionsPanel({ assetId }: TaggerSuggestionsPanelProps) {
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reviewQuery = useQuery({
    queryKey: ["taggerReview", assetId],
    queryFn: () => getTaggerReview(assetId),
    enabled: assetId > 0,
    refetchInterval: (query) => {
      const state = (query.state.data as TaggerReview | null | undefined)?.state;
      return state === "queued" || state === "running" ? 1000 : false;
    },
  });

  const runtimeQuery = useQuery({
    queryKey: ["aiRuntimeStatuses"],
    queryFn: getAIRuntimeStatuses,
    staleTime: 5000,
  });

  const review = reviewQuery.data ?? null;
  const runtime = runtimeQuery.data?.find((value) => value.capability === "tagger");
  const runtimeReady = runtime?.state === "ready" || runtime?.state === "running";

  const grouped = useMemo(() => {
    const base: Record<TaggerSuggestionKind, TaggerSuggestion[]> = {
      character: [],
      general: [],
      rating: [],
    };
    for (const value of review?.suggestions ?? []) {
      if (value.kind in base) base[value.kind].push(value);
    }
    return base;
  }, [review]);

  const pendingCount = useMemo(
    () => (review?.suggestions ?? []).filter((value) => value.state === "pending").length,
    [review],
  );

  const refresh = async (tagsChanged: boolean) => {
    const tasks: Promise<unknown>[] = [
      queryClient.invalidateQueries({ queryKey: ["taggerReview", assetId] }),
      queryClient.invalidateQueries({ queryKey: ["aiRuntimeStatuses"] }),
    ];
    if (tagsChanged) {
      tasks.push(
        queryClient.invalidateQueries({ queryKey: ["assetDetail", assetId] }),
        queryClient.invalidateQueries({ queryKey: ["assets"] }),
        queryClient.invalidateQueries({ queryKey: ["tags"] }),
      );
    }
    await Promise.all(tasks);
  };

  const runAction = async (
    suggestion: TaggerSuggestion | null,
    action: "accept" | "reject" | "accept_all" | "reject_all",
  ) => {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      await reviewTaggerSuggestions(assetId, suggestion?.id ?? 0, action);
      await refresh(action === "accept" || action === "accept_all");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  };

  const reanalyze = async () => {
    if (busy || !runtimeReady) return;
    setBusy(true);
    setError(null);
    try {
      await reanalyzeAssets([assetId], "tagger", 100);
      await refresh(false);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="rounded-xl border border-border bg-background/30 p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h4 className="text-[12px] font-semibold">Anime / Danbooru Tagger</h4>
            {review && (
              <span className={\`rounded-full border px-2 py-0.5 text-[9px] font-medium \${
                review.state === "ready"
                  ? "border-emerald-500/25 bg-emerald-500/10 text-emerald-300"
                  : review.state === "failed"
                    ? "border-destructive/30 bg-destructive/10 text-destructive"
                    : "border-border bg-muted/35 text-muted-foreground"
              }\`}>
                {STATE_LABEL[review.state]}
              </span>
            )}
          </div>
          <p className="mt-1 text-[10px] leading-relaxed text-muted-foreground">
            AI候補は手動タグとは別に保存され、採用したgeneral / characterだけ通常タグへ追加されます。
          </p>
        </div>

        <button
          type="button"
          className="ui-secondary-button shrink-0"
          disabled={busy || !runtimeReady}
          onClick={() => void reanalyze()}
          title={runtimeReady ? "この画像をTaggerで再解析" : "Tagger runtimeが利用できません"}
        >
          {review?.state === "running" || review?.state === "queued" ? "解析中…" : "再解析"}
        </button>
      </div>

      {!runtimeReady && (
        <div className="mt-3 rounded-lg border border-border bg-muted/20 px-2.5 py-2 text-[10px] leading-relaxed text-muted-foreground">
          {runtime?.state === "disabled"
            ? "Taggerは現在OFFです。採用モデルの統合後、AI設定から有効化できます。"
            : runtime?.state === "error"
              ? \`Tagger runtimeエラー: \${runtime.error || "詳細不明"}\`
              : "Taggerモデル/runtimeはまだ準備されていません。"}
        </div>
      )}

      {review?.errorMessage && (
        <div className="mt-3 rounded-lg border border-destructive/30 bg-destructive/10 px-2.5 py-2 text-[10px] leading-relaxed text-destructive">
          {review.errorMessage}
        </div>
      )}

      {review && (review.engine || review.modelId) && (
        <p className="mt-2 break-all text-[9px] text-muted-foreground/75">
          {review.engine || "engine不明"} · {review.modelId || "model不明"}
          {review.modelVersion ? \` @ \${review.modelVersion}\` : ""}
        </p>
      )}

      {review && review.suggestions.length > 0 ? (
        <div className="mt-3 space-y-3">
          {pendingCount > 0 && (
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border bg-muted/15 px-2.5 py-2">
              <span className="text-[10px] text-muted-foreground">未確認 {pendingCount}件</span>
              <div className="flex gap-1.5">
                <button
                  type="button"
                  className="ui-mini-button"
                  disabled={busy}
                  onClick={() => void runAction(null, "accept_all")}
                >
                  すべて採用
                </button>
                <button
                  type="button"
                  className="ui-mini-button text-muted-foreground"
                  disabled={busy}
                  onClick={() => void runAction(null, "reject_all")}
                >
                  すべて却下
                </button>
              </div>
            </div>
          )}

          {(["character", "general", "rating"] as const).map((kind) => {
            const values = grouped[kind];
            if (values.length === 0) return null;
            const meta = KIND_META[kind];
            return (
              <div key={kind}>
                <div className="mb-1.5">
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-[10px] font-semibold">{meta.label}</p>
                    <span className="text-[9px] text-muted-foreground">{values.length}件</span>
                  </div>
                  <p className="mt-0.5 text-[9px] leading-relaxed text-muted-foreground/75">{meta.note}</p>
                </div>
                <div className="space-y-1.5">
                  {values.map((value) => (
                    <SuggestionRow
                      key={value.id}
                      value={value}
                      busy={busy}
                      onAction={(item, action) => runAction(item, action)}
                    />
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        <div className="mt-3 rounded-lg border border-dashed border-border px-3 py-4 text-center text-[10px] text-muted-foreground">
          {reviewQuery.isFetching
            ? "Tagger解析情報を読み込んでいます…"
            : review
              ? "この解析には表示できるタグ候補がありません。"
              : "この画像のTagger解析結果はまだありません。"}
        </div>
      )}

      {error && (
        <p className="mt-2 rounded-lg border border-destructive/25 bg-destructive/10 px-2.5 py-2 text-[10px] leading-relaxed text-destructive">
          {error}
        </p>
      )}
    </section>
  );
}
