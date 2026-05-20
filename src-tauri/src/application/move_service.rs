use crate::db::Database;
use crate::domain::MoveConflictPolicy;
use anyhow::{Context, Result};
use rusqlite::params;
use std::fs;
use std::path::Path;

pub struct MoveService<'a> {
    db: &'a Database,
}

pub struct MoveResult {
    pub succeeded: u32,
    pub skipped: u32,
    pub errors: u32,
    pub error_messages: Vec<String>,
}

impl<'a> MoveService<'a> {
    pub fn new(db: &'a Database) -> Self {
        Self { db }
    }

    pub fn move_assets(
        &self,
        asset_ids: &[i64],
        destination_folder: &str,
        policy: MoveConflictPolicy,
    ) -> Result<MoveResult> {
        let mut result = MoveResult {
            succeeded: 0,
            skipped: 0,
            errors: 0,
            error_messages: Vec::new(),
        };

        let dest_path = Path::new(destination_folder);
        if !dest_path.exists() {
            fs::create_dir_all(dest_path).with_context(|| {
                format!("Failed to create destination directory: {}", destination_folder)
            })?;
        }

        let conn = self.db.connection();
        let tx = conn.unchecked_transaction()?;

        for asset_id in asset_ids {
            let sp = tx.savepoint().map_err(|e| anyhow::anyhow!("Failed to create savepoint: {}", e))?;
            match self.move_single(&sp, *asset_id, dest_path, &policy) {
                Ok(move_outcome) => match move_outcome {
                    MoveOutcome::Moved => {
                        sp.commit().map_err(|e| anyhow::anyhow!("Failed to commit savepoint: {}", e))?;
                        result.succeeded += 1;
                    }
                    MoveOutcome::Skipped => {
                        sp.commit().map_err(|e| anyhow::anyhow!("Failed to commit savepoint: {}", e))?;
                        result.skipped += 1;
                    }
                },
                Err(e) => {
                    let _ = sp.rollback();
                    result.errors += 1;
                    result.error_messages.push(format!("Asset {}: {}", asset_id, e));
                }
            }
        }

        tx.commit()?;
        Ok(result)
    }

    fn move_single(
        &self,
        tx: &rusqlite::Transaction<'_>,
        asset_id: i64,
        dest_path: &Path,
        policy: &MoveConflictPolicy,
    ) -> Result<MoveOutcome> {
        let asset = tx.query_row(
            "SELECT id, file_path, file_name FROM assets WHERE id = ?",
            [asset_id],
            |row| {
                Ok((
                    row.get::<_, i64>(0)?,
                    row.get::<_, String>(1)?,
                    row.get::<_, String>(2)?,
                ))
            },
        )?;

        let (_id, source_path, file_name) = asset;
        let dest_file = dest_path.join(&file_name);

        if dest_file.exists() {
            match policy {
                MoveConflictPolicy::Skip => {
                    return Ok(MoveOutcome::Skipped);
                }
                MoveConflictPolicy::Rename => {
                    let stem = Path::new(&file_name)
                        .file_stem()
                        .and_then(|s| s.to_str())
                        .unwrap_or("unnamed");
                    let ext = Path::new(&file_name)
                        .extension()
                        .and_then(|s| s.to_str())
                        .unwrap_or("");
                    let mut counter = 1;
                    loop {
                        let new_name = if ext.is_empty() {
                            format!("{}_{}", stem, counter)
                        } else {
                            format!("{}_{}.{}", stem, counter, ext)
                        };
                        let new_dest = dest_path.join(&new_name);
                        if !new_dest.exists() {
                            return self.do_move(tx, asset_id, &source_path, &new_dest);
                        }
                        counter += 1;
                        if counter > 1000 {
                            anyhow::bail!("Too many name conflicts for asset {}", asset_id);
                        }
                    }
                }
                MoveConflictPolicy::Fail => {
                    anyhow::bail!("Destination file already exists: {:?}", dest_file);
                }
            }
        }

        self.do_move(tx, asset_id, &source_path, &dest_file)
    }

    fn do_move(
        &self,
        tx: &rusqlite::Transaction<'_>,
        asset_id: i64,
        source_path: &str,
        dest_path: &Path,
    ) -> Result<MoveOutcome> {
        let source = Path::new(source_path);

        let source_meta = fs::metadata(source).with_context(|| {
            format!("Failed to read source metadata: {}", source_path)
        })?;

        let dest_dir = dest_path.parent().ok_or_else(|| {
            anyhow::anyhow!("Destination path has no parent: {:?}", dest_path)
        })?;

        fs::create_dir_all(dest_dir).with_context(|| {
            format!("Failed to create destination directory: {:?}", dest_dir)
        })?;

        let result = fs::rename(source, dest_path);

        if result.is_err() {
            fs::copy(source, dest_path).with_context(|| {
                format!("Failed to copy from {:?} to {:?}", source, dest_path)
            })?;
            fs::remove_file(source).with_context(|| {
                format!("Failed to remove source after copy: {}", source_path)
            })?;
        }

        let new_path_str = dest_path
            .to_str()
            .ok_or_else(|| anyhow::anyhow!("Destination path is not valid UTF-8"))?;
        let new_folder = dest_dir
            .to_str()
            .ok_or_else(|| anyhow::anyhow!("Destination folder is not valid UTF-8"))?;

        tx.execute(
            "UPDATE assets SET file_path = ?, folder_path = ?, updated_at = datetime('now') WHERE id = ?",
            params![new_path_str, new_folder, asset_id],
        )?;

        Ok(MoveOutcome::Moved)
    }
}

enum MoveOutcome {
    Moved,
    Skipped,
}
