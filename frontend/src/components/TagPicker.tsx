import { useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { createTag, setAssetTags } from "../api/client";
import type { TagDTO } from "../api/client";

const MAX_VISIBLE_CANDIDATES = 80;
const tagCollator = new Intl.Collator("ja", { sensitivity: "base", numeric: true });

interface TagPickerProps {
  assetId: number;
  tags: TagDTO[];
  assignedTags: TagDTO[];
  onAssignedTagsChange: (tags: TagDTO[]) => void;
  onChanged?: () => void | Promise<void>;
}

function normalized(value: string): string {
  return value.trim().toLocaleLowerCase("ja-JP");
}

export function TagPicker({ assetId, tags, assignedTags, onAssignedTagsChange, onChanged }: TagPickerProps) {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [newColor, setNewColor] = useState("#6366f1");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const assignedIds = useMemo(() => new Set(assignedTags.map((tag) => tag.id)), [assignedTags]);
  const searchKey = normalized(search);

  const candidates = useMemo(() => {
    const filtered = tags.filter((tag) => {
      if (assignedIds.has(tag.id)) return false;
      return !searchKey || normalized(tag.name).includes(searchKey);
    });
    filtered.sort((a, b) => tagCollator.compare(a.name, b.name));
    return filtered;
  }, [assignedIds, searchKey, tags]);

  const visibleCandidates = candidates.slice(0, MAX_VISIBLE_CANDIDATES);
  const exactTag = useMemo(
    () => tags.find((tag) => normalized(tag.name) === searchKey),
    [searchKey, tags]
  );
  const canCreate = search.trim().length > 0 && !exactTag;

  const persist = async (nextTags: TagDTO[]) => {
    if (assetId <= 0 || busy) return;
    setBusy(true);
    setError(null);
    try {
      await setAssetTags(assetId, nextTags.map((tag) => tag.id));
      onAssignedTagsChange(nextTags);
      await onChanged?.();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : String(saveError));
    } finally {
      setBusy(false);
    }
  };

  const addTag = async (tag: TagDTO) => {
    if (assignedIds.has(tag.id)) return;
    await persist([...assignedTags, tag]);
    setSearch("");
  };

  const removeTag = async (tagId: number) => {
    await persist(assignedTags.filter((tag) => tag.id !== tagId));
  };

  const createAndAssign = async () => {
    const name = search.trim();
    if (!name || busy) return;
    if (exactTag) {
      if (!assignedIds.has(exactTag.id)) await addTag(exactTag);
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const created = await createTag(name, newColor);
      if (!created) throw new Error("タグを作成できませんでした。同名タグがないか確認してください。");
      const nextTags = [...assignedTags, created];
      await setAssetTags(assetId, nextTags.map((tag) => tag.id));
      onAssignedTagsChange(nextTags);
      setSearch("");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["tags"] }),
        Promise.resolve(onChanged?.()),
      ]);
    } catch (createError) {
      setError(createError instanceof Error ? createError.message : String(createError));
    } finally {
      setBusy(false);
    }
  };

  const handleEnter = async () => {
    if (!search.trim() || busy) return;
    if (exactTag && !assignedIds.has(exactTag.id)) {
      await addTag(exactTag);
      return;
    }
    if (canCreate) await createAndAssign();
  };

  return (
    <div className="space-y-2.5">
      <div>
        <div className="mb-1.5 flex items-center justify-between gap-2">
          <span className="text-[10px] font-medium text-muted-foreground">付与済み {assignedTags.length}件</span>
          {assignedTags.length > 0 && <span className="text-[10px] text-muted-foreground/70">クリックで解除</span>}
        </div>
        {assignedTags.length > 0 ? (
          <div className="flex flex-wrap gap-1.5">
            {assignedTags
              .slice()
              .sort((a, b) => tagCollator.compare(a.name, b.name))
              .map((tag) => (
                <button
                  key={tag.id}
                  type="button"
                  disabled={busy}
                  onClick={() => void removeTag(tag.id)}
                  className="h-7 max-w-full px-2.5 rounded-full border border-primary/40 bg-primary/15 text-[11px] text-foreground inline-flex items-center gap-1.5"
                  title={`「${tag.name}」を外す`}
                >
                  <span className="w-2 h-2 rounded-full flex-shrink-0" style={{ backgroundColor: tag.color || "transparent" }} />
                  <span className="truncate">{tag.name}</span>
                  <span className="text-muted-foreground">×</span>
                </button>
              ))}
          </div>
        ) : (
          <p className="text-[11px] text-muted-foreground">この画像にはまだタグがありません。</p>
        )}
      </div>

      <div className="space-y-1.5">
        <label className="ui-label" htmlFor={`tag-search-${assetId}`}>タグを検索・追加</label>
        <div className="flex gap-1.5">
          <input
            id={`tag-search-${assetId}`}
            className="ui-input min-w-0 flex-1"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault();
                void handleEnter();
              }
            }}
            placeholder="タグ名を入力…"
            autoComplete="off"
          />
          {canCreate && (
            <input
              type="color"
              value={newColor}
              onChange={(event) => setNewColor(event.target.value)}
              className="w-8 h-8 rounded-lg border border-border bg-transparent flex-shrink-0"
              aria-label="新しいタグの色"
              title="新しいタグの色"
            />
          )}
        </div>
      </div>

      {canCreate && (
        <button
          type="button"
          className="ui-primary-button w-full justify-center"
          disabled={busy}
          onClick={() => void createAndAssign()}
        >
          ＋ 「{search.trim()}」を新規作成して付与
        </button>
      )}

      {tags.length > 0 && (
        <div className="rounded-lg border border-border bg-muted/15 overflow-hidden">
          <div className="px-2.5 py-1.5 border-b border-border flex items-center justify-between text-[10px] text-muted-foreground">
            <span>{searchKey ? `候補 ${candidates.length}件` : `未付与 ${candidates.length}件`}</span>
            {candidates.length > MAX_VISIBLE_CANDIDATES && <span>上位{MAX_VISIBLE_CANDIDATES}件を表示</span>}
          </div>
          <div className="max-h-40 overflow-y-auto p-1">
            {visibleCandidates.map((tag) => (
              <button
                key={tag.id}
                type="button"
                disabled={busy}
                onClick={() => void addTag(tag)}
                className="w-full min-h-8 px-2 rounded-md flex items-center gap-2 text-left text-[11px] hover:bg-accent"
              >
                <span className="w-2.5 h-2.5 rounded-full flex-shrink-0" style={{ backgroundColor: tag.color || "transparent" }} />
                <span className="min-w-0 flex-1 truncate">{tag.name}</span>
                <span className="text-muted-foreground">＋</span>
              </button>
            ))}
            {visibleCandidates.length === 0 && !canCreate && (
              <p className="px-2 py-3 text-center text-[11px] text-muted-foreground">該当する未付与タグはありません</p>
            )}
          </div>
          {candidates.length > MAX_VISIBLE_CANDIDATES && (
            <p className="px-2.5 py-1.5 border-t border-border text-[10px] text-muted-foreground">タグ名を入力して絞り込むと、残りの候補もすぐ探せます。</p>
          )}
        </div>
      )}

      {error && <p className="text-[11px] leading-relaxed text-destructive">{error}</p>}
    </div>
  );
}
