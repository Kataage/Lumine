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
      <div>
        <p className="text-[11px] font-semibold">画像メモリキャッシュ</p>
        <p className="mt-1 text-[10px] leading-relaxed text-muted-foreground">
          一度表示した画像をメモリに保持する上限です。戻ったときの再読み込みを減らします。生成サムネイルはディスクへ保存しません。
        </p>
      </div>
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
      <p className="text-[10px] leading-relaxed text-muted-foreground">
        変更は即時反映されます。容量を下げると古いキャッシュから自動的に解放します。
      </p>
      {error && <p role="alert" className="text-[10px] text-destructive">保存に失敗しました: {error}</p>}
    </section>
  );
}
