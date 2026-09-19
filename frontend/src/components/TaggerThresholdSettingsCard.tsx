import { useEffect, useState } from "react";
import {
  getTaggerThresholdOverrides,
  setTaggerThresholdOverrides,
  type TaggerThresholdOverrides,
} from "../api/client";

type Draft = Record<keyof TaggerThresholdOverrides, string>;

const EMPTY_DRAFT: Draft = {
  general: "",
  character: "",
  rating: "",
};

function toDraft(value: TaggerThresholdOverrides): Draft {
  return {
    general: value.general === null ? "" : String(value.general),
    character: value.character === null ? "" : String(value.character),
    rating: value.rating === null ? "" : String(value.rating),
  };
}

function parseThreshold(label: string, value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  if (!Number.isFinite(parsed) || parsed < 0 || parsed > 1) {
    throw new Error(`${label} thresholdは0〜1で入力してください。`);
  }
  return parsed;
}

export function TaggerThresholdSettingsCard() {
  const [draft, setDraft] = useState<Draft>(EMPTY_DRAFT);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    void getTaggerThresholdOverrides()
      .then((value) => {
        if (!alive) return;
        setDraft(toDraft(value));
        setError(null);
      })
      .catch((cause) => {
        if (!alive) return;
        setError(cause instanceof Error ? cause.message : String(cause));
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, []);

  const update = (key: keyof Draft, value: string) => {
    setSaved(false);
    setDraft((current) => ({ ...current, [key]: value }));
  };

  const save = async () => {
    if (saving) return;
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      const value: TaggerThresholdOverrides = {
        general: parseThreshold("general", draft.general),
        character: parseThreshold("character", draft.character),
        rating: parseThreshold("rating", draft.rating),
      };
      await setTaggerThresholdOverrides(value);
      setDraft(toDraft(value));
      setSaved(true);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setSaving(false);
    }
  };

  const reset = async () => {
    if (saving) return;
    setDraft(EMPTY_DRAFT);
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      await setTaggerThresholdOverrides({
        general: null,
        character: null,
        rating: null,
      });
      setSaved(true);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setSaving(false);
    }
  };

  const fields: Array<{
    key: keyof Draft;
    label: string;
    note: string;
  }> = [
    {
      key: "general",
      label: "General",
      note: "一般Danbooruタグ。空欄なら採用モデルの既定値。",
    },
    {
      key: "character",
      label: "Character",
      note: "キャラクタータグ。空欄なら採用モデルの既定値。",
    },
    {
      key: "rating",
      label: "Rating",
      note: "safe / sensitive / questionable / explicit等。空欄ならモデル既定値。",
    },
  ];

  return (
    <div className="border-t border-border/70 px-4 py-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-[12px] font-medium">Threshold override</p>
          <p className="mt-1 max-w-2xl text-[10px] leading-relaxed text-muted-foreground">
            モデル固有の推奨thresholdを既定として使います。必要な項目だけ0〜1で上書きできます。
            画像の★評価とは無関係です。
          </p>
        </div>
        {saved && (
          <span className="rounded-full border border-emerald-500/25 bg-emerald-500/10 px-2.5 py-1 text-[9px] font-medium text-emerald-300">
            保存済み
          </span>
        )}
      </div>

      <div className="mt-3 grid gap-2.5 md:grid-cols-3">
        {fields.map((field) => (
          <label key={field.key} className="rounded-xl border border-border bg-background/35 p-3">
            <span className="text-[10px] font-semibold">{field.label}</span>
            <input
              type="number"
              min="0"
              max="1"
              step="0.01"
              inputMode="decimal"
              placeholder="モデル既定"
              disabled={loading || saving}
              value={draft[field.key]}
              onChange={(event) => update(field.key, event.target.value)}
              className="mt-2 w-full rounded-lg border border-border bg-background px-2.5 py-2 text-[11px] outline-none transition-colors focus:border-primary/45 disabled:opacity-60"
            />
            <span className="mt-1.5 block text-[9px] leading-relaxed text-muted-foreground">
              {field.note}
            </span>
          </label>
        ))}
      </div>

      <div className="mt-3 flex flex-wrap items-center justify-end gap-2">
        <button
          type="button"
          className="ui-secondary-button"
          disabled={loading || saving}
          onClick={() => void reset()}
        >
          モデル既定に戻す
        </button>
        <button
          type="button"
          className="ui-primary-button"
          disabled={loading || saving}
          onClick={() => void save()}
        >
          {saving ? "保存中…" : "保存"}
        </button>
      </div>

      {error && (
        <p className="mt-2 rounded-lg border border-destructive/25 bg-destructive/10 px-2.5 py-2 text-[10px] leading-relaxed text-destructive">
          {error}
        </p>
      )}
    </div>
  );
}
