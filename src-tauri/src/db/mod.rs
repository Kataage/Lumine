use rusqlite::{Connection, Result};
use std::path::Path;
use std::sync::Mutex;

pub mod migrations;

pub struct Database {
    pub conn: Mutex<Connection>,
}

pub struct AssetRow {
    pub id: i64,
    pub file_path: String,
    pub modified_at_fs: Option<String>,
}

impl Database {
    pub fn new(path: &Path) -> Result<Self> {
        let conn = Connection::open(path)?;
        conn.execute_batch("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;")?;
        Ok(Self {
            conn: Mutex::new(conn),
        })
    }

    pub fn connection(&self) -> std::sync::MutexGuard<'_, Connection> {
        self.conn.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    pub fn get_asset_by_id(&self, asset_id: i64) -> Result<AssetRow> {
        let conn = self.connection();
        let mut stmt = conn.prepare("SELECT id, file_path, modified_at_fs FROM assets WHERE id = ?")?;
        let asset = stmt.query_row([asset_id], |row| {
            Ok(AssetRow {
                id: row.get(0)?,
                file_path: row.get(1)?,
                modified_at_fs: row.get(2)?,
            })
        })?;
        Ok(asset)
    }
}
