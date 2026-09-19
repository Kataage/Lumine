import { commands as cmds } from "../../wailsjs/go/models";
import * as Go from "../../wailsjs/go/commands/AppCommands";
import { EventsOn, EventsOff } from "../../wailsjs/runtime/runtime";
import { queryClient } from "../queryClient";
import { normalizeAISettings, type AISettings } from "../utils/aiSettings";

export type LibraryDTO = cmds.LibraryDTO;
export type AssetDTO = cmds.AssetDTO & { semanticScore?: number };
export type FolderDTO = cmds.FolderDTO;
export type TagDTO = cmds.TagDTO;
export type AssetListRequest = cmds.AssetListRequest;
export interface AssetListResponse {
  assets: AssetDTO[];
  totalCount: number;
  semanticSearchSessionId?: string;
}

export interface SemanticSearchProgress {
  requestId: string;
  stage: string;
  scannedCount: number;
  totalCount: number;
  elapsedMs: number;
}

export interface SemanticIndexStatus {
  state: string;
  loadedCount: number;
  totalCount: number;
  dimensions: number;
  elapsedMs: number;
  updatedAgoMs: number;
  modelId?: string;
  error?: string;
}

export interface AIJobQueueStatus {
  started: boolean;
  workers: number;
  activeCount: number;
  activeCapabilities: string[];
  pausedCapabilities: string[];
}

export interface AIRuntimeStatus {
  capability: string;
  state: AIRuntimeState;
  modelId?: string;
  version?: string;
  engine?: string;
  error?: string;
}

export interface AIHealthSnapshot {
  settings: AISettings;
  settingsPersisted: boolean;
  settingsUpdatedAt?: string;
  semanticRuntime: AIRuntimeStatus;
  semanticIndex: SemanticIndexStatus;
  queue: AIJobQueueStatus;
  shuttingDown: boolean;
  semanticSearchEnabled: boolean;
}

export interface AIBridgeStatus {
  available: boolean;
  missing: string[];
}

export type CopyRequest = cmds.CopyRequest;
export type CopyResult = cmds.CopyResult;
export type MoveRequest = cmds.MoveRequest;
export type MoveResult = cmds.MoveResult;
export type PostDTO = cmds.PostDTO;
export type PostTargetDTO = cmds.PostTargetDTO;
export type PostAccountDTO = cmds.PostAccountDTO;

export interface PostRecordRequest {
  assetIds: number[];
  targetId: number;
  accountId: number;
  title: string;
  body: string;
  hashtags: string;
  platformMetadataJson: string;
  externalPostId: string;
  externalUrl: string;
  publishedAt: string;
}

export interface PostRecordAssetDTO {
  id: number;
  fileName: string;
  filePath: string;
}

export interface PostRecordDTO {
  id: number;
  title: string;
  body: string;
  hashtags: string;
  platformMetadataJson: string;
  status: string;
  publishedAt?: string;
  createdAt: string;
  updatedAt: string;
  assetIds: number[];
  assets: PostRecordAssetDTO[];
  targetId: number;
  targetName: string;
  targetKind: string;
  accountId: number;
  accountDisplay: string;
  accountIdentifier: string;
  externalPostId?: string;
  externalUrl?: string;
}

export interface CreativeAssetRefDTO {
  id: number;
  fileName: string;
  filePath: string;
}

export interface WorkDTO {
  id: number;
  title: string;
  description: string;
  coverAssetId?: number;
  assetIds: number[];
  assets: CreativeAssetRefDTO[];
  createdAt: string;
  updatedAt: string;
}

export interface GenerationGroupDTO {
  id: number;
  workId?: number;
  name: string;
  prompt: string;
  negativePrompt: string;
  modelName: string;
  sampler: string;
  scheduler: string;
  steps: number;
  cfgScale: number;
  workflowJson: string;
  notes: string;
  assetIds: number[];
  assets: CreativeAssetRefDTO[];
  createdAt: string;
  updatedAt: string;
}

export interface AssetRelationDTO {
  id: number;
  parentAssetId: number;
  parentFileName: string;
  parentFilePath: string;
  childAssetId: number;
  childFileName: string;
  childFilePath: string;
  relationType: string;
  note: string;
  createdAt: string;
}

export interface AssetCreativeContextDTO {
  works: WorkDTO[];
  groups: GenerationGroupDTO[];
  relations: AssetRelationDTO[];
}

export interface CreateGenerationGroupRequest {
  assetIds: number[];
  workId?: number;
  name: string;
  prompt: string;
  negativePrompt: string;
  modelName: string;
  sampler: string;
  scheduler: string;
  steps: number;
  cfgScale: number;
  workflowJson: string;
  notes: string;
}

export interface ScanProgress {
  libraryId: number;
  scannedCount: number;
  addedCount: number;
  updatedCount: number;
  skippedCount: number;
  failedCount: number;
  isDone: boolean;
}

export interface LibrarySyncResult {
  libraryId: number;
  scannedCount: number;
  addedCount: number;
  updatedCount: number;
  removedCount: number;
  skippedCount: number;
  failedCount: number;
  changed: boolean;
}

export interface DeleteAssetFilesResult {
  deletedCount: number;
  failedCount: number;
  deletedIds: number[];
  failedIds: number[];
  errors?: string[];
}

export type AIRuntimeState = "disabled" | "model_not_installed" | "ready" | "running" | "error";

export interface AIStorageInfo {
  modelsPath: string;
  runtimesPath: string;
}

