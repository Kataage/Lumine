-- Add library settings for excluded folders and supported extensions

CREATE TABLE IF NOT EXISTS library_settings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    library_id INTEGER NOT NULL REFERENCES libraries(id) ON DELETE CASCADE,
    setting_key TEXT NOT NULL,
    setting_value TEXT NOT NULL,
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE(library_id, setting_key)
);

CREATE INDEX IF NOT EXISTS idx_library_settings_library_key ON library_settings(library_id, setting_key);
