use crate::db::Database;
use crate::domain::{Asset, Post, PostAccount, PostStatus, PostTarget, StatusLabel, ThumbStatus};
use anyhow::Result;
use rusqlite::params;

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
                    a.thumb_status, a.thumb_path, a.rating, a.status_label, a.is_favorite, a.color_label, a.indexed_at, a.updated_at
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
                    thumb_path: row.get(14)?,
                    rating: row.get(15)?,
                    status_label: StatusLabel::from(row.get::<_, String>(16)?.as_str()),
                    is_favorite: row.get::<_, i32>(17)? != 0,
                    color_label: row.get(18)?,
                    indexed_at: row.get(19)?,
                    updated_at: row.get(20)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(assets)
    }

    pub fn execute_scheduled_posts(&self) -> Result<u32> {
        let conn = self.db.connection();
        let mut stmt = conn.prepare(
            "SELECT id FROM posts
             WHERE status = 'scheduled'
             AND scheduled_at IS NOT NULL
             AND scheduled_at <= datetime('now')",
        )?;
        let post_ids: Vec<i64> = stmt
            .query_map([], |row| row.get(0))?
            .filter_map(|r| r.ok())
            .collect();

        let mut updated = 0u32;
        for post_id in post_ids {
            conn.execute(
                "UPDATE posts SET status = 'published', published_at = datetime('now'), updated_at = datetime('now')
                 WHERE id = ?",
                [post_id],
            )?;
            conn.execute(
                "UPDATE post_destinations SET status = 'published', published_at = datetime('now')
                 WHERE post_id = ? AND status = 'scheduled'",
                [post_id],
            )?;
            updated += 1;
        }

        Ok(updated)
    }
}
