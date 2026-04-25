use crate::db::Database;
use crate::domain::{
    Asset, AssetQuery, Library, MoveConflictPolicy, Post, PostAccount, PostStatus, PostTarget,
    SortField, SortOrder, StatusLabel, Tag, ThumbStatus,
};
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

 let canonical_path = path.canonicalize()
 .with_context(|| format!("Failed to resolve path: {}", root_path))?;

 if !canonical_path.is_dir() {
 anyhow::bail!("Path is not a directory: {}", root_path);
 }

 let metadata = std::fs::metadata(&canonical_path)
 .with_context(|| format!("Failed to read metadata: {}", root_path))?;

 if metadata.permissions().readonly() {
 anyhow::bail!("Path is not writable (read-only permissions): {}", root_path);
 }

 let conn = self.db.connection();
 conn.execute(
 "INSERT INTO libraries (name, root_path) VALUES (?, ?)",
 params![name, root_path],
 )?;
 let id = conn.last_insert_rowid();
 drop(conn);

 self.get_library(id)
 }
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
                    thumb_status, rating, status_label, is_favorite, color_label, indexed_at, updated_at
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
                    thumb_status: ThumbStatus::from(row.get::<_, String>(13)?.as_str()),
                    rating: row.get(14)?,
                    status_label: StatusLabel::from(row.get::<_, String>(15)?.as_str()),
                    is_favorite: row.get::<_, i32>(16)? != 0,
                    color_label: row.get(17)?,
                    indexed_at: row.get(18)?,
                    updated_at: row.get(19)?,
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
                    thumb_status, rating, status_label, is_favorite, color_label, indexed_at, updated_at
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
                    thumb_status: ThumbStatus::from(row.get::<_, String>(13)?.as_str()),
                    rating: row.get(14)?,
                    status_label: StatusLabel::from(row.get::<_, String>(15)?.as_str()),
                    is_favorite: row.get::<_, i32>(16)? != 0,
                    color_label: row.get(17)?,
                    indexed_at: row.get(18)?,
                    updated_at: row.get(19)?,
                })
            },
        )?;
        Ok(asset)
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

pub struct TagService<'a> {
    db: &'a Database,
}

impl<'a> TagService<'a> {
    pub fn new(db: &'a Database) -> Self {
        Self { db }
    }

    pub fn list_tags(&self) -> Result<Vec<Tag>> {
        let conn = self.db.connection();
        let mut stmt = conn.prepare("SELECT id, name, color, created_at FROM tags ORDER BY name")?;
        let tags = stmt
            .query_map([], |row| {
                Ok(Tag {
                    id: row.get(0)?,
                    name: row.get(1)?,
                    color: row.get(2)?,
                    created_at: row.get(3)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(tags)
    }

    pub fn create_tag(&self, name: &str, color: Option<&str>) -> Result<Tag> {
        let conn = self.db.connection();
        conn.execute("INSERT INTO tags (name, color) VALUES (?, ?)", params![name, color])?;
        let id = conn.last_insert_rowid();
        drop(conn);
        self.get_tag(id)
    }

    pub fn get_tag(&self, id: i64) -> Result<Tag> {
        let conn = self.db.connection();
        let tag = conn.query_row(
            "SELECT id, name, color, created_at FROM tags WHERE id = ?",
            [id],
            |row| {
                Ok(Tag {
                    id: row.get(0)?,
                    name: row.get(1)?,
                    color: row.get(2)?,
                    created_at: row.get(3)?,
                })
            },
        )?;
        Ok(tag)
    }

    pub fn set_asset_tags(&self, asset_id: i64, tag_ids: &[i64]) -> Result<()> {
        let conn = self.db.connection();
        let tx = conn.unchecked_transaction()?;
        tx.execute("DELETE FROM asset_tags WHERE asset_id = ?", [asset_id])?;
        for tag_id in tag_ids {
            tx.execute(
                "INSERT OR IGNORE INTO asset_tags (asset_id, tag_id) VALUES (?, ?)",
                params![asset_id, tag_id],
            )?;
        }
        tx.commit()?;
        Ok(())
    }
}

pub struct PostService<'a> {
    db: &'a Database,
}

impl<'a> PostService<'a> {
    pub fn new(db: &'a Database) -> Self {
        Self { db }
    }

    pub fn list_post_targets(&self) -> Result<Vec<PostTarget>> {
        let conn = self.db.connection();
        let mut stmt =
            conn.prepare("SELECT id, name, kind, created_at FROM post_targets ORDER BY name")?;
        let targets = stmt
            .query_map([], |row| {
                Ok(PostTarget {
                    id: row.get(0)?,
                    name: row.get(1)?,
                    kind: row.get(2)?,
                    created_at: row.get(3)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(targets)
    }

    pub fn create_post_target(&self, name: &str, kind: &str) -> Result<PostTarget> {
        let conn = self.db.connection();
        conn.execute(
            "INSERT INTO post_targets (name, kind) VALUES (?, ?)",
            params![name, kind],
        )?;
        let id = conn.last_insert_rowid();
        drop(conn);
        self.get_post_target(id)
    }

    pub fn get_post_target(&self, id: i64) -> Result<PostTarget> {
        let conn = self.db.connection();
        let target = conn.query_row(
            "SELECT id, name, kind, created_at FROM post_targets WHERE id = ?",
            [id],
            |row| {
                Ok(PostTarget {
                    id: row.get(0)?,
                    name: row.get(1)?,
                    kind: row.get(2)?,
                    created_at: row.get(3)?,
                })
            },
        )?;
        Ok(target)
    }

    pub fn list_post_accounts(&self, target_id: Option<i64>) -> Result<Vec<PostAccount>> {
        let conn = self.db.connection();
        let sql = match target_id {
            Some(_) => "SELECT id, post_target_id, display_name, account_identifier, is_active, created_at, updated_at
                       FROM post_accounts WHERE post_target_id = ? ORDER BY display_name",
            None => "SELECT id, post_target_id, display_name, account_identifier, is_active, created_at, updated_at
                    FROM post_accounts ORDER BY display_name",
        };
        let mut stmt = conn.prepare(sql)?;
        let accounts = if let Some(tid) = target_id {
            stmt.query_map([tid], |row| {
                Ok(PostAccount {
                    id: row.get(0)?,
                    post_target_id: row.get(1)?,
                    display_name: row.get(2)?,
                    account_identifier: row.get(3)?,
                    is_active: row.get::<_, i32>(4)? != 0,
                    created_at: row.get(5)?,
                    updated_at: row.get(6)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?
        } else {
            stmt.query_map([], |row| {
                Ok(PostAccount {
                    id: row.get(0)?,
                    post_target_id: row.get(1)?,
                    display_name: row.get(2)?,
                    account_identifier: row.get(3)?,
                    is_active: row.get::<_, i32>(4)? != 0,
                    created_at: row.get(5)?,
                    updated_at: row.get(6)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?
        };
        Ok(accounts)
    }

    pub fn create_post_account(
        &self,
        target_id: i64,
        display_name: &str,
        account_identifier: &str,
    ) -> Result<PostAccount> {
        let conn = self.db.connection();
        conn.execute(
            "INSERT INTO post_accounts (post_target_id, display_name, account_identifier) VALUES (?, ?, ?)",
            params![target_id, display_name, account_identifier],
        )?;
        let id = conn.last_insert_rowid();
        drop(conn);
        self.get_post_account(id)
    }

    pub fn get_post_account(&self, id: i64) -> Result<PostAccount> {
        let conn = self.db.connection();
        let account = conn.query_row(
            "SELECT id, post_target_id, display_name, account_identifier, is_active, created_at, updated_at
             FROM post_accounts WHERE id = ?",
            [id],
            |row| {
                Ok(PostAccount {
                    id: row.get(0)?,
                    post_target_id: row.get(1)?,
                    display_name: row.get(2)?,
                    account_identifier: row.get(3)?,
                    is_active: row.get::<_, i32>(4)? != 0,
                    created_at: row.get(5)?,
                    updated_at: row.get(6)?,
                })
            },
        )?;
        Ok(account)
    }

    pub fn list_posts(&self, status: Option<PostStatus>) -> Result<Vec<Post>> {
        let conn = self.db.connection();
        let sql = match status {
            Some(_) => "SELECT id, title, body, hashtags, status, scheduled_at, published_at, created_at, updated_at
                       FROM posts WHERE status = ? ORDER BY updated_at DESC",
            None => "SELECT id, title, body, hashtags, status, scheduled_at, published_at, created_at, updated_at
                    FROM posts ORDER BY updated_at DESC",
        };
        let mut stmt = conn.prepare(sql)?;
        let posts = if let Some(s) = status {
            let status_str: &'static str = s.into();
            stmt.query_map([status_str], |row| {
                Ok(Post {
                    id: row.get(0)?,
                    title: row.get(1)?,
                    body: row.get(2)?,
                    hashtags: row.get(3)?,
                    status: PostStatus::from(row.get::<_, String>(4)?.as_str()),
                    scheduled_at: row.get(5)?,
                    published_at: row.get(6)?,
                    created_at: row.get(7)?,
                    updated_at: row.get(8)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?
        } else {
            stmt.query_map([], |row| {
                Ok(Post {
                    id: row.get(0)?,
                    title: row.get(1)?,
                    body: row.get(2)?,
                    hashtags: row.get(3)?,
                    status: PostStatus::from(row.get::<_, String>(4)?.as_str()),
                    scheduled_at: row.get(5)?,
                    published_at: row.get(6)?,
                    created_at: row.get(7)?,
                    updated_at: row.get(8)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?
        };
        Ok(posts)
    }

    pub fn create_post(
        &self,
        title: &str,
        body: &str,
        hashtags: &str,
        status: PostStatus,
    ) -> Result<Post> {
        let conn = self.db.connection();
        let status_str: &'static str = status.into();
        conn.execute(
            "INSERT INTO posts (title, body, hashtags, status) VALUES (?, ?, ?, ?)",
            params![title, body, hashtags, status_str],
        )?;
        let id = conn.last_insert_rowid();
        drop(conn);
        self.get_post(id)
    }

    pub fn get_post(&self, id: i64) -> Result<Post> {
        let conn = self.db.connection();
        let post = conn.query_row(
            "SELECT id, title, body, hashtags, status, scheduled_at, published_at, created_at, updated_at
             FROM posts WHERE id = ?",
            [id],
            |row| {
                Ok(Post {
                    id: row.get(0)?,
                    title: row.get(1)?,
                    body: row.get(2)?,
                    hashtags: row.get(3)?,
                    status: PostStatus::from(row.get::<_, String>(4)?.as_str()),
                    scheduled_at: row.get(5)?,
                    published_at: row.get(6)?,
                    created_at: row.get(7)?,
                    updated_at: row.get(8)?,
                })
            },
        )?;
        Ok(post)
    }

    pub fn update_post(&self, id: i64, title: &str, body: &str, hashtags: &str) -> Result<()> {
        let conn = self.db.connection();
        conn.execute(
            "UPDATE posts SET title = ?, body = ?, hashtags = ?, updated_at = datetime('now') WHERE id = ?",
            params![title, body, hashtags, id],
        )?;
        Ok(())
    }

    pub fn attach_assets_to_post(&self, post_id: i64, asset_ids: &[i64]) -> Result<()> {
        let conn = self.db.connection();
        let tx = conn.unchecked_transaction()?;
        for (order, asset_id) in asset_ids.iter().enumerate() {
            tx.execute(
                "INSERT OR IGNORE INTO post_assets (post_id, asset_id, sort_order) VALUES (?, ?, ?)",
                params![post_id, asset_id, order as i32],
            )?;
        }
        tx.commit()?;
        Ok(())
    }

    pub fn get_post_assets(&self, post_id: i64) -> Result<Vec<Asset>> {
        let conn = self.db.connection();
        let mut stmt = conn.prepare(
            "SELECT a.id, a.library_id, a.folder_path, a.file_name, a.file_path, a.extension, a.file_size,
                    a.created_at_fs, a.modified_at_fs, a.width, a.height, a.mime_type, a.hash_blake3,
                    a.thumb_status, a.rating, a.status_label, a.is_favorite, a.color_label, a.indexed_at, a.updated_at
             FROM assets a
             JOIN post_assets pa ON a.id = pa.asset_id
             WHERE pa.post_id = ?
             ORDER BY pa.sort_order",
        )?;
        let assets = stmt
            .query_map([post_id], |row| {
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
                    thumb_status: ThumbStatus::from(row.get::<_, String>(13)?.as_str()),
                    rating: row.get(14)?,
                    status_label: StatusLabel::from(row.get::<_, String>(15)?.as_str()),
                    is_favorite: row.get::<_, i32>(16)? != 0,
                    color_label: row.get(17)?,
                    indexed_at: row.get(18)?,
                    updated_at: row.get(19)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(assets)
    }
}