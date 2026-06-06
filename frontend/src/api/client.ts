export interface LibraryDTO {
  id: number;
  name: string;
  rootPath: string;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  lastScannedAt?: string;
  assetCount?: number;
}

export interface AssetDTO {
  id: number;
  libraryId: number;
  folderPath: string;
  fileName: string;
  filePath: string;
  extension: string;
  fileSize: number;
  createdAtFs?: string;
  modifiedAtFs?: string;
  width: number;
  height: number;
  mimeType?: string;
  thumbStatus: string;
  rating: number;
  statusLabel: string;
  isFavorite: boolean;
  colorLabel?: string;
  noteContent?: string;
  tags?: TagDTO[];
  cameraModel?: string;
  lensModel?: string;
  focalLength?: string;
  aperture?: string;
  shutterSpeed?: string;
  iso?: number;
  exifDate?: string;
  gpsLatitude?: string;
  gpsLongitude?: string;
  hashBlake3?: string;
}

export interface FolderDTO {
  id: number;
  libraryId: number;
  path: string;
  parentPath?: string;
}

export interface TagDTO {
  id: number;
  name: string;
  color: string;
}

export interface AssetListRequest {
  libraryId: number;
  folderPath?: string;
  search?: string;
  rating?: number;
  statusLabel?: string;
  isFavorite?: boolean;
  tagIds?: number[];
  hasNote?: boolean;
  extension?: string;
  colorLabel?: string;
  sortBy?: string;
  sortDesc?: boolean;
  offset: number;
  limit: number;
}

export interface AssetListResponse {
  assets: AssetDTO[];
  totalCount: number;
}

export interface PostDTO {
  id: number;
  title: string;
  body: string;
  hashtags: string;
  status: string;
  scheduledAt?: string;
  publishedAt?: string;
  assetIds?: number[];
  createdAt: string;
  updatedAt: string;
}

export interface PostTargetDTO {
  id: number;
  name: string;
  kind: string;
}

export interface PostAccountDTO {
  id: number;
  postTargetId: number;
  displayName: string;
  accountIdentifier: string;
  isActive: boolean;
}

export interface ImageInfo {
  filePath: string;
  fileName: string;
  folderPath: string;
  extension: string;
  fileSize: number;
}

export interface CopyRequest {
  assetIds: number[];
  targetFolder: string;
}

export interface CopyResult {
  copiedCount: number;
  failedIds: number[];
  errors: string[];
}

export interface ScanResult {
  images: ImageInfo[];
  totalCount: number;
  hasMore: boolean;
}

export interface MoveRequest {
  assetIds: number[];
  destinationFolder: string;
  conflictPolicy: string;
}

export interface MoveResult {
  movedCount: number;
  skippedCount: number;
  failedCount: number;
  errors?: string[];
}

export interface ScanProgress {
  libraryID: number;
  scannedCount: number;
  addedCount: number;
  updatedCount: number;
  skippedCount: number;
  failedCount: number;
  isDone: boolean;
}

