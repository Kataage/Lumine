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
  const [error, setError] = useState<string | null>(null);
  const [showQuickTarget, setShowQuickTarget] = useState(false);
  const [showQuickAccount, setShowQuickAccount] = useState(false);
  const [newTargetName, setNewTargetName] = useState("");
  const [newTargetKind, setNewTargetKind] = useState("twitter");
  const [newAccountDisplay, setNewAccountDisplay] = useState("");
  const [newAccountIdentifier, setNewAccountIdentifier] = useState("");

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    void Promise.all([listPostTargets(), listPostAccounts()]).then(([loadedTargets, loadedAccounts]) => {
      setTargets(loadedTargets ?? []);
      setAccounts(loadedAccounts ?? []);
      const firstTarget = loadedTargets?.[0]?.id ?? 0;
      setTargetId(firstTarget);
      const firstAccount = loadedAccounts?.find((account) => account.postTargetId === firstTarget)?.id ?? 0;
      setAccountId(firstAccount);
      if (!loadedTargets?.length) setShowQuickTarget(true);
      else if (!firstAccount) setShowQuickAccount(true);
    });
    return () => {
      document.body.style.overflow = previousOverflow;
    };
  }, []);

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) onClose();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [busy, onClose]);

  const filteredAccounts = useMemo(
    () => accounts.filter((account) => account.postTargetId === targetId && account.isActive),
    [accounts, targetId]
  );

  useEffect(() => {
    if (filteredAccounts.some((account) => account.id === accountId)) return;
    setAccountId(filteredAccounts[0]?.id ?? 0);
    setShowQuickAccount(filteredAccounts.length === 0 && targetId > 0);
  }, [accountId, filteredAccounts, targetId]);

  const quickCreateTarget = async () => {
    if (!newTargetName.trim()) return;
    setError(null);
    const target = await createPostTarget(newTargetName.trim(), newTargetKind);
    if (!target) {
      setError("投稿先を追加できませんでした。");
      return;
    }
    setTargets((current) => [...current, target]);
    setTargetId(target.id);
    setNewTargetName("");
    setShowQuickTarget(false);
    setShowQuickAccount(true);
  };

  const quickCreateAccount = async () => {
    if (!targetId || !newAccountDisplay.trim()) return;
    setError(null);
    const account = await createPostAccount(targetId, newAccountDisplay.trim(), newAccountIdentifier.trim());
    if (!account) {
      setError("アカウントを追加できませんでした。");
      return;
    }
    setAccounts((current) => [...current, account]);
    setAccountId(account.id);
    setNewAccountDisplay("");
    setNewAccountIdentifier("");
    setShowQuickAccount(false);
  };

  const save = async () => {
    if (!targetId || !accountId || assetIds.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      const record = await createPostRecord({
        assetIds,
        targetId,
        accountId,
        title: title.trim(),
        externalPostId: externalPostId.trim(),
      });
      if (!record) throw new Error("投稿記録を保存できませんでした");
      onSaved?.();
      onClose();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : String(saveError));
    } finally {
      setBusy(false);
    }
  };

  return createPortal(
    <div
      className="fixed inset-0 z-[110] flex items-center justify-center p-4"
      style={{ backgroundColor: "rgba(0, 0, 0, 0.82)", backdropFilter: "blur(2px)" }}
      onMouseDown={(event) => {
        if (event.currentTarget === event.target && !busy) onClose();
      }}
    >
      <div
        className="w-full max-w-xl max-h-[calc(100dvh-32px)] overflow-hidden rounded-2xl border border-border shadow-2xl isolate"
        style={{ backgroundColor: "hsl(var(--card))", color: "hsl(var(--card-foreground))", boxShadow: "0 24px 80px rgba(0, 0, 0, 0.7)" }}
        role="dialog"
        aria-modal="true"
        aria-label="投稿記録を追加"
      >
        <div className="h-14 px-4 border-b border-border flex items-center justify-between gap-3" style={{ backgroundColor: "hsl(var(--card))" }}>
          <div className="min-w-0">
            <h2 className="text-sm font-semibold">投稿記録を追加</h2>
            <p className="text-[11px] text-muted-foreground truncate">選択した{assetIds.length}件の画像を、どこへ投稿したか記録します。</p>
          </div>
          <button onClick={onClose} disabled={busy} className="ui-icon-button text-lg" aria-label="閉じる">×</button>
        </div>

        <div className="overflow-auto p-4 space-y-4 max-h-[calc(100dvh-154px)]" style={{ backgroundColor: "hsl(var(--card))" }}>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <label className="space-y-1.5">
              <span className="ui-label">投稿先</span>
              <select
                value={targetId}
                onChange={(event) => setTargetId(Number(event.target.value))}
                className="ui-input w-full"
              >
                <option value={0}>投稿先を選択</option>
                {targets.map((target) => <option key={target.id} value={target.id}>{target.name} ({target.kind})</option>)}
              </select>
            </label>

            <label className="space-y-1.5">
              <span className="ui-label">アカウント</span>
              <select
                value={accountId}
                onChange={(event) => setAccountId(Number(event.target.value))}
                className="ui-input w-full"
                disabled={!targetId}
              >
                <option value={0}>アカウントを選択</option>
                {filteredAccounts.map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.displayName}{account.accountIdentifier ? ` (${account.accountIdentifier})` : ""}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="flex flex-wrap gap-2">
            <button className="ui-secondary-button" onClick={() => setShowQuickTarget((value) => !value)}>＋ 投稿先を追加</button>
            <button className="ui-secondary-button" onClick={() => setShowQuickAccount((value) => !value)} disabled={!targetId}>＋ アカウントを追加</button>
          </div>

          {showQuickTarget && (
            <div className="rounded-xl border border-border p-3 space-y-2" style={{ backgroundColor: "hsl(var(--muted))" }}>
              <p className="text-xs font-medium">投稿先をその場で追加</p>
              <div className="grid grid-cols-[minmax(0,1fr)_120px_auto] gap-2">
                <input className="ui-input min-w-0" value={newTargetName} onChange={(event) => setNewTargetName(event.target.value)} placeholder="例: Pixiv" />
                <select className="ui-input" value={newTargetKind} onChange={(event) => setNewTargetKind(event.target.value)}>
                  <option value="twitter">X</option>
                  <option value="pixiv">Pixiv</option>
                  <option value="misskey">Misskey</option>
                  <option value="bluesky">Bluesky</option>
                  <option value="other">その他</option>
                </select>
                <button className="ui-primary-button" onClick={() => void quickCreateTarget()}>追加</button>
              </div>
            </div>
          )}

          {showQuickAccount && targetId > 0 && (
            <div className="rounded-xl border border-border p-3 space-y-2" style={{ backgroundColor: "hsl(var(--muted))" }}>
              <p className="text-xs font-medium">この投稿先のアカウントを追加</p>
              <div className="grid grid-cols-1 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] gap-2">
                <input className="ui-input min-w-0" value={newAccountDisplay} onChange={(event) => setNewAccountDisplay(event.target.value)} placeholder="表示名" />
                <input className="ui-input min-w-0" value={newAccountIdentifier} onChange={(event) => setNewAccountIdentifier(event.target.value)} placeholder="@username / ID (任意)" />
                <button className="ui-primary-button" onClick={() => void quickCreateAccount()}>追加</button>
              </div>
            </div>
          )}

          <label className="block space-y-1.5">
            <span className="ui-label">記録名 <span className="font-normal text-muted-foreground">(任意)</span></span>
            <input
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              className="ui-input w-full"
              placeholder="例: 2026/09/04 Pixiv投稿"
            />
          </label>

          <label className="block space-y-1.5">
            <span className="ui-label">投稿URL / 外部ID <span className="font-normal text-muted-foreground">(任意)</span></span>
            <input
              value={externalPostId}
              onChange={(event) => setExternalPostId(event.target.value)}
              className="ui-input w-full"
              placeholder="投稿ページURLや作品IDなど"
            />
            <p className="text-[11px] text-muted-foreground">後から「どこに投稿した画像か」を確認しやすくするための補助情報です。</p>
          </label>

          {error && <div className="rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive">{error}</div>}
        </div>

        <div className="min-h-16 px-4 py-3 border-t border-border flex items-center justify-between gap-3" style={{ backgroundColor: "hsl(var(--card))" }}>
          <p className="text-[11px] text-muted-foreground">画像ファイル自体は変更・移動されません。</p>
          <div className="flex items-center gap-2">
            <button className="ui-secondary-button" onClick={onClose} disabled={busy}>キャンセル</button>
            <button className="ui-primary-button min-w-28" onClick={() => void save()} disabled={busy || !targetId || !accountId || assetIds.length === 0}>
              {busy ? "保存中…" : "投稿記録を保存"}
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body
  );
}
