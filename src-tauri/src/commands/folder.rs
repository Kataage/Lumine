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

#[derive(serde::Serialize)]
pub struct FolderAssetRow {
    pub id: i64,
    pub file_path: String,
    pub file_name: String,
    pub folder_path: String,
    pub extension: String,
    pub file_size: i64,
    pub modified_at: String,
    pub thumb_status: String,
    pub thumb_path: Option<String>,
}

#[tauri::command]
pub fn list_assets_from_folder(
    state: State<'_, Arc<Mutex<Database>>>,
    library_id: i64,
    library_root_path: String,
) -> Result<Vec<FolderAssetRow>, String> {
    let db = state.lock().map_err(|e| e.to_string())?;

    let excluded_folders = {
        let service = crate::application::LibrarySettingsService::new(&db);
        service.get_excluded_folders(library_id).unwrap_or_default()
    };

    let supported_extensions = {
        let service = crate::application::LibrarySettingsService::new(&db);
        service.get_supported_extensions(library_id).unwrap_or_default()
    };

    let quick_assets = scan_folder_quick(&library_root_path, &excluded_folders, &supported_extensions);

    let conn = db.connection();

    let mut result = Vec::new();

    for quick in &quick_assets {
        let exists: bool = conn
            .query_row(
                "SELECT EXISTS(SELECT 1 FROM assets WHERE file_path = ?)",
                [&quick.file_path],
                |row| row.get(0),
            )
            .map_err(|e| e.to_string())?;

        if !exists {
            conn.execute(
                "INSERT INTO assets (library_id, folder_path, file_name, file_path, extension, file_size, modified_at_fs, thumb_status)
                 VALUES (0, ?, ?, ?, ?, ?, ?, 'none')
                 ON CONFLICT(file_path) DO NOTHING",
                [
                    &quick.folder_path,
                    &quick.file_name,
                    &quick.file_path,
                    &quick.extension,
                    &quick.file_size.to_string(),
                    &quick.modified_at,
                ],
            )
            .ok();
        }

        let row: Option<(i64, String, String, String, String, i64, String, String, Option<String>)> = conn
            .query_row(
                "SELECT id, file_path, file_name, folder_path, extension, file_size,
                        COALESCE(modified_at_fs, ''), thumb_status, thumb_path
                 FROM assets WHERE file_path = ?",
                [&quick.file_path],
                |row| {
                    Ok((
                        row.get(0)?,
                        row.get(1)?,
                        row.get(2)?,
                        row.get(3)?,
                        row.get(4)?,
                        row.get(5)?,
                        row.get(6)?,
                        row.get(7)?,
                        row.get(8)?,
                    ))
                },
            )
            .ok();

        if let Some((id, file_path, file_name, folder_path, extension, file_size, modified_at, thumb_status, thumb_path)) = row {
            result.push(FolderAssetRow {
                id,
                file_path,
                file_name,
                folder_path,
                extension,
                file_size,
                modified_at,
                thumb_status,
                thumb_path,
            });
        } else {
            result.push(FolderAssetRow {
                id: quick.id,
                file_path: quick.file_path.clone(),
                file_name: quick.file_name.clone(),
                folder_path: quick.folder_path.clone(),
                extension: quick.extension.clone(),
                file_size: quick.file_size,
                modified_at: quick.modified_at.clone(),
                thumb_status: "none".to_string(),
                thumb_path: None,
            });
        }
    }

    Ok(result)
}
