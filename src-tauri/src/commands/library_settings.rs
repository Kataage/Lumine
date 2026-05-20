use crate::application::LibrarySettingsService;
use crate::db::Database;
use std::sync::{Arc, Mutex};
use tauri::State;

#[tauri::command]
pub fn get_excluded_folders(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
) -> Result<Vec<String>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibrarySettingsService::new(&db);
    service.get_excluded_folders(library_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn set_excluded_folders(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
    folders: Vec<String>,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibrarySettingsService::new(&db);
    service
        .set_excluded_folders(library_id, &folders)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn get_supported_extensions(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
) -> Result<Vec<String>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibrarySettingsService::new(&db);
    service
        .get_supported_extensions(library_id)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn set_supported_extensions(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
    extensions: Vec<String>,
) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibrarySettingsService::new(&db);
    service
        .set_supported_extensions(library_id, &extensions)
        .map_err(|e| e.to_string())
}