export interface SemanticModelInfo {
  id: string;
  version: string;
  engine: string;
  displayName: string;
  license: string;
  sizeBytes: number;
  installed: boolean;
  runtime: {
    capability: string;
    state: AIRuntimeState;
    modelId?: string;
    version?: string;
    engine?: string;
    error?: string;
  };
}

export interface LightweightRuntimeInfo {
  id: string;
  version: string;
  sizeBytes: number;
  installed: boolean;
  executablePath?: string;
  platform: string;
  architecture: string;
}

export interface LightweightVisionModelInfo {
  id: string;
  version: string;
  engine: string;
  displayName: string;
  license: string;
  sizeBytes: number;
  installed: boolean;
  runtime: SemanticModelInfo["runtime"];
  llamaRuntime: LightweightRuntimeInfo;
}

export interface LightweightVisionResult {
  schemaVersion: number;
  shortCaption: string;
  detailedCaption: string;
  subject: string;
  background: string;
  composition: string;
  viewpoint: string;
  visibleText: string[];
  notes: string[];
  completionTokens?: number;
}

export interface LightweightVisionAnalysis {
  assetId: number;
  state: "queued" | "running" | "ready" | "failed" | "stale";
  engine?: string;
  modelId?: string;
  modelVersion?: string;
  result?: LightweightVisionResult;
  errorMessage?: string;
  analyzedAt?: string;
  updatedAt: string;
}

export interface AdvancedVisionCandidateInfo {
  id: string;
  version: string;
  engine: string;
  displayName: string;
  license: string;
  sizeBytes: number;
  installed: boolean;
}

export interface AdvancedVisionStatusInfo {
  runtime: SemanticModelInfo["runtime"];
  llamaRuntime: LightweightRuntimeInfo;
  models: AdvancedVisionCandidateInfo[];
  activeModelId?: string;
}

export interface AdvancedVisionResult {
  schemaVersion: number;
  summary: string;
  subjects: string[];
  environment: string;
  composition: string;
  viewpoint: string;
  actions: string[];
  relationships: string[];
  context: string;
  differences: string[];
  commonalities: string[];
  reversePromptHints: string[];
  visibleText: string[];
  notes: string[];
  completionTokens?: number;
}

export interface AdvancedVisionRun {
  id: number;
  operation: "analyze_deep" | "compare_images" | "reverse_prompt_support";
  instruction: string;
  state: "running" | "ready" | "failed";
  engine: string;
  modelId: string;
  modelVersion: string;
  assetIds: number[];
  result?: AdvancedVisionResult;
  errorMessage?: string;
  createdAt: string;
  completedAt?: string;
}

export interface PromptEngineCandidateInfo {
  id: string;
  version: string;
  engine: string;
  displayName: string;
  license: string;
  sizeBytes: number;
  installed: boolean;
  reference: boolean;
}

export interface PromptEngineStatusInfo {
  runtime: SemanticModelInfo["runtime"];
  llamaRuntime: LightweightRuntimeInfo;
  models: PromptEngineCandidateInfo[];
  activeModelId?: string;
  selectionNote: string;
}

export type PromptEngineOperation =
  | "idea_to_prompt"
  | "improve_prompt"
  | "convert_prompt"
  | "edit_prompt";

export interface PromptEngineRequest {
  operation: PromptEngineOperation;
  idea?: string;
  positive?: string;
  negative?: string;
  sourceProfile?: string;
  targetProfile?: string;
  sourceProfileId?: string;
  targetProfileId?: string;
  instruction?: string;
  contextJson?: string;
  characters?: string[];
  loras?: string[];
}

export interface PromptEngineResult {
  positive: string;
  negative: string;
  characters: string[];
  loras: string[];
  composition: string;
  notes: string[];
  completionTokens?: number;
  engine: string;
  modelId: string;
  modelVersion: string;
}

export interface ImagePromptRequest {
  assetId: number;
  targetProfileId: string;
  instruction?: string;
  useAdvancedVision: boolean;
}

export interface ImagePromptSource {
  kind: string;
  label: string;
  state: string;
  engine?: string;
  modelId?: string;
  modelVersion?: string;
  dataJson?: string;
  note?: string;
}

export interface ImagePromptResult {
  assetId: number;
  targetProfileId: string;
  positive: string;
  negative: string;
  characters: string[];
  loras: string[];
  composition: string;
  notes: string[];
  sources: ImagePromptSource[];
  promptEngineUsed: boolean;
  aiEngine?: string;
  aiModelId?: string;
  aiModelVersion?: string;
}

export interface ImagePromptProjectResult {
  project: PromptProject;
  result: ImagePromptResult;
}

export interface GenerationMetadataLoRA {
  name: string;
  weight: number;
  triggerWords: string[];
}

export interface AssetGenerationMetadata {
  assetId: number;
  present: boolean;
  schemaVersion: number;
  parserVersion: number;
  sourceFormat: string;
  positive: string;
  negative: string;
  checkpoint: string;
  loras: GenerationMetadataLoRA[];
  sampler: string;
  scheduler: string;
  cfg: number;
  steps: number;
  seed: number;
  width: number;
  height: number;
  suggestedProfileId?: string;
  rawPromptJson?: string;
  rawWorkflowJson?: string;
  parameters?: string;
  rawJson: string;
  parsedAt?: string;
}

export interface ModelProfile {
  id: string;
  name: string;
  family: string;
  checkpointName: string;
  promptStyle: string;
  qualityTags: string[];
  negativePromptPolicy: string;
  tagOrder: string[];
  triggerWords: string[];
  loraTriggerSyntax: string;
  weightSyntax: string;
  systemGuidance: string;
  notes: string;
  builtIn: boolean;
  createdAt?: string;
  updatedAt?: string;
}

export interface ModelProfileInput {
  name: string;
  family: string;
  checkpointName: string;
  promptStyle: string;
  qualityTags: string[];
  negativePromptPolicy: string;
  tagOrder: string[];
  triggerWords: string[];
  loraTriggerSyntax: string;
  weightSyntax: string;
  systemGuidance: string;
  notes: string;
}

export interface PromptProjectLoRA {
  name: string;
  weight: number;
  triggerWords: string[];
}

export interface PromptVersion {
  schemaVersion: number;
  id: number;
  variantId: number;
  parentVersionId?: number;
  positive: string;
  negative: string;
  source: "manual" | "llm" | "vlm" | "tagger" | "derived" | "metadata";
  changeInstruction: string;
  profileId: string;
  profileSnapshotJson: string;
  aiEngine: string;
  aiModelId: string;
  aiModelVersion: string;
  metadataJson: string;
  createdAt: string;
}

export interface PromptVariant {
  id: number;
  projectId: number;
  name: string;
  versions: PromptVersion[];
  createdAt: string;
}

export interface PromptProject {
  schemaVersion: number;
  id: number;
  title: string;
  idea: string;
  notes: string;
  targetProfileId: string;
  characters: string[];
  loras: PromptProjectLoRA[];
  referenceAssetIds: number[];
  relatedAssetIds: number[];
  variants?: PromptVariant[];
  deleted: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface PromptProjectInput {
  title: string;
  idea: string;
  notes: string;
  targetProfileId: string;
  characters: string[];
  loras: PromptProjectLoRA[];
  referenceAssetIds: number[];
  relatedAssetIds: number[];
}

export interface PromptVersionInput {
  variantId: number;
  parentVersionId?: number;
  positive: string;
  negative: string;
  source: PromptVersion["source"];
  changeInstruction: string;
  profileId: string;
  aiEngine: string;
  aiModelId: string;
  aiModelVersion: string;
  metadataJson: string;
}

export const selectFolder = Go.SelectFolder;
export const listLibraries = Go.ListLibraries;
export const addLibrary = Go.AddLibrary;
export const updateLibrary = Go.UpdateLibrary;
export const enableLibrary = Go.EnableLibrary;
export const disableLibrary = Go.DisableLibrary;
export const removeLibrary = Go.RemoveLibrary;
export const getExcludedDirs = Go.GetExcludedDirs;
export const setExcludedDirs = Go.SetExcludedDirs;
export const getSupportedExtensions = Go.GetSupportedExtensions;
export const setSupportedExtensions = Go.SetSupportedExtensions;
export const listAssets = Go.ListAssets;

type DynamicCommands = {
  GetAISettings?: () => Promise<AISettings | null>;
  GetAIHealthSnapshot?: () => Promise<AIHealthSnapshot | null>;
  GetAIStorageInfo?: () => Promise<AIStorageInfo | null>;
  SetAISettings?: (settings: AISettings) => Promise<AISettings | null>;
  PatchAISettings?: (patch: Record<string, boolean>) => Promise<AISettings | null>;
  IsAICapabilityEnabled?: (capability: string) => Promise<boolean>;
  SemanticSearchAssets?: (request: AssetListRequest) => Promise<AssetListResponse | null>;
  SemanticSearchAssetsWithID?: (request: AssetListRequest, requestId: string) => Promise<AssetListResponse | null>;
  SemanticSearchPage?: (sessionId: string, offset: number, limit: number) => Promise<AssetListResponse | null>;
  CancelSemanticSearch?: (requestId: string) => Promise<void>;
  GetSemanticIndexStatus?: () => Promise<SemanticIndexStatus>;
  ListSimilarAssets?: (assetId: number, request: AssetListRequest) => Promise<AssetListResponse | null>;
  GetDefaultSemanticModelInfo?: () => Promise<SemanticModelInfo | null>;
  EnsureSemanticSearchReady?: () => Promise<void>;
  InstallDefaultSemanticModel?: () => Promise<unknown>;
  LoadDefaultSemanticModel?: () => Promise<void>;
  GetDefaultLightweightVisionModelInfo?: () => Promise<LightweightVisionModelInfo | null>;
  InstallLightweightVisionRuntime?: () => Promise<LightweightRuntimeInfo | null>;
  RemoveLightweightVisionRuntime?: () => Promise<void>;
  InstallDefaultLightweightVisionModel?: () => Promise<unknown>;
  RemoveDefaultLightweightVisionModel?: () => Promise<void>;
  LoadDefaultLightweightVisionModel?: () => Promise<void>;
  GetLightweightVisionAnalysis?: (assetId: number) => Promise<LightweightVisionAnalysis | null>;
  EnqueueLightweightVisionBackfill?: () => Promise<number>;
  ReanalyzeAssets?: (assetIds: number[], capability: string, priority: number) => Promise<number>;
  GetAdvancedVisionStatus?: () => Promise<AdvancedVisionStatusInfo | null>;
  InstallAdvancedVisionRuntime?: () => Promise<LightweightRuntimeInfo | null>;
  RemoveAdvancedVisionRuntime?: () => Promise<void>;
  InstallAdvancedVisionModel?: (modelId: string) => Promise<unknown>;
  RemoveAdvancedVisionModel?: (modelId: string) => Promise<void>;
  LoadAdvancedVisionModel?: (modelId: string) => Promise<void>;
  RunAdvancedVision?: (operation: string, assetIds: number[], instruction: string) => Promise<AdvancedVisionRun | null>;
  GetAdvancedVisionRun?: (runId: number) => Promise<AdvancedVisionRun | null>;
  ListAdvancedVisionRunsForAsset?: (assetId: number, limit: number) => Promise<AdvancedVisionRun[]>;
  GetPromptEngineStatus?: () => Promise<PromptEngineStatusInfo | null>;
  InstallPromptEngineRuntime?: () => Promise<LightweightRuntimeInfo | null>;
  RemovePromptEngineRuntime?: () => Promise<void>;
  InstallPromptEngineModel?: (modelId: string) => Promise<unknown>;
  RemovePromptEngineModel?: (modelId: string) => Promise<void>;
  LoadPromptEngineModel?: (modelId: string) => Promise<void>;
  RunPromptEngine?: (request: PromptEngineRequest) => Promise<PromptEngineResult | null>;
  BuildImagePrompt?: (request: ImagePromptRequest) => Promise<ImagePromptResult | null>;
  CreatePromptProjectFromImage?: (request: ImagePromptRequest) => Promise<ImagePromptProjectResult | null>;
  GetAssetGenerationMetadata?: (assetId: number, refresh: boolean) => Promise<AssetGenerationMetadata | null>;
  ListModelProfiles?: () => Promise<ModelProfile[]>;
  GetModelProfile?: (id: string) => Promise<ModelProfile | null>;
  CreateModelProfile?: (input: ModelProfileInput) => Promise<ModelProfile | null>;
  UpdateModelProfile?: (id: string, input: ModelProfileInput) => Promise<ModelProfile | null>;
  DuplicateModelProfile?: (id: string, newName: string) => Promise<ModelProfile | null>;
  DeleteModelProfile?: (id: string) => Promise<void>;
  ListPromptProjects?: (includeDeleted: boolean, limit: number) => Promise<PromptProject[]>;
  GetPromptProject?: (id: number, includeDeleted: boolean) => Promise<PromptProject | null>;
  CreatePromptProject?: (input: PromptProjectInput) => Promise<PromptProject | null>;
  UpdatePromptProject?: (id: number, input: PromptProjectInput) => Promise<PromptProject | null>;
  DeletePromptProject?: (id: number) => Promise<void>;
  RestorePromptProject?: (id: number) => Promise<void>;
  CreatePromptVariant?: (projectId: number, name: string, fromVersionId: number) => Promise<PromptVariant | null>;
  CreatePromptVersion?: (input: PromptVersionInput) => Promise<PromptVersion | null>;
  GetViewerAssetDetail?: (id: number) => Promise<AssetDTO | null>;
  ScanLibraryViewer?: (libraryId: number) => Promise<void>;
  SyncLibraryViewer?: (libraryId: number) => Promise<LibrarySyncResult | null>;
  DeleteAssetFiles?: (ids: number[]) => Promise<DeleteAssetFilesResult | null>;
  CreatePostRecord?: (request: PostRecordRequest) => Promise<PostRecordDTO | null>;
  ListPostRecords?: (offset: number, limit: number) => Promise<PostRecordDTO[]>;
  GetPostRecordsByAsset?: (assetId: number) => Promise<PostRecordDTO[]>;
  ListWorks?: (limit: number) => Promise<WorkDTO[]>;
  CreateWork?: (title: string, description: string, assetIds: number[]) => Promise<WorkDTO | null>;
  AddAssetsToWork?: (workId: number, assetIds: number[]) => Promise<void>;
  ListGenerationGroups?: (limit: number) => Promise<GenerationGroupDTO[]>;
  CreateGenerationGroup?: (request: CreateGenerationGroupRequest) => Promise<GenerationGroupDTO | null>;
  AddAssetsToGenerationGroup?: (groupId: number, assetIds: number[]) => Promise<void>;
  CreateAssetRelation?: (parentAssetId: number, childAssetId: number, relationType: string, note: string) => Promise<AssetRelationDTO | null>;
  DeleteAssetRelation?: (id: number) => Promise<void>;
  GetAssetCreativeContext?: (assetId: number) => Promise<AssetCreativeContextDTO | null>;
};

function appCommands(): DynamicCommands | undefined {
  return (window as unknown as { go?: { commands?: { AppCommands?: DynamicCommands } } }).go?.commands?.AppCommands;
}

export async function getAssetDetail(assetId: number): Promise<AssetDTO | null> {
  const method = appCommands()?.GetViewerAssetDetail;
  if (!method) return Go.GetAssetDetail(assetId);
  return method(assetId);
}

export async function scanLibrary(libraryId: number): Promise<void> {
  const method = appCommands()?.ScanLibraryViewer;
  if (method) {
    await method(libraryId);
    return;
  }
  await Go.ScanLibrary(libraryId);
}

export async function syncLibrary(libraryId: number): Promise<LibrarySyncResult | null> {
  const method = appCommands()?.SyncLibraryViewer;
  if (!method) return null;
  return method(libraryId);
}

export async function deleteAssetFiles(ids: number[]): Promise<DeleteAssetFilesResult> {
  const method = appCommands()?.DeleteAssetFiles;
  if (!method) throw new Error("画像削除APIが利用できません。最新版のLumineを起動してください。");
  const result = await method(ids);
  if (!result) throw new Error("画像削除の結果を取得できませんでした。");
  return {
    ...result,
    deletedIds: result.deletedIds ?? [],
    failedIds: result.failedIds ?? [],
    errors: result.errors ?? [],
  };
}

export async function createPostRecord(request: PostRecordRequest): Promise<PostRecordDTO | null> {
  const method = appCommands()?.CreatePostRecord;
  if (!method) throw new Error("投稿記録APIが利用できません。最新版のLumineを起動してください。");
  const record = await method(request);
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ["postRecords"] }),
    ...request.assetIds.map((assetId) => queryClient.invalidateQueries({ queryKey: ["assetPostRecords", assetId] })),
  ]);
  return record;
}

export async function listPostRecords(offset = 0, limit = 100): Promise<PostRecordDTO[]> {
  const method = appCommands()?.ListPostRecords;
  if (!method) return [];
  return (await method(offset, limit)) ?? [];
}

export async function getPostRecordsByAsset(assetId: number): Promise<PostRecordDTO[]> {
  const method = appCommands()?.GetPostRecordsByAsset;
  if (!method) return [];
  return (await method(assetId)) ?? [];
}

function requireDynamic<K extends keyof DynamicCommands>(name: K): NonNullable<DynamicCommands[K]> {
  const method = appCommands()?.[name];
  if (!method) throw new Error(`${String(name)} APIが利用できません。最新版のLumineを起動してください。`);
  return method as NonNullable<DynamicCommands[K]>;
}

export async function getAISettings(): Promise<AISettings> {
  const value = await requireDynamic("GetAISettings")();
  return normalizeAISettings(value);
}

export function getAIBridgeStatus(): AIBridgeStatus {
  const commands = appCommands();
  const required = [
    "GetAISettings",
    "PatchAISettings",
    "GetAIHealthSnapshot",
    "GetDefaultSemanticModelInfo",
    "EnsureSemanticSearchReady",
    "SemanticSearchAssetsWithID",
    "GetSemanticIndexStatus",
    "CancelSemanticSearch",
  ] as const;
  const missing = required.filter((name) => typeof commands?.[name] !== "function");
  return { available: missing.length === 0, missing: [...missing] };
}

export async function getAIHealthSnapshot(): Promise<AIHealthSnapshot> {
  const value = await requireDynamic("GetAIHealthSnapshot")();
  if (!value) throw new Error("AI状態を取得できませんでした。");
  return {
    ...value,
    settings: normalizeAISettings(value.settings),
    queue: {
      started: value.queue?.started === true,
      workers: Number(value.queue?.workers ?? 0),
      activeCount: Number(value.queue?.activeCount ?? 0),
      activeCapabilities: value.queue?.activeCapabilities ?? [],
      pausedCapabilities: value.queue?.pausedCapabilities ?? [],
    },
  };
}

export async function getAIStorageInfo(): Promise<AIStorageInfo> {
  const value = await requireDynamic("GetAIStorageInfo")();
  if (!value) throw new Error("AI保存先を取得できませんでした。");
  return value;
}

export async function setAISettings(settings: AISettings): Promise<AISettings> {
  const value = await requireDynamic("SetAISettings")(settings);
  return normalizeAISettings(value ?? settings);
}

export async function patchAISettings(patch: Partial<Record<keyof AISettings, boolean>>): Promise<AISettings> {
  const value = await requireDynamic("PatchAISettings")(patch as Record<string, boolean>);
  if (!value) throw new Error("AI設定を更新できませんでした。");
  return normalizeAISettings(value);
}

export async function isAICapabilityEnabled(capability: string): Promise<boolean> {
  return requireDynamic("IsAICapabilityEnabled")(capability);
}

export async function semanticSearchAssets(request: AssetListRequest, requestId = ""): Promise<AssetListResponse> {
  const commands = appCommands();
  if (requestId && commands?.SemanticSearchAssetsWithID) {
    return (await commands.SemanticSearchAssetsWithID(request, requestId)) ?? { assets: [], totalCount: 0 };
  }
  return (await requireDynamic("SemanticSearchAssets")(request)) ?? { assets: [], totalCount: 0 };
}

export async function semanticSearchPage(sessionId: string, offset: number, limit: number): Promise<AssetListResponse> {
  return (await requireDynamic("SemanticSearchPage")(sessionId, offset, limit)) ?? { assets: [], totalCount: 0 };
}

export async function cancelSemanticSearch(requestId: string): Promise<void> {
  if (!requestId) return;
  const method = appCommands()?.CancelSemanticSearch;
  if (method) await method(requestId);
}

export async function getSemanticIndexStatus(): Promise<SemanticIndexStatus> {
  const method = appCommands()?.GetSemanticIndexStatus;
  if (!method) {
    return {
      state: "unavailable",
      loadedCount: 0,
      totalCount: 0,
      dimensions: 0,
      elapsedMs: 0,
      updatedAgoMs: 0,
    };
  }
  return method();
}

export function onSemanticSearchProgress(callback: (progress: SemanticSearchProgress) => void): () => void {
  return EventsOn("semantic-search:progress", (value: unknown) => callback(value as SemanticSearchProgress));
}

export async function listSimilarAssets(assetId: number, request: AssetListRequest): Promise<AssetListResponse> {
  return (await requireDynamic("ListSimilarAssets")(assetId, request)) ?? { assets: [], totalCount: 0 };
}

