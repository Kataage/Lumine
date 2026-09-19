package db

import (
	"database/sql"
	"fmt"

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
