import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  addAssetsToGenerationGroup,
  addAssetsToWork,
  createGenerationGroup,
  createWork,
  deleteAssetRelation,
  getAssetCreativeContext,
  listGenerationGroups,
  listWorks,
} from "../api/client";
import { useAppDialog } from "./AppDialogProvider";

const RELATION_LABELS: Record<string, string> = {
  variation: "バリエーション",
  img2img: "img2img",
  inpaint: "Inpaint",
  outpaint: "Outpaint",
  upscale: "アップスケール",
  crop: "クロップ",
  edit: "編集",
  animation: "動画化",
  reference: "参照",
  derived: "派生",
};

export function CreativeContextPanel({ assetId }: { assetId: number }) {
  const queryClient = useQueryClient();
  const dialog = useAppDialog();
  const [showWorkEditor, setShowWorkEditor] = useState(false);
  const [showGroupEditor, setShowGroupEditor] = useState(false);
  const [newWorkTitle, setNewWorkTitle] = useState("");
  const [newWorkDescription, setNewWorkDescription] = useState("");
  const [selectedWorkId, setSelectedWorkId] = useState(0);
  const [newGroupName, setNewGroupName] = useState("");
  const [newGroupPrompt, setNewGroupPrompt] = useState("");
  const [selectedGroupId, setSelectedGroupId] = useState(0);
  const [busy, setBusy] = useState(false);

  const { data: context = { works: [], groups: [], relations: [] }, isLoading } = useQuery({
    queryKey: ["assetCreativeContext", assetId],
    queryFn: () => getAssetCreativeContext(assetId),
    enabled: assetId > 0,
  });
  const { data: allWorks = [] } = useQuery({ queryKey: ["works"], queryFn: () => listWorks(200), staleTime: 30_000 });
  const { data: allGroups = [] } = useQuery({ queryKey: ["generationGroups"], queryFn: () => listGenerationGroups(200), staleTime: 30_000 });

  const availableWorks = useMemo(() => {
    const current = new Set(context.works.map((work) => work.id));
    return allWorks.filter((work) => !current.has(work.id));
  }, [allWorks, context.works]);
  const availableGroups = useMemo(() => {
    const current = new Set(context.groups.map((group) => group.id));
    return allGroups.filter((group) => !current.has(group.id));
  }, [allGroups, context.groups]);

  const refresh = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["assetCreativeContext", assetId] }),
      queryClient.invalidateQueries({ queryKey: ["works"] }),
      queryClient.invalidateQueries({ queryKey: ["generationGroups"] }),
    ]);
  };

  const createNewWork = async () => {
    const title = newWorkTitle.trim();
    if (!title || busy) return;
    setBusy(true);
    try {
      const work = await createWork(title, newWorkDescription.trim(), [assetId]);
      if (!work) throw new Error("作品を作成できませんでした");
      setNewWorkTitle("");
      setNewWorkDescription("");
      setShowWorkEditor(false);
      await refresh();
    } catch (cause) {
      await dialog.notify({ title: "作品を作成できませんでした", description: "入力内容とデータベースの状態を確認してください。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
    } finally {
      setBusy(false);
    }
  };

  const addToWork = async () => {
    if (!selectedWorkId || busy) return;
    setBusy(true);
    try {
      await addAssetsToWork(selectedWorkId, [assetId]);
      setSelectedWorkId(0);
      setShowWorkEditor(false);
      await refresh();
    } catch (cause) {
      await dialog.notify({ title: "作品へ追加できませんでした", description: "画像の紐付けに失敗しました。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
    } finally {
      setBusy(false);
    }
  };

  const createNewGroup = async () => {
    const name = newGroupName.trim();
    if (!name || busy) return;
    setBusy(true);
    try {
      const group = await createGenerationGroup({
        assetIds: [assetId], name, prompt: newGroupPrompt.trim(), negativePrompt: "", modelName: "", sampler: "", scheduler: "", steps: 0, cfgScale: 0, workflowJson: "", notes: "",
      });
      if (!group) throw new Error("生成グループを作成できませんでした");
      setNewGroupName("");
      setNewGroupPrompt("");
      setShowGroupEditor(false);
      await refresh();
    } catch (cause) {
      await dialog.notify({ title: "生成グループを作成できませんでした", description: "グループ情報を保存できませんでした。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
    } finally {
      setBusy(false);
    }
  };

  const addToGroup = async () => {
    if (!selectedGroupId || busy) return;
    setBusy(true);
    try {
      await addAssetsToGenerationGroup(selectedGroupId, [assetId]);
      setSelectedGroupId(0);
      setShowGroupEditor(false);
      await refresh();
    } catch (cause) {
      await dialog.notify({ title: "生成グループへ追加できませんでした", description: "画像の紐付けに失敗しました。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
    } finally {
      setBusy(false);
    }
  };

  const removeRelation = async (relationId: number, description: string) => {
    const approved = await dialog.confirm({ title: "関係性を削除しますか？", description, confirmLabel: "関係性を削除", tone: "danger" });
    if (!approved) return;
    try {
      await deleteAssetRelation(relationId, [assetId]);
      await refresh();
    } catch (cause) {
      await dialog.notify({ title: "関係性を削除できませんでした", description: "データベースの更新に失敗しました。", detail: cause instanceof Error ? cause.message : String(cause), tone: "danger" });
    }
  };

  if (isLoading) return <div className="rounded-xl border border-border bg-muted/10 p-3 text-[11px] text-muted-foreground">制作情報を読み込み中…</div>;

  return (
    <div className="space-y-3">
      <section className="rounded-xl border border-border bg-muted/10 p-3 space-y-2.5">
        <div className="flex items-center justify-between gap-2">
          <div><p className="text-xs font-semibold">作品</p><p className="text-[10px] text-muted-foreground">ファイルではなく、人が認識する作品単位</p></div>
          <button type="button" className="ui-mini-button" onClick={() => setShowWorkEditor((value) => !value)}>{showWorkEditor ? "閉じる" : "整理"}</button>
        </div>
        {context.works.length > 0 ? context.works.map((work) => (
          <div key={work.id} className="rounded-lg bg-background/60 px-2.5 py-2">
            <div className="flex items-center justify-between gap-2"><p className="text-[11px] font-medium truncate">{work.title}</p><span className="text-[10px] text-muted-foreground">{work.assetIds.length}枚</span></div>
            {work.description && <p className="mt-1 text-[10px] leading-relaxed text-muted-foreground line-clamp-2">{work.description}</p>}
          </div>
        )) : <p className="text-[11px] text-muted-foreground">まだ作品に紐付いていません。</p>}
        {showWorkEditor && (
          <div className="border-t border-border pt-2.5 space-y-2.5">
            {availableWorks.length > 0 && <div className="flex gap-2"><select className="ui-input min-w-0 flex-1" value={selectedWorkId} onChange={(event) => setSelectedWorkId(Number(event.target.value))}><option value={0}>既存作品を選択</option>{availableWorks.map((work) => <option key={work.id} value={work.id}>{work.title}</option>)}</select><button className="ui-secondary-button" type="button" disabled={!selectedWorkId || busy} onClick={() => void addToWork()}>追加</button></div>}
            <div className="space-y-2"><input className="ui-input w-full" value={newWorkTitle} onChange={(event) => setNewWorkTitle(event.target.value)} placeholder="新しい作品名" /><textarea className="ui-input w-full min-h-16 resize-y" value={newWorkDescription} onChange={(event) => setNewWorkDescription(event.target.value)} placeholder="作品の説明（任意）" /><button type="button" className="ui-primary-button w-full" disabled={!newWorkTitle.trim() || busy} onClick={() => void createNewWork()}>この画像から作品を作成</button></div>
          </div>
        )}
      </section>

      <section className="rounded-xl border border-border bg-muted/10 p-3 space-y-2.5">
        <div className="flex items-center justify-between gap-2">
          <div><p className="text-xs font-semibold">生成グループ</p><p className="text-[10px] text-muted-foreground">同じ生成意図・プロンプト系列の画像群</p></div>
          <button type="button" className="ui-mini-button" onClick={() => setShowGroupEditor((value) => !value)}>{showGroupEditor ? "閉じる" : "整理"}</button>
        </div>
        {context.groups.length > 0 ? context.groups.map((group) => (
          <div key={group.id} className="rounded-lg bg-background/60 px-2.5 py-2 space-y-1">
            <div className="flex items-center justify-between gap-2"><p className="text-[11px] font-medium truncate">{group.name}</p><span className="text-[10px] text-muted-foreground">{group.assetIds.length}枚</span></div>
            {(group.modelName || group.steps > 0 || group.cfgScale > 0) && <p className="text-[10px] text-muted-foreground">{[group.modelName, group.steps > 0 ? `${group.steps} steps` : "", group.cfgScale > 0 ? `CFG ${group.cfgScale}` : ""].filter(Boolean).join(" · ")}</p>}
            {group.prompt && <p className="text-[10px] leading-relaxed text-muted-foreground line-clamp-2">{group.prompt}</p>}
          </div>
        )) : <p className="text-[11px] text-muted-foreground">生成グループは未設定です。</p>}
        {showGroupEditor && (
          <div className="border-t border-border pt-2.5 space-y-2.5">
            {availableGroups.length > 0 && <div className="flex gap-2"><select className="ui-input min-w-0 flex-1" value={selectedGroupId} onChange={(event) => setSelectedGroupId(Number(event.target.value))}><option value={0}>既存グループを選択</option>{availableGroups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select><button className="ui-secondary-button" type="button" disabled={!selectedGroupId || busy} onClick={() => void addToGroup()}>追加</button></div>}
            <div className="space-y-2"><input className="ui-input w-full" value={newGroupName} onChange={(event) => setNewGroupName(event.target.value)} placeholder="新しいグループ名" /><textarea className="ui-input w-full min-h-20 resize-y font-mono text-[10px]" value={newGroupPrompt} onChange={(event) => setNewGroupPrompt(event.target.value)} placeholder="共通プロンプト（任意）" /><button type="button" className="ui-primary-button w-full" disabled={!newGroupName.trim() || busy} onClick={() => void createNewGroup()}>この画像から生成グループを作成</button></div>
          </div>
        )}
      </section>

      <section className="rounded-xl border border-border bg-muted/10 p-3 space-y-2.5">
        <div><p className="text-xs font-semibold">派生関係</p><p className="text-[10px] text-muted-foreground">どの画像から、どの処理で派生したか</p></div>
        {context.relations.length > 0 ? context.relations.map((relation) => {
          const currentIsParent = relation.parentAssetId === assetId;
          const otherName = currentIsParent ? relation.childFileName : relation.parentFileName;
          const direction = currentIsParent ? "→" : "←";
          const description = `${relation.parentFileName} → ${relation.childFileName}（${RELATION_LABELS[relation.relationType] ?? relation.relationType}）`;
          return <div key={relation.id} className="rounded-lg bg-background/60 px-2.5 py-2 group"><div className="flex items-start gap-2"><div className="min-w-0 flex-1"><p className="text-[11px] truncate"><span className="text-primary font-semibold">{direction}</span> {otherName || `Asset #${currentIsParent ? relation.childAssetId : relation.parentAssetId}`}</p><p className="mt-0.5 text-[10px] text-muted-foreground">{RELATION_LABELS[relation.relationType] ?? relation.relationType}{relation.note ? ` · ${relation.note}` : ""}</p></div><button type="button" className="text-[10px] text-muted-foreground opacity-0 group-hover:opacity-100 focus:opacity-100 hover:text-destructive" onClick={() => void removeRelation(relation.id, description)}>削除</button></div></div>;
        }) : <p className="text-[11px] text-muted-foreground">派生関係はありません。2枚を選択すると関係性を作成できます。</p>}
      </section>
    </div>
  );
}
