use crate::application::TagService;
use crate::db::Database;
use crate::domain::Tag;
use std::sync::Mutex;
use tauri::State;

#[tauri::command]
pub fn list_tags(state: State<'_, Mutex<Database>>) -> Result<Vec<Tag>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = TagService::new(&db);
    service.list_tags().map_err(|e| e.to_string())
}

#[tauri::command]
pub fn create_tag(
    state: State<'_, Mutex<Database>>,
    name: String,
    color: Option<String>,
) -> Result<Tag, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = TagService::new(&db);
    service
        .create_tag(&name, color.as_deref())
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn set_asset_tags(
    state: State<'_, Mutex<Database>>,
    asset_id: i64,
    tag_ids: Vec<i64>,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = TagService::new(&db);
    service.set_asset_tags(asset_id, &tag_ids).map_err(|e| e.to_string())
}