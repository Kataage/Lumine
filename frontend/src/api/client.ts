import { commands as cmds } from "../../wailsjs/go/models";
import * as Go from "../../wailsjs/go/commands/AppCommands";
import { EventsOn, EventsOff } from "../../wailsjs/runtime/runtime";
import { queryClient } from "../queryClient";\nimport { normalizeAISettings, type AISettings } from "../utils/aiSettings";

export type LibraryDTO = cmds.LibraryDTO;
export type AssetDTO = cmds.AssetDTO;
export type FolderDTO = cmds.FolderDTO;
export type TagDTO = cmds.TagDTO;
export type AssetListRequest = cmds.AssetListRequest;
export type AssetListResponse = cmds.AssetListResponse;
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

export async function setAISettings(settings: AISettings): Promise<AISettings> {
  const value = await requireDynamic("SetAISettings")(settings);
  return normalizeAISettings(value ?? settings);
}

export async function isAICapabilityEnabled(capability: string): Promise<boolean> {
  return requireDynamic("IsAICapabilityEnabled")(capability);
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
