use crate::application::LibraryService;
use crate::db::Database;
use crate::domain::Library;
use crate::infrastructure::{file_scanner::ScanResult, FileScanner};
use crate::jobs::JobSystem;
use std::sync::{Arc, Mutex};
use tauri::{Emitter, State};

#[tauri::command]
pub fn get_app_bootstrap(state: State<'_, Arc<Mutex<Database>>>) -> Result<BootstrapData, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let library_service = LibraryService::new(&db);
    let libraries = library_service.list_libraries().map_err(|e| e.to_string())?;
    Ok(BootstrapData { libraries })
}

#[tauri::command]
pub fn list_libraries(state: State<'_, Arc<Mutex<Database>>>) -> Result<Vec<Library>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibraryService::new(&db);
    service.list_libraries().map_err(|e| e.to_string())
}

#[tauri::command]
pub fn add_library(
    state: State<'_, Arc<Mutex<Database>>>,
    name: String,
    root_path: String,
) -> Result<Library, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibraryService::new(&db);
    service.add_library(&name, &root_path).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn remove_library(state: State<'_, Arc<Mutex<Database>>>, library_id: i64) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibraryService::new(&db);
    service.remove_library(library_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn scan_library(
    state: State<'_, Arc<Mutex<Database>>>,
    job_system: State<'_, JobSystem>,
    library_id: i64,
) -> Result<ScanResult, String> {
    let job_id = format!("scan_{}_{}", library_id, chrono::Utc::now().timestamp());
    let _ = job_system.app_handle().emit("job_started", &job_id);

    let db = state.lock().map_err(|e| e.to_string())?;
    let library_service = LibraryService::new(&db);
    let library = library_service.get_library(library_id).map_err(|e| e.to_string())?;
    let scanner = FileScanner::new(&db);
    let result = scanner
        .scan_library(library_id, &library.root_path)
        .map_err(|e| e.to_string())?;

    let conn = db.connection();
    let mut stmt = conn.prepare("SELECT id FROM assets WHERE library_id = ? AND thumb_status = 'none' ORDER BY id DESC LIMIT ?")
        .map_err(|e| e.to_string())?;
    let asset_ids: Vec<i64> = stmt
        .query_map(rusqlite::params![library_id, result.added], |row| row.get(0))
        .map_err(|e| e.to_string())?
        .filter_map(|r| r.ok())
        .collect();

    for asset_id in asset_ids {
        job_system.queue_thumbnail(asset_id);
    }

    let _ = job_system.app_handle().emit("job_completed", &job_id);

    Ok(result)
}

#[derive(serde::Serialize)]
pub struct BootstrapData {
    pub libraries: Vec<Library>,
}