CREATE TABLE IF NOT EXISTS asset_generation_metadata (
    asset_id INTEGER PRIMARY KEY REFERENCES assets(id) ON DELETE CASCADE,
    schema_version INTEGER NOT NULL DEFAULT 1,
    parser_version INTEGER NOT NULL DEFAULT 1,
    source_format TEXT NOT NULL DEFAULT '',
    file_size INTEGER NOT NULL DEFAULT 0,
    modified_at_fs TEXT NOT NULL DEFAULT '',
    raw_json TEXT NOT NULL DEFAULT '{}',
    normalized_json TEXT NOT NULL DEFAULT '{}',
    parsed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
