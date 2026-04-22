export interface Library {
  id: number;
  name: string;
  root_path: string;
  is_enabled: boolean;
  created_at: string;
  updated_at: string;
  last_scanned_at: string | null;
}

export interface Asset {
  id: number;
  library_id: number;
  folder_path: string;
  file_name: string;
  file_path: string;
  extension: string;
  file_size: number;
  created_at_fs: string | null;
  modified_at_fs: string | null;
  width: number | null;
  height: number | null;
  mime_type: string | null;
  hash_blake3: string | null;
  thumb_status: ThumbStatus;
  rating: number;
  status_label: StatusLabel;
  is_favorite: boolean;
  color_label: string | null;
  indexed_at: string;
  updated_at: string;
}

export type ThumbStatus = "none" | "queued" | "ready" | "failed";

export type StatusLabel =
  | "unorganized"
  | "selected"
  | "posting_candidate"
  | "posted";

export interface AssetNote {
  id: number;
  asset_id: number;
  content: string;
  created_at: string;
  updated_at: string;
}

export interface Tag {
  id: number;
  name: string;
  color: string | null;
  created_at: string;
}

export interface PostTarget {
  id: number;
  name: string;
  kind: string;
  created_at: string;
}

export interface PostAccount {
  id: number;
  post_target_id: number;
  display_name: string;
  account_identifier: string;
  is_active: boolean;
  created_at: string;
  updated_at: string;
}

export interface Post {
  id: number;
  title: string;
  body: string;
  hashtags: string;
  status: PostStatus;
  scheduled_at: string | null;
  published_at: string | null;
  created_at: string;
  updated_at: string;
}

export type PostStatus =
  | "draft"
  | "scheduled"
  | "published"
  | "failed"
  | "on_hold";

export interface PostAsset {
  post_id: number;
  asset_id: number;
  sort_order: number;
}

export interface JobLog {
  id: number;
  job_type: string;
  status: string;
  message: string;
  payload_json: string | null;
  started_at: string;
  finished_at: string | null;
}