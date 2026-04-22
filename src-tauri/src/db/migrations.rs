use rusqlite::Connection;
use std::fs;
use std::path::Path;

const MIGRATIONS_DIR: &str = "migrations";

pub fn run_migrations(conn: &Connection) -> rusqlite::Result<()> {
    let migrations_path = Path::new(MIGRATIONS_DIR);
    if !migrations_path.exists() {
        return Ok(());
    }

    let mut migrations: Vec<(u32, String)> = Vec::new();
    for entry in fs::read_dir(migrations_path)? {
        let entry = entry?;
        let path = entry.path();
        if path.extension().and_then(|s| s.to_str()) == Some("sql") {
            if let Some(name) = path.file_stem().and_then(|s| s.to_str()) {
                if let Some(version) = name.split('_').next().and_then(|s| s.parse::<u32>().ok()) {
                    let content = fs::read_to_string(&path)?;
                    migrations.push((version, content));
                }
            }
        }
    }

    migrations.sort_by_key(|(v, _)| *v);

    conn.execute_batch(
        "CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at TEXT NOT NULL DEFAULT (datetime('now'))
        )",
    )?;

    for (version, sql) in migrations {
        let exists: bool = conn.query_row(
            "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version = ?)",
            [version],
            |row| row.get(0),
        )?;

        if !exists {
            let tx = conn.unchecked_transaction()?;
            for statement in sql.split(';') {
                let trimmed = statement.trim();
                if !trimmed.is_empty() {
                    tx.execute(trimmed, [])?;
                }
            }
            tx.execute(
                "INSERT INTO schema_migrations (version) VALUES (?)",
                [version],
            )?;
            tx.commit()?;
        }
    }

    Ok(())
}