import { useState } from "react";
import { MEMORY_IMAGE_CACHE_BUDGET_OPTIONS_MIB } from "../utils/imagePipeline";
import {
  getMemoryImageCacheBudgetMiB,
  saveMemoryImageCacheBudgetMiB,
} from "../utils/imageCacheSettings";

export function MemoryCacheSettingsPanel() {
  const [budgetMiB, setBudgetMiB] = useState(() => getMemoryImageCacheBudgetMiB());
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  return (
    <section className="mx-3 mb-4 rounded-xl border border-border bg-muted/10 p-3 space-y-2.5">
      <p className="text-[11px] font-semibold">画像メモリキャッシュ</p>
      <select
        className="ui-input w-full"
        value={budgetMiB}
        disabled={saving}
        onChange={async (event) => {
          const next = Number(event.target.value);
          const previous = budgetMiB;
          setBudgetMiB(next);
          setSaving(true);
          setError(null);
          try {
            const saved = await saveMemoryImageCacheBudgetMiB(next);
            setBudgetMiB(saved);
          } catch (cause) {
            setBudgetMiB(previous);
            setError(cause instanceof Error ? cause.message : String(cause));
          } finally {
            setSaving(false);
          }
        }}
      >
        {MEMORY_IMAGE_CACHE_BUDGET_OPTIONS_MIB.map((value) => (
          <option key={value} value={value}>
            {value >= 1024 ? `${value / 1024} GiB` : `${value} MiB`}{value === 1024 ? "（推奨・既定）" : ""}
          </option>
        ))}
      </select>
      {error && <p role="alert" className="text-[10px] text-destructive">保存に失敗しました: {error}</p>}
    </section>
  );
}
