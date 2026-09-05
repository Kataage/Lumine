import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { createPostAccount, createPostTarget, deletePost, deletePostAccount, deletePostTarget, listPostAccounts, listPostRecords, listPostTargets } from "../api/client";
import type { PostAccountDTO, PostTargetDTO } from "../api/client";

const postKindLabel: Record<string, string> = { pixiv: "Pixiv", twitter: "X", misskey: "Misskey", bluesky: "Bluesky", other: "その他" };

function PanelTitle({ title, description }: { title: string; description: string }) {
  return (
    <div className="px-3 pt-3 pb-2">
      <p className="text-xs font-semibold">{title}</p>
      <p className="text-[11px] text-muted-foreground leading-relaxed">{description}</p>
    </div>
  );
}

export function PostRecordsPanel() {
  const queryClient = useQueryClient();
  const { data: records = [] } = useQuery({ queryKey: ["postRecords"], queryFn: () => listPostRecords(0, 100), staleTime: 30_000 });
  const [targets, setTargets] = useState<PostTargetDTO[]>([]);
  const [accounts, setAccounts] = useState<PostAccountDTO[]>([]);
  const [showSettings, setShowSettings] = useState(true);
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
      setSettingsError(`投稿先設定を読み込めませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setSettingsLoading(false);
    }
  };

  useEffect(() => { void reloadSettings(); }, []);

  const addTarget = async (event: FormEvent) => {
    event.preventDefault();
    const name = targetName.trim();
    if (!name) {
      setSettingsError("投稿先名を入力してください。例: Pixiv / X");
      setSettingsNotice(null);
      return;
    }
    setTargetBusy(true);
    setSettingsError(null);
    setSettingsNotice(null);
    try {
      const created = await createPostTarget(name, targetKind);
      if (!created) throw new Error("アプリから登録結果が返りませんでした");
      setTargetName("");
      await reloadSettings();
      setAccountTargetId(created.id);
      setSettingsNotice(`投稿先「${created.name}」を追加しました。次に、その投稿先で使うアカウントを登録してください。`);
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
      setSettingsError("先に投稿先を追加・選択してください。");
      setSettingsNotice(null);
      return;
    }
    if (!displayName) {
      setSettingsError("アカウントの表示名を入力してください。");
      setSettingsNotice(null);
      return;
    }
    setAccountBusy(true);
    setSettingsError(null);
    setSettingsNotice(null);
    try {
      const created = await createPostAccount(accountTargetId, displayName, accountIdentifier.trim());
      if (!created) throw new Error("アプリから登録結果が返りませんでした");
      setAccountName("");
      setAccountIdentifier("");
      await reloadSettings();
      setSettingsNotice(`アカウント「${created.displayName}」を追加しました。画像を選択して「＋ 投稿記録」を押せば記録できます。`);
    } catch (cause) {
      setSettingsError(`アカウントを追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setAccountBusy(false);
    }
  };

  const accountTargetName = (targetId: number) => targets.find((target) => target.id === targetId)?.name ?? "不明な投稿先";

  return (
    <div className="pb-3">
      <PanelTitle title="投稿記録" description="画像をどのサービス・アカウントへ投稿したか残します。" />
      <div className="px-3 space-y-3">
        <div className="rounded-xl border border-primary/25 bg-primary/5 p-3">
          <p className="text-xs font-semibold">使い方は3ステップ</p>
          <ol className="mt-2 space-y-1.5 text-[11px] leading-relaxed text-muted-foreground">
            <li><span className="font-semibold text-foreground">1.</span> 投稿先を登録 — Pixiv、X、Misskeyなどのサービス</li>
            <li><span className="font-semibold text-foreground">2.</span> アカウントを登録 — そのサービス上で使う自分のアカウント</li>
            <li><span className="font-semibold text-foreground">3.</span> 画像を選択 → 右の詳細、または複数選択バーの「＋ 投稿記録」</li>
          </ol>
        </div>

        <button type="button" className="ui-secondary-button w-full justify-between" onClick={() => setShowSettings((value) => !value)} aria-expanded={showSettings}>
          <span>投稿先・アカウントを設定</span>
          <span className="text-[10px] text-muted-foreground">{targets.length}投稿先 / {accounts.length}アカウント {showSettings ? "▲" : "▼"}</span>
        </button>

        {showSettings && (
          <div className="space-y-4 rounded-xl border border-border p-3 bg-muted/10">
            {settingsLoading && <p className="text-[11px] text-muted-foreground">設定を読み込んでいます…</p>}
            {settingsError && <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-[11px] text-destructive">{settingsError}</div>}
            {settingsNotice && <div role="status" className="rounded-lg border border-primary/30 bg-primary/10 px-3 py-2 text-[11px]">{settingsNotice}</div>}

            <section className="space-y-2">
              <div><p className="ui-label">1. 投稿先（サービス）</p><p className="text-[10px] text-muted-foreground">「Pixiv」「X」など、投稿する場所を登録します。</p></div>
              <form onSubmit={addTarget} className="space-y-2">
                <input aria-label="投稿先名" className="ui-input w-full" value={targetName} onChange={(event) => setTargetName(event.target.value)} placeholder="例: Pixiv" />
                <div className="flex gap-2">
                  <select aria-label="投稿先の種類" className="ui-input min-w-0 flex-1" value={targetKind} onChange={(event) => setTargetKind(event.target.value)}>
                    <option value="pixiv">Pixiv</option><option value="twitter">X</option><option value="misskey">Misskey</option><option value="bluesky">Bluesky</option><option value="other">その他</option>
                  </select>
                  <button type="submit" className="ui-primary-button" disabled={targetBusy}>{targetBusy ? "追加中…" : "投稿先を追加"}</button>
                </div>
              </form>
              <div className="space-y-1">
                {targets.map((target) => (
                  <div key={target.id} className="min-h-8 px-2 rounded-lg bg-background/50 flex items-center gap-2 text-[11px]">
                    <span className="min-w-0 flex-1 truncate">{target.name} <span className="text-muted-foreground">({postKindLabel[target.kind] ?? target.kind})</span></span>
                    <button type="button" className="text-muted-foreground hover:text-destructive" onClick={async () => {
                      if (!confirm(`投稿先「${target.name}」を削除しますか？\nこの投稿先のアカウントと既存記録の紐付けも削除されます。`)) return;
                      try { await deletePostTarget(target.id); await reloadSettings(); await queryClient.invalidateQueries({ queryKey: ["postRecords"] }); setSettingsNotice(`投稿先「${target.name}」を削除しました。`); }
                      catch (cause) { setSettingsError(`投稿先を削除できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`); }
                    }}>削除</button>
                  </div>
                ))}
                {!settingsLoading && targets.length === 0 && <p className="text-[10px] text-muted-foreground">投稿先はまだありません。上の欄へ名前を入力して追加してください。</p>}
              </div>
            </section>

            <section className="space-y-2 border-t border-border pt-3">
              <div><p className="ui-label">2. アカウント</p><p className="text-[10px] text-muted-foreground">投稿先ごとに、実際に使うアカウントを登録します。</p></div>
              <form onSubmit={addAccount} className="space-y-2">
                <select aria-label="アカウントの投稿先" className="ui-input w-full" value={accountTargetId} onChange={(event) => setAccountTargetId(Number(event.target.value))} disabled={!targets.length}>
                  <option value={0}>投稿先を選択</option>{targets.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}
                </select>
                <input aria-label="アカウント表示名" className="ui-input w-full" value={accountName} onChange={(event) => setAccountName(event.target.value)} placeholder="表示名（例: メインアカウント）" disabled={!targets.length} />
                <div className="flex gap-2">
                  <input aria-label="アカウントID" className="ui-input min-w-0 flex-1" value={accountIdentifier} onChange={(event) => setAccountIdentifier(event.target.value)} placeholder="@username / ID（任意）" disabled={!targets.length} />
                  <button type="submit" className="ui-primary-button" disabled={accountBusy || !targets.length}>{accountBusy ? "追加中…" : "アカウントを追加"}</button>
                </div>
              </form>
              <div className="space-y-1">
                {accounts.map((account) => (
                  <div key={account.id} className="min-h-8 px-2 rounded-lg bg-background/50 flex items-center gap-2 text-[11px]">
                    <span className="min-w-0 flex-1 truncate"><span className="text-muted-foreground">{accountTargetName(account.postTargetId)} · </span>{account.displayName}{account.accountIdentifier ? ` · ${account.accountIdentifier}` : ""}</span>
                    <button type="button" className="text-muted-foreground hover:text-destructive" onClick={async () => {
                      if (!confirm(`アカウント「${account.displayName}」を削除しますか？\n既存記録との紐付けも削除されます。`)) return;
                      try { await deletePostAccount(account.id); await reloadSettings(); await queryClient.invalidateQueries({ queryKey: ["postRecords"] }); setSettingsNotice(`アカウント「${account.displayName}」を削除しました。`); }
                      catch (cause) { setSettingsError(`アカウントを削除できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`); }
                    }}>削除</button>
                  </div>
                ))}
                {!settingsLoading && targets.length > 0 && accounts.length === 0 && <p className="text-[10px] text-muted-foreground">アカウントはまだありません。投稿先を選び、表示名を入力して追加してください。</p>}
              </div>
            </section>
          </div>
        )}

        {targets.length > 0 && accounts.length > 0 && (
          <div className="rounded-xl border border-border bg-muted/20 p-3 text-[11px] leading-relaxed text-muted-foreground"><span className="font-medium text-foreground">準備完了。</span> 画像を選択し、右側の詳細パネルまたは複数選択時の「＋ 投稿記録」から登録できます。</div>
        )}

        <div className="space-y-2">
          <p className="ui-label">記録済み</p>
          {records.map((record) => (
            <div key={record.id} className="rounded-xl border border-border bg-muted/15 p-2.5">
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1"><p className="text-xs font-medium truncate">{record.targetName} · {record.accountDisplay}</p><p className="mt-0.5 text-[10px] text-muted-foreground truncate">{record.title || "投稿記録"} · 画像{record.assetIds.length}件</p></div>
                <button type="button" className="text-[10px] text-muted-foreground hover:text-destructive" onClick={async () => { if (!confirm("この投稿記録を削除しますか？")) return; await deletePost(record.id); await Promise.all([queryClient.invalidateQueries({ queryKey: ["postRecords"] }), queryClient.invalidateQueries({ queryKey: ["assetPostRecords"] })]); }}>削除</button>
              </div>
              {record.assets.length > 0 && <div className="mt-2 space-y-1">{record.assets.slice(0, 3).map((asset) => <div key={asset.id} className="h-6 px-2 rounded-md bg-background/60 flex items-center gap-1.5" title={asset.filePath}><span className="text-[10px] text-muted-foreground">画像</span><span className="min-w-0 flex-1 truncate text-[10px]">{asset.fileName}</span></div>)}{record.assets.length > 3 && <p className="pl-2 text-[10px] text-muted-foreground">ほか {record.assets.length - 3}件</p>}</div>}
              <p className="mt-1.5 text-[10px] text-muted-foreground">{record.publishedAt ? new Date(record.publishedAt).toLocaleString("ja-JP") : ""}</p>
              {record.externalPostId && <p className="mt-1 text-[10px] text-primary break-all">{record.externalPostId}</p>}
            </div>
          ))}
          {records.length === 0 && <p className="py-4 text-xs text-center text-muted-foreground">投稿記録はまだありません</p>}
        </div>
      </div>
    </div>
  );
}