export async function getDefaultSemanticModelInfo(): Promise<SemanticModelInfo> {
  const value = await requireDynamic("GetDefaultSemanticModelInfo")();
  if (!value) throw new Error("Semantic Searchモデル情報を取得できませんでした。");
  return value;
}

export async function ensureSemanticSearchReady(): Promise<void> {
  await requireDynamic("EnsureSemanticSearchReady")();
}

export async function installDefaultSemanticModel(): Promise<void> {
  await requireDynamic("InstallDefaultSemanticModel")();
}

export async function loadDefaultSemanticModel(): Promise<void> {
  await requireDynamic("LoadDefaultSemanticModel")();
}

export async function getDefaultLightweightVisionModelInfo(): Promise<LightweightVisionModelInfo> {
  const value = await requireDynamic("GetDefaultLightweightVisionModelInfo")();
  if (!value) throw new Error("Lightweight Visionモデル情報を取得できませんでした。");
  return value;
}

export async function installLightweightVisionRuntime(): Promise<void> {
  await requireDynamic("InstallLightweightVisionRuntime")();
}

export async function removeLightweightVisionRuntime(): Promise<void> {
  await requireDynamic("RemoveLightweightVisionRuntime")();
}

export async function installDefaultLightweightVisionModel(): Promise<void> {
  await requireDynamic("InstallDefaultLightweightVisionModel")();
}

export async function removeDefaultLightweightVisionModel(): Promise<void> {
  await requireDynamic("RemoveDefaultLightweightVisionModel")();
}

export async function loadDefaultLightweightVisionModel(): Promise<void> {
  await requireDynamic("LoadDefaultLightweightVisionModel")();
}

export async function getLightweightVisionAnalysis(assetId: number): Promise<LightweightVisionAnalysis | null> {
  return requireDynamic("GetLightweightVisionAnalysis")(assetId);
}

export async function enqueueLightweightVisionBackfill(): Promise<number> {
  return requireDynamic("EnqueueLightweightVisionBackfill")();
}

export async function reanalyzeAssets(assetIds: number[], capability: string, priority = 100): Promise<number> {
  return requireDynamic("ReanalyzeAssets")(assetIds, capability, priority);
}

export async function getAdvancedVisionStatus(): Promise<AdvancedVisionStatusInfo> {
  const value = await requireDynamic("GetAdvancedVisionStatus")();
  if (!value) throw new Error("Advanced Visionの状態を取得できませんでした。");
  return value;
}

export async function installAdvancedVisionRuntime(): Promise<void> {
  await requireDynamic("InstallAdvancedVisionRuntime")();
}

export async function removeAdvancedVisionRuntime(): Promise<void> {
  await requireDynamic("RemoveAdvancedVisionRuntime")();
}

export async function installAdvancedVisionModel(modelId: string): Promise<void> {
  await requireDynamic("InstallAdvancedVisionModel")(modelId);
}

export async function removeAdvancedVisionModel(modelId: string): Promise<void> {
  await requireDynamic("RemoveAdvancedVisionModel")(modelId);
}

export async function loadAdvancedVisionModel(modelId: string): Promise<void> {
  await requireDynamic("LoadAdvancedVisionModel")(modelId);
}

export async function runAdvancedVision(
  operation: AdvancedVisionRun["operation"],
  assetIds: number[],
  instruction = "",
): Promise<AdvancedVisionRun> {
  const value = await requireDynamic("RunAdvancedVision")(operation, assetIds, instruction);
  if (!value) throw new Error("Advanced Visionの結果を取得できませんでした。");
  return value;
}

export async function getAdvancedVisionRun(runId: number): Promise<AdvancedVisionRun | null> {
  return requireDynamic("GetAdvancedVisionRun")(runId);
}

export async function listAdvancedVisionRunsForAsset(assetId: number, limit = 20): Promise<AdvancedVisionRun[]> {
  return (await requireDynamic("ListAdvancedVisionRunsForAsset")(assetId, limit)) ?? [];
}

export async function getPromptEngineStatus(): Promise<PromptEngineStatusInfo> {
  const value = await requireDynamic("GetPromptEngineStatus")();
  if (!value) throw new Error("Prompt Engineの状態を取得できませんでした。");
  return {
    ...value,
    models: value.models ?? [],
  };
}

export async function installPromptEngineRuntime(): Promise<void> {
  await requireDynamic("InstallPromptEngineRuntime")();
}

export async function removePromptEngineRuntime(): Promise<void> {
  await requireDynamic("RemovePromptEngineRuntime")();
}

export async function installPromptEngineModel(modelId: string): Promise<void> {
  await requireDynamic("InstallPromptEngineModel")(modelId);
}

export async function removePromptEngineModel(modelId: string): Promise<void> {
  await requireDynamic("RemovePromptEngineModel")(modelId);
}

export async function loadPromptEngineModel(modelId: string): Promise<void> {
  await requireDynamic("LoadPromptEngineModel")(modelId);
}

export async function runPromptEngine(request: PromptEngineRequest): Promise<PromptEngineResult> {
  const value = await requireDynamic("RunPromptEngine")(request);
  if (!value) throw new Error("Prompt Engineの結果を取得できませんでした。");
  return {
    ...value,
    characters: value.characters ?? [],
    loras: value.loras ?? [],
    notes: value.notes ?? [],
  };
}

function normalizeImagePromptResult(value: ImagePromptResult): ImagePromptResult {
  return {
    ...value,
    characters: value.characters ?? [],
    loras: value.loras ?? [],
    notes: value.notes ?? [],
    sources: value.sources ?? [],
  };
}

export async function buildImagePrompt(request: ImagePromptRequest): Promise<ImagePromptResult> {
  const value = await requireDynamic("BuildImagePrompt")(request);
  if (!value) throw new Error("Image → Promptの結果を取得できませんでした。");
  return normalizeImagePromptResult(value);
}

export async function createPromptProjectFromImage(request: ImagePromptRequest): Promise<ImagePromptProjectResult> {
  const value = await requireDynamic("CreatePromptProjectFromImage")(request);
  if (!value) throw new Error("画像からPrompt Projectを作成できませんでした。");
  await queryClient.invalidateQueries({ queryKey: ["promptProjects"] });
  return {
    project: normalizePromptProject(value.project),
    result: normalizeImagePromptResult(value.result),
  };
}

export async function getAssetGenerationMetadata(assetId: number, refresh = false): Promise<AssetGenerationMetadata> {
  const value = await requireDynamic("GetAssetGenerationMetadata")(assetId, refresh);
  if (!value) throw new Error("生成metadataを取得できませんでした。");
  return {
    ...value,
    loras: (value.loras ?? []).map((lora) => ({
      ...lora,
      triggerWords: lora.triggerWords ?? [],
    })),
  };
}

function normalizeModelProfile(profile: ModelProfile): ModelProfile {
  return {
    ...profile,
    qualityTags: profile.qualityTags ?? [],
    tagOrder: profile.tagOrder ?? [],
    triggerWords: profile.triggerWords ?? [],
  };
}

export async function listModelProfiles(): Promise<ModelProfile[]> {
  const values = (await requireDynamic("ListModelProfiles")()) ?? [];
  return values.map(normalizeModelProfile);
}

export async function getModelProfile(id: string): Promise<ModelProfile> {
  const value = await requireDynamic("GetModelProfile")(id);
  if (!value) throw new Error("Model Profileが見つかりません。");
  return normalizeModelProfile(value);
}

export async function createModelProfile(input: ModelProfileInput): Promise<ModelProfile> {
  const value = await requireDynamic("CreateModelProfile")(input);
  if (!value) throw new Error("Model Profileを作成できませんでした。");
  return normalizeModelProfile(value);
}

export async function updateModelProfile(id: string, input: ModelProfileInput): Promise<ModelProfile> {
  const value = await requireDynamic("UpdateModelProfile")(id, input);
  if (!value) throw new Error("Model Profileを更新できませんでした。");
  return normalizeModelProfile(value);
}

export async function duplicateModelProfile(id: string, newName = ""): Promise<ModelProfile> {
  const value = await requireDynamic("DuplicateModelProfile")(id, newName);
  if (!value) throw new Error("Model Profileを複製できませんでした。");
  return normalizeModelProfile(value);
}

export async function deleteModelProfile(id: string): Promise<void> {
  await requireDynamic("DeleteModelProfile")(id);
}

function normalizePromptProject(project: PromptProject): PromptProject {
  return {
    ...project,
    characters: project.characters ?? [],
    loras: (project.loras ?? []).map((lora) => ({ ...lora, triggerWords: lora.triggerWords ?? [] })),
    referenceAssetIds: project.referenceAssetIds ?? [],
    relatedAssetIds: project.relatedAssetIds ?? [],
    variants: project.variants?.map((variant) => ({
      ...variant,
      versions: variant.versions ?? [],
    })),
  };
}

export async function listPromptProjects(includeDeleted = false, limit = 200): Promise<PromptProject[]> {
  const values = (await requireDynamic("ListPromptProjects")(includeDeleted, limit)) ?? [];
  return values.map(normalizePromptProject);
}

export async function getPromptProject(id: number, includeDeleted = false): Promise<PromptProject> {
  const value = await requireDynamic("GetPromptProject")(id, includeDeleted);
  if (!value) throw new Error("Prompt Projectが見つかりません。");
  return normalizePromptProject(value);
}

export async function createPromptProject(input: PromptProjectInput): Promise<PromptProject> {
  const value = await requireDynamic("CreatePromptProject")(input);
  if (!value) throw new Error("Prompt Projectを作成できませんでした。");
  await queryClient.invalidateQueries({ queryKey: ["promptProjects"] });
  return normalizePromptProject(value);
}

export async function updatePromptProject(id: number, input: PromptProjectInput): Promise<PromptProject> {
  const value = await requireDynamic("UpdatePromptProject")(id, input);
  if (!value) throw new Error("Prompt Projectを更新できませんでした。");
  await queryClient.invalidateQueries({ queryKey: ["promptProjects"] });
  await queryClient.invalidateQueries({ queryKey: ["promptProject", id] });
  return normalizePromptProject(value);
}

export async function deletePromptProject(id: number): Promise<void> {
  await requireDynamic("DeletePromptProject")(id);
  await queryClient.invalidateQueries({ queryKey: ["promptProjects"] });
  await queryClient.invalidateQueries({ queryKey: ["promptProject", id] });
}

export async function restorePromptProject(id: number): Promise<void> {
  await requireDynamic("RestorePromptProject")(id);
  await queryClient.invalidateQueries({ queryKey: ["promptProjects"] });
  await queryClient.invalidateQueries({ queryKey: ["promptProject", id] });
}

export async function createPromptVariant(projectId: number, name: string, fromVersionId = 0): Promise<PromptVariant> {
  const value = await requireDynamic("CreatePromptVariant")(projectId, name, fromVersionId);
  if (!value) throw new Error("Prompt Variantを作成できませんでした。");
  await queryClient.invalidateQueries({ queryKey: ["promptProject", projectId] });
  return { ...value, versions: value.versions ?? [] };
}

export async function createPromptVersion(input: PromptVersionInput): Promise<PromptVersion> {
  const value = await requireDynamic("CreatePromptVersion")(input);
  if (!value) throw new Error("Prompt Versionを保存できませんでした。");
  await queryClient.invalidateQueries({ queryKey: ["promptProjects"] });
  return value;
}

export async function listWorks(limit = 200): Promise<WorkDTO[]> {
  return (await requireDynamic("ListWorks")(limit)) ?? [];
}

export async function createWork(title: string, description: string, assetIds: number[]): Promise<WorkDTO | null> {
  const result = await requireDynamic("CreateWork")(title, description, assetIds);
  await invalidateCreative(assetIds);
  return result;
}

export async function addAssetsToWork(workId: number, assetIds: number[]): Promise<void> {
  await requireDynamic("AddAssetsToWork")(workId, assetIds);
  await invalidateCreative(assetIds);
}

export async function listGenerationGroups(limit = 200): Promise<GenerationGroupDTO[]> {
  return (await requireDynamic("ListGenerationGroups")(limit)) ?? [];
}

export async function createGenerationGroup(request: CreateGenerationGroupRequest): Promise<GenerationGroupDTO | null> {
  const result = await requireDynamic("CreateGenerationGroup")(request);
  await invalidateCreative(request.assetIds);
  return result;
}

export async function addAssetsToGenerationGroup(groupId: number, assetIds: number[]): Promise<void> {
  await requireDynamic("AddAssetsToGenerationGroup")(groupId, assetIds);
  await invalidateCreative(assetIds);
}

export async function createAssetRelation(parentAssetId: number, childAssetId: number, relationType: string, note: string): Promise<AssetRelationDTO | null> {
  const result = await requireDynamic("CreateAssetRelation")(parentAssetId, childAssetId, relationType, note);
  await invalidateCreative([parentAssetId, childAssetId]);
  return result;
}

export async function deleteAssetRelation(id: number, assetIds: number[] = []): Promise<void> {
  await requireDynamic("DeleteAssetRelation")(id);
  await invalidateCreative(assetIds);
}

export async function getAssetCreativeContext(assetId: number): Promise<AssetCreativeContextDTO> {
  const result = await requireDynamic("GetAssetCreativeContext")(assetId);
  return result ?? { works: [], groups: [], relations: [] };
}

async function invalidateCreative(assetIds: number[]): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ["works"] }),
    queryClient.invalidateQueries({ queryKey: ["generationGroups"] }),
    ...assetIds.map((assetId) => queryClient.invalidateQueries({ queryKey: ["assetCreativeContext", assetId] })),
  ]);
}

export async function listTags(): Promise<TagDTO[]> {
  return (await Go.ListTags()) ?? [];
}

export const updateAssetNote = Go.UpdateAssetNote;
export const setAssetTags = Go.SetAssetTags;
export const updateAssetRating = Go.UpdateAssetRating;
export const updateAssetStatus = Go.UpdateAssetStatus;
export const toggleAssetFavorite = Go.ToggleAssetFavorite;
export const updateAssetColorLabel = Go.UpdateAssetColorLabel;
export const bulkUpdateRating = Go.BulkUpdateRating;
export const bulkUpdateStatus = Go.BulkUpdateStatus;
export const bulkUpdateFavorite = Go.BulkUpdateFavorite;
export const bulkUpdateColorLabel = Go.BulkUpdateColorLabel;
export const moveAssets = Go.MoveAssets;
export const cancelScan = Go.CancelScan;
export const createTag = Go.CreateTag;
export const deleteTag = Go.DeleteTag;
export const listPosts = Go.ListPosts;
export const createPostDraft = Go.CreatePostDraft;
export const updatePost = Go.UpdatePost;
export const deletePost = Go.DeletePost;
export const attachAssetsToPost = Go.AttachAssetsToPost;
export const getPostsByAsset = Go.GetPostsByAsset;
export const listPostTargets = Go.ListPostTargets;
export const createPostTarget = Go.CreatePostTarget;
export const deletePostTarget = Go.DeletePostTarget;
export const listPostAccounts = Go.ListPostAccounts;
export const createPostAccount = Go.CreatePostAccount;
export const deletePostAccount = Go.DeletePostAccount;
export const getSetting = Go.GetSetting;
export const setSetting = Go.SetSetting;
export const getAppBootstrap = Go.GetAppBootstrap;
export const scanFolder = Go.ScanFolder;
export const getFolderTree = Go.GetFolderTree;
export const bulkDeleteAssets = Go.BulkDeleteAssets;
export const copyAssets = Go.CopyAssets;

export { EventsOn, EventsOff };

export function onScanProgress(callback: (progress: ScanProgress) => void): void {
  EventsOn("scan:progress", (data: unknown) => {
    callback(data as ScanProgress);
  });
}

export function offScanProgress(): void {
  EventsOff("scan:progress");
}

export function getLocalImageUrl(filePath: string): string {
  return `/local?path=${encodeURIComponent(filePath)}`;
}
