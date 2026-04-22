use crate::application::PostService;
use crate::db::Database;
use crate::domain::{Asset, Post, PostAccount, PostStatus, PostTarget};
use std::sync::Mutex;
use tauri::State;

#[tauri::command]
pub fn list_post_targets(
    state: State<'_, Mutex<Database>>,
) -> Result<Vec<PostTarget>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service.list_post_targets().map_err(|e| e.to_string())
}

#[tauri::command]
pub fn create_post_target(
    state: State<'_, Mutex<Database>>,
    name: String,
    kind: String,
) -> Result<PostTarget, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service.create_post_target(&name, &kind).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn list_post_accounts(
    state: State<'_, Mutex<Database>>,
    target_id: Option<i64>,
) -> Result<Vec<PostAccount>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service.list_post_accounts(target_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn create_post_account(
    state: State<'_, Mutex<Database>>,
    target_id: i64,
    display_name: String,
    account_identifier: String,
) -> Result<PostAccount, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service
        .create_post_account(target_id, &display_name, &account_identifier)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn list_posts(state: State<'_, Mutex<Database>>, status: Option<String>) -> Result<Vec<Post>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    let post_status = status.as_ref().map(|s| PostStatus::from(s.as_str()));
    service.list_posts(post_status).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn create_post_draft(
    state: State<'_, Mutex<Database>>,
    title: String,
    body: String,
    hashtags: String,
) -> Result<Post, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service
        .create_post(&title, &body, &hashtags, PostStatus::Draft)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn update_post(
    state: State<'_, Mutex<Database>>,
    post_id: i64,
    title: String,
    body: String,
    hashtags: String,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service
        .update_post(post_id, &title, &body, &hashtags)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn attach_assets_to_post(
    state: State<'_, Mutex<Database>>,
    post_id: i64,
    asset_ids: Vec<i64>,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service
        .attach_assets_to_post(post_id, &asset_ids)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn get_post_assets(
    state: State<'_, Mutex<Database>>,
    post_id: i64,
) -> Result<Vec<Asset>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service.get_post_assets(post_id).map_err(|e| e.to_string())
}