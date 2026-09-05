import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import {
  createPostAccount,
  createPostRecord,
  createPostTarget,
  listPostAccounts,
  listPostTargets,
} from "../api/client";
import type { PostAccountDTO, PostTargetDTO } from "../api/client";

interface PostRecordModalProps {
  assetIds: number[];
  defaultTitle?: string;
  onClose: () => void;
  onSaved?: () => void;
}

export function PostRecordModal({ assetIds, defaultTitle = "", onClose, onSaved }: PostRecordModalProps) {
  const [targets, setTargets] = useState<PostTargetDTO[]>([]);
  const [accounts, setAccounts] = useState<PostAccountDTO[]>([]);
  const [targetId, setTargetId] = useState(0);
  const [accountId, setAccountId] = useState(0);
  const [title, setTitle] = useState(defaultTitle);
  const [externalPostId, setExternalPostId] = useState("");
  const [busy, setBusy] = useState(false);
  const [setupBusy, setSetupBusy] = useState(false);
  const [setupLoading, setSetupLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [showQuickTarget, setShowQuickTarget] = useState(false);
  const [showQuickAccount, setShowQuickAccount] = useState(false);
  const [newTargetName, setNewTargetName] = useState("");
  const [newTargetKind, setNewTargetKind] = useState("pixiv");
  const [newAccountDisplay, setNewAccountDisplay] = useState("");
  const [newAccountIdentifier, setNewAccountIdentifier] = useState("");

  const loadSetup = async (preferredTargetId = 0, preferredAccountId = 0) => {
    setSetupLoading(true);
    try {
      const [loadedTargets, loadedAccounts] = await Promise.all([listPostTargets(), listPostAccounts()]);
      const safeTargets = loadedTargets ?? [];
      const safeAccounts = loadedAccounts ?? [];
      setTargets(safeTargets);
      setAccounts(safeAccounts);

      const nextTargetId = safeTargets.some((target) => target.id === preferredTargetId)
        ? preferredTargetId
        : (safeTargets.some((target) => target.id === targetId) ? targetId : (safeTargets[0]?.id ?? 0));
      setTargetId(nextTargetId);

      const matchingAccounts = safeAccounts.filter((account) => account.postTargetId === nextTargetId && account.isActive);
      const nextAccountId = matchingAccounts.some((account) => account.id === preferredAccountId)
        ? preferredAccountId
        : (matchingAccounts.some((account) => account.id === accountId) ? accountId : (matchingAccounts[0]?.id ?? 0));
      setAccountId(nextAccountId);

      if (!safeTargets.length) {
        setShowQuickTarget(true);
        setShowQuickAccount(false);
      } else if (!matchingAccounts.length) {
        setShowQuickAccount(true);
      }
    } catch (cause) {
      setError(`投稿先設定を読み込めませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setSetupLoading(false);
    }
  };

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    void loadSetup();
    return () => { document.body.style.overflow = previousOverflow; };
  }, []);

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy && !setupBusy) onClose();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [busy, onClose, setupBusy]);

  const filteredAccounts = useMemo(
    () => accounts.filter((account) => account.postTargetId === targetId && account.isActive),
    [accounts, targetId]
  );

  useEffect(() => {
    if (filteredAccounts.some((account) => account.id === accountId)) return;
    setAccountId(filteredAccounts[0]?.id ?? 0);
    if (targetId > 0 && filteredAccounts.length === 0) setShowQuickAccount(true);
  }, [accountId, filteredAccounts, targetId]);

  const quickCreateTarget = async () => {
    const name = newTargetName.trim();
    if (!name) {
      setError("投稿先名を入力してください。例: Pixiv / X");
      setNotice(null);
      return;
    }
    setSetupBusy(true);
    setError(null);
    setNotice(null);
    try {
      const target = await createPostTarget(name, newTargetKind);
      if (!target) throw new Error("アプリから登録結果が返りませんでした");
      setNewTargetName("");
      setShowQuickTarget(false);
      setShowQuickAccount(true);
      await loadSetup(target.id, 0);
      setNotice(`投稿先「${target.name}」を追加しました。次に、その投稿先で使うアカウントを追加してください。`);
    } catch (cause) {
      setError(`投稿先を追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setSetupBusy(false);
    }
  };

  const quickCreateAccount = async () => {
    const displayName = newAccountDisplay.trim();
    if (!targetId) {
      setError("先に投稿先を追加・選択してください。");
      setNotice(null);
      return;
    }
    if (!displayName) {
      setError("アカウントの表示名を入力してください。");
      setNotice(null);
      return;
    }
    setSetupBusy(true);
    setError(null);
    setNotice(null);
    try {
      const account = await createPostAccount(targetId, displayName, newAccountIdentifier.trim());
      if (!account) throw new Error("アプリから登録結果が返りませんでした");
      setNewAccountDisplay("");
      setNewAccountIdentifier("");
      setShowQuickAccount(false);
      await loadSetup(targetId, account.id);
      setNotice(`アカウント「${account.displayName}」を追加しました。投稿記録を保存できます。`);
    } catch (cause) {
      setError(`アカウントを追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setSetupBusy(false);
    }
  };

  const save = async () => {
    if (assetIds.length === 0) {
      setError("記録する画像が選択されていません。");
      return;
    }
    if (!targetId) {
      setError("投稿先を選択してください。未登録の場合は「＋ 投稿先を追加」から登録できます。");
      setShowQuickTarget(targets.length === 0);
      return;
    }
    if (!accountId) {
      setError("アカウントを選択してください。未登録の場合は「＋ アカウントを追加」から登録できます。");
      setShowQuickAccount(true);
      return;
    }
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const record = await createPostRecord({ assetIds, targetId, accountId, title: title.trim(), externalPostId: externalPostId.trim() });
      if (!record) throw new Error("投稿記録を保存できませんでした");
      onSaved?.();
      onClose();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  };

  return createPortal(
    <div className="fixed inset-0 z-[110] flex items-center justify-center p-4" style={{ backgroundColor: "rgba(0, 0, 0, 0.84)", backdropFilter: "blur(3px)" }} onMouseDown={(event) => { if (event.currentTarget === event.target && !busy && !setupBusy) onClose(); }}>
      <div className="w-full max-w-xl max-h-[calc(100dvh-32px)] overflow-hidden rounded-2xl border border-border shadow-2xl isolate" style={{ backgroundColor: "hsl(var(--card))", color: "hsl(var(--card-foreground))", boxShadow: "0 24px 80px rgba(0, 0, 0, 0.72)" }} role="dialog" aria-modal="true" aria-label="投稿記録を追加">
        <div className="h-14 px-4 border-b border-border flex items-center justify-between gap-3" style={{ backgroundColor: "hsl(var(--card))" }}>
          <div className="min-w-0"><h2 className="text-sm font-semibold">投稿記録を追加</h2><p className="text-[11px] text-muted-foreground truncate">選択した{assetIds.length}件の画像を、どこへ投稿したか記録します。</p></div>
          <button type="button" onClick={onClose} disabled={busy || setupBusy} className="ui-icon-button text-lg" aria-label="閉じる">×</button>
        </div>

        <div className="overflow-auto p-4 space-y-4 max-h-[calc(100dvh-154px)]" style={{ backgroundColor: "hsl(var(--card))" }}>
          <div className="rounded-xl border border-primary/25 bg-primary/5 p-3 text-[11px] leading-relaxed">
            <p className="font-semibold text-xs">初回だけ、投稿先とアカウントを登録します</p>
            <p className="mt-1 text-muted-foreground">投稿先 = Pixiv / X などのサービス。アカウント = そのサービス上で使う自分のアカウントです。登録後は画像を選ぶだけで使えます。</p>
          </div>

          {setupLoading && <p className="text-[11px] text-muted-foreground">投稿先設定を読み込んでいます…</p>}

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <label className="space-y-1.5"><span className="ui-label">投稿先</span><select value={targetId} onChange={(event) => setTargetId(Number(event.target.value))} className="ui-input w-full"><option value={0}>投稿先を選択</option>{targets.map((target) => <option key={target.id} value={target.id}>{target.name} ({target.kind})</option>)}</select></label>
            <label className="space-y-1.5"><span className="ui-label">アカウント</span><select value={accountId} onChange={(event) => setAccountId(Number(event.target.value))} className="ui-input w-full" disabled={!targetId}><option value={0}>アカウントを選択</option>{filteredAccounts.map((account) => <option key={account.id} value={account.id}>{account.displayName}{account.accountIdentifier ? ` (${account.accountIdentifier})` : ""}</option>)}</select></label>
          </div>

          <div className="flex flex-wrap gap-2">
            <button type="button" className="ui-secondary-button" aria-expanded={showQuickTarget} onClick={() => { setShowQuickTarget((value) => !value); setError(null); setNotice(null); }}>＋ 投稿先を追加</button>
            <button type="button" className="ui-secondary-button" aria-expanded={showQuickAccount} onClick={() => { if (!targetId) { setError("先に投稿先を追加・選択してください。"); setShowQuickTarget(true); return; } setShowQuickAccount((value) => !value); setError(null); setNotice(null); }} disabled={setupBusy}>＋ アカウントを追加</button>
          </div>

          {showQuickTarget && (
            <div className="rounded-xl border border-border p-3 space-y-2 bg-muted/20">
              <p className="text-xs font-medium">投稿先（サービス）を追加</p>
              <input aria-label="新しい投稿先名" className="ui-input w-full" value={newTargetName} onChange={(event) => setNewTargetName(event.target.value)} placeholder="例: Pixiv" />
              <div className="flex gap-2">
                <select aria-label="新しい投稿先の種類" className="ui-input min-w-0 flex-1" value={newTargetKind} onChange={(event) => setNewTargetKind(event.target.value)}><option value="pixiv">Pixiv</option><option value="twitter">X</option><option value="misskey">Misskey</option><option value="bluesky">Bluesky</option><option value="other">その他</option></select>
                <button type="button" className="ui-primary-button" onClick={() => void quickCreateTarget()} disabled={setupBusy}>{setupBusy ? "追加中…" : "投稿先を追加"}</button>
              </div>
            </div>
          )}

          {showQuickAccount && targetId > 0 && (
            <div className="rounded-xl border border-border p-3 space-y-2 bg-muted/20">
              <p className="text-xs font-medium">この投稿先のアカウントを追加</p>
              <input aria-label="新しいアカウント表示名" className="ui-input w-full" value={newAccountDisplay} onChange={(event) => setNewAccountDisplay(event.target.value)} placeholder="表示名（例: メインアカウント）" />
              <div className="flex gap-2">
                <input aria-label="新しいアカウントID" className="ui-input min-w-0 flex-1" value={newAccountIdentifier} onChange={(event) => setNewAccountIdentifier(event.target.value)} placeholder="@username / ID（任意）" />
                <button type="button" className="ui-primary-button" onClick={() => void quickCreateAccount()} disabled={setupBusy}>{setupBusy ? "追加中…" : "アカウントを追加"}</button>
              </div>
            </div>
          )}

          {error && <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive">{error}</div>}
          {notice && <div role="status" className="rounded-lg border border-primary/30 bg-primary/10 px-3 py-2 text-xs">{notice}</div>}

          <label className="block space-y-1.5"><span className="ui-label">記録名 <span className="font-normal text-muted-foreground">(任意)</span></span><input value={title} onChange={(event) => setTitle(event.target.value)} className="ui-input w-full" placeholder="例: 2026/09/05 Pixiv投稿" /></label>
          <label className="block space-y-1.5"><span className="ui-label">投稿URL / 外部ID <span className="font-normal text-muted-foreground">(任意)</span></span><input value={externalPostId} onChange={(event) => setExternalPostId(event.target.value)} className="ui-input w-full" placeholder="投稿ページURLや作品IDなど" /><p className="text-[11px] text-muted-foreground">後から「どこに投稿した画像か」を確認しやすくするための補助情報です。</p></label>
        </div>

        <div className="min-h-16 px-4 py-3 border-t border-border flex items-center justify-between gap-3" style={{ backgroundColor: "hsl(var(--card))" }}>
          <p className="text-[11px] text-muted-foreground">画像ファイル自体は変更・移動されません。</p>
          <div className="flex items-center gap-2"><button type="button" className="ui-secondary-button" onClick={onClose} disabled={busy || setupBusy}>キャンセル</button><button type="button" className="ui-primary-button min-w-28" onClick={() => void save()} disabled={busy || setupBusy || setupLoading || assetIds.length === 0}>{busy ? "保存中…" : "投稿記録を保存"}</button></div>
        </div>
      </div>
    </div>,
    document.body
  );
}
