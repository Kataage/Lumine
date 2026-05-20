use crate::db::Database;
use crate::domain::Tag;
use anyhow::Result;
use rusqlite::params;

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

    pub fn get_asset_tags(&self, asset_id: i64) -> Result<Vec<Tag>> {
        let conn = self.db.connection();
        let mut stmt = conn.prepare(
            "SELECT t.id, t.name, t.color, t.created_at
             FROM tags t
             INNER JOIN asset_tags at ON t.id = at.tag_id
             WHERE at.asset_id = ?
             ORDER BY t.name",
        )?;
        let tags = stmt
            .query_map([asset_id], |row| {
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
}
