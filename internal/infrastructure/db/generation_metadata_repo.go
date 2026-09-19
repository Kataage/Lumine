package db

import (
	"database/sql"
	"encoding/json"
	"fmt"
	"path/filepath"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

type GenerationMetadataRepo struct {
	db *DB
}

func NewGenerationMetadataRepo(database *DB) *GenerationMetadataRepo {
	return &GenerationMetadataRepo{db: database}
}

func (r *GenerationMetadataRepo) Upsert(value domain.StoredGenerationMetadata) error {
	_, err := r.db.Exec(`
		INSERT INTO asset_generation_metadata (
			asset_id, schema_version, parser_version, source_format, file_size,
			modified_at_fs, raw_json, normalized_json, parsed_at, updated_at
		) VALUES (?, ?, ?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
		ON CONFLICT(asset_id) DO UPDATE SET
			schema_version = excluded.schema_version,
			parser_version = excluded.parser_version,
			source_format = excluded.source_format,
			file_size = excluded.file_size,
			modified_at_fs = excluded.modified_at_fs,
			raw_json = excluded.raw_json,
			normalized_json = excluded.normalized_json,
			parsed_at = CURRENT_TIMESTAMP,
			updated_at = CURRENT_TIMESTAMP
	`, value.AssetID, value.SchemaVersion, value.ParserVersion, value.SourceFormat,
		value.FileSize, value.ModifiedAtFS, value.RawJSON, value.NormalizedJSON)
	if err != nil {
		return fmt.Errorf("upsert generation metadata: %w", err)
	}
	return nil
}

func (r *GenerationMetadataRepo) Get(assetID int64) (*domain.StoredGenerationMetadata, error) {
	var value domain.StoredGenerationMetadata
	err := r.db.QueryRow(`
		SELECT asset_id, schema_version, parser_version, source_format, file_size,
			modified_at_fs, raw_json, normalized_json, parsed_at, updated_at
		FROM asset_generation_metadata WHERE asset_id = ?
	`, assetID).Scan(
		&value.AssetID, &value.SchemaVersion, &value.ParserVersion, &value.SourceFormat,
		&value.FileSize, &value.ModifiedAtFS, &value.RawJSON, &value.NormalizedJSON,
		&value.ParsedAt, &value.UpdatedAt,
	)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get generation metadata: %w", err)
	}
	return &value, nil
}

func (r *GenerationMetadataRepo) Delete(assetID int64) error {
	_, err := r.db.Exec("DELETE FROM asset_generation_metadata WHERE asset_id = ?", assetID)
	return err
}

func normalizeLoRAKnowledgeName(name string) string {
	name = strings.TrimSpace(filepath.Base(name))
	ext := filepath.Ext(name)
	if ext != "" {
		name = strings.TrimSuffix(name, ext)
	}
	return name
}

func cleanLoRATriggerWords(values []string) []string {
	seen := make(map[string]struct{}, len(values))
	result := make([]string, 0, len(values))
	for _, value := range values {
		value = strings.TrimSpace(value)
		if value == "" {
			continue
		}
		key := strings.ToLower(value)
		if _, ok := seen[key]; ok {
			continue
		}
		seen[key] = struct{}{}
		result = append(result, value)
	}
	return result
}

func (r *GenerationMetadataRepo) MergeLoRATriggers(name string, triggers []string) error {
	name = normalizeLoRAKnowledgeName(name)
	triggers = cleanLoRATriggerWords(triggers)
	if name == "" || len(triggers) == 0 {
		return nil
	}
	existing, err := r.GetLoRATriggers(name)
	if err != nil {
		return err
	}
	merged := cleanLoRATriggerWords(append(existing, triggers...))
	encoded, _ := json.Marshal(merged)
	_, err = r.db.Exec(`
		INSERT INTO lora_trigger_knowledge (lora_name, trigger_words_json)
		VALUES (?, ?)
		ON CONFLICT(lora_name) DO UPDATE SET
			trigger_words_json = excluded.trigger_words_json,
			updated_at = CURRENT_TIMESTAMP
	`, name, string(encoded))
	if err != nil {
		return fmt.Errorf("merge LoRA trigger knowledge: %w", err)
	}
	return nil
}

func (r *GenerationMetadataRepo) GetLoRATriggers(name string) ([]string, error) {
	name = normalizeLoRAKnowledgeName(name)
	if name == "" {
		return []string{}, nil
	}
	var raw string
	err := r.db.QueryRow(
		"SELECT trigger_words_json FROM lora_trigger_knowledge WHERE lora_name = ? COLLATE NOCASE",
		name,
	).Scan(&raw)
	if err == sql.ErrNoRows {
		return []string{}, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get LoRA trigger knowledge: %w", err)
	}
	var values []string
	if err := json.Unmarshal([]byte(raw), &values); err != nil {
		return nil, fmt.Errorf("decode LoRA trigger knowledge: %w", err)
	}
	return cleanLoRATriggerWords(values), nil
}
