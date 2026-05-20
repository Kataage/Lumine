use crate::application::LibraryService;
use crate::db::Database;
use crate::domain::Library;
use crate::infrastructure::FileScanner;
use crate::jobs::JobSystem;
use std::sync::{Arc, Mutex};
use tauri::State;

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

#[derive(serde::Serialize)]
pub struct ScanResultWithJob {
    pub added: u32,
    pub unchanged: u32,
    pub errors: u32,
    pub thumbnail_job_id: Option<String>,
}

#[tauri::command]
pub fn scan_library(
    state: State<'_, Arc<Mutex<Database>>>,
    job_system: State<'_, Arc<JobSystem>>,
    library_id: i64,
) -> Result<ScanResultWithJob, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let library_service = LibraryService::new(&db);
    let library = library_service.get_library(library_id).map_err(|e| e.to_string())?;
    let scanner = FileScanner::new(&db);
    let result = scanner
        .scan_library(library_id, &library.root_path)
        .map_err(|e| e.to_string())?;

    let thumbnail_job_id = job_system.start_thumbnail_generation(library_id);

    Ok(ScanResultWithJob {
        added: result.added,
        unchanged: result.unchanged,
        errors: result.errors,
        thumbnail_job_id: Some(thumbnail_job_id),
    })
}

#[derive(serde::Serialize)]
pub struct BootstrapData {
    pub libraries: Vec<Library>,
}
