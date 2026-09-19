package db

import (
	"database/sql"
	"encoding/json"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/promptprofile"
)

type ModelProfileRepo struct {
	db *DB
}

func NewModelProfileRepo(database *DB) *ModelProfileRepo {
	return &ModelProfileRepo{db: database}
}

func encodeProfileList(values []string) string {
	if values == nil {
		values = []string{}
	}
	data, _ := json.Marshal(values)
	return string(data)
}

func decodeProfileList(raw string) ([]string, error) {
	if strings.TrimSpace(raw) == "" {
		return []string{}, nil
	}
	var values []string
	if err := json.Unmarshal([]byte(raw), &values); err != nil {
		return nil, err
	}
	if values == nil {
		values = []string{}
	}
	return values, nil
}

func (r *ModelProfileRepo) Create(profile *domain.ModelProfile) (*domain.ModelProfile, error) {
	if profile == nil {
		return nil, fmt.Errorf("model profile is required")
	}
	profile.BuiltIn = false
	if err := promptprofile.Validate(*profile); err != nil {
		return nil, err
	}
	_, err := r.db.Exec(`
		INSERT INTO custom_model_profiles (
			id, name, family, checkpoint_name, prompt_style, quality_tags_json,
			negative_prompt_policy, tag_order_json, trigger_words_json,
			lora_trigger_syntax, weight_syntax, system_guidance, notes
		) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
	`,
		profile.ID, strings.TrimSpace(profile.Name), strings.TrimSpace(profile.Family),
		strings.TrimSpace(profile.CheckpointName), strings.TrimSpace(profile.PromptStyle),
		encodeProfileList(profile.QualityTags), strings.TrimSpace(profile.NegativePromptPolicy),
		encodeProfileList(profile.TagOrder), encodeProfileList(profile.TriggerWords),
		strings.TrimSpace(profile.LoRATriggerSyntax), strings.TrimSpace(profile.WeightSyntax),
		strings.TrimSpace(profile.SystemGuidance), strings.TrimSpace(profile.Notes),
	)
	if err != nil {
		return nil, fmt.Errorf("create model profile: %w", err)
	}
	return r.Get(profile.ID)
}

func (r *ModelProfileRepo) Update(profile *domain.ModelProfile) (*domain.ModelProfile, error) {
	if profile == nil {
		return nil, fmt.Errorf("model profile is required")
	}
	profile.BuiltIn = false
	if err := promptprofile.Validate(*profile); err != nil {
		return nil, err
	}
	result, err := r.db.Exec(`
		UPDATE custom_model_profiles SET
			name = ?, family = ?, checkpoint_name = ?, prompt_style = ?,
			quality_tags_json = ?, negative_prompt_policy = ?, tag_order_json = ?,
			trigger_words_json = ?, lora_trigger_syntax = ?, weight_syntax = ?,
			system_guidance = ?, notes = ?, updated_at = CURRENT_TIMESTAMP
		WHERE id = ?
	`,
		strings.TrimSpace(profile.Name), strings.TrimSpace(profile.Family),
		strings.TrimSpace(profile.CheckpointName), strings.TrimSpace(profile.PromptStyle),
		encodeProfileList(profile.QualityTags), strings.TrimSpace(profile.NegativePromptPolicy),
		encodeProfileList(profile.TagOrder), encodeProfileList(profile.TriggerWords),
		strings.TrimSpace(profile.LoRATriggerSyntax), strings.TrimSpace(profile.WeightSyntax),
		strings.TrimSpace(profile.SystemGuidance), strings.TrimSpace(profile.Notes), profile.ID,
	)
	if err != nil {
		return nil, fmt.Errorf("update model profile: %w", err)
	}
	affected, _ := result.RowsAffected()
	if affected == 0 {
		return nil, fmt.Errorf("model profile not found: %s", profile.ID)
	}
	return r.Get(profile.ID)
}

func scanCustomModelProfile(scanner interface{ Scan(...any) error }) (*domain.ModelProfile, error) {
	var profile domain.ModelProfile
	var qualityJSON, orderJSON, triggerJSON string
	if err := scanner.Scan(
		&profile.ID, &profile.Name, &profile.Family, &profile.CheckpointName,
		&profile.PromptStyle, &qualityJSON, &profile.NegativePromptPolicy,
		&orderJSON, &triggerJSON, &profile.LoRATriggerSyntax, &profile.WeightSyntax,
		&profile.SystemGuidance, &profile.Notes, &profile.CreatedAt, &profile.UpdatedAt,
	); err != nil {
		return nil, err
	}
	var err error
	if profile.QualityTags, err = decodeProfileList(qualityJSON); err != nil {
		return nil, fmt.Errorf("decode quality tags for %s: %w", profile.ID, err)
	}
	if profile.TagOrder, err = decodeProfileList(orderJSON); err != nil {
		return nil, fmt.Errorf("decode tag order for %s: %w", profile.ID, err)
	}
	if profile.TriggerWords, err = decodeProfileList(triggerJSON); err != nil {
		return nil, fmt.Errorf("decode trigger words for %s: %w", profile.ID, err)
	}
	profile.BuiltIn = false
	return &profile, nil
}

const modelProfileColumns = `
	id, name, family, checkpoint_name, prompt_style, quality_tags_json,
	negative_prompt_policy, tag_order_json, trigger_words_json,
	lora_trigger_syntax, weight_syntax, system_guidance, notes, created_at, updated_at
`

func (r *ModelProfileRepo) Get(id string) (*domain.ModelProfile, error) {
	profile, err := scanCustomModelProfile(r.db.QueryRow(
		"SELECT "+modelProfileColumns+" FROM custom_model_profiles WHERE id = ?", id,
	))
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get model profile %s: %w", id, err)
	}
	return profile, nil
}

func (r *ModelProfileRepo) List() ([]domain.ModelProfile, error) {
	rows, err := r.db.Query(
		"SELECT " + modelProfileColumns + " FROM custom_model_profiles ORDER BY name COLLATE NOCASE, id",
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := make([]domain.ModelProfile, 0)
	for rows.Next() {
		profile, err := scanCustomModelProfile(rows)
		if err != nil {
			return nil, err
		}
		result = append(result, *profile)
	}
	return result, rows.Err()
}

func (r *ModelProfileRepo) Delete(id string) error {
	result, err := r.db.Exec("DELETE FROM custom_model_profiles WHERE id = ?", id)
	if err != nil {
		return err
	}
	affected, _ := result.RowsAffected()
	if affected == 0 {
		return fmt.Errorf("model profile not found: %s", id)
	}
	return nil
}
