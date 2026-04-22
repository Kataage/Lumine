use crate::application::AssetService;
use crate::db::Database;
use crate::domain::{Asset, AssetQuery, SortField, SortOrder, StatusLabel};
use std::sync::Mutex;
use tauri::State;

#[tauri::command]
pub fn list_assets(
    state: State<'_, Mutex<Database>>,
    library_id: Option<i64>,
    folder_path: Option<String>,
    search: Option<String>,
    tags: Option<Vec<i64>>,
    rating_min: Option<i32>,
    status_label: Option<String>,
    has_note: Option<bool>,
    is_favorite: Option<bool>,
    extension: Option<String>,
    sort_field: Option<String>,
    sort_order: Option<String>,
    offset: Option<i64>,
    limit: Option<i64>,
) -> Result<Vec<Asset>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = AssetService::new(&db);

    let sort_field = match sort_field.as_deref() {
        Some("created_at") => SortField::CreatedAt,
        Some("name") => SortField::Name,
        Some("size") => SortField::Size,
        Some("rating") => SortField::Rating,
        Some("status") => SortField::Status,
        _ => SortField::ModifiedAt,
    };

    let sort_order = match sort_order.as_deref() {
        Some("asc") => SortOrder::Asc,
        _ => SortOrder::Desc,
    };

    let query = AssetQuery {
        library_id,
        folder_path,
        search,
        tags,
        rating_min,
        status_label,
        has_note,
        is_favorite,
        extension,
        sort_field,
        sort_order,
        offset: offset.unwrap_or(0),
        limit: limit.unwrap_or(100),
    };

    service.list_assets(&query).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn get_asset_detail(state: State<'_, Mutex<Database>>, asset_id: i64) -> Result<Asset, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = AssetService::new(&db);
    service.get_asset(asset_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn update_asset_note(
    state: State<'_, Mutex<Database>>,
    asset_id: i64,
    content: String,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = AssetService::new(&db);
    service.update_asset_note(asset_id, &content).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn set_asset_rating(
    state: State<'_, Mutex<Database>>,
    asset_id: i64,
    rating: i32,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = AssetService::new(&db);
    service.set_asset_rating(asset_id, rating).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn set_asset_status(
    state: State<'_, Mutex<Database>>,
    asset_id: i64,
    status: String,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = AssetService::new(&db);
    let status_label = StatusLabel::from(status.as_str());
    service.set_asset_status(asset_id, status_label).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn set_asset_favorite(
    state: State<'_, Mutex<Database>>,
    asset_id: i64,
    is_favorite: bool,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = AssetService::new(&db);
    service.set_asset_favorite(asset_id, is_favorite).map_err(|e| e.to_string())
}