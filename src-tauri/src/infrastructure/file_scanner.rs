use crate::db::Database;
use anyhow::{Context, Result};
use std::fs;
use std::path::Path;
use walkdir::WalkDir;

const DEFAULT_IMAGE_EXTENSIONS: &[&str] = &[
    "jpg", "jpeg", "png", "gif", "bmp", "webp", "tiff", "tif", "ico", "svg", "avif", "apng",
];

pub struct FileScanner<'a> {
    db: &'a Database,
}

impl<'a> FileScanner<'a> {
    pub fn new(db: &'a Database) -> Self {
        Self { db }
    }

    pub fn scan_library(&self, library_id: i64, root_path: &str) -> Result<ScanResult> {
        let root = Path::new(root_path);
        if !root.exists() {
            anyhow::bail!("Path does not exist: {}", root_path);
        }

        let excluded_folders = self.get_excluded_folders(library_id)?;
        let supported_extensions = self.get_supported_extensions(library_id)?;

        let mut result = ScanResult::default();
        let mut conn = self.db.connection();
        let tx = conn.transaction()?;

        for entry in WalkDir::new(root)
            .follow_links(false)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            let path = entry.path();
            if !path.is_file() {
                continue;
            }

            let extension = match path.extension().and_then(|s| s.to_str()) {
                Some(ext) => ext.to_lowercase(),
                None => continue,
            };

            if !supported_extensions.is_empty() && !supported_extensions.contains(&extension) {
                continue;
            } else if supported_extensions.is_empty()
                && !DEFAULT_IMAGE_EXTENSIONS.contains(&extension.as_str())
            {
                continue;
            }

            let folder_path = path
                .parent()
                .and_then(|p| p.to_str())
                .unwrap_or(root_path);

            if self.is_folder_excluded(folder_path, root_path, &excluded_folders) {
                continue;
            }

            let file_name = match path.file_name().and_then(|s| s.to_str()) {
                Some(name) => name,
                None => continue,
            };

            let file_path = match path.to_str() {
                Some(p) => p,
                None => continue,
            };

            let metadata = match fs::metadata(path) {
                Ok(m) => m,
                Err(_) => continue,
            };

            let file_size = metadata.len() as i64;
            let modified_at = metadata
                .modified()
                .ok()
                .and_then(|t| {
                    let datetime: chrono::DateTime<chrono::Utc> = t.into();
                    Some(datetime.format("%Y-%m-%d %H:%M:%S").to_string())
                });

            let exists: bool = tx
                .query_row(
                    "SELECT EXISTS(SELECT 1 FROM assets WHERE file_path = ?)",
                    [file_path],
                    |row| row.get(0),
                )
                .with_context(|| format!("Failed to check if asset exists: {}", file_path))?;

            if !exists {
                tx.execute(
                    "INSERT INTO assets (library_id, folder_path, file_name, file_path, extension, file_size, modified_at_fs)
                     VALUES (?, ?, ?, ?, ?, ?, ?)",
                    rusqlite::params![library_id, folder_path, file_name, file_path, extension, file_size, modified_at],
                )?;
                result.added += 1;
            } else {
                result.unchanged += 1;
            }
        }

        tx.execute(
            "UPDATE libraries SET last_scanned_at = datetime('now'), updated_at = datetime('now') WHERE id = ?",
            [library_id],
        )?;

        tx.commit()?;
        Ok(result)
    }

    fn get_excluded_folders(&self, library_id: i64) -> Result<Vec<String>> {
        let conn = self.db.connection();
        let mut stmt = conn.prepare(
            "SELECT setting_value FROM library_settings
             WHERE library_id = ? AND setting_key = 'excluded_folders'",
        )?;
        let value: Option<String> = stmt
            .query_row([library_id], |row| row.get(0))
            .optional()?;
        Ok(value
            .map(|v| serde_json::from_str(&v).unwrap_or_default())
            .unwrap_or_default())
    }

    fn get_supported_extensions(&self, library_id: i64) -> Result<Vec<String>> {
        let conn = self.db.connection();
        let mut stmt = conn.prepare(
            "SELECT setting_value FROM library_settings
             WHERE library_id = ? AND setting_key = 'supported_extensions'",
        )?;
        let value: Option<String> = stmt
            .query_row([library_id], |row| row.get(0))
            .optional()?;
        Ok(value
            .map(|v| serde_json::from_str(&v).unwrap_or_default())
            .unwrap_or_default())
    }

    fn is_folder_excluded(
        &self,
        folder_path: &str,
        root_path: &str,
        excluded_folders: &[String],
    ) -> bool {
        let relative = folder_path.strip_prefix(root_path).unwrap_or(folder_path);
        excluded_folders.iter().any(|excluded| {
            relative.starts_with(excluded)
                || relative.starts_with(&format!("/{}", excluded))
                || relative.starts_with(&format!("\\{}", excluded))
        })
    }

    pub fn is_image_file(path: &Path) -> bool {
        if let Some(ext) = path.extension().and_then(|s| s.to_str()) {
            DEFAULT_IMAGE_EXTENSIONS.contains(&ext.to_lowercase().as_str())
        } else {
            false
        }
    }
}

#[derive(Debug, Clone, Default, serde::Serialize)]
pub struct ScanResult {
    pub added: u32,
    pub unchanged: u32,
    pub errors: u32,
}

impl ScanResult {
    pub fn add_error(&mut self) {
        self.errors += 1;
    }
}