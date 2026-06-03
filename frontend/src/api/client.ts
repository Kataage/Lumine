export interface LibraryDTO {
  id: number;
  name: string;
  rootPath: string;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  lastScannedAt?: string;
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

export interface ImageInfo {
  filePath: string;
  fileName: string;
  folderPath: string;
  extension: string;
  fileSize: number;
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

declare global {
  interface Window {
    go: {
      main: {
        AppCommands: {
          ListLibraries: () => Promise<LibraryDTO[]>;
          AddLibrary: (name: string, rootPath: string) => Promise<LibraryDTO | null>;
          RemoveLibrary: (id: number) => Promise<void>;
          SelectFolder: () => Promise<string>;
          ListAssets: (req: AssetListRequest) => Promise<AssetListResponse | null>;
          GetAssetDetail: (id: number) => Promise<AssetDTO | null>;
          UpdateAssetNote: (assetId: number, content: string) => Promise<void>;
          SetAssetTags: (assetId: number, tagIds: number[]) => Promise<void>;
          UpdateAssetRating: (assetId: number, rating: number) => Promise<void>;
          UpdateAssetStatus: (assetId: number, status: string) => Promise<void>;
          ToggleAssetFavorite: (assetId: number, favorite: boolean) => Promise<void>;
          BulkUpdateRating: (ids: number[], rating: number) => Promise<void>;
          BulkUpdateStatus: (ids: number[], status: string) => Promise<void>;
          BulkUpdateFavorite: (ids: number[], favorite: boolean) => Promise<void>;
          MoveAssets: (req: MoveRequest) => Promise<MoveResult | null>;
          ScanLibrary: (libraryId: number) => Promise<void>;
          CancelScan: () => Promise<void>;
          ListTags: () => Promise<TagDTO[]>;
          CreateTag: (name: string, color: string) => Promise<TagDTO | null>;
          DeleteTag: (id: number) => Promise<void>;
          ListPosts: (offset: number, limit: number) => Promise<PostDTO[]>;
          CreatePostDraft: (title: string, body: string, hashtags: string) => Promise<PostDTO | null>;
          AttachAssetsToPost: (postId: number, assetIds: number[]) => Promise<void>;
          GetPostsByAsset: (assetId: number) => Promise<PostDTO[]>;
          ListPostTargets: () => Promise<PostTargetDTO[]>;
          CreatePostTarget: (name: string, kind: string) => Promise<PostTargetDTO | null>;
          DeletePost: (id: number) => Promise<void>;
          GetSetting: (key: string) => Promise<string>;
          SetSetting: (key: string, valueJSON: string) => Promise<void>;
          GetAppBootstrap: () => Promise<Record<string, unknown>>;
          ScanFolder: (folderPath: string, offset: number, limit: number) => Promise<ScanResult | null>;
        };
      };
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

export async function removeLibrary(id: number): Promise<void> {
  return cmd().RemoveLibrary(id);
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

export async function moveAssets(req: MoveRequest): Promise<MoveResult | null> {
  return cmd().MoveAssets(req);
}

export async function scanLibrary(libraryId: number): Promise<void> {
  return cmd().ScanLibrary(libraryId);
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

export async function attachAssetsToPost(postId: number, assetIds: number[]): Promise<void> {
  return cmd().AttachAssetsToPost(postId, assetIds);
}

export async function listPostTargets(): Promise<PostTargetDTO[]> {
  return cmd().ListPostTargets();
}

export async function getAppBootstrap(): Promise<Record<string, unknown>> {
  return cmd().GetAppBootstrap();
}

export async function scanFolder(folderPath: string, offset: number, limit: number): Promise<ScanResult | null> {
  return cmd().ScanFolder(folderPath, offset, limit);
}

export function getLocalImageUrl(filePath: string): string {
  const normalizedPath = filePath.replace(/\\/g, "/");
  return `/local/${normalizedPath}`;
}