declare global {
  interface Window {
    go: {
      main: {
        AppCommands: {
          ListLibraries: () => Promise<LibraryDTO[]>;
          AddLibrary: (name: string, rootPath: string) => Promise<LibraryDTO | null>;
          UpdateLibrary: (id: number, name: string, rootPath: string) => Promise<LibraryDTO | null>;
          EnableLibrary: (id: number) => Promise<void>;
          DisableLibrary: (id: number) => Promise<void>;
          RemoveLibrary: (id: number) => Promise<void>;
          SelectFolder: () => Promise<string>; // rejects with error if dialog fails
          GetExcludedDirs: (libraryId: number) => Promise<string[]>;
          SetExcludedDirs: (libraryId: number, dirs: string[]) => Promise<void>;
          GetSupportedExtensions: () => Promise<string[]>;
          SetSupportedExtensions: (exts: string[]) => Promise<void>;
          ListAssets: (req: AssetListRequest) => Promise<AssetListResponse | null>;
          GetAssetDetail: (id: number) => Promise<AssetDTO | null>;
          UpdateAssetNote: (assetId: number, content: string) => Promise<void>;
          SetAssetTags: (assetId: number, tagIds: number[]) => Promise<void>;
          UpdateAssetRating: (assetId: number, rating: number) => Promise<void>;
          UpdateAssetStatus: (assetId: number, status: string) => Promise<void>;
          ToggleAssetFavorite: (assetId: number, favorite: boolean) => Promise<void>;
          UpdateAssetColorLabel: (assetId: number, label: string) => Promise<void>;
          BulkUpdateRating: (ids: number[], rating: number) => Promise<void>;
          BulkUpdateStatus: (ids: number[], status: string) => Promise<void>;
          BulkUpdateFavorite: (ids: number[], favorite: boolean) => Promise<void>;
          BulkUpdateColorLabel: (ids: number[], label: string) => Promise<void>;
          MoveAssets: (req: MoveRequest) => Promise<MoveResult | null>;
          ScanLibrary: (libraryId: number) => Promise<void>;
          CancelScan: () => Promise<void>;
          ListTags: () => Promise<TagDTO[]>;
          CreateTag: (name: string, color: string) => Promise<TagDTO | null>;
          DeleteTag: (id: number) => Promise<void>;
          ListPosts: (offset: number, limit: number) => Promise<PostDTO[]>;
          CreatePostDraft: (title: string, body: string, hashtags: string) => Promise<PostDTO | null>;
          UpdatePost: (id: number, title: string, body: string, hashtags: string, status: string) => Promise<PostDTO | null>;
          DeletePost: (id: number) => Promise<void>;
          AttachAssetsToPost: (postId: number, assetIds: number[]) => Promise<void>;
          GetPostsByAsset: (assetId: number) => Promise<PostDTO[]>;
          ListPostTargets: () => Promise<PostTargetDTO[]>;
          CreatePostTarget: (name: string, kind: string) => Promise<PostTargetDTO | null>;
          DeletePostTarget: (id: number) => Promise<void>;
          ListPostAccounts: () => Promise<PostAccountDTO[]>;
          CreatePostAccount: (targetId: number, displayName: string, identifier: string) => Promise<PostAccountDTO | null>;
          DeletePostAccount: (id: number) => Promise<void>;
          GetSetting: (key: string) => Promise<string>;
          SetSetting: (key: string, valueJSON: string) => Promise<void>;
          GetAppBootstrap: () => Promise<Record<string, unknown>>;
          ScanFolder: (folderPath: string, offset: number, limit: number) => Promise<ScanResult | null>;
          GetFolderTree: (libraryId: number) => Promise<FolderDTO[]>;
          BulkDeleteAssets: (ids: number[]) => Promise<void>;
          CopyAssets: (req: CopyRequest) => Promise<CopyResult>;
        };
      };
    };
    runtime: {
      EventsOn: (event: string, callback: (...args: unknown[]) => void) => void;
      EventsOff: (event: string) => void;
    };
  }
}

const cmd = () => window.go.main.AppCommands;

export async function selectFolder(): Promise<string> {
  return cmd().SelectFolder();
}

export async function listLibraries(): Promise<LibraryDTO[]> {
  return cmd().ListLibraries();
}

export async function addLibrary(name: string, rootPath: string): Promise<LibraryDTO | null> {
  return cmd().AddLibrary(name, rootPath);
}

export async function updateLibrary(id: number, name: string, rootPath: string): Promise<LibraryDTO | null> {
  return cmd().UpdateLibrary(id, name, rootPath);
}

export async function enableLibrary(id: number): Promise<void> {
  return cmd().EnableLibrary(id);
}

export async function disableLibrary(id: number): Promise<void> {
  return cmd().DisableLibrary(id);
}

export async function removeLibrary(id: number): Promise<void> {
  return cmd().RemoveLibrary(id);
}

export async function getExcludedDirs(libraryId: number): Promise<string[]> {
  return cmd().GetExcludedDirs(libraryId);
}

export async function setExcludedDirs(libraryId: number, dirs: string[]): Promise<void> {
  return cmd().SetExcludedDirs(libraryId, dirs);
}

export async function getSupportedExtensions(): Promise<string[]> {
  return cmd().GetSupportedExtensions();
}

export async function setSupportedExtensions(exts: string[]): Promise<void> {
  return cmd().SetSupportedExtensions(exts);
}

export async function listAssets(req: AssetListRequest): Promise<AssetListResponse | null> {
  return cmd().ListAssets(req);
}

export async function getAssetDetail(id: number): Promise<AssetDTO | null> {
  return cmd().GetAssetDetail(id);
}

export async function updateAssetNote(assetId: number, content: string): Promise<void> {
  return cmd().UpdateAssetNote(assetId, content);
}

export async function setAssetTags(assetId: number, tagIds: number[]): Promise<void> {
  return cmd().SetAssetTags(assetId, tagIds);
}

export async function updateAssetRating(assetId: number, rating: number): Promise<void> {
  return cmd().UpdateAssetRating(assetId, rating);
}

export async function updateAssetStatus(assetId: number, status: string): Promise<void> {
  return cmd().UpdateAssetStatus(assetId, status);
}

export async function toggleAssetFavorite(assetId: number, favorite: boolean): Promise<void> {
  return cmd().ToggleAssetFavorite(assetId, favorite);
}

export async function updateAssetColorLabel(assetId: number, label: string): Promise<void> {
  return cmd().UpdateAssetColorLabel(assetId, label);
}

export async function bulkUpdateRating(ids: number[], rating: number): Promise<void> {
  return cmd().BulkUpdateRating(ids, rating);
}

export async function bulkUpdateStatus(ids: number[], status: string): Promise<void> {
  return cmd().BulkUpdateStatus(ids, status);
}

export async function bulkUpdateFavorite(ids: number[], favorite: boolean): Promise<void> {
  return cmd().BulkUpdateFavorite(ids, favorite);
}

export async function bulkUpdateColorLabel(ids: number[], label: string): Promise<void> {
  return cmd().BulkUpdateColorLabel(ids, label);
}

export async function moveAssets(req: MoveRequest): Promise<MoveResult | null> {
  return cmd().MoveAssets(req);
}

export async function scanLibrary(libraryId: number): Promise<void> {
  return cmd().ScanLibrary(libraryId);
}

export async function cancelScan(): Promise<void> {
  return cmd().CancelScan();
}

export async function listTags(): Promise<TagDTO[]> {
  return cmd().ListTags();
}

export async function createTag(name: string, color: string): Promise<TagDTO | null> {
  return cmd().CreateTag(name, color);
}

export async function deleteTag(id: number): Promise<void> {
  return cmd().DeleteTag(id);
}

export async function listPosts(offset: number, limit: number): Promise<PostDTO[]> {
  return cmd().ListPosts(offset, limit);
}

export async function createPostDraft(title: string, body: string, hashtags: string): Promise<PostDTO | null> {
  return cmd().CreatePostDraft(title, body, hashtags);
}

export async function updatePost(id: number, title: string, body: string, hashtags: string, status: string): Promise<PostDTO | null> {
  return cmd().UpdatePost(id, title, body, hashtags, status);
}

export async function deletePost(id: number): Promise<void> {
  return cmd().DeletePost(id);
}

export async function attachAssetsToPost(postId: number, assetIds: number[]): Promise<void> {
  return cmd().AttachAssetsToPost(postId, assetIds);
}

export async function getPostsByAsset(assetId: number): Promise<PostDTO[]> {
  return cmd().GetPostsByAsset(assetId);
}

export async function listPostTargets(): Promise<PostTargetDTO[]> {
  return cmd().ListPostTargets();
}

export async function createPostTarget(name: string, kind: string): Promise<PostTargetDTO | null> {
  return cmd().CreatePostTarget(name, kind);
}

export async function deletePostTarget(id: number): Promise<void> {
  return cmd().DeletePostTarget(id);
}

export async function listPostAccounts(): Promise<PostAccountDTO[]> {
  return cmd().ListPostAccounts();
}

export async function createPostAccount(targetId: number, displayName: string, identifier: string): Promise<PostAccountDTO | null> {
  return cmd().CreatePostAccount(targetId, displayName, identifier);
}

export async function deletePostAccount(id: number): Promise<void> {
  return cmd().DeletePostAccount(id);
}

export async function getSetting(key: string): Promise<string> {
  return cmd().GetSetting(key);
}

export async function setSetting(key: string, valueJSON: string): Promise<void> {
  return cmd().SetSetting(key, valueJSON);
}

export async function getAppBootstrap(): Promise<Record<string, unknown>> {
  return cmd().GetAppBootstrap();
}

export async function scanFolder(folderPath: string, offset: number, limit: number): Promise<ScanResult | null> {
  return cmd().ScanFolder(folderPath, offset, limit);
}

export function onScanProgress(callback: (progress: ScanProgress) => void): void {
  window.runtime?.EventsOn("scan:progress", (data: unknown) => {
    callback(data as ScanProgress);
  });
}

export function offScanProgress(): void {
  window.runtime?.EventsOff("scan:progress");
}

export function getLocalImageUrl(filePath: string): string {
  const normalizedPath = filePath.replace(/\\/g, "/");
  return `/local/${normalizedPath}`;
}

export async function getFolderTree(libraryId: number): Promise<FolderDTO[]> {
  return cmd().GetFolderTree(libraryId);
}

export async function bulkDeleteAssets(ids: number[]): Promise<void> {
  return cmd().BulkDeleteAssets(ids);
}

export async function copyAssets(req: CopyRequest): Promise<CopyResult> {
  return cmd().CopyAssets(req);
}
