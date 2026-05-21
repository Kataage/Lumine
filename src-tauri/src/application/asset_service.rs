use crate::db::Database;
use crate::domain::{Asset, AssetQuery, SortField, SortOrder, StatusLabel};
use anyhow::Result;
use rusqlite::{params, OptionalExtension};

pub struct AssetService<'a> {
    db: &'a Database,
}

impl<'a> AssetService<'a> {
    pub fn new(db: &'a Database) -> Self {
        Self { db }
    }

    pub fn list_assets(&self, query: &AssetQuery) -> Result<Vec<Asset>> {
        let conn = self.db.connection();
        let mut sql = String::from(
            "SELECT id, library_id, folder_path, file_name, file_path, extension, file_size,
                    created_at_fs, modified_at_fs, width, height, mime_type, hash_blake3,
                    rating, status_label, is_favorite, color_label, indexed_at, updated_at
             FROM assets WHERE 1=1",
        );
        let mut params_vec: Vec<Box<dyn rusqlite::ToSql>> = Vec::new();

        if let Some(library_id) = query.library_id {
            sql.push_str(" AND library_id = ?");
            params_vec.push(Box::new(library_id));
        }
        if let Some(ref folder_path) = query.folder_path {
            sql.push_str(" AND folder_path = ?");
            params_vec.push(Box::new(folder_path.clone()));
        }
        if let Some(ref search) = query.search {
            sql.push_str(" AND (file_name LIKE ? OR file_path LIKE ?)");
            let search_pattern = format!("%{}%", search);
            params_vec.push(Box::new(search_pattern.clone()));
            params_vec.push(Box::new(search_pattern));
        }
        if let Some(rating_min) = query.rating_min {
            sql.push_str(" AND rating >= ?");
            params_vec.push(Box::new(rating_min));
        }
        if let Some(ref status_label) = query.status_label {
            sql.push_str(" AND status_label = ?");
            params_vec.push(Box::new(status_label.clone()));
        }
        if let Some(is_favorite) = query.is_favorite {
            sql.push_str(" AND is_favorite = ?");
            params_vec.push(Box::new(if is_favorite { 1 } else { 0 }));
        }
        if let Some(ref extension) = query.extension {
            sql.push_str(" AND extension = ?");
            params_vec.push(Box::new(extension.clone()));
        }
        if let Some(ref tags) = query.tags {
            if !tags.is_empty() {
                sql.push_str(&format!(
                    " AND id IN (SELECT asset_id FROM asset_tags WHERE tag_id IN ({}) GROUP BY asset_id HAVING COUNT(DISTINCT tag_id) = {})",
                    tags.iter().map(|_| "?").collect::<Vec<_>>().join(","),
                    tags.len()
                ));
                for tag_id in tags {
                    params_vec.push(Box::new(*tag_id));
                }
            }
        }
        if let Some(has_note) = query.has_note {
            if has_note {
                sql.push_str(" AND EXISTS (SELECT 1 FROM asset_notes WHERE asset_id = assets.id)");
            } else {
                sql.push_str(" AND NOT EXISTS (SELECT 1 FROM asset_notes WHERE asset_id = assets.id)");
            }
        }

        let sort_field = match query.sort_field {
            SortField::ModifiedAt => "modified_at_fs",
            SortField::CreatedAt => "created_at_fs",
            SortField::Name => "file_name",
            SortField::Size => "file_size",
            SortField::Rating => "rating",
            SortField::Status => "status_label",
        };
        let sort_order = match query.sort_order {
            SortOrder::Asc => "ASC",
            SortOrder::Desc => "DESC",
        };
        sql.push_str(&format!(" ORDER BY {} {}", sort_field, sort_order));
        sql.push_str(" LIMIT ? OFFSET ?");
        params_vec.push(Box::new(query.limit));
        params_vec.push(Box::new(query.offset));

        let params_refs: Vec<&dyn rusqlite::ToSql> = params_vec.iter().map(|p| p.as_ref()).collect();

        let mut stmt = conn.prepare(&sql)?;
        let assets = stmt
            .query_map(params_refs.as_slice(), |row| {
                Ok(Asset {
                    id: row.get(0)?,
                    library_id: row.get(1)?,
                    folder_path: row.get(2)?,
                    file_name: row.get(3)?,
                    file_path: row.get(4)?,
                    extension: row.get(5)?,
                    file_size: row.get(6)?,
                    created_at_fs: row.get(7)?,
                    modified_at_fs: row.get(8)?,
                    width: row.get(9)?,
                    height: row.get(10)?,
                    mime_type: row.get(11)?,
                    hash_blake3: row.get(12)?,
                    rating: row.get(13)?,
                    status_label: StatusLabel::from(row.get::<_, String>(14)?.as_str()),
                    is_favorite: row.get::<_, i32>(15)? != 0,
                    color_label: row.get(16)?,
                    indexed_at: row.get(17)?,
                    updated_at: row.get(18)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(assets)
    }

    pub fn get_asset(&self, id: i64) -> Result<Asset> {
        let conn = self.db.connection();
        let asset = conn.query_row(
            "SELECT id, library_id, folder_path, file_name, file_path, extension, file_size,
                    created_at_fs, modified_at_fs, width, height, mime_type, hash_blake3,
                    rating, status_label, is_favorite, color_label, indexed_at, updated_at
             FROM assets WHERE id = ?",
            [id],
            |row| {
                Ok(Asset {
                    id: row.get(0)?,
                    library_id: row.get(1)?,
                    folder_path: row.get(2)?,
                    file_name: row.get(3)?,
                    file_path: row.get(4)?,
                    extension: row.get(5)?,
                    file_size: row.get(6)?,
                    created_at_fs: row.get(7)?,
                    modified_at_fs: row.get(8)?,
                    width: row.get(9)?,
                    height: row.get(10)?,
                    mime_type: row.get(11)?,
                    hash_blake3: row.get(12)?,
                    rating: row.get(13)?,
                    status_label: StatusLabel::from(row.get::<_, String>(14)?.as_str()),
                    is_favorite: row.get::<_, i32>(15)? != 0,
                    color_label: row.get(16)?,
                    indexed_at: row.get(17)?,
                    updated_at: row.get(18)?,
                })
            },
        )?;
        Ok(asset)
    }

    pub fn get_asset_note(&self, asset_id: i64) -> Result<Option<String>> {
        let conn = self.db.connection();
        let mut stmt = conn.prepare("SELECT content FROM asset_notes WHERE asset_id = ?")?;
        let note = stmt
            .query_row([asset_id], |row| row.get(0))
            .optional()?;
        Ok(note)
    }

    pub fn update_asset_note(&self, asset_id: i64, content: &str) -> Result<()> {
        let conn = self.db.connection();
        conn.execute(
            "INSERT INTO asset_notes (asset_id, content) VALUES (?, ?)
             ON CONFLICT(asset_id) DO UPDATE SET content = excluded.content, updated_at = datetime('now')",
            params![asset_id, content],
        )?;
        Ok(())
    }

    pub fn set_asset_rating(&self, asset_id: i64, rating: i32) -> Result<()> {
        let conn = self.db.connection();
        conn.execute(
            "UPDATE assets SET rating = ?, updated_at = datetime('now') WHERE id = ?",
            params![rating, asset_id],
        )?;
        Ok(())
    }

    pub fn set_asset_status(&self, asset_id: i64, status: StatusLabel) -> Result<()> {
        let conn = self.db.connection();
        let status_str: &'static str = status.into();
        conn.execute(
            "UPDATE assets SET status_label = ?, updated_at = datetime('now') WHERE id = ?",
            params![status_str, asset_id],
        )?;
        Ok(())
    }

    pub fn set_asset_favorite(&self, asset_id: i64, is_favorite: bool) -> Result<()> {
        let conn = self.db.connection();
        conn.execute(
            "UPDATE assets SET is_favorite = ?, updated_at = datetime('now') WHERE id = ?",
            params![if is_favorite { 1 } else { 0 }, asset_id],
        )?;
        Ok(())
    }
}
