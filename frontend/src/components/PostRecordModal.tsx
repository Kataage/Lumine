import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import {
  createPostAccount,
  createPostRecord,
  createPostTarget,
  getAssetDetail,
  listPostAccounts,
  listPostTargets,
} from "../api/client";
import type { AssetDTO, PostAccountDTO, PostTargetDTO } from "../api/client";

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

function toLocalDateTimeInput(date = new Date()) {
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

export function PostRecordModal({ assetIds, defaultTitle = "", onClose, onSaved }: PostRecordModalProps) {
  const [targets, setTargets] = useState<PostTargetDTO[]>([]);
  const [accounts, setAccounts] = useState<PostAccountDTO[]>([]);
  const [assets, setAssets] = useState<AssetDTO[]>([]);
  const [orderedIds, setOrderedIds] = useState(assetIds);
  const [targetId, setTargetId] = useState(0);
  const [accountId, setAccountId] = useState(0);
  const [title, setTitle] = useState(defaultTitle);
  const [body, setBody] = useState("");
  const [tags, setTags] = useState<string[]>([]);
  const [tagInput, setTagInput] = useState("");
  const [externalPostId, setExternalPostId] = useState("");
  const [externalUrl, setExternalUrl] = useState("");
  const [publishedAt, setPublishedAt] = useState(toLocalDateTimeInput());
  const [ageRestriction, setAgeRestriction] = useState("全年齢");
  const [aiGenerated, setAiGenerated] = useState(true);
  const [busy, setBusy] = useState(false);
  const [setupBusy, setSetupBusy] = useState(false);
  const [setupLoading, setSetupLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showSetup, setShowSetup] = useState(false);
  const [newTargetName, setNewTargetName] = useState("");
  const [newTargetKind, setNewTargetKind] = useState("pixiv");
  const [newAccountDisplay, setNewAccountDisplay] = useState("");
  const [newAccountIdentifier, setNewAccountIdentifier] = useState("");

  const selectedTarget = targets.find((target) => target.id === targetId);
  const targetKind = selectedTarget?.kind ?? "other";
  const filteredAccounts = useMemo(
    () => accounts.filter((account) => account.postTargetId === targetId && account.isActive),
    [accounts, targetId]
  );

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
    void Promise.all(assetIds.map((id) => getAssetDetail(id))).then((items) => setAssets(items.filter((item): item is AssetDTO => Boolean(item))));
    return () => { document.body.style.overflow = previousOverflow; };
  }, []);

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy && !setupBusy) onClose();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [busy, onClose, setupBusy]);

  useEffect(() => {
    if (filteredAccounts.some((account) => account.id === accountId)) return;
    setAccountId(filteredAccounts[0]?.id ?? 0);
    if (targetId > 0 && filteredAccounts.length === 0) setShowSetup(true);
  }, [accountId, filteredAccounts, targetId]);

  const addTag = () => {
    const value = tagInput.trim().replace(/^#/, "");
    if (!value || tags.includes(value)) {
      setTagInput("");
      return;
    }
    setTags((current) => [...current, value]);
    setTagInput("");
  };

  const moveImage = (index: number, delta: -1 | 1) => {
    setOrderedIds((current) => {
      const nextIndex = index + delta;
      if (nextIndex < 0 || nextIndex >= current.length) return current;
      const next = [...current];
      [next[index], next[nextIndex]] = [next[nextIndex], next[index]];
      return next;
    });
  };

  const quickCreateTarget = async () => {
    const name = newTargetName.trim();
    if (!name) { setError("投稿先名を入力してください"); return; }
    setSetupBusy(true); setError(null);
    try {
      const target = await createPostTarget(name, newTargetKind);
      if (!target) throw new Error("登録結果を取得できませんでした");
      setNewTargetName("");
      await loadSetup(target.id, 0);
    } catch (cause) {
      setError(`投稿先を追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally { setSetupBusy(false); }
  };

  const quickCreateAccount = async () => {
    const displayName = newAccountDisplay.trim();
    if (!targetId) { setError("投稿先を選択してください"); return; }
    if (!displayName) { setError("アカウント名を入力してください"); return; }
    setSetupBusy(true); setError(null);
    try {
      const account = await createPostAccount(targetId, displayName, newAccountIdentifier.trim());
      if (!account) throw new Error("登録結果を取得できませんでした");
      setNewAccountDisplay(""); setNewAccountIdentifier("");
      await loadSetup(targetId, account.id);
      setShowSetup(false);
    } catch (cause) {
      setError(`アカウントを追加できませんでした: ${cause instanceof Error ? cause.message : String(cause)}`);
    } finally { setSetupBusy(false); }
  };

  const save = async () => {
    if (orderedIds.length === 0) { setError("画像が選択されていません"); return; }
    if (!targetId) { setError("投稿先を選択してください"); setShowSetup(true); return; }
    if (!accountId) { setError("アカウントを選択してください"); setShowSetup(true); return; }
    if (targetKind === "pixiv" && !title.trim()) { setError("Pixivの投稿タイトルを入力してください"); return; }

    setBusy(true); setError(null);
    try {
      const metadata: Record<string, unknown> = {};
      if (targetKind === "pixiv") {
        metadata.ageRestriction = ageRestriction;
        metadata.aiGenerated = aiGenerated;
      }
      const publishedISO = publishedAt ? new Date(publishedAt).toISOString() : "";
      const record = await createPostRecord({
        assetIds: orderedIds,
        targetId,
        accountId,
        title: title.trim(),
        body: body.trim(),
        hashtags: tags.join("\n"),
        platformMetadataJson: JSON.stringify(metadata),
        externalPostId: externalPostId.trim(),
        externalUrl: externalUrl.trim(),
        publishedAt: publishedISO,
      });
      if (!record) throw new Error("公開記録を保存できませんでした");
      onSaved?.(); onClose();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally { setBusy(false); }
  };

  return createPortal(
    <div className="fixed inset-0 z-[110] flex items-center justify-center p-4 bg-black/85 backdrop-blur-sm" onMouseDown={(event) => { if (event.currentTarget === event.target && !busy && !setupBusy) onClose(); }}>
      <div className="w-full max-w-3xl max-h-[calc(100dvh-32px)] flex flex-col overflow-hidden rounded-2xl border border-border bg-card text-card-foreground shadow-2xl" role="dialog" aria-modal="true" aria-label="公開記録を追加">
        <div className="h-14 px-4 border-b border-border flex items-center justify-between gap-3 flex-shrink-0">
          <div className="min-w-0 flex items-center gap-2"><h2 className="text-sm font-semibold">公開記録</h2><span className="rounded-full bg-muted px-2 py-0.5 text-[9px] text-muted-foreground">{orderedIds.length}枚</span></div>
          <button type="button" onClick={onClose} disabled={busy || setupBusy} className="ui-icon-button text-lg" aria-label="閉じる">×</button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto p-4 space-y-5">
          {error && <div role="alert" className="rounded-xl border border-destructive/40 bg-destructive/10 px-3 py-2.5 text-xs text-destructive whitespace-pre-wrap">{error}</div>}

          <section className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <label className="space-y-1.5"><span className="ui-label">投稿先</span><select value={targetId} onChange={(event) => setTargetId(Number(event.target.value))} className="ui-input w-full"><option value={0}>選択</option>{targets.map((target) => <option key={target.id} value={target.id}>{target.name}</option>)}</select></label>
              <label className="space-y-1.5"><span className="ui-label">アカウント</span><select value={accountId} onChange={(event) => setAccountId(Number(event.target.value))} className="ui-input w-full" disabled={!targetId}><option value={0}>選択</option>{filteredAccounts.map((account) => <option key={account.id} value={account.id}>{account.displayName}{account.accountIdentifier ? ` · ${account.accountIdentifier}` : ""}</option>)}</select></label>
            </div>
            <div className="flex justify-end"><button type="button" className="text-[11px] text-muted-foreground hover:text-foreground" onClick={() => setShowSetup((value) => !value)}>{showSetup ? "投稿先設定を閉じる" : "投稿先・アカウントを設定"}</button></div>
            {setupLoading && <p className="text-[11px] text-muted-foreground">投稿先設定を読み込み中…</p>}
            {showSetup && <div className="rounded-xl border border-border bg-muted/10 p-3 space-y-3">
              <div className="grid grid-cols-[minmax(0,1fr)_120px_auto] gap-2"><input className="ui-input" value={newTargetName} onChange={(event) => setNewTargetName(event.target.value)} placeholder="投稿先名" /><select className="ui-input" value={newTargetKind} onChange={(event) => setNewTargetKind(event.target.value)}>{POST_KIND_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}</select><button className="ui-secondary-button" type="button" onClick={() => void quickCreateTarget()} disabled={setupBusy}>投稿先追加</button></div>
              <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] gap-2"><input className="ui-input" value={newAccountDisplay} onChange={(event) => setNewAccountDisplay(event.target.value)} placeholder="アカウント表示名" disabled={!targetId} /><input className="ui-input" value={newAccountIdentifier} onChange={(event) => setNewAccountIdentifier(event.target.value)} placeholder="@ID（任意）" disabled={!targetId} /><button className="ui-secondary-button" type="button" onClick={() => void quickCreateAccount()} disabled={setupBusy || !targetId}>アカウント追加</button></div>
            </div>}
          </section>

          <section className="rounded-xl border border-border bg-muted/10 p-3 space-y-2.5">
            <p className="text-xs font-semibold">投稿画像と順序</p>
            <div className="space-y-1.5">{orderedIds.map((id, index) => {
              const asset = assets.find((item) => item.id === id);
              return <div key={id} className="flex items-center gap-2 rounded-lg bg-background/60 p-2"><span className="w-5 text-right text-[10px] text-muted-foreground">{index + 1}</span><span className="min-w-0 flex-1 truncate text-[11px]">{asset?.fileName ?? `Asset #${id}`}</span><button className="ui-mini-button" type="button" onClick={() => moveImage(index, -1)} disabled={index === 0}>↑</button><button className="ui-mini-button" type="button" onClick={() => moveImage(index, 1)} disabled={index === orderedIds.length - 1}>↓</button></div>;
            })}</div>
          </section>

          <section className="space-y-3">
            <label className="block space-y-1.5"><span className="ui-label">{targetKind === "twitter" ? "管理名（任意）" : "タイトル"}</span><input className="ui-input w-full" value={title} onChange={(event) => setTitle(event.target.value)} placeholder={targetKind === "pixiv" ? "Pixivのタイトル" : "Lumine内で見分けるタイトル"} /></label>
            <label className="block space-y-1.5"><span className="ui-label">{targetKind === "pixiv" ? "キャプション / 詳細" : targetKind === "twitter" ? "投稿本文" : "本文 / 詳細"}</span><textarea className="ui-input w-full min-h-32 resize-y leading-relaxed" value={body} onChange={(event) => setBody(event.target.value)} placeholder="実際に投稿した文章" /></label>
            <div className="space-y-1.5"><span className="ui-label">{targetKind === "pixiv" ? "タグ" : "タグ / ハッシュタグ"}</span><div className="flex flex-wrap gap-1.5">{tags.map((tag) => <button key={tag} type="button" onClick={() => setTags((current) => current.filter((item) => item !== tag))} className="rounded-full border border-border bg-muted px-2.5 py-1 text-[10px] hover:border-destructive/40">#{tag} ×</button>)}</div><input className="ui-input w-full" value={tagInput} onChange={(event) => setTagInput(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === ",") { event.preventDefault(); addTag(); } }} onBlur={addTag} placeholder="入力して Enter（複数可）" /></div>
          </section>

          {targetKind === "pixiv" && <section className="rounded-xl border border-border bg-muted/10 p-3 space-y-3"><p className="text-xs font-semibold">Pixiv投稿情報</p><div className="grid grid-cols-1 sm:grid-cols-2 gap-3"><label className="space-y-1.5"><span className="ui-label">年齢制限</span><select className="ui-input w-full" value={ageRestriction} onChange={(event) => setAgeRestriction(event.target.value)}><option>全年齢</option><option>R-18</option><option>R-18G</option></select></label><label className="flex items-center justify-between gap-3 rounded-lg border border-border bg-background/50 px-3 py-2"><span><span className="block text-[11px] font-medium">AI生成作品</span></span><input type="checkbox" checked={aiGenerated} onChange={(event) => setAiGenerated(event.target.checked)} /></label></div></section>}

          <section className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <label className="space-y-1.5"><span className="ui-label">投稿日時</span><input type="datetime-local" className="ui-input w-full" value={publishedAt} onChange={(event) => setPublishedAt(event.target.value)} /></label>
            <label className="space-y-1.5"><span className="ui-label">作品 / 投稿ID <span className="font-normal text-muted-foreground">任意</span></span><input className="ui-input w-full" value={externalPostId} onChange={(event) => setExternalPostId(event.target.value)} placeholder="123456789" /></label>
            <label className="space-y-1.5 sm:col-span-2"><span className="ui-label">投稿URL <span className="font-normal text-muted-foreground">任意</span></span><input className="ui-input w-full" value={externalUrl} onChange={(event) => setExternalUrl(event.target.value)} placeholder="https://..." /></label>
          </section>
        </div>

        <div className="px-4 py-3 border-t border-border flex items-center justify-between gap-3 flex-shrink-0"><p className="text-[9px] text-muted-foreground">保存後は公開時点の内容を保持</p><div className="flex gap-2"><button type="button" className="ui-secondary-button" onClick={onClose} disabled={busy || setupBusy}>キャンセル</button><button type="button" className="ui-primary-button min-w-24" onClick={() => void save()} disabled={busy || setupBusy}>{busy ? "保存中…" : "公開記録を保存"}</button></div></div>
      </div>
    </div>, document.body
  );
}
