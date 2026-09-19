CREATE TABLE IF NOT EXISTS custom_model_profiles (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    family TEXT NOT NULL DEFAULT 'custom',
    checkpoint_name TEXT NOT NULL DEFAULT '',
    prompt_style TEXT NOT NULL DEFAULT '',
    quality_tags_json TEXT NOT NULL DEFAULT '[]',
    negative_prompt_policy TEXT NOT NULL DEFAULT '',
    tag_order_json TEXT NOT NULL DEFAULT '[]',
    trigger_words_json TEXT NOT NULL DEFAULT '[]',
    lora_trigger_syntax TEXT NOT NULL DEFAULT '<lora:{name}:{weight}>',
    weight_syntax TEXT NOT NULL DEFAULT '({text}:{weight})',
    system_guidance TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_custom_model_profiles_name
    ON custom_model_profiles(name COLLATE NOCASE);
