use crate::application::LibraryService;
use crate::db::Database;
use crate::domain::Library;
use crate::infrastructure::{file_scanner::ScanResult, FileScanner};
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

#[tauri::command]
pub fn scan_library(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
) -> Result<ScanResult, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let library_service = LibraryService::new(&db);
    let library = library_service.get_library(library_id).map_err(|e| e.to_string())?;
    let scanner = FileScanner::new(&db);
    let result = scanner
        .scan_library(library_id, &library.root_path)
        .map_err(|e| e.to_string())?;

    Ok(result)
}

#[derive(serde::Serialize)]
pub struct BootstrapData {
    pub libraries: Vec<Library>,
}
