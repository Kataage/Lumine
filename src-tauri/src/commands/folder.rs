use crate::db::Database;
use crate::domain::Library;
use crate::infrastructure::scan_folder_quick;
use std::sync::{Arc, Mutex};
use tauri::State;

#[tauri::command]
pub fn list_libraries(state: State<'_, Arc<Mutex<Database>>>) -> Result<Vec<Library>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    crate::application::LibraryService::new(&db)
        .list_libraries()
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub fn get_library_path(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
) -> Result<String, String> {
    let db = state.lock().map_err(|e| e.to_string())?;
    let library = crate::application::LibraryService::new(&db)
        .get_library(library_id)
        .map_err(|e| e.to_string())?;
    Ok(library.root_path)
}

#[tauri::command]
pub fn list_assets_from_folder(
    library_root_path: String,
    offset: Option<i64>,
    limit: Option<i64>,
) -> Result<Vec<crate::infrastructure::QuickAsset>, String> {
    let all = scan_folder_quick(&library_root_path);
    let offset = offset.unwrap_or(0) as usize;
    let limit = limit.unwrap_or(100) as usize;
    let end = (offset + limit).min(all.len());
    if offset > all.len() {
        return Ok(Vec::new());
    }
    Ok(all[offset..end].to_vec())
}
