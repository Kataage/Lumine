import { commands as cmds } from "../../wailsjs/go/models";
import * as Go from "../../wailsjs/go/commands/AppCommands";
import { EventsOn, EventsOff } from "../../wailsjs/runtime/runtime";
import { queryClient } from "../queryClient";

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

export interface ScanProgress {
  libraryId: number;
  scannedCount: number;
  addedCount: number;
  updatedCount: number;
  skippedCount: number;
  failedCount: number;
  isDone: boolean;
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

function viewerCommands() {
  return (window as unknown as {
    go?: {
      commands?: {
        AppCommands?: {
          GetViewerAssetDetail?: (id: number) => Promise<AssetDTO | null>;
          ScanLibraryViewer?: (libraryId: number) => Promise<void>;
        };
      };
    };
  }).go?.commands?.AppCommands;
}

export async function getAssetDetail(assetId: number): Promise<AssetDTO | null> {
  const method = viewerCommands()?.GetViewerAssetDetail;
  if (!method) return Go.GetAssetDetail(assetId);
  return method(assetId);
}

export async function scanLibrary(libraryId: number): Promise<void> {
  const method = viewerCommands()?.ScanLibraryViewer;
  if (method) await method(libraryId);
  else await Go.ScanLibrary(libraryId);

  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ["assets", libraryId] }),
    queryClient.invalidateQueries({ queryKey: ["folderTree", libraryId] }),
  ]);
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
export const listTags = Go.ListTags;
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
