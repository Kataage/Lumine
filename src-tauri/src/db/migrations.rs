use rusqlite::Connection;

const INITIAL_SCHEMA: &str = include_str!("../../migrations/001_initial_schema.sql");
const LIBRARY_SETTINGS_SCHEMA: &str = include_str!("../../migrations/002_library_settings.sql");
const COLOR_LABELS_SCHEMA: &str = include_str!("../../migrations/003_color_labels_and_filters.sql");
const MIGRATION_VERSION: u32 = 3;

pub fn run_migrations(conn: &Connection) -> rusqlite::Result<()> {
    conn.execute_batch(
        "CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at TEXT NOT NULL DEFAULT (datetime('now'))
        )",
    )?;

    for (version, sql) in [
        (1, INITIAL_SCHEMA),
        (2, LIBRARY_SETTINGS_SCHEMA),
        (3, COLOR_LABELS_SCHEMA),
    ] {
        let applied: bool = conn.query_row(
            "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version = ?)",
            [version],
            |row| row.get(0),
        )?;

        if !applied {
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