import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  createPostAccount,
  createPostTarget,
  deletePost,
  deletePostAccount,
  deletePostTarget,
  listPostAccounts,
  listPostRecords,
  listPostTargets,
} from "../api/client";
import type { PostAccountDTO, PostTargetDTO } from "../api/client";

const postKindLabel: Record<string, string> = {
  pixiv: "Pixiv",
  twitter: "X",
  misskey: "Misskey",
  bluesky: "Bluesky",
  other: "その他",
};

function formatRecordDate(value?: string) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return date.toLocaleDateString("ja-JP", { year: "numeric", month: "2-digit", day: "2-digit" });
}

export function PostRecordsPanel() {
  const queryClient = useQueryClient();
  const { data: records = [], isLoading: recordsLoading } = useQuery({
    queryKey: ["postRecords"],
    queryFn: () => listPostRecords(0, 100),
    staleTime: 30_000,
  });
  const [targets, setTargets] = useState<PostTargetDTO[]>([]);
  const [accounts, setAccounts] = useState<PostAccountDTO[]>([]);
  const [showSettings, setShowSettings] = useState(false);
  const [targetName, setTargetName] = useState("");
  const [targetKind, setTargetKind] = useState("pixiv");
  const [accountTargetId, setAccountTargetId] = useState(0);
  const [accountName, setAccountName] = useState("");
  const [accountIdentifier, setAccountIdentifier] = useState("");
  const [settingsError, setSettingsError] = useState<string | null>(null);
  const [settingsNotice, setSettingsNotice] = useState<string | null>(null);
  const [settingsLoading, setSettingsLoading] = useState(true);
  const [targetBusy, setTargetBusy] = useState(false);
  const [accountBusy, setAccountBusy] = useState(false);

  const reloadSettings = async () => {
    setSettingsLoading(true);
    try {
      const [nextTargets, nextAccounts] = await Promise.all([listPostTargets(), listPostAccounts()]);
      const safeTargets = nextTargets ?? [];
      const safeAccounts = nextAccounts ?? [];
      setTargets(safeTargets);
      setAccounts(safeAccounts);
      setAccountTargetId((current) => safeTargets.some((target) => target.id === current) ? current : (safeTargets[0]?.id ?? 0));
      if (!safeTargets.length || !safeAccounts.length) setShowSettings(true);
    } catch (cause) {
      setSettingsError(`設定を読み込めませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setSettingsLoading(false);
    }
  };

  useEffect(() => { void reloadSettings(); }, []);

  const addTarget = async (event: FormEvent) => {
    event.preventDefault();
    const name = targetName.trim();
    if (!name) {
      setSettingsError("投稿先名を入力してください");
      setSettingsNotice(null);
      return;
    }
    setTargetBusy(true);
    setSettingsError(null);
    setSettingsNotice(null);
    try {
      const created = await createPostTarget(name, targetKind);
      if (!created) throw new Error("登録結果を取得できませんでした");
      setTargetName("");
      await reloadSettings();
      setAccountTargetId(created.id);
      setSettingsNotice(`${created.name} を追加しました`);
    } catch (cause) {
      setSettingsError(`投稿先を追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setTargetBusy(false);
    }
  };

  const addAccount = async (event: FormEvent) => {
    event.preventDefault();
    const displayName = accountName.trim();
    if (!accountTargetId) {
      setSettingsError("投稿先を選択してください");
      setSettingsNotice(null);
      return;
    }
    if (!displayName) {
      setSettingsError("アカウント名を入力してください");
      setSettingsNotice(null);
      return;
    }
    setAccountBusy(true);
    setSettingsError(null);
    setSettingsNotice(null);
    try {
      const created = await createPostAccount(accountTargetId, displayName, accountIdentifier.trim());
      if (!created) throw new Error("登録結果を取得できませんでした");
      setAccountName("");
      setAccountIdentifier("");
      await reloadSettings();
      setSettingsNotice(`${created.displayName} を追加しました`);
    } catch (cause) {
      setSettingsError(`アカウントを追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setAccountBusy(false);
    }
  };

  const accountTargetName = (targetId: number) => targets.find((target) => target.id === targetId)?.name ?? "不明";

  return (
    <div className="px-3 py-3 space-y-3">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 min-w-0">
          <p className="text-xs font-semibold">投稿記録</p>
          <span className="rounded-full bg-muted px-2 py-0.5 text-[10px] text-muted-foreground">{records.length}</span>
        </div>
        <button
          type="button"
          className="ui-secondary-button"
          onClick={() => { setShowSettings((value) => !value); setSettingsError(null); setSettingsNotice(null); }}
          aria-expanded={showSettings}
        >
          {showSettings ? "設定を閉じる" : "投稿先設定"}
        </button>
      </div>

      <div className="space-y-2">
        {recordsLoading && <p className="py-3 text-center text-[11px] text-muted-foreground">読み込み中…</p>}
        {!recordsLoading && records.length === 0 && (
          <div className="rounded-xl border border-dashed border-border px-3 py-5 text-center">
            <p className="text-xs font-medium">記録はありません</p>
            <p className="mt-1 text-[10px] text-muted-foreground">画像を選択して「投稿記録」から追加できます。</p>
          </div>
        )}

        {records.map((record) => {
          const dateLabel = formatRecordDate(record.publishedAt || record.createdAt);
          return (
            <div key={record.id} className="rounded-xl border border-border bg-muted/10 p-2.5 group">
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1.5 min-w-0">
                    <p className="text-xs font-semibold truncate">{record.targetName}</p>
                    <span className="text-[10px] text-muted-foreground">·</span>
                    <p className="text-[11px] text-muted-foreground truncate">{record.accountDisplay}</p>
                  </div>
                  <div className="mt-1 flex items-center gap-2 text-[10px] text-muted-foreground">
                    {dateLabel && <span>{dateLabel}</span>}
                    <span>{record.assetIds.length}件</span>
                  </div>
                </div>
                <button
                  type="button"
                  className="opacity-0 group-hover:opacity-100 focus:opacity-100 text-[10px] text-muted-foreground hover:text-destructive transition-opacity"
                  onClick={async () => {
                    if (!confirm("この投稿記録を削除しますか？")) return;
                    await deletePost(record.id);
                    await Promise.all([
                      queryClient.invalidateQueries({ queryKey: ["postRecords"] }),
                      queryClient.invalidateQueries({ queryKey: ["assetPostRecords"] }),
                    ]);
                  }}
                >
                  削除
                </button>
              </div>

              {record.title && <p className="mt-2 text-[11px] truncate" title={record.title}>{record.title}</p>}
              {record.externalPostId && (
                <p className="mt-1.5 rounded-md bg-background/60 px-2 py-1.5 text-[10px] text-primary break-all select-text">
                  {record.externalPostId}
                </p>
              )}
              {record.assets.length > 1 && (
                <div className="mt-2 flex flex-wrap gap-1">
                  {record.assets.slice(0, 3).map((asset) => (
                    <span key={asset.id} className="max-w-full truncate rounded-md bg-background/60 px-2 py-1 text-[10px] text-muted-foreground" title={asset.filePath}>
                      {asset.fileName}
                    </span>
                  ))}
                  {record.assets.length > 3 && <span className="px-1 py-1 text-[10px] text-muted-foreground">+{record.assets.length - 3}</span>}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {showSettings && (
        <div className="rounded-xl border border-border bg-muted/10 p-3 space-y-4">
          <div className="flex items-center justify-between gap-2">
            <p className="text-[11px] font-semibold">投稿先とアカウント</p>
            <span className="text-[10px] text-muted-foreground">{targets.length} / {accounts.length}</span>
          </div>

          {settingsLoading && <p className="text-[11px] text-muted-foreground">読み込み中…</p>}
          {settingsError && <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-[11px] text-destructive">{settingsError}</div>}
          {settingsNotice && <div role="status" className="rounded-lg border border-primary/25 bg-primary/5 px-3 py-2 text-[11px]">{settingsNotice}</div>}

          <section className="space-y-2">
            <p className="ui-label">投稿先</p>
            <form onSubmit={addTarget} className="flex gap-2">
              <input aria-label="投稿先名" className="ui-input min-w-0 flex-1" value={targetName} onChange={(event) => setTargetName(event.target.value)} placeholder="Pixiv / X など" />
              <select aria-label="投稿先の種類" className="ui-input w-24" value={targetKind} onChange={(event) => setTargetKind(event.target.value)}>
                <option value="pixiv">Pixiv</option>
                <option value="twitter">X</option>
                <option value="misskey">Misskey</option>
                <option value="bluesky">Bluesky</option>
                <option value="other">その他</option>
              </select>
              <button type="submit" className="ui-primary-button" disabled={targetBusy}>{targetBusy ? "…" : "追加"}</button>
            </form>
            <div className="space-y-1">
              {targets.map((target) => (
                <div key={target.id} className="min-h-8 px-2 rounded-lg bg-background/50 flex items-center gap-2 text-[11px] group">
                  <span className="min-w-0 flex-1 truncate">{target.name} <span className="text-muted-foreground">· {postKindLabel[target.kind] ?? target.kind}</span></span>
                  <button
                    type="button"
                    className="opacity-0 group-hover:opacity-100 focus:opacity-100 text-[10px] text-muted-foreground hover:text-destructive"
                    onClick={async () => {
                      if (!confirm(`投稿先「${target.name}」を削除しますか？`)) return;
                      try {
                        await deletePostTarget(target.id);
                        await reloadSettings();
                        await queryClient.invalidateQueries({ queryKey: ["postRecords"] });
                      } catch (cause) {
                        setSettingsError(`削除できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
                      }
                    }}
                  >
                    削除
                  </button>
                </div>
              ))}
            </div>
          </section>

          <section className="space-y-2 border-t border-border pt-3">
            <p className="ui-label">アカウント</p>
            <form onSubmit={addAccount} className="space-y-2">
              <select aria-label="アカウントの投稿先" className="ui-input w-full" value={accountTargetId} onChange={(event) => setAccountTargetId(Number(event.target.value))} disabled={!targets.length}>
                <option value={0}>投稿先を選択</option>
                {targets.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}
              </select>
              <div className="flex gap-2">
                <input aria-label="アカウント表示名" className="ui-input min-w-0 flex-1" value={accountName} onChange={(event) => setAccountName(event.target.value)} placeholder="表示名" disabled={!targets.length} />
                <input aria-label="アカウントID" className="ui-input min-w-0 flex-1" value={accountIdentifier} onChange={(event) => setAccountIdentifier(event.target.value)} placeholder="@ID（任意）" disabled={!targets.length} />
                <button type="submit" className="ui-primary-button" disabled={accountBusy || !targets.length}>{accountBusy ? "…" : "追加"}</button>
              </div>
            </form>
            <div className="space-y-1">
              {accounts.map((account) => (
                <div key={account.id} className="min-h-8 px-2 rounded-lg bg-background/50 flex items-center gap-2 text-[11px] group">
                  <span className="min-w-0 flex-1 truncate"><span className="text-muted-foreground">{accountTargetName(account.postTargetId)} · </span>{account.displayName}{account.accountIdentifier ? ` · ${account.accountIdentifier}` : ""}</span>
                  <button
                    type="button"
                    className="opacity-0 group-hover:opacity-100 focus:opacity-100 text-[10px] text-muted-foreground hover:text-destructive"
                    onClick={async () => {
                      if (!confirm(`アカウント「${account.displayName}」を削除しますか？`)) return;
                      try {
                        await deletePostAccount(account.id);
                        await reloadSettings();
                        await queryClient.invalidateQueries({ queryKey: ["postRecords"] });
                      } catch (cause) {
                        setSettingsError(`削除できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
                      }
                    }}
                  >
                    削除
                  </button>
                </div>
              ))}
            </div>
          </section>
        </div>
      )}
    </div>
  );
}
