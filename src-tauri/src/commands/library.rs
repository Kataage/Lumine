use crate::application::LibraryService;
use crate::db::Database;
use crate::domain::Library;
use crate::infrastructure::{file_scanner::ScanResult, FileScanner};
use crate::jobs::JobSystem;
use std::sync::Mutex;
use tauri::{Emitter, State};

#[tauri::command]
pub fn get_app_bootstrap(state: State<'_, Mutex<Database>>) -> Result<BootstrapData, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let library_service = LibraryService::new(&db);
    let libraries = library_service.list_libraries().map_err(|e| e.to_string())?;
    Ok(BootstrapData { libraries })
}

#[tauri::command]
pub fn list_libraries(state: State<'_, Mutex<Database>>) -> Result<Vec<Library>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibraryService::new(&db);
    service.list_libraries().map_err(|e| e.to_string())
}

#[tauri::command]
pub fn add_library(
    state: State<'_, Mutex<Database>>,
    name: String,
    root_path: String,
) -> Result<Library, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibraryService::new(&db);
    service.add_library(&name, &root_path).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn remove_library(state: State<'_, Mutex<Database>>, library_id: i64) -> Result<(), String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let service = LibraryService::new(&db);
    service.remove_library(library_id).map_err(|e| e.to_string())
}

#[tauri::command]
pub fn scan_library(
    state: State<'_, Mutex<Database>>,
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

    let _ = job_system.app_handle().emit("job_completed", &job_id);

    Ok(result)
}

#[derive(serde::Serialize)]
pub struct BootstrapData {
    pub libraries: Vec<Library>,
}