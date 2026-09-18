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
import type { PostAccountDTO, PostTargetDTO, PostRecordDTO } from "../api/client";
import { useAppDialog } from "./AppDialogProvider";

const postKindLabel: Record<string, string> = { pixiv: "Pixiv", twitter: "X", misskey: "Misskey", bluesky: "Bluesky", other: "その他" };

function formatRecordDate(value?: string) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return date.toLocaleString("ja-JP", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

function parseMetadata(record: PostRecordDTO): Record<string, unknown> {
  try {
    const parsed = JSON.parse(record.platformMetadataJson || "{}");
    return parsed && typeof parsed === "object" ? parsed as Record<string, unknown> : {};
  } catch {
    return {};
  }
}

function splitTags(value: string) {
  return value.split(/[\n,]+/).map((item) => item.trim().replace(/^#/, "")).filter(Boolean);
}

export function PostRecordsPanel() {
  const queryClient = useQueryClient();
  const dialog = useAppDialog();
  const { data: records = [], isLoading: recordsLoading } = useQuery({ queryKey: ["postRecords"], queryFn: () => listPostRecords(0, 100), staleTime: 30_000 });
  const [targets, setTargets] = useState<PostTargetDTO[]>([]);
  const [accounts, setAccounts] = useState<PostAccountDTO[]>([]);
  const [showSettings, setShowSettings] = useState(false);
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [targetName, setTargetName] = useState("");
  const [targetKind, setTargetKind] = useState("pixiv");
  const [accountTargetId, setAccountTargetId] = useState(0);
  const [accountName, setAccountName] = useState("");
  const [accountIdentifier, setAccountIdentifier] = useState("");
  const [settingsError, setSettingsError] = useState<string | null>(null);
  const [settingsLoading, setSettingsLoading] = useState(true);
  const [targetBusy, setTargetBusy] = useState(false);
  const [accountBusy, setAccountBusy] = useState(false);

  const reloadSettings = async () => {
    setSettingsLoading(true);
    try {
      const [nextTargets, nextAccounts] = await Promise.all([listPostTargets(), listPostAccounts()]);
      const safeTargets = nextTargets ?? [];
      const safeAccounts = nextAccounts ?? [];
      setTargets(safeTargets); setAccounts(safeAccounts);
      setAccountTargetId((current) => safeTargets.some((target) => target.id === current) ? current : (safeTargets[0]?.id ?? 0));
      if (!safeTargets.length || !safeAccounts.length) setShowSettings(true);
    } catch (cause) {
      setSettingsError(`設定を読み込めませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally { setSettingsLoading(false); }
  };

  useEffect(() => { void reloadSettings(); }, []);

  const addTarget = async (event: FormEvent) => {
    event.preventDefault();
    const name = targetName.trim();
    if (!name) { setSettingsError("投稿先名を入力してください"); return; }
    setTargetBusy(true); setSettingsError(null);
    try {
      const created = await createPostTarget(name, targetKind);
      if (!created) throw new Error("登録結果を取得できませんでした");
      setTargetName(""); await reloadSettings(); setAccountTargetId(created.id);
    } catch (cause) { setSettingsError(`投稿先を追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`); }
    finally { setTargetBusy(false); }
  };

  const addAccount = async (event: FormEvent) => {
    event.preventDefault();
    const displayName = accountName.trim();
    if (!accountTargetId) { setSettingsError("投稿先を選択してください"); return; }
    if (!displayName) { setSettingsError("アカウント名を入力してください"); return; }
    setAccountBusy(true); setSettingsError(null);
    try {
      const created = await createPostAccount(accountTargetId, displayName, accountIdentifier.trim());
      if (!created) throw new Error("登録結果を取得できませんでした");
      setAccountName(""); setAccountIdentifier(""); await reloadSettings();
    } catch (cause) { setSettingsError(`アカウントを追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`); }
    finally { setAccountBusy(false); }
  };

  const deleteRecord = async (record: PostRecordDTO) => {
    const approved = await dialog.confirm({
      title: "公開記録を削除しますか？",
      description: `${record.targetName} / ${record.accountDisplay} の公開記録をLumineから削除します。\n元画像ファイルは削除されません。`,
      confirmLabel: "記録を削除",
      tone: "danger",
    });
    if (!approved) return;
    try {
      await deletePost(record.id);
      await Promise.all([queryClient.invalidateQueries({ queryKey: ["postRecords"] }), queryClient.invalidateQueries({ queryKey: ["assetPostRecords"] })]);
    } catch (cause) {
      await dialog.notify({ title: "公開記録を削除できませんでした", description: "データベースの更新に失敗しました。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
    }
  };

  const accountTargetName = (targetId: number) => targets.find((target) => target.id === targetId)?.name ?? "不明";

  return (
    <div className="px-3 py-3 space-y-3">
      <div className="flex items-center justify-between gap-2"><div className="flex items-center gap-2 min-w-0"><p className="text-xs font-semibold">公開履歴</p><span className="rounded-full bg-muted px-2 py-0.5 text-[10px] text-muted-foreground">{records.length}</span></div><button type="button" className="ui-secondary-button" onClick={() => { setShowSettings((value) => !value); setSettingsError(null); }} aria-expanded={showSettings}>{showSettings ? "設定を閉じる" : "投稿先設定"}</button></div>

      <div className="space-y-2">
        {recordsLoading && <p className="py-3 text-center text-[11px] text-muted-foreground">読み込み中…</p>}
        {!recordsLoading && records.length === 0 && <div className="rounded-xl border border-dashed border-border px-3 py-5 text-center"><p className="text-[11px] text-muted-foreground">公開履歴はありません</p></div>}
        {records.map((record) => {
          const metadata = parseMetadata(record);
          const tags = splitTags(record.hashtags);
          const expanded = expandedId === record.id;
          return <article key={record.id} className="rounded-xl border border-border bg-muted/10 overflow-hidden">
            <div className="p-3">
              <div className="flex items-start gap-2"><div className="min-w-0 flex-1"><div className="flex items-center gap-1.5"><span className="rounded-md bg-primary/10 px-1.5 py-0.5 text-[9px] font-medium text-primary">{postKindLabel[record.targetKind] ?? record.targetKind}</span><p className="text-[11px] text-muted-foreground truncate">{record.accountDisplay}{record.accountIdentifier ? ` · ${record.accountIdentifier}` : ""}</p></div><h3 className="mt-1.5 text-xs font-semibold leading-snug break-words">{record.title || "（タイトルなし）"}</h3><p className="mt-1 text-[9px] text-muted-foreground">{formatRecordDate(record.publishedAt || record.createdAt)} · {record.assetIds.length}枚</p></div><button type="button" className="text-[10px] text-muted-foreground hover:text-destructive" onClick={() => void deleteRecord(record)}>削除</button></div>
              {record.body && <p className={`mt-2 whitespace-pre-wrap text-[10px] leading-relaxed text-muted-foreground ${expanded ? "" : "line-clamp-3"}`}>{record.body}</p>}
              {tags.length > 0 && <div className="mt-2 flex flex-wrap gap-1">{tags.slice(0, expanded ? tags.length : 6).map((tag) => <span key={tag} className="rounded-full bg-background/70 px-2 py-0.5 text-[9px] text-muted-foreground">#{tag}</span>)}{!expanded && tags.length > 6 && <span className="text-[9px] text-muted-foreground">+{tags.length - 6}</span>}</div>}
              <div className="mt-2 flex flex-wrap gap-1.5 text-[9px] text-muted-foreground">{metadata.aiGenerated === true && <span className="rounded-md border border-border px-1.5 py-0.5">AI生成</span>}{typeof metadata.ageRestriction === "string" && <span className="rounded-md border border-border px-1.5 py-0.5">{metadata.ageRestriction}</span>}</div>
              <div className="mt-2 space-y-1">{record.assets.slice(0, expanded ? record.assets.length : 4).map((asset, index) => <div key={asset.id} title={asset.filePath} className="min-w-0 rounded-md bg-background/60 px-2 py-1.5 text-[9px] text-muted-foreground truncate"><span className="mr-1 text-foreground/60">{index + 1}.</span>{asset.fileName}</div>)}</div>
              {(record.externalUrl || record.externalPostId) && <p className="mt-2 break-all rounded-md bg-background/60 px-2 py-1.5 text-[9px] text-primary select-text">{record.externalUrl || record.externalPostId}</p>}
            </div>
            <button type="button" className="w-full border-t border-border bg-background/30 py-1.5 text-[9px] text-muted-foreground hover:text-foreground" onClick={() => setExpandedId(expanded ? null : record.id)}>{expanded ? "詳細を閉じる" : "投稿内容をすべて表示"}</button>
          </article>;
        })}
      </div>

      {showSettings && <div className="rounded-xl border border-border bg-muted/10 p-3 space-y-4">
        <div className="flex items-center justify-between gap-2"><p className="text-[11px] font-semibold">投稿先とアカウント</p><span className="text-[10px] text-muted-foreground">{targets.length} / {accounts.length}</span></div>
        {settingsLoading && <p className="text-[11px] text-muted-foreground">読み込み中…</p>}
        {settingsError && <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-[11px] text-destructive">{settingsError}</div>}
        <section className="space-y-2">
          <p className="ui-label">投稿先</p>
          <form onSubmit={addTarget} className="space-y-2">
            <input aria-label="投稿先名" className="ui-input w-full" value={targetName} onChange={(event) => setTargetName(event.target.value)} placeholder="投稿先名（例: Pixiv）" />
            <div className="flex gap-2">
              <select aria-label="投稿先の種類" className="ui-input min-w-0 flex-1" value={targetKind} onChange={(event) => setTargetKind(event.target.value)}><option value="pixiv">Pixiv</option><option value="twitter">X</option><option value="misskey">Misskey</option><option value="bluesky">Bluesky</option><option value="other">その他</option></select>
              <button type="submit" className="ui-primary-button" disabled={targetBusy}>{targetBusy ? "追加中…" : "追加"}</button>
            </div>
          </form>
          <div className="space-y-1">{targets.map((target) => <div key={target.id} className="min-h-8 px-2 rounded-lg bg-background/50 flex items-center gap-2 text-[11px]"><span className="min-w-0 flex-1 truncate">{target.name} <span className="text-muted-foreground">· {postKindLabel[target.kind] ?? target.kind}</span></span><button type="button" className="text-[10px] text-muted-foreground hover:text-destructive" onClick={async () => { const approved = await dialog.confirm({ title: "投稿先を削除しますか？", description: `「${target.name}」と、その投稿先に紐づく設定を削除します。`, confirmLabel: "投稿先を削除", tone: "danger" }); if (!approved) return; try { await deletePostTarget(target.id); await reloadSettings(); await queryClient.invalidateQueries({ queryKey: ["postRecords"] }); } catch (cause) { await dialog.notify({ title: "投稿先を削除できませんでした", description: "この投稿先を使用している公開記録が残っている可能性があります。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" }); } }}>削除</button></div>)}</div>
        </section>
        <section className="space-y-2 border-t border-border pt-3">
          <p className="ui-label">アカウント</p>
          <form onSubmit={addAccount} className="space-y-2">
            <select aria-label="アカウントの投稿先" className="ui-input w-full" value={accountTargetId} onChange={(event) => setAccountTargetId(Number(event.target.value))} disabled={!targets.length}><option value={0}>投稿先を選択</option>{targets.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}</select>
            <input aria-label="アカウント表示名" className="ui-input w-full" value={accountName} onChange={(event) => setAccountName(event.target.value)} placeholder="表示名" disabled={!targets.length} />
            <input aria-label="アカウントID" className="ui-input w-full" value={accountIdentifier} onChange={(event) => setAccountIdentifier(event.target.value)} placeholder="@ID（任意）" disabled={!targets.length} />
            <button type="submit" className="ui-primary-button w-full justify-center" disabled={accountBusy || !targets.length}>{accountBusy ? "追加中…" : "アカウントを追加"}</button>
          </form>
          <div className="space-y-1">{accounts.map((account) => <div key={account.id} className="min-h-8 px-2 rounded-lg bg-background/50 flex items-center gap-2 text-[11px]"><span className="min-w-0 flex-1 truncate"><span className="text-muted-foreground">{accountTargetName(account.postTargetId)} · </span>{account.displayName}{account.accountIdentifier ? ` · ${account.accountIdentifier}` : ""}</span><button type="button" className="text-[10px] text-muted-foreground hover:text-destructive" onClick={async () => { const approved = await dialog.confirm({ title: "アカウントを削除しますか？", description: `「${account.displayName}」を投稿設定から削除します。`, confirmLabel: "アカウントを削除", tone: "danger" }); if (!approved) return; try { await deletePostAccount(account.id); await reloadSettings(); await queryClient.invalidateQueries({ queryKey: ["postRecords"] }); } catch (cause) { await dialog.notify({ title: "アカウントを削除できませんでした", description: "このアカウントを使用している公開記録が残っている可能性があります。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" }); } }}>削除</button></div>)}</div>
        </section>
      </div>}
    </div>
  );
}
