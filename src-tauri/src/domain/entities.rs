use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Library {
    pub id: i64,
    pub name: String,
    pub root_path: String,
    pub is_enabled: bool,
    pub created_at: String,
    pub updated_at: String,
    pub last_scanned_at: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Folder {
    pub id: i64,
    pub library_id: i64,
    pub path: String,
    pub parent_path: Option<String>,
    pub is_excluded: bool,
    pub created_at: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Asset {
    pub id: i64,
    pub library_id: i64,
    pub folder_path: String,
    pub file_name: String,
    pub file_path: String,
    pub extension: String,
    pub file_size: i64,
    pub created_at_fs: Option<String>,
    pub modified_at_fs: Option<String>,
    pub width: Option<i32>,
    pub height: Option<i32>,
    pub mime_type: Option<String>,
    pub hash_blake3: Option<String>,
    pub thumb_status: ThumbStatus,
    pub rating: i32,
    pub status_label: StatusLabel,
    pub is_favorite: bool,
    pub color_label: Option<String>,
    pub indexed_at: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ThumbStatus {
    None,
    Queued,
    Ready,
    Failed,
}

impl Default for ThumbStatus {
    fn default() -> Self {
        Self::None
    }
}

impl From<&str> for ThumbStatus {
    fn from(s: &str) -> Self {
        match s {
            "queued" => Self::Queued,
            "ready" => Self::Ready,
            "failed" => Self::Failed,
            _ => Self::None,
        }
    }
}

impl From<ThumbStatus> for &'static str {
    fn from(status: ThumbStatus) -> Self {
        match status {
            ThumbStatus::None => "none",
            ThumbStatus::Queued => "queued",
            ThumbStatus::Ready => "ready",
            ThumbStatus::Failed => "failed",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum StatusLabel {
    Unorganized,
    Selected,
    PostingCandidate,
    Posted,
}

impl Default for StatusLabel {
    fn default() -> Self {
        Self::Unorganized
    }
}

impl From<&str> for StatusLabel {
    fn from(s: &str) -> Self {
        match s {
            "selected" => Self::Selected,
            "posting_candidate" => Self::PostingCandidate,
            "posted" => Self::Posted,
            _ => Self::Unorganized,
        }
    }
}

impl From<StatusLabel> for &'static str {
    fn from(label: StatusLabel) -> Self {
        match label {
            StatusLabel::Unorganized => "unorganized",
            StatusLabel::Selected => "selected",
            StatusLabel::PostingCandidate => "posting_candidate",
            StatusLabel::Posted => "posted",
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AssetNote {
    pub id: i64,
    pub asset_id: i64,
    pub content: String,
    pub created_at: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Tag {
    pub id: i64,
    pub name: String,
    pub color: Option<String>,
    pub created_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PostTarget {
    pub id: i64,
    pub name: String,
    pub kind: String,
    pub created_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PostAccount {
    pub id: i64,
    pub post_target_id: i64,
    pub display_name: String,
    pub account_identifier: String,
    pub is_active: bool,
    pub created_at: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum PostStatus {
    Draft,
    Scheduled,
    Published,
    Failed,
    OnHold,
}

impl Default for PostStatus {
    fn default() -> Self {
        Self::Draft
    }
}

impl From<&str> for PostStatus {
    fn from(s: &str) -> Self {
        match s {
            "scheduled" => Self::Scheduled,
            "published" => Self::Published,
            "failed" => Self::Failed,
            "on_hold" => Self::OnHold,
            _ => Self::Draft,
        }
    }
}

impl From<PostStatus> for &'static str {
    fn from(status: PostStatus) -> Self {
        match status {
            PostStatus::Draft => "draft",
            PostStatus::Scheduled => "scheduled",
            PostStatus::Published => "published",
            PostStatus::Failed => "failed",
            PostStatus::OnHold => "on_hold",
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Post {
    pub id: i64,
    pub title: String,
    pub body: String,
    pub hashtags: String,
    pub status: PostStatus,
    pub scheduled_at: Option<String>,
    pub published_at: Option<String>,
    pub created_at: String,
    pub updated_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PostAsset {
    pub post_id: i64,
    pub asset_id: i64,
    pub sort_order: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct JobLog {
    pub id: i64,
    pub job_type: String,
    pub status: String,
    pub message: String,
    pub payload_json: Option<String>,
    pub started_at: String,
    pub finished_at: Option<String>,
}