import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { useApp } from "../App";
import { useAppDialog } from "./AppDialogProvider";
import {
  createPromptProject,
  createPromptVariant,
  createPromptVersion,
  deletePromptProject,
  getAISettings,
  getPromptEngineStatus,
  getPromptProject,
  listModelProfiles,
  listPromptProjects,
  runPromptEngine,
  updatePromptProject,
  type PromptEngineOperation,
  type PromptEngineResult,
  type PromptProject,
  type PromptProjectInput,
  type PromptVariant,
  type PromptVersion,
} from "../api/client";
import { formatPromptLoRALines, parseIDList, parsePromptLoRALines, promptVersionLabel } from "../utils/promptStudio";

function localTime(value: string): string {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("ja-JP");
}

function profileName(profiles: Array<{ id: string; name: string }>, id: string): string {
  return profiles.find((profile) => profile.id === id)?.name ?? (id || "未指定");
}

export function PromptProjectsPanel() {
  const { state, setState } = useApp();
  const [newTitle, setNewTitle] = useState("");
  const [creating, setCreating] = useState(false);
  const { data: projects = [], refetch } = useQuery({
    queryKey: ["promptProjects", false],
    queryFn: () => listPromptProjects(false, 300),
  });

  const createProject = async () => {
    const title = newTitle.trim();
    if (!title || creating) return;
    setCreating(true);
    try {
      const project = await createPromptProject({
        title,
        idea: "",
        notes: "",
        targetProfileId: "illustrious-xl",
        characters: [],
        loras: [],
        referenceAssetIds: [],
        relatedAssetIds: [],
      });
      setNewTitle("");
      setState((current) => ({ ...current, selectedPromptProjectId: project.id }));
      await refetch();
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="p-3 space-y-3">
      <div className="rounded-xl border border-border bg-muted/20 p-2.5">
        <p className="text-[11px] font-semibold">新しいPrompt Project</p>
        <div className="mt-2 flex gap-1.5">
          <input
            className="ui-input flex-1"
            value={newTitle}
            onChange={(event) => setNewTitle(event.target.value)}
            onKeyDown={(event) => { if (event.key === "Enter") void createProject(); }}
            placeholder="プロジェクト名"
          />
          <button type="button" className="ui-primary-button" disabled={!newTitle.trim() || creating} onClick={() => void createProject()}>
            ＋
          </button>
        </div>
      </div>

      <div>
        <div className="mb-1.5 flex items-center justify-between">
          <span className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">Projects</span>
          <span className="text-[10px] text-muted-foreground">{projects.length}</span>
        </div>
        <div className="space-y-1">
          {projects.map((project) => (
            <button
              type="button"
              key={project.id}
              onClick={() => setState((current) => ({ ...current, selectedPromptProjectId: project.id }))}
              className={`w-full rounded-lg border px-2.5 py-2 text-left transition-colors ${
                state.selectedPromptProjectId === project.id
                  ? "border-primary/30 bg-primary/10 text-foreground"
                  : "border-transparent text-muted-foreground hover:border-border hover:bg-accent/50 hover:text-foreground"
              }`}
            >
              <span className="block truncate text-xs font-medium">{project.title}</span>
              <span className="mt-0.5 block truncate text-[10px] opacity-70">
                {project.targetProfileId || "Profile未指定"} · {localTime(project.updatedAt)}
              </span>
            </button>
          ))}
          {projects.length === 0 && (
            <p className="rounded-lg border border-dashed border-border p-3 text-center text-[10px] text-muted-foreground">
              Prompt Projectはまだありません
            </p>
          )}
        </div>
      </div>
    </div>
  );
}

function EditorLabel({ children }: { children: string }) {
  return <label className="ui-label mb-1">{children}</label>;
}

function MiniInfo({ children }: { children: ReactNode }) {
  return <span className="rounded-md border border-border bg-muted/40 px-2 py-1 text-[10px] text-muted-foreground">{children}</span>;
}

export function PromptStudioWorkspace() {
  const { state, setState } = useApp();
  const dialog = useAppDialog();
  const projectID = state.selectedPromptProjectId ?? 0;

  const { data: project, refetch: refetchProject } = useQuery({
    queryKey: ["promptProject", projectID],
    queryFn: () => getPromptProject(projectID),
    enabled: projectID > 0,
  });
  const { data: profiles = [] } = useQuery({
    queryKey: ["modelProfiles"],
    queryFn: listModelProfiles,
    staleTime: 60_000,
  });
  const { data: aiSettings } = useQuery({
    queryKey: ["aiSettings"],
    queryFn: getAISettings,
    staleTime: 5_000,
  });
  const { data: engineStatus } = useQuery({
    queryKey: ["promptEngineStatus"],
    queryFn: getPromptEngineStatus,
    refetchInterval: 5_000,
  });

  const [title, setTitle] = useState("");
  const [idea, setIdea] = useState("");
  const [notes, setNotes] = useState("");
  const [targetProfileID, setTargetProfileID] = useState("");
  const [charactersText, setCharactersText] = useState("");
  const [lorasText, setLorasText] = useState("");
  const [referenceIDsText, setReferenceIDsText] = useState("");
  const [relatedIDsText, setRelatedIDsText] = useState("");
  const [positive, setPositive] = useState("");
  const [negative, setNegative] = useState("");
  const [instruction, setInstruction] = useState("");
  const [selectedVariantID, setSelectedVariantID] = useState(0);
  const [activeVersionID, setActiveVersionID] = useState(0);
  const [newVariantName, setNewVariantName] = useState("");
  const [busy, setBusy] = useState(false);
  const [aiResult, setAIResult] = useState<PromptEngineResult | null>(null);

  useEffect(() => {
    if (!project) return;
    setTitle(project.title);
    setIdea(project.idea);
    setNotes(project.notes);
    setTargetProfileID(project.targetProfileId || profiles[0]?.id || "");
    setCharactersText((project.characters ?? []).join(", "));
    setLorasText(formatPromptLoRALines(project.loras ?? []));
    setReferenceIDsText((project.referenceAssetIds ?? []).join(", "));
    setRelatedIDsText((project.relatedAssetIds ?? []).join(", "));

    const firstVariant = project.variants?.[0];
    setSelectedVariantID(firstVariant?.id ?? 0);
    const latest = firstVariant?.versions?.[0];
    setActiveVersionID(latest?.id ?? 0);
    setPositive(latest?.positive ?? "");
    setNegative(latest?.negative ?? "");
    setInstruction("");
    setAIResult(null);
  }, [project?.id]);

  useEffect(() => {
    if (!project || targetProfileID || profiles.length === 0) return;
    setTargetProfileID(project.targetProfileId || profiles[0].id);
  }, [profiles, project, targetProfileID]);

  const variants = project?.variants ?? [];
  const selectedVariant = variants.find((variant) => variant.id === selectedVariantID) ?? variants[0];
  const versions = selectedVariant?.versions ?? [];
  const activeVersion = useMemo(
    () => versions.find((version) => version.id === activeVersionID) ?? versions[0],
    [versions, activeVersionID],
  );

  const promptEngineEnabled = Boolean(aiSettings?.enabled && aiSettings?.promptEngine);
  const promptRuntimeReady = engineStatus?.runtime.state === "ready" || engineStatus?.runtime.state === "running";
  const aiAvailable = promptEngineEnabled && promptRuntimeReady;

  const statusText = !aiSettings?.enabled
    ? "AI全体がOFFです。手動編集・保存・履歴管理は利用できます。"
    : !aiSettings?.promptEngine
      ? "Prompt EngineがOFFです。手動編集・保存・履歴管理は利用できます。"
      : !promptRuntimeReady
        ? `Prompt Engineは有効ですが、現在のruntime状態は「${engineStatus?.runtime.state ?? "不明"}」です。`
        : `Prompt Engine ready · ${engineStatus?.runtime.modelId ?? "model"}`;

  const projectInput = (): PromptProjectInput => ({
    title: title.trim() || "Untitled Prompt",
    idea,
    notes,
    targetProfileId: targetProfileID,
    characters: charactersText.split(",").map((value) => value.trim()).filter(Boolean),
    loras: parsePromptLoRALines(lorasText),
    referenceAssetIds: parseIDList(referenceIDsText),
    relatedAssetIds: parseIDList(relatedIDsText),
  });

  const saveProjectMetadata = async (): Promise<PromptProject | null> => {
    if (!project) return null;
    const updated = await updatePromptProject(project.id, projectInput());
    await refetchProject();
    return updated;
  };

  const loadVersion = (version: PromptVersion) => {
    setActiveVersionID(version.id);
    setPositive(version.positive);
    setNegative(version.negative);
    setTargetProfileID(version.profileId || project?.targetProfileId || targetProfileID);
    setInstruction("");
    setAIResult(null);
  };

  const switchVariant = (variant: PromptVariant) => {
    setSelectedVariantID(variant.id);
    const latest = variant.versions?.[0];
    setActiveVersionID(latest?.id ?? 0);
    setPositive(latest?.positive ?? "");
    setNegative(latest?.negative ?? "");
    setInstruction("");
    setAIResult(null);
  };

  const saveManualVersion = async () => {
    if (!project || !selectedVariant) return;
    setBusy(true);
    try {
      await saveProjectMetadata();
      const created = await createPromptVersion({
        variantId: selectedVariant.id,
        parentVersionId: activeVersion?.id,
        positive,
        negative,
        source: "manual",
        changeInstruction: instruction.trim(),
        profileId: targetProfileID,
        aiEngine: "",
        aiModelId: "",
        aiModelVersion: "",
        metadataJson: "{}",
      });
      setActiveVersionID(created.id);
      setInstruction("");
      await refetchProject();
    } finally {
      setBusy(false);
    }
  };

  const runAI = async (operation: PromptEngineOperation) => {
    if (!project || !selectedVariant || !aiAvailable || busy) return;
    setBusy(true);
    try {
      await saveProjectMetadata();
      const loraDescriptors = parsePromptLoRALines(lorasText).map((lora) => {
        const triggers = lora.triggerWords.length ? ` triggers: ${lora.triggerWords.join(", ")}` : "";
        return `${lora.name} @ ${lora.weight}${triggers}`;
      });
      const result = await runPromptEngine({
        operation,
        idea,
        positive,
        negative,
        sourceProfileId: activeVersion?.profileId || undefined,
        targetProfileId: targetProfileID || undefined,
        instruction,
        characters: charactersText.split(",").map((value) => value.trim()).filter(Boolean),
        loras: loraDescriptors,
      });
      setPositive(result.positive);
      setNegative(result.negative);
      setAIResult(result);

      const created = await createPromptVersion({
        variantId: selectedVariant.id,
        parentVersionId: activeVersion?.id,
        positive: result.positive,
        negative: result.negative,
        source: "llm",
        changeInstruction: instruction.trim() || operation,
        profileId: targetProfileID,
        aiEngine: result.engine,
        aiModelId: result.modelId,
        aiModelVersion: result.modelVersion,
        metadataJson: JSON.stringify({
          operation,
          characters: result.characters,
          loras: result.loras,
          composition: result.composition,
          notes: result.notes,
          completionTokens: result.completionTokens,
        }),
      });
      setActiveVersionID(created.id);
      setInstruction("");
      await refetchProject();
    } catch (error) {
      await dialog.notify({
        title: "Prompt Engineの処理に失敗しました",
        description: "現在の編集内容は失われていません。",
        detail: error instanceof Error ? error.message : String(error),
        tone: "danger",
      });
    } finally {
      setBusy(false);
    }
  };

  const createVariant = async () => {
    if (!project || !newVariantName.trim()) return;
    setBusy(true);
    try {
      const variant = await createPromptVariant(project.id, newVariantName.trim(), activeVersion?.id ?? 0);
      setNewVariantName("");
      setSelectedVariantID(variant.id);
      setActiveVersionID(variant.versions?.[0]?.id ?? 0);
      if (variant.versions?.[0]) {
        setPositive(variant.versions[0].positive);
        setNegative(variant.versions[0].negative);
      }
      await refetchProject();
    } finally {
      setBusy(false);
    }
  };

  const addSelectedReferences = () => {
    const merged = new Set([...parseIDList(referenceIDsText), ...Array.from(state.selectedAssets)]);
    setReferenceIDsText(Array.from(merged).join(", "));
  };

  const copyPrompt = async () => {
    const text = negative.trim() ? `${positive}\n\nNegative:\n${negative}` : positive;
    await navigator.clipboard.writeText(text);
  };

  const exportProject = () => {
    if (!project) return;
    const payload = {
      exportSchemaVersion: 1,
      exportedAt: new Date().toISOString(),
      project,
      editor: {
        title,
        idea,
        notes,
        targetProfileId: targetProfileID,
        characters: projectInput().characters,
        loras: projectInput().loras,
        referenceAssetIds: projectInput().referenceAssetIds,
        relatedAssetIds: projectInput().relatedAssetIds,
        selectedVariantId: selectedVariant?.id ?? null,
        activeVersionId: activeVersion?.id ?? null,
        positive,
        negative,
      },
    };
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `lumine-prompt-${project.id}.json`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  const removeProject = async () => {
    if (!project) return;
    const approved = await dialog.confirm({
      title: "Prompt Projectを削除しますか？",
      description: "履歴はsoft-deleteされるため、データベース上では復元可能な状態で保持されます。",
      confirmLabel: "削除",
      tone: "danger",
    });
    if (!approved) return;
    await deletePromptProject(project.id);
    setState((current) => ({ ...current, selectedPromptProjectId: null }));
  };

  if (!projectID) {
    return (
      <div className="flex h-full flex-1 items-center justify-center bg-background p-8">
        <div className="max-w-lg rounded-2xl border border-border bg-card p-8 text-center">
          <div className="text-3xl">✦</div>
          <h2 className="mt-3 text-lg font-semibold">Prompt Studio</h2>
          <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
            左側からPrompt Projectを作成または選択してください。AIを使わなくてもPromptの保存・Version履歴・Variant分岐を利用できます。
          </p>
        </div>
      </div>
    );
  }

  if (!project) {
    return <div className="flex h-full flex-1 items-center justify-center text-sm text-muted-foreground">Prompt Projectを読み込んでいます…</div>;
  }

  return (
    <div className="flex h-full min-w-0 flex-1 flex-col bg-background">
      <div className="flex min-h-14 items-center gap-3 border-b border-border bg-card/80 px-4">
        <div className="min-w-0 flex-1">
          <input
            className="w-full bg-transparent text-sm font-semibold outline-none"
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            aria-label="Prompt Project title"
          />
          <p className="mt-0.5 text-[10px] text-muted-foreground">
            Project #{project.id} · 更新 {localTime(project.updatedAt)}
          </p>
        </div>
        <button type="button" className="ui-secondary-button" onClick={() => void saveProjectMetadata()} disabled={busy}>Project保存</button>
        <button type="button" className="ui-secondary-button" onClick={() => void copyPrompt()} disabled={!positive}>コピー</button>
        <button type="button" className="ui-secondary-button" onClick={exportProject}>JSON Export</button>
        <button type="button" className="ui-secondary-button" onClick={() => void removeProject()}>削除</button>
      </div>

      <div className="grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)_18rem]">
        <div className="min-h-0 overflow-y-auto p-4">
          <div className={`mb-4 rounded-xl border p-3 text-xs ${
            aiAvailable ? "border-emerald-500/30 bg-emerald-500/5 text-emerald-200" : "border-border bg-muted/20 text-muted-foreground"
          }`}>
            <div className="flex items-center justify-between gap-3">
              <span>{statusText}</span>
              {!aiAvailable && (
                <button
                  type="button"
                  className="ui-mini-button"
                  onClick={() => setState((current) => ({ ...current, sidebarView: "settings" }))}
                >
                  AI設定
                </button>
              )}
            </div>
          </div>

          <section className="grid gap-3 rounded-2xl border border-border bg-card p-4">
            <div>
              <EditorLabel>Idea / 制作意図</EditorLabel>
              <textarea className="ui-input min-h-24 w-full resize-y" value={idea} onChange={(event) => setIdea(event.target.value)} placeholder="日本語でも可。作りたい絵・構図・雰囲気を自由に記述" />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <EditorLabel>Target Model Profile</EditorLabel>
                <select className="ui-input w-full" value={targetProfileID} onChange={(event) => setTargetProfileID(event.target.value)}>
                  <option value="">未指定</option>
                  {profiles.map((profile) => <option key={profile.id} value={profile.id}>{profile.name}{profile.builtIn ? "" : " (Custom)"}</option>)}
                </select>
              </div>
              <div>
                <EditorLabel>Characters</EditorLabel>
                <input className="ui-input w-full" value={charactersText} onChange={(event) => setCharactersText(event.target.value)} placeholder="character_a, character_b" />
              </div>
            </div>
            <div>
              <EditorLabel>LoRA / Trigger</EditorLabel>
              <textarea
                className="ui-input min-h-20 w-full resize-y font-mono"
                value={lorasText}
                onChange={(event) => setLorasText(event.target.value)}
                placeholder={"1行1LoRA: file.safetensors | 0.8 | trigger1, trigger2"}
              />
            </div>
            <div>
              <EditorLabel>Project Notes</EditorLabel>
              <textarea className="ui-input min-h-16 w-full resize-y" value={notes} onChange={(event) => setNotes(event.target.value)} />
            </div>
          </section>

          <section className="mt-4 rounded-2xl border border-border bg-card p-4">
            <div className="mb-3 flex flex-wrap items-center gap-2">
              <span className="text-xs font-semibold">Prompt Editor</span>
              <MiniInfo>{selectedVariant?.name ?? "Main"}</MiniInfo>
              {activeVersion && <MiniInfo>{profileName(profiles, activeVersion.profileId)}</MiniInfo>}
              {activeVersion?.source && <MiniInfo>{activeVersion.source}</MiniInfo>}
            </div>

            <div>
              <EditorLabel>Positive</EditorLabel>
              <textarea className="ui-input min-h-48 w-full resize-y font-mono leading-relaxed" value={positive} onChange={(event) => setPositive(event.target.value)} placeholder="positive prompt" />
            </div>
            <div className="mt-3">
              <EditorLabel>Negative</EditorLabel>
              <textarea className="ui-input min-h-28 w-full resize-y font-mono leading-relaxed" value={negative} onChange={(event) => setNegative(event.target.value)} placeholder="negative prompt" />
            </div>
            <div className="mt-3">
              <EditorLabel>自然文で差分修正 / Versionメモ</EditorLabel>
              <textarea
                className="ui-input min-h-16 w-full resize-y"
                value={instruction}
                onChange={(event) => setInstruction(event.target.value)}
                placeholder="例: 服装は変えず、背景だけ夜の街に変更"
              />
            </div>

            <div className="mt-3 flex flex-wrap gap-2">
              <button type="button" className="ui-primary-button" onClick={() => void runAI("idea_to_prompt")} disabled={!aiAvailable || !idea.trim() || busy}>生成</button>
              <button type="button" className="ui-secondary-button" onClick={() => void runAI("improve_prompt")} disabled={!aiAvailable || !positive.trim() || busy}>改善</button>
              <button type="button" className="ui-secondary-button" onClick={() => void runAI("convert_prompt")} disabled={!aiAvailable || !positive.trim() || !targetProfileID || busy}>変換</button>
              <button type="button" className="ui-secondary-button" onClick={() => void runAI("edit_prompt")} disabled={!aiAvailable || !positive.trim() || !instruction.trim() || busy}>差分修正</button>
              <div className="flex-1" />
              <button type="button" className="ui-secondary-button" onClick={() => void saveManualVersion()} disabled={!selectedVariant || busy}>手動Version保存</button>
            </div>

            {aiResult && (
              <div className="mt-3 rounded-xl border border-border bg-muted/20 p-3 text-[11px] text-muted-foreground">
                <div className="flex flex-wrap gap-1.5">
                  {aiResult.characters.map((value) => <MiniInfo key={`c-${value}`}>{value}</MiniInfo>)}
                  {aiResult.loras.map((value) => <MiniInfo key={`l-${value}`}>{value}</MiniInfo>)}
                </div>
                {aiResult.composition && <p className="mt-2"><span className="font-semibold text-foreground">Composition:</span> {aiResult.composition}</p>}
                {aiResult.notes.length > 0 && <p className="mt-1"><span className="font-semibold text-foreground">Notes:</span> {aiResult.notes.join(" / ")}</p>}
              </div>
            )}
          </section>

          <section className="mt-4 rounded-2xl border border-border bg-card p-4">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <div className="flex items-center justify-between gap-2">
                  <EditorLabel>Reference image IDs</EditorLabel>
                  <button type="button" className="ui-mini-button" onClick={addSelectedReferences} disabled={state.selectedAssets.size === 0}>
                    Viewer選択中を追加 ({state.selectedAssets.size})
                  </button>
                </div>
                <textarea className="ui-input min-h-16 w-full resize-y" value={referenceIDsText} onChange={(event) => setReferenceIDsText(event.target.value)} placeholder="12, 15, 21" />
              </div>
              <div>
                <EditorLabel>Related generated asset IDs</EditorLabel>
                <textarea className="ui-input min-h-16 w-full resize-y" value={relatedIDsText} onChange={(event) => setRelatedIDsText(event.target.value)} placeholder="生成後のasset ID" />
              </div>
            </div>
          </section>
        </div>

        <aside className="min-h-0 overflow-y-auto border-l border-border bg-card/60 p-3">
          <section>
            <div className="mb-2 flex items-center justify-between">
              <span className="text-[11px] font-semibold">Variants</span>
              <span className="text-[10px] text-muted-foreground">{variants.length}</span>
            </div>
            <div className="space-y-1">
              {variants.map((variant) => (
                <button
                  type="button"
                  key={variant.id}
                  className={`w-full rounded-lg border px-2 py-1.5 text-left text-[11px] ${
                    selectedVariant?.id === variant.id ? "border-primary/30 bg-primary/10" : "border-transparent hover:border-border hover:bg-accent/40"
                  }`}
                  onClick={() => switchVariant(variant)}
                >
                  <span className="font-medium">{variant.name}</span>
                  <span className="ml-2 text-[9px] text-muted-foreground">{variant.versions?.length ?? 0} versions</span>
                </button>
              ))}
            </div>
            <div className="mt-2 flex gap-1.5">
              <input className="ui-input min-w-0 flex-1" value={newVariantName} onChange={(event) => setNewVariantName(event.target.value)} placeholder="Variant名" />
              <button type="button" className="ui-mini-button" onClick={() => void createVariant()} disabled={!newVariantName.trim() || busy}>分岐</button>
            </div>
          </section>

          <section className="mt-5">
            <div className="mb-2 flex items-center justify-between">
              <span className="text-[11px] font-semibold">Version history</span>
              <span className="text-[10px] text-muted-foreground">{versions.length}</span>
            </div>
            <div className="space-y-1.5">
              {versions.map((version, index) => (
                <button
                  type="button"
                  key={version.id}
                  onClick={() => loadVersion(version)}
                  className={`w-full rounded-lg border p-2 text-left ${
                    activeVersion?.id === version.id ? "border-primary/30 bg-primary/10" : "border-border/60 bg-background/30 hover:bg-accent/30"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-[11px] font-semibold">{promptVersionLabel(index, versions.length)}</span>
                    <span className="text-[9px] text-muted-foreground">{version.source}</span>
                  </div>
                  <p className="mt-1 line-clamp-2 text-[10px] text-muted-foreground">{version.positive || "(empty)"}</p>
                  {version.changeInstruction && <p className="mt-1 line-clamp-1 text-[9px] text-muted-foreground/70">↳ {version.changeInstruction}</p>}
                  <p className="mt-1 text-[9px] text-muted-foreground/60">{localTime(version.createdAt)}</p>
                </button>
              ))}
              {versions.length === 0 && <p className="rounded-lg border border-dashed border-border p-3 text-center text-[10px] text-muted-foreground">まだVersionがありません</p>}
            </div>
          </section>
        </aside>
      </div>
    </div>
  );
}
