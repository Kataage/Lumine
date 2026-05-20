use crate::application::PostService;
use crate::db::Database;
use crate::domain::{Post, PostAccount, PostStatus, PostTarget};
use std::sync::{Arc, Mutex};
use tauri::State;

#[tauri::command]
pub fn list_post_targets(state: State<'_, Arc<Mutex<Database>>>) -> Result<Vec<PostTarget>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service.list_post_targets().map_err(|e| e.to_string())
}

#[tauri::command]
pub fn create_post_target(
    state: State<'_, Arc<Mutex<Database>>>,
    name: String,
    kind: String,
) -> Result<PostTarget, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service
        .create_post_target(&name, &kind)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn list_post_accounts(
    state: State<'_, Arc<Mutex<Database>>>,
    target_id: Option<i64>,
) -> Result<Vec<PostAccount>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service.list_post_accounts(target_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn create_post_account(
    state: State<'_, Arc<Mutex<Database>>>,
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
pub fn list_posts(
    state: State<'_, Arc<Mutex<Database>>>,
    status: Option<String>,
) -> Result<Vec<Post>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    let status_enum = status.map(|s| PostStatus::from(s.as_str()));
    service.list_posts(status_enum).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn create_post_draft(
    state: State<'_, Arc<Mutex<Database>>>,
    title: String,
    body: String,
    hashtags: String,
    scheduled_at: Option<String>,
) -> Result<Post, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    let status = if scheduled_at.is_some() {
        PostStatus::Scheduled
    } else {
        PostStatus::Draft
    };
    let mut post = service
        .create_post(&title, &body, &hashtags, status)
        .map_err(|e| e.to_string())?;

    if let Some(scheduled) = scheduled_at {
        let conn = db.connection();
        conn.execute(
            "UPDATE posts SET scheduled_at = ? WHERE id = ?",
            [&scheduled, &post.id.to_string()],
        )
        .map_err(|e| e.to_string())?;
        post.scheduled_at = Some(scheduled);
        post.status = PostStatus::Scheduled;
    }

    Ok(post)
}

#[tauri::command]
pub fn update_post(
    state: State<'_, Arc<Mutex<Database>>>,
    post_id: i64,
    title: String,
    body: String,
    hashtags: String,
    scheduled_at: Option<String>,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service
        .update_post(post_id, &title, &body, &hashtags)
        .map_err(|e| e.to_string())?;

    if let Some(scheduled) = scheduled_at {
        let conn = db.connection();
        conn.execute(
            "UPDATE posts SET scheduled_at = ?, status = 'scheduled' WHERE id = ?",
            [&scheduled, &post_id.to_string()],
        )
        .map_err(|e| e.to_string())?;
    }

    Ok(())
}

#[tauri::command]
pub fn attach_assets_to_post(
    state: State<'_, Arc<Mutex<Database>>>,
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
    state: State<'_, Arc<Mutex<Database>>>,
    post_id: i64,
) -> Result<Vec<crate::domain::Asset>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service.get_post_assets(post_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn execute_scheduled_posts(
    state: State<'_, Arc<Mutex<Database>>>,
) -> Result<u32, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = PostService::new(&db);
    service.execute_scheduled_posts().map_err(|e| e.to_string())
}