use crate::infrastructure::FolderWatcher;
use std::sync::Arc;
use tauri::State;

#[tauri::command]
pub fn start_folder_watcher(
    folder_watcher: State<'_, Arc<FolderWatcher>>,
    library_id: i64,
    root_path: String,
) -> Result<(), String> {
    folder_watcher.start_watching(library_id, &root_path)
}

#[tauri::command]
pub fn stop_folder_watcher(
    folder_watcher: State<'_, Arc<FolderWatcher>>,
    library_id: i64,
) -> Result<(), String> {
    folder_watcher.stop_watching(library_id);
    Ok(())
}

#[tauri::command]
pub fn is_folder_watching(
    folder_watcher: State<'_, Arc<FolderWatcher>>,
    library_id: i64,
) -> Result<bool, String> {
    Ok(folder_watcher.is_watching(library_id))
}
