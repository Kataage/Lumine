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

const POST_KIND_OPTIONS = [
  { value: "pixiv", label: "Pixiv" },
  { value: "twitter", label: "X" },
  { value: "misskey", label: "Misskey" },
  { value: "bluesky", label: "Bluesky" },
  { value: "other", label: "その他" },
];

const postKindLabel = (kind: string) => POST_KIND_OPTIONS.find((option) => option.value === kind)?.label ?? kind;

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
  const [showSetup, setShowSetup] = useState(false);
  const [showTitle, setShowTitle] = useState(Boolean(defaultTitle));
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
      setShowSetup(safeTargets.length === 0 || matchingAccounts.length === 0);
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
    if (targetId > 0 && filteredAccounts.length === 0) setShowSetup(true);
  }, [accountId, filteredAccounts, targetId]);

  const quickCreateTarget = async () => {
    const name = newTargetName.trim();
    if (!name) {
      setError("投稿先名を入力してください");
      return;
    }
    setSetupBusy(true);
    setError(null);
    setNotice(null);
    try {
      const target = await createPostTarget(name, newTargetKind);
      if (!target) throw new Error("登録結果を取得できませんでした");
      setNewTargetName("");
      await loadSetup(target.id, 0);
      setNotice(`${target.name} を追加しました`);
    } catch (cause) {
      setError(`投稿先を追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setSetupBusy(false);
    }
  };

  const quickCreateAccount = async () => {
    const displayName = newAccountDisplay.trim();
    if (!targetId) {
      setError("投稿先を選択してください");
      return;
    }
    if (!displayName) {
      setError("アカウント名を入力してください");
      return;
    }
    setSetupBusy(true);
    setError(null);
    setNotice(null);
    try {
      const account = await createPostAccount(targetId, displayName, newAccountIdentifier.trim());
      if (!account) throw new Error("登録結果を取得できませんでした");
      setNewAccountDisplay("");
      setNewAccountIdentifier("");
      await loadSetup(targetId, account.id);
      setShowSetup(false);
      setNotice(`${account.displayName} を追加しました`);
    } catch (cause) {
      setError(`アカウントを追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally {
      setSetupBusy(false);
    }
  };

  const save = async () => {
    if (assetIds.length === 0) {
      setError("画像が選択されていません");
      return;
    }
    if (!targetId) {
      setError("投稿先を選択してください");
      setShowSetup(true);
      return;
    }
    if (!accountId) {
      setError("アカウントを選択してください");
      setShowSetup(true);
      return;
    }

    setBusy(true);
    setError(null);
    setNotice(null);
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
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  };

  return createPortal(
    <div
      className="fixed inset-0 z-[110] flex items-center justify-center p-4"
      style={{ backgroundColor: "rgba(0, 0, 0, 0.84)", backdropFilter: "blur(3px)" }}
      onMouseDown={(event) => { if (event.currentTarget === event.target && !busy && !setupBusy) onClose(); }}
    >
      <div
        className="w-full max-w-lg max-h-[calc(100dvh-32px)] flex flex-col overflow-hidden rounded-2xl border border-border shadow-2xl isolate"
        style={{ backgroundColor: "hsl(var(--card))", color: "hsl(var(--card-foreground))", boxShadow: "0 24px 80px rgba(0, 0, 0, 0.72)" }}
        role="dialog"
        aria-modal="true"
        aria-label="投稿を記録"
      >
        <div className="h-14 px-4 border-b border-border flex items-center justify-between gap-3 flex-shrink-0">
          <div className="flex items-center gap-2 min-w-0">
            <h2 className="text-sm font-semibold">投稿を記録</h2>
            <span className="rounded-full bg-muted px-2 py-0.5 text-[10px] text-muted-foreground">{assetIds.length}件</span>
          </div>
          <button type="button" onClick={onClose} disabled={busy || setupBusy} className="ui-icon-button text-lg" aria-label="閉じる">×</button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto p-4 space-y-4">
          {setupLoading ? (
            <div className="flex items-center gap-2 py-4 text-xs text-muted-foreground">
              <span className="w-4 h-4 border-2 border-muted-foreground/25 border-t-primary rounded-full animate-spin" />
              読み込み中…
            </div>
          ) : (
            <>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <label className="space-y-1.5">
                  <span className="ui-label">投稿先</span>
                  <select value={targetId} onChange={(event) => setTargetId(Number(event.target.value))} className="ui-input w-full">
                    <option value={0}>選択</option>
                    {targets.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}
                  </select>
                </label>
                <label className="space-y-1.5">
                  <span className="ui-label">アカウント</span>
                  <select value={accountId} onChange={(event) => setAccountId(Number(event.target.value))} className="ui-input w-full" disabled={!targetId}>
                    <option value={0}>選択</option>
                    {filteredAccounts.map((account) => (
                      <option key={account.id} value={account.id}>
                        {account.displayName}{account.accountIdentifier ? ` · ${account.accountIdentifier}` : ""}
                      </option>
                    ))}
                  </select>
                </label>
              </div>

              <div className="flex items-center justify-between gap-3">
                <div className="min-w-0 text-[11px] text-muted-foreground truncate">
                  {targetId > 0
                    ? `${targets.find((target) => target.id === targetId)?.name ?? "投稿先"}${filteredAccounts.length ? ` · ${filteredAccounts.length}アカウント` : " · アカウント未登録"}`
                    : "投稿先を選択"}
                </div>
                <button
                  type="button"
                  className="ui-secondary-button flex-shrink-0"
                  aria-expanded={showSetup}
                  onClick={() => { setShowSetup((value) => !value); setError(null); setNotice(null); }}
                >
                  {showSetup ? "設定を閉じる" : "投稿先設定"}
                </button>
              </div>

              {showSetup && (
                <div className="rounded-xl border border-border bg-muted/10 p-3 space-y-3">
                  <div className="space-y-2">
                    <p className="text-[11px] font-semibold">投稿先を追加</p>
                    <div className="flex gap-2">
                      <input
                        aria-label="新しい投稿先名"
                        className="ui-input min-w-0 flex-1"
                        value={newTargetName}
                        onChange={(event) => setNewTargetName(event.target.value)}
                        placeholder="Pixiv / X など"
                      />
                      <select aria-label="新しい投稿先の種類" className="ui-input w-28" value={newTargetKind} onChange={(event) => setNewTargetKind(event.target.value)}>
                        {POST_KIND_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                      </select>
                      <button type="button" className="ui-primary-button" onClick={() => void quickCreateTarget()} disabled={setupBusy}>追加</button>
                    </div>
                  </div>

                  <div className="border-t border-border pt-3 space-y-2">
                    <p className="text-[11px] font-semibold">アカウントを追加</p>
                    <div className="grid grid-cols-1 sm:grid-cols-[1fr_1fr_auto] gap-2">
                      <input
                        aria-label="新しいアカウント表示名"
                        className="ui-input"
                        value={newAccountDisplay}
                        onChange={(event) => setNewAccountDisplay(event.target.value)}
                        placeholder="表示名"
                        disabled={!targetId}
                      />
                      <input
                        aria-label="新しいアカウントID"
                        className="ui-input"
                        value={newAccountIdentifier}
                        onChange={(event) => setNewAccountIdentifier(event.target.value)}
                        placeholder="@ID（任意）"
                        disabled={!targetId}
                      />
                      <button type="button" className="ui-primary-button" onClick={() => void quickCreateAccount()} disabled={setupBusy || !targetId}>追加</button>
                    </div>
                  </div>

                  {targets.length > 0 && (
                    <div className="flex flex-wrap gap-1.5 pt-1">
                      {targets.map((target) => (
                        <span key={target.id} className="rounded-md bg-background/70 px-2 py-1 text-[10px] text-muted-foreground">
                          {target.name} · {postKindLabel(target.kind)}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </>
          )}

          {error && <div role="alert" className="rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-xs text-destructive">{error}</div>}
          {notice && <div role="status" className="rounded-lg border border-primary/25 bg-primary/5 px-3 py-2 text-xs">{notice}</div>}

          <label className="block space-y-1.5">
            <span className="ui-label">投稿URL / ID <span className="font-normal text-muted-foreground">任意</span></span>
            <input value={externalPostId} onChange={(event) => setExternalPostId(event.target.value)} className="ui-input w-full" placeholder="URL、作品ID、投稿IDなど" />
          </label>

          <div>
            <button type="button" className="text-[11px] text-muted-foreground hover:text-foreground" onClick={() => setShowTitle((value) => !value)} aria-expanded={showTitle}>
              {showTitle ? "− 記録名を隠す" : "＋ 記録名を追加"}
            </button>
            {showTitle && (
              <input
                aria-label="記録名"
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                className="ui-input w-full mt-2"
                placeholder="任意のメモ名"
              />
            )}
          </div>
        </div>

        <div className="px-4 py-3 border-t border-border flex items-center justify-end gap-2 flex-shrink-0">
          <button type="button" className="ui-secondary-button" onClick={onClose} disabled={busy || setupBusy}>キャンセル</button>
          <button
            type="button"
            className="ui-primary-button min-w-24"
            onClick={() => void save()}
            disabled={busy || setupBusy || setupLoading || assetIds.length === 0 || !targetId || !accountId}
          >
            {busy ? "保存中…" : "保存"}
          </button>
        </div>
      </div>
    </div>,
    document.body
  );
}
