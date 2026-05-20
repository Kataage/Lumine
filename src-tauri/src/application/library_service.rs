use crate::db::Database;
use crate::domain::Library;
use anyhow::{Context, Result};
use rusqlite::params;
use std::path::Path;

pub struct LibraryService<'a> {
    db: &'a Database,
}

impl<'a> LibraryService<'a> {
    pub fn new(db: &'a Database) -> Self {
        Self { db }
    }

    pub fn list_libraries(&self) -> Result<Vec<Library>> {
        let conn = self.db.connection();
        let mut stmt = conn.prepare(
            "SELECT id, name, root_path, is_enabled, created_at, updated_at, last_scanned_at
             FROM libraries ORDER BY name",
        )?;
        let libs = stmt
            .query_map([], |row| {
                Ok(Library {
                    id: row.get(0)?,
                    name: row.get(1)?,
                    root_path: row.get(2)?,
                    is_enabled: row.get::<_, i32>(3)? != 0,
                    created_at: row.get(4)?,
                    updated_at: row.get(5)?,
                    last_scanned_at: row.get(6)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(libs)
    }

    pub fn add_library(&self, name: &str, root_path: &str) -> Result<Library> {
        let path = Path::new(root_path);
        let canonical_path = path
            .canonicalize()
            .with_context(|| format!("Failed to resolve path: {}", root_path))?;

        if !canonical_path.is_dir() {
            anyhow::bail!("Path is not a directory: {}", root_path);
        }

        let conn = self.db.connection();
        conn.execute(
            "INSERT INTO libraries (name, root_path) VALUES (?, ?)",
            params![name, canonical_path.to_string_lossy()],
        )?;
        let id = conn.last_insert_rowid();
        drop(conn);
        self.get_library(id)
    }

    pub fn get_library(&self, id: i64) -> Result<Library> {
        let conn = self.db.connection();
        let lib = conn.query_row(
            "SELECT id, name, root_path, is_enabled, created_at, updated_at, last_scanned_at
             FROM libraries WHERE id = ?",
            [id],
            |row| {
                Ok(Library {
                    id: row.get(0)?,
                    name: row.get(1)?,
                    root_path: row.get(2)?,
                    is_enabled: row.get::<_, i32>(3)? != 0,
                    created_at: row.get(4)?,
                    updated_at: row.get(5)?,
                    last_scanned_at: row.get(6)?,
                })
            },
        )?;
        Ok(lib)
    }

    pub fn remove_library(&self, id: i64) -> Result<()> {
        let conn = self.db.connection();
        conn.execute("DELETE FROM libraries WHERE id = ?", [id])?;
        Ok(())
    }
}
