use crate::application::LibraryService;
use crate::db::Database;
use crate::domain::Library;
use crate::infrastructure::FileScanner;
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

#[derive(serde::Serialize, Clone)]
pub struct ScanResult {
    pub added: u32,
    pub unchanged: u32,
    pub errors: u32,
}

#[tauri::command]
pub fn scan_library(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
) -> Result<ScanResult, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let library_service = LibraryService::new(&db);
    let library = library_service.get_library(library_id).map_err(|e| e.to_string())?;
    let root_path = library.root_path.clone();

    let db_arc = state.inner().clone();

    std::thread::spawn(move || {
        let db_guard = db_arc.lock();
        if let Ok(db) = db_guard {
            let scanner = FileScanner::new(&db);
            let result = scanner.scan_library(library_id, &root_path);

            if let Ok(res) = result {
                tracing::info!(
                    "Scan completed for library {}: +{} ={} errors={}",
                    library_id,
                    res.added,
                    res.unchanged,
                    res.errors
                );
            } else {
                tracing::error!("Scan failed for library {}: {:?}", library_id, result);
            }
        }
    });

    Ok(ScanResult {
        added: 0,
        unchanged: 0,
        errors: 0,
    })
}

#[derive(serde::Serialize)]
pub struct BootstrapData {
    pub libraries: Vec<Library>,
}
