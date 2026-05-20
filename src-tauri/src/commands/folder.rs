use crate::application::LibraryService;
use crate::db::Database;
use crate::infrastructure::scan_folder_quick;
use std::sync::{Arc, Mutex};
use tauri::State;

#[tauri::command]
pub fn get_library_path(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
) -> Result<String, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let library = LibraryService::new(&db)
        .get_library(library_id)
        .map_err(|e| e.to_string())?;
    Ok(library.root_path)
}

#[tauri::command]
pub fn list_assets_from_folder(
    library_root_path: String,
) -> Result<Vec<crate::infrastructure::QuickAsset>, String> {
    Ok(scan_folder_quick(&library_root_path))
}
