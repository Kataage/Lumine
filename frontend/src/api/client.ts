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
  semanticCoverageReadyCount?: number;
  semanticCoverageTotalCount?: number;
  semanticCoverageQueuedCount?: number;
  semanticCoverageRunningCount?: number;
  semanticCoverageFailedCount?: number;
  semanticCoverageStaleCount?: number;
}

export interface SemanticSearchProgress {
  requestId: string;
  stage: string;
  scannedCount: number;
  totalCount: number;
  elapsedMs: number;
}

export interface SemanticEmbeddingUpdated {
  assetId: number;
  modelVersion: string;
}

export interface SemanticIndexStatus {
  state: string;
  loadedCount: number;
  totalCount: number;
  dimensions: number;
  elapsedMs: number;
  updatedAgoMs: number;
  modelId?: string;
  persistent: boolean;
  overlayCount: number;
  error?: string;
}

export interface AIJobQueueStatus {
  started: boolean;
  workers: number;
  activeCount: number;
  activeCapabilities: string[];
  pausedCapabilities: string[];
}

export interface SemanticStageTimings {
  fileOpenMs: number;
  fileReadMs: number;
  decodeMs: number;
  preprocessMs: number;
  runtimeLockWaitMs: number;
  ortRunLockWaitMs: number;
  tensorSetupMs: number;
  ortRunMs: number;
  embeddingPersistenceMs: number;
  completionPersistenceMs: number;
  indexLockWaitMs: number;
  indexUpdateMs: number;
}

export interface SemanticJobTraceSnapshot {
  jobId: number;
  assetId: number;
  attempt: number;
  workerId: number;
  startedAt: string;
  finishedAt: string;
  queueWaitMs: number;
  claimToReadyMs: number;
  enqueueToReadyMs: number;
  stages: SemanticStageTimings;
  outcome: string;
  errorStage?: string;
  error?: string;
  sqliteCode?: number;
}

export interface SemanticPipelineDiagnosticsSnapshot {
  enabled: boolean;
  collectedAt: string;
  resetAt: string;
  readyLastMinute: number;
  ortRunsLastMinute: number;
  retryCount: number;
  reInferenceCount: number;
  discardedAfterCancel: number;
  failedCompletionCount: number;
  sqliteBusyCount: number;
  sqliteBusySnapshotCount: number;
  sqliteErrorCodes: Record<string, number>;
  snapshotAttempts: number;
  snapshotSuccesses: number;
  snapshotDiscarded: number;
  snapshotFailures: number;
  snapshotBytes: number;
  snapshotDurationMs: number;
  viewerActive: boolean;
  viewerActiveForMs: number;
  viewerActiveTotalMs: number;
  queueDepth: number;
  runningJobs: number;
  longRunningJobs: number;
  activeWorkers: number;
  workerCount: number;
  executionProvider?: string;
  adapterId?: number;
  recentJobs: SemanticJobTraceSnapshot[];
}

export type TaggerSuggestionKind = "general" | "character" | "rating";
export type TaggerSuggestionState = "pending" | "accepted" | "rejected";

export interface TaggerSuggestion {
  id: number;
  assetId: number;
  kind: TaggerSuggestionKind;
  name: string;
  confidence: number;
  state: TaggerSuggestionState;
  threshold: number;
  engine: string;
  modelId: string;
  modelVersion: string;
  createdAt: string;
  updatedAt: string;
}

export interface TaggerReview {
  assetId: number;
  state: "queued" | "running" | "ready" | "failed" | "stale";
  engine?: string;
  modelId?: string;
  modelVersion?: string;
  suggestions: TaggerSuggestion[];
  errorMessage?: string;
  analyzedAt?: string;
  updatedAt?: string;
}

export interface TaggerThresholdOverrides {
  general: number | null;
  character: number | null;
  rating: number | null;
}

