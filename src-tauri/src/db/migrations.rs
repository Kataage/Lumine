use rusqlite::Connection;

const INITIAL_SCHEMA: &str = include_str!("../../migrations/001_initial_schema.sql");
const MIGRATION_VERSION: u32 = 1;

pub fn run_migrations(conn: &Connection) -> rusqlite::Result<()> {
    conn.execute_batch(
        "CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at TEXT NOT NULL DEFAULT (datetime('now'))
        )",
    )?;

    let applied: bool = conn.query_row(
        "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version = ?)",
        [MIGRATION_VERSION],
        |row| row.get(0),
    )?;

    if !applied {
        let tx = conn.unchecked_transaction()?;
        for statement in INITIAL_SCHEMA.split(';') {
            let trimmed = statement.trim();
            if !trimmed.is_empty() {
                tx.execute(trimmed, [])?;
            }
        }
        tx.execute(
            "INSERT INTO schema_migrations (version) VALUES (?)",
            [MIGRATION_VERSION],
        )?;
        tx.commit()?;
    }

    Ok(())
}