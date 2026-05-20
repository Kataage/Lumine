use crate::db::Database;
use anyhow::Result;
use rusqlite::OptionalExtension;
use rusqlite::params;

pub struct LibrarySettingsService<'a> {
    db: &'a Database,
}

impl<'a> LibrarySettingsService<'a> {
    pub fn new(db: &'a Database) -> Self {
        Self { db }
    }

    pub fn get_excluded_folders(&self, library_id: i64) -> Result<Vec<String>> {
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

    pub fn set_excluded_folders(
        &self,
        library_id: i64,
        folders: &[String],
    ) -> Result<()> {
        let conn = self.db.connection();
        let value = serde_json::to_string(folders)?;
        conn.execute(
            "INSERT INTO library_settings (library_id, setting_key, setting_value)
             VALUES (?, 'excluded_folders', ?)
             ON CONFLICT(library_id, setting_key)
             DO UPDATE SET setting_value = excluded.setting_value, updated_at = datetime('now')",
            params![library_id, value],
        )?;
        Ok(())
    }

    pub fn get_supported_extensions(&self, library_id: i64) -> Result<Vec<String>> {
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
            .unwrap_or_else(|| {
                vec![
                    "jpg".to_string(),
                    "jpeg".to_string(),
                    "png".to_string(),
                    "gif".to_string(),
                    "bmp".to_string(),
                    "webp".to_string(),
                    "tiff".to_string(),
                    "tif".to_string(),
                    "ico".to_string(),
                    "svg".to_string(),
                    "avif".to_string(),
                    "apng".to_string(),
                ]
            }))
    }

    pub fn set_supported_extensions(
        &self,
        library_id: i64,
        extensions: &[String],
    ) -> Result<()> {
        let conn = self.db.connection();
        let value = serde_json::to_string(extensions)?;
        conn.execute(
            "INSERT INTO library_settings (library_id, setting_key, setting_value)
             VALUES (?, 'supported_extensions', ?)
             ON CONFLICT(library_id, setting_key)
             DO UPDATE SET setting_value = excluded.setting_value, updated_at = datetime('now')",
            params![library_id, value],
        )?;
        Ok(())
    }
}