export interface AIRuntimeStatus {
  capability: string;
  state: AIRuntimeState;
  modelId?: string;
  version?: string;
  engine?: string;
  executionProvider?: string;
  adapterId?: number;
  warning?: string;
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

export type AIRuntimeState = "disabled" | "model_not_installed" | "not_loaded" | "ready" | "running" | "error";

export interface AIStorageInfo {
  mode: "installed" | "portable";
  rootPath: string;
  dataPath: string;
  databasePath: string;
  logsPath: string;
  modelsPath: string;
  runtimesPath: string;
  semanticIndexPath: string;
  webviewDataPath: string;
  preferredRootPath?: string;
  legacyPath?: string;
  legacyDetected: boolean;
  usingLegacy: boolean;
  migrationAvailable: boolean;
  migrationPending: boolean;
  migrationSourcePath?: string;
  migrationTargetPath?: string;
  migrationRequiresRestart: boolean;
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
    executionProvider?: string;
    warning?: string;
    error?: string;
  };
}

export interface LightweightRuntimeInfo {
  id: string;
  version: string;
  backend: "cpu" | "vulkan";
  sizeBytes: number;
  installed: boolean;
  executablePath?: string;
  platform: string;
  architecture: string;
  fallbackInstalled: boolean;
  fallbackSizeBytes?: number;
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
export const enqueueAutomaticSemanticAssets = Go.EnqueueAutomaticSemanticAssets;

type DynamicCommands = {
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
  GetTaggerReviewJSON?: (assetId: number) => Promise<string>;
  ReviewTaggerSuggestions?: (assetId: number, suggestionId: number, action: string) => Promise<void>;
  GetTaggerThresholdOverridesJSON?: () => Promise<string>;
  SetTaggerThresholdOverridesJSON?: (encoded: string) => Promise<void>;
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

function isTaggerReview(value: unknown): value is TaggerReview {
  if (!value || typeof value !== "object") return false;
  const source = value as Partial<TaggerReview>;
  if (typeof source.assetId !== "number" || typeof source.state !== "string") return false;
  if (!["queued", "running", "ready", "failed", "stale"].includes(source.state)) return false;
  return Array.isArray(source.suggestions);
}

export async function getTaggerReview(assetId: number): Promise<TaggerReview | null> {
  const method = appCommands()?.GetTaggerReviewJSON;
  if (!method) return null;
  const encoded = await method(assetId);
  if (!encoded) return null;
  const parsed: unknown = JSON.parse(encoded);
  if (!isTaggerReview(parsed)) {
    throw new Error("Tagger解析結果の形式が不正です。");
  }
  return {
    ...parsed,
    suggestions: parsed.suggestions.map((value) => ({
      ...value,
      confidence: Number.isFinite(value.confidence) ? value.confidence : 0,
      threshold: Number.isFinite(value.threshold) ? value.threshold : 0,
    })),
  };
}

export async function reviewTaggerSuggestions(
  assetId: number,
  suggestionId: number,
  action: "accept" | "reject" | "accept_all" | "reject_all",
): Promise<void> {
  const method = appCommands()?.ReviewTaggerSuggestions;
  if (!method) throw new Error("TaggerレビューAPIが利用できません。最新版のLumineを起動してください。");
  await method(assetId, suggestionId, action);
}

export async function getTaggerThresholdOverrides(): Promise<TaggerThresholdOverrides> {
  const method = appCommands()?.GetTaggerThresholdOverridesJSON;
  if (!method) return { general: null, character: null, rating: null };
  const encoded = await method();
  if (!encoded) return { general: null, character: null, rating: null };
  const parsed = JSON.parse(encoded) as Partial<TaggerThresholdOverrides>;
  const normalize = (value: unknown): number | null => {
    if (value === null || value === undefined) return null;
    if (typeof value !== "number" || !Number.isFinite(value) || value < 0 || value > 1) {
      throw new Error("Tagger threshold設定の形式が不正です。");
    }
    return value;
  };
  return {
    general: normalize(parsed.general),
    character: normalize(parsed.character),
    rating: normalize(parsed.rating),
  };
}

export async function setTaggerThresholdOverrides(value: TaggerThresholdOverrides): Promise<void> {
  const method = appCommands()?.SetTaggerThresholdOverridesJSON;
  if (!method) throw new Error("Tagger threshold設定APIが利用できません。最新版のLumineを起動してください。");
  await method(JSON.stringify(value));
}

export async function getAIRuntimeStatuses(): Promise<AIRuntimeStatus[]> {
  const values = await Go.GetAIRuntimeStatuses();
  return (values ?? []).map((value) => normalizeRuntimeStatus(value));
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

function normalizeRuntimeState(state: string): AIRuntimeState {
  switch (state) {
    case "disabled":
    case "model_not_installed":
    case "not_loaded":
    case "ready":
    case "running":
    case "error":
      return state;
    default:
      throw new Error(`未知のAI runtime stateです: ${state}`);
  }
}

function normalizeRuntimeStatus(value: {
  capability: string;
  state: string;
  modelId?: string;
  version?: string;
  engine?: string;
  executionProvider?: string;
  adapterId?: number;
  warning?: string;
  error?: string;
}): AIRuntimeStatus {
  return {
    capability: value.capability,
    state: normalizeRuntimeState(value.state),
    modelId: value.modelId,
    version: value.version,
    engine: value.engine,
    executionProvider: value.executionProvider,
    adapterId: value.adapterId,
    warning: value.warning,
    error: value.error,
  };
}

function normalizeLightweightRuntimeInfo(value: cmds.LightweightRuntimeInfo): LightweightRuntimeInfo {
  if (value.backend !== "cpu" && value.backend !== "vulkan") {
    throw new Error(`未知のllama.cpp backendです: ${value.backend}`);
  }
  return {
    id: value.id,
    version: value.version,
    backend: value.backend,
    sizeBytes: value.sizeBytes,
    installed: value.installed,
    executablePath: value.executablePath,
    platform: value.platform,
    architecture: value.architecture,
    fallbackInstalled: value.fallbackInstalled,
    fallbackSizeBytes: value.fallbackSizeBytes,
  };
}

function normalizeAIStorageInfo(value: cmds.AIStorageInfo): AIStorageInfo {
  if (value.mode !== "installed" && value.mode !== "portable") {
    throw new Error(`未知のLumine storage modeです: ${value.mode}`);
  }
  return {
    mode: value.mode,
    rootPath: value.rootPath,
    dataPath: value.dataPath,
    databasePath: value.databasePath,
    logsPath: value.logsPath,
    modelsPath: value.modelsPath,
    runtimesPath: value.runtimesPath,
    semanticIndexPath: value.semanticIndexPath,
    webviewDataPath: value.webviewDataPath,
    preferredRootPath: value.preferredRootPath,
    legacyPath: value.legacyPath,
    legacyDetected: value.legacyDetected,
    usingLegacy: value.usingLegacy,
    migrationAvailable: value.migrationAvailable,
    migrationPending: value.migrationPending,
    migrationSourcePath: value.migrationSourcePath,
    migrationTargetPath: value.migrationTargetPath,
    migrationRequiresRestart: value.migrationRequiresRestart,
  };
}

function normalizeLightweightAnalysisState(
  state: string,
): LightweightVisionAnalysis["state"] {
  switch (state) {
    case "queued":
    case "running":
    case "ready":
    case "failed":
    case "stale":
      return state;
    default:
      throw new Error(`未知のLightweight Vision stateです: ${state}`);
  }
}

function normalizeAdvancedOperation(
  operation: string,
): AdvancedVisionRun["operation"] {
  switch (operation) {
    case "analyze_deep":
    case "compare_images":
    case "reverse_prompt_support":
      return operation;
    default:
      throw new Error(`未知のAdvanced Vision operationです: ${operation}`);
  }
}

function normalizeAdvancedState(state: string): AdvancedVisionRun["state"] {
  switch (state) {
    case "running":
    case "ready":
    case "failed":
      return state;
    default:
      throw new Error(`未知のAdvanced Vision stateです: ${state}`);
  }
}

function normalizeAdvancedRun(value: cmds.AdvancedVisionRunDTO): AdvancedVisionRun {
  return {
    ...value,
    operation: normalizeAdvancedOperation(value.operation),
    state: normalizeAdvancedState(value.state),
    assetIds: value.assetIds ?? [],
    result: value.result
      ? {
          ...value.result,
          subjects: value.result.subjects ?? [],
          actions: value.result.actions ?? [],
          relationships: value.result.relationships ?? [],
          differences: value.result.differences ?? [],
          commonalities: value.result.commonalities ?? [],
          reversePromptHints: value.result.reversePromptHints ?? [],
          visibleText: value.result.visibleText ?? [],
          notes: value.result.notes ?? [],
        }
      : undefined,
  };
}

export async function getAISettings(): Promise<AISettings> {
  return normalizeAISettings(await Go.GetAISettings());
}

export async function getAIHealthSnapshot(): Promise<AIHealthSnapshot> {
  const value = await Go.GetAIHealthSnapshot();
  return {
    settings: normalizeAISettings(value.settings),
    settingsPersisted: value.settingsPersisted,
    settingsUpdatedAt:
      typeof value.settingsUpdatedAt === "string" ? value.settingsUpdatedAt : undefined,
    semanticRuntime: normalizeRuntimeStatus(value.semanticRuntime),
    semanticIndex: value.semanticIndex,
    queue: {
      started: value.queue?.started === true,
      workers: Number(value.queue?.workers ?? 0),
      activeCount: Number(value.queue?.activeCount ?? 0),
      activeCapabilities: value.queue?.activeCapabilities ?? [],
      pausedCapabilities: value.queue?.pausedCapabilities ?? [],
    },
    shuttingDown: value.shuttingDown,
    semanticSearchEnabled: value.semanticSearchEnabled,
  };
}

export async function getAIStorageInfo(): Promise<AIStorageInfo> {
  return normalizeAIStorageInfo(await Go.GetAIStorageInfo());
}

export async function requestLegacyStorageMigration(): Promise<AIStorageInfo> {
  return normalizeAIStorageInfo(await Go.RequestLegacyStorageMigration());
}

export async function cancelLegacyStorageMigration(): Promise<AIStorageInfo> {
  return normalizeAIStorageInfo(await Go.CancelLegacyStorageMigration());
}

export async function setAISettings(settings: AISettings): Promise<AISettings> {
  return normalizeAISettings(await Go.SetAISettings(settings));
}

export async function patchAISettings(
  patch: Partial<Record<keyof AISettings, boolean>>,
): Promise<AISettings> {
  return normalizeAISettings(await Go.PatchAISettings(patch as Record<string, boolean>));
}

export async function isAICapabilityEnabled(capability: string): Promise<boolean> {
  return Go.IsAICapabilityEnabled(capability);
}

export async function semanticSearchAssets(
  request: AssetListRequest,
  requestId = "",
): Promise<AssetListResponse> {
  return requestId
    ? Go.SemanticSearchAssetsWithID(request, requestId)
    : Go.SemanticSearchAssets(request);
}

export async function semanticSearchPage(
  sessionId: string,
  offset: number,
  limit: number,
): Promise<AssetListResponse> {
  return Go.SemanticSearchPage(sessionId, offset, limit);
}

export async function cancelSemanticSearch(requestId: string): Promise<void> {
  if (!requestId) return;
  await Go.CancelSemanticSearch(requestId);
}

export async function getSemanticIndexStatus(): Promise<SemanticIndexStatus> {
  return Go.GetSemanticIndexStatus();
}

export async function getSemanticPipelineDiagnostics(): Promise<SemanticPipelineDiagnosticsSnapshot> {
  return Go.GetSemanticPipelineDiagnostics() as unknown as SemanticPipelineDiagnosticsSnapshot;
}

export async function resetSemanticPipelineDiagnostics(): Promise<SemanticPipelineDiagnosticsSnapshot> {
  return Go.ResetSemanticPipelineDiagnostics() as unknown as SemanticPipelineDiagnosticsSnapshot;
}

export function onSemanticSearchProgress(callback: (progress: SemanticSearchProgress) => void): () => void {
  return EventsOn("semantic-search:progress", (value: unknown) => callback(value as SemanticSearchProgress));
}

export function onSemanticEmbeddingUpdated(callback: (event: SemanticEmbeddingUpdated) => void): () => void {
  return EventsOn("semantic:embedding-updated", (value: unknown) => callback(value as SemanticEmbeddingUpdated));
}

export async function listSimilarAssets(
  assetId: number,
  request: AssetListRequest,
): Promise<AssetListResponse> {
  return Go.ListSimilarAssets(assetId, request);
}

export async function getDefaultSemanticModelInfo(): Promise<SemanticModelInfo> {
  const value = await Go.GetDefaultSemanticModelInfo();
  return {
    ...value,
    runtime: normalizeRuntimeStatus(value.runtime),
  };
}

export async function ensureSemanticSearchReady(): Promise<void> {
  await Go.EnsureSemanticSearchReady();
}

export async function installDefaultSemanticModel(): Promise<void> {
  await Go.InstallDefaultSemanticModel();
}

export async function loadDefaultSemanticModel(): Promise<void> {
  await Go.LoadDefaultSemanticModel();
}

export async function getDefaultLightweightVisionModelInfo(): Promise<LightweightVisionModelInfo> {
  const value = await Go.GetDefaultLightweightVisionModelInfo();
  return {
    ...value,
    runtime: normalizeRuntimeStatus(value.runtime),
    llamaRuntime: normalizeLightweightRuntimeInfo(value.llamaRuntime),
  };
}

export async function installLightweightVisionRuntime(): Promise<void> {
  await Go.InstallLightweightVisionRuntime();
}

export async function removeLightweightVisionRuntime(): Promise<void> {
  await Go.RemoveLightweightVisionRuntime();
}

export async function installDefaultLightweightVisionModel(): Promise<void> {
  await Go.InstallDefaultLightweightVisionModel();
}

export async function removeDefaultLightweightVisionModel(): Promise<void> {
  await Go.RemoveDefaultLightweightVisionModel();
}

export async function loadDefaultLightweightVisionModel(): Promise<void> {
  await Go.LoadDefaultLightweightVisionModel();
}

export async function getLightweightVisionAnalysis(
  assetId: number,
): Promise<LightweightVisionAnalysis | null> {
  const value = await Go.GetLightweightVisionAnalysis(assetId);
  if (!value) return null;
  return {
    ...value,
    state: normalizeLightweightAnalysisState(value.state),
    result: value.result
      ? {
          ...value.result,
          visibleText: value.result.visibleText ?? [],
          notes: value.result.notes ?? [],
        }
      : undefined,
  };
}

export async function enqueueLightweightVisionBackfill(): Promise<number> {
  return Go.EnqueueLightweightVisionBackfill();
}

export async function reanalyzeAssets(
  assetIds: number[],
  capability: string,
  priority = 100,
): Promise<number> {
  return Go.ReanalyzeAssets(assetIds, capability, priority);
}

export async function getAdvancedVisionStatus(): Promise<AdvancedVisionStatusInfo> {
  const value = await Go.GetAdvancedVisionStatus();
  return {
    ...value,
    runtime: normalizeRuntimeStatus(value.runtime),
    llamaRuntime: normalizeLightweightRuntimeInfo(value.llamaRuntime),
    models: value.models ?? [],
  };
}

export async function installAdvancedVisionRuntime(): Promise<void> {
  await Go.InstallAdvancedVisionRuntime();
}

export async function removeAdvancedVisionRuntime(): Promise<void> {
  await Go.RemoveAdvancedVisionRuntime();
}

export async function installAdvancedVisionModel(modelId: string): Promise<void> {
  await Go.InstallAdvancedVisionModel(modelId);
}

export async function removeAdvancedVisionModel(modelId: string): Promise<void> {
  await Go.RemoveAdvancedVisionModel(modelId);
}

export async function loadAdvancedVisionModel(modelId: string): Promise<void> {
  await Go.LoadAdvancedVisionModel(modelId);
}

export async function runAdvancedVision(
  operation: AdvancedVisionRun["operation"],
  assetIds: number[],
  instruction = "",
): Promise<AdvancedVisionRun> {
  return normalizeAdvancedRun(await Go.RunAdvancedVision(operation, assetIds, instruction));
}

export async function getAdvancedVisionRun(runId: number): Promise<AdvancedVisionRun | null> {
  const value = await Go.GetAdvancedVisionRun(runId);
  return value ? normalizeAdvancedRun(value) : null;
}

export async function listAdvancedVisionRunsForAsset(
  assetId: number,
  limit = 20,
): Promise<AdvancedVisionRun[]> {
  return (await Go.ListAdvancedVisionRunsForAsset(assetId, limit)).map(normalizeAdvancedRun);
}

export async function getPromptEngineStatus(): Promise<PromptEngineStatusInfo> {
  const value = await Go.GetPromptEngineStatus();
  return {
    ...value,
    runtime: normalizeRuntimeStatus(value.runtime),
    llamaRuntime: normalizeLightweightRuntimeInfo(value.llamaRuntime),
    models: value.models ?? [],
  };
}

export async function installPromptEngineRuntime(): Promise<void> {
  await Go.InstallPromptEngineRuntime();
}

export async function removePromptEngineRuntime(): Promise<void> {
  await Go.RemovePromptEngineRuntime();
}

export async function installPromptEngineModel(modelId: string): Promise<void> {
  await Go.InstallPromptEngineModel(modelId);
}

export async function removePromptEngineModel(modelId: string): Promise<void> {
  await Go.RemovePromptEngineModel(modelId);
}

export async function loadPromptEngineModel(modelId: string): Promise<void> {
  await Go.LoadPromptEngineModel(modelId);
}

export async function runPromptEngine(request: PromptEngineRequest): Promise<PromptEngineResult> {
  const value = await Go.RunPromptEngine(request);
  return {
    ...value,
    characters: value.characters ?? [],
    loras: value.loras ?? [],
    notes: value.notes ?? [],
  };
}

function normalizeImagePromptResult(
  value: cmds.ImagePromptResultDTO,
): ImagePromptResult {
  return {
    ...value,
    characters: value.characters ?? [],
    loras: value.loras ?? [],
    notes: value.notes ?? [],
    sources: value.sources ?? [],
  };
}

export async function buildImagePrompt(request: ImagePromptRequest): Promise<ImagePromptResult> {
  return normalizeImagePromptResult(await Go.BuildImagePrompt(request));
}

export async function createPromptProjectFromImage(
  request: ImagePromptRequest,
): Promise<ImagePromptProjectResult> {
  const value = await Go.CreatePromptProjectFromImage(request);
  await queryClient.invalidateQueries({ queryKey: ["promptProjects"] });
  return {
    project: normalizePromptProject(value.project as unknown as PromptProject),
    result: normalizeImagePromptResult(value.result),
  };
}

export async function getAssetGenerationMetadata(
  assetId: number,
  refresh = false,
): Promise<AssetGenerationMetadata> {
  const value = await Go.GetAssetGenerationMetadata(assetId, refresh);
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
