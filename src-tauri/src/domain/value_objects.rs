use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SortField {
    ModifiedAt,
    CreatedAt,
    Name,
    Size,
    Rating,
    Status,
}

impl Default for SortField {
    fn default() -> Self {
        Self::ModifiedAt
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SortOrder {
    Asc,
    Desc,
}

impl Default for SortOrder {
    fn default() -> Self {
        Self::Desc
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct AssetQuery {
    pub library_id: Option<i64>,
    pub folder_path: Option<String>,
    pub search: Option<String>,
    pub tags: Option<Vec<i64>>,
    pub rating_min: Option<i32>,
    pub status_label: Option<String>,
    pub has_note: Option<bool>,
    pub is_favorite: Option<bool>,
    pub extension: Option<String>,
    pub sort_field: SortField,
    pub sort_order: SortOrder,
    pub offset: i64,
    pub limit: i64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum MoveConflictPolicy {
    Skip,
    Rename,
    Fail,
}

impl Default for MoveConflictPolicy {
    fn default() -> Self {
        Self::Skip
    }
}

impl From<&str> for MoveConflictPolicy {
    fn from(s: &str) -> Self {
        match s {
            "rename" => Self::Rename,
            "fail" => Self::Fail,
            _ => Self::Skip,
        }
    }
}

impl MoveConflictPolicy {
    pub fn as_str(&self) -> &'static str {
        match self {
            MoveConflictPolicy::Skip => "skip",
            MoveConflictPolicy::Rename => "rename",
            MoveConflictPolicy::Fail => "fail",
        }
    }
}