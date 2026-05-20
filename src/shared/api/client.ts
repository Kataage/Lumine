import { invoke } from "@tauri-apps/api/core";
import type {
  Asset,
  Library,
  Post,
  PostAccount,
  PostTarget,
  Tag,
} from "../../entities/types";

export interface AssetQuery {
  library_id?: number;
  folder_path?: string;
  search?: string;
  tags?: number[];
  rating_min?: number;
  status_label?: string;
  has_note?: boolean;
  is_favorite?: boolean;
  extension?: string;
  sort_field?: string;
  sort_order?: string;
  offset?: number;
  limit?: number;
}

export async function getAppBootstrap() {
  return invoke<{ libraries: Library[] }>("get_app_bootstrap");
}

export async function listLibraries() {
  return invoke<Library[]>("list_libraries");
}

export async function addLibrary(name: string, rootPath: string) {
  return invoke<Library>("add_library", { name, rootPath });
}

export async function removeLibrary(libraryId: number) {
  return invoke<void>("remove_library", { libraryId });
}

export async function scanLibrary(libraryId: number) {
  return invoke<{ added: number; unchanged: number; errors: number; thumbnail_job_id: string | null }>("scan_library", {
    libraryId,
  });
}

export async function listAssets(query: AssetQuery = {}) {
  return invoke<Asset[]>("list_assets", {
    libraryId: query.library_id ?? null,
    folderPath: query.folder_path ?? null,
    search: query.search ?? null,
    tags: query.tags ?? null,
    ratingMin: query.rating_min ?? null,
    statusLabel: query.status_label ?? null,
    hasNote: query.has_note ?? null,
    isFavorite: query.is_favorite ?? null,
    extension: query.extension ?? null,
    sortField: query.sort_field ?? null,
    sortOrder: query.sort_order ?? null,
    offset: query.offset ?? null,
    limit: query.limit ?? null,
  });
}

export async function getAssetDetail(assetId: number) {
  return invoke<Asset>("get_asset_detail", { assetId });
}

export async function getAssetNote(assetId: number) {
  return invoke<string | null>("get_asset_note", { assetId });
}

export async function updateAssetNote(assetId: number, content: string) {
  return invoke<void>("update_asset_note", { assetId, content });
}

export async function setAssetRating(assetId: number, rating: number) {
  return invoke<void>("set_asset_rating", { assetId, rating });
}

export async function setAssetStatus(assetId: number, status: string) {
  return invoke<void>("set_asset_status", { assetId, status });
}

export async function setAssetFavorite(assetId: number, isFavorite: boolean) {
  return invoke<void>("set_asset_favorite", { assetId, isFavorite });
}

export async function listTags() {
  return invoke<Tag[]>("list_tags");
}

export async function createTag(name: string, color?: string) {
  return invoke<Tag>("create_tag", { name, color: color ?? null });
}

export async function setAssetTags(assetId: number, tagIds: number[]) {
  return invoke<void>("set_asset_tags", { assetId, tagIds });
}

export async function listPostTargets() {
  return invoke<PostTarget[]>("list_post_targets");
}

export async function createPostTarget(name: string, kind: string) {
  return invoke<PostTarget>("create_post_target", { name, kind });
}

export async function listPostAccounts(targetId?: number) {
  return invoke<PostAccount[]>("list_post_accounts", {
    targetId: targetId ?? null,
  });
}

export async function createPostAccount(
  targetId: number,
  displayName: string,
  accountIdentifier: string
) {
  return invoke<PostAccount>("create_post_account", {
    targetId,
    displayName,
    accountIdentifier,
  });
}

export async function listPosts(status?: string) {
  return invoke<Post[]>("list_posts", { status: status ?? null });
}

export async function createPostDraft(
  title: string,
  body: string,
  hashtags: string
) {
  return invoke<Post>("create_post_draft", { title, body, hashtags });
}

export async function updatePost(
  postId: number,
  title: string,
  body: string,
  hashtags: string
) {
  return invoke<void>("update_post", { postId, title, body, hashtags });
}

export async function attachAssetsToPost(postId: number, assetIds: number[]) {
  return invoke<void>("attach_assets_to_post", { postId, assetIds });
}

export async function getPostAssets(postId: number) {
  return invoke<Asset[]>("get_post_assets", { postId });
}

export interface MoveResult {
  succeeded: number;
  skipped: number;
  errors: number;
  error_messages: string[];
}

export async function moveAssets(
  assetIds: number[],
  destinationFolder: string,
  conflictPolicy: string
) {
  return invoke<MoveResult>("move_assets", {
    assetIds,
    destinationFolder,
    conflictPolicy,
  });
}

export async function getLibraryPath(libraryId: number) {
  return invoke<string>("get_library_path", { libraryId });
}

export async function listAssetsFromFolder(libraryRootPath: string) {
  return invoke<Array<{
    id: number;
    file_path: string;
    file_name: string;
    folder_path: string;
    extension: string;
    file_size: number;
    modified_at: string;
    thumb_status: string;
    thumb_path: string | null;
  }>>("list_assets_from_folder", { libraryRootPath });
}

export async function startThumbnailGeneration(libraryId: number) {
  return invoke<string>("start_thumbnail_generation", { libraryId });
}

export interface JobStatus {
  id: string;
  job_type: string;
  status: string;
  progress: number;
  total: number;
  completed: number;
  message: string;
}

export async function getJobStatus(jobId: string) {
  return invoke<JobStatus>("get_job_status", { jobId });
}

export async function listJobs() {
  return invoke<JobStatus[]>("list_jobs");
}

export async function cancelJob(jobId: string) {
  return invoke<void>("cancel_job", { jobId });
}