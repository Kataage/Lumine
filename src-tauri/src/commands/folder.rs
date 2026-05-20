use crate::infrastructure::scan_folder_quick;
use std::sync::{Arc, Mutex};
use tauri::State;

#[tauri::command]
pub fn list_assets_from_folder(
    library_root_path: String,
) -> Result<Vec<crate::infrastructure::QuickAsset>, String> {
    Ok(scan_folder_quick(&library_root_path))
}
