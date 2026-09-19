package commands

import (
	"encoding/json"
	"fmt"
	"path/filepath"
	"strings"
	"time"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/generationmeta"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

type GenerationLoRADTO struct {
	Name         string   `json:"name"`
	Weight       float64  `json:"weight"`
	TriggerWords []string `json:"triggerWords"`
}

type AssetGenerationMetadataDTO struct {
	AssetID            int64               `json:"assetId"`
	Present            bool                `json:"present"`
	SchemaVersion      int                 `json:"schemaVersion"`
	ParserVersion      int                 `json:"parserVersion"`
	SourceFormat       string              `json:"sourceFormat"`
	Positive           string              `json:"positive"`
	Negative           string              `json:"negative"`
	Checkpoint         string              `json:"checkpoint"`
	LoRAs              []GenerationLoRADTO `json:"loras"`
	Sampler            string              `json:"sampler"`
	Scheduler          string              `json:"scheduler"`
	CFG                float64             `json:"cfg"`
	Steps              int                 `json:"steps"`
	Seed               int64               `json:"seed"`
	Width              int                 `json:"width"`
	Height             int                 `json:"height"`
	SuggestedProfileID string              `json:"suggestedProfileId,omitempty"`
	RawPromptJSON      string              `json:"rawPromptJson,omitempty"`
	RawWorkflowJSON    string              `json:"rawWorkflowJson,omitempty"`
	Parameters         string              `json:"parameters,omitempty"`
	RawJSON            string              `json:"rawJson"`
	ParsedAt           string              `json:"parsedAt,omitempty"`
}

func generationLoRADTOs(values []domain.GenerationLoRA) []GenerationLoRADTO {
	result := make([]GenerationLoRADTO, 0, len(values))
	for _, value := range values {
		result = append(result, GenerationLoRADTO{
			Name:         value.Name,
			Weight:       value.Weight,
			TriggerWords: append([]string{}, value.TriggerWords...),
		})
	}
	return result
}

func generationPromptLoRAs(values []domain.GenerationLoRA) []PromptLoRADTO {
	result := make([]PromptLoRADTO, 0, len(values))
	for _, value := range values {
		result = append(result, PromptLoRADTO{
			Name:         value.Name,
			Weight:       value.Weight,
			TriggerWords: append([]string{}, value.TriggerWords...),
		})
	}
	return result
}

func generationMetadataPresent(value domain.GenerationMetadata, raw map[string]string) bool {
	return strings.TrimSpace(value.Positive) != "" ||
		strings.TrimSpace(value.Negative) != "" ||
		strings.TrimSpace(value.Checkpoint) != "" ||
		len(value.LoRAs) > 0 ||
		value.Seed != 0 || value.Steps != 0 || value.CFG != 0 ||
		value.Width != 0 || value.Height != 0 ||
		len(raw) > 0
}

func generationModifiedKey(value time.Time) string {
	if value.IsZero() {
		return ""
	}
	return value.UTC().Format(time.RFC3339Nano)
}

func (c *AppCommands) enrichLoRATriggerKnowledge(value *domain.GenerationMetadata) error {
	if value == nil {
		return nil
	}
	repo := db.NewGenerationMetadataRepo(c.db)
	for index := range value.LoRAs {
		lora := &value.LoRAs[index]
		if len(lora.TriggerWords) > 0 {
			if err := repo.MergeLoRATriggers(lora.Name, lora.TriggerWords); err != nil {
				return err
			}
		}
		known, err := repo.GetLoRATriggers(lora.Name)
		if err != nil {
			return err
		}
		lora.TriggerWords = cleanPromptProjectCharacters(append(lora.TriggerWords, known...))
	}
	return nil
}

func normalizeCheckpointKey(name string) string {
	name = strings.ToLower(strings.TrimSpace(filepath.Base(name)))
	for _, suffix := range []string{".safetensors", ".ckpt", ".gguf", ".pt", ".pth"} {
		name = strings.TrimSuffix(name, suffix)
	}
	return name
}

func (c *AppCommands) inferModelProfileFromCheckpoint(checkpoint string) string {
	key := normalizeCheckpointKey(checkpoint)
	if key == "" {
		return ""
	}
	if custom, err := db.NewModelProfileRepo(c.db).List(); err == nil {
		for _, profile := range custom {
			if normalizeCheckpointKey(profile.CheckpointName) == key {
				return profile.ID
			}
		}
	}
	switch {
	case strings.Contains(key, "illustrious"), strings.Contains(key, "ilxl"), strings.Contains(key, "illux"):
		return "illustrious-xl"
	case strings.Contains(key, "noob"):
		return "noobai-xl"
	case strings.Contains(key, "pony"):
		return "pony-xl"
	case strings.Contains(key, "flux"):
		return "flux"
	case strings.Contains(key, "sd1.5"), strings.Contains(key, "sd15"), strings.Contains(key, "v1-5"):
		return "sd15"
	case strings.Contains(key, "sdxl"), strings.Contains(key, "xl"):
		return "sdxl-generic"
	default:
		return ""
	}
}

func (c *AppCommands) generationMetadataDTO(
	assetID int64,
	value domain.GenerationMetadata,
	rawJSON string,
	parsedAt time.Time,
) AssetGenerationMetadataDTO {
	return AssetGenerationMetadataDTO{
		AssetID:            assetID,
		Present:            generationMetadataPresent(value, rawMapFromJSON(rawJSON)),
		SchemaVersion:      value.SchemaVersion,
		ParserVersion:      generationmeta.ParserVersion,
		SourceFormat:       value.SourceFormat,
		Positive:           value.Positive,
		Negative:           value.Negative,
		Checkpoint:         value.Checkpoint,
		LoRAs:              generationLoRADTOs(value.LoRAs),
		Sampler:            value.Sampler,
		Scheduler:          value.Scheduler,
		CFG:                value.CFG,
		Steps:              value.Steps,
		Seed:               value.Seed,
		Width:              value.Width,
		Height:             value.Height,
		SuggestedProfileID: c.inferModelProfileFromCheckpoint(value.Checkpoint),
		RawPromptJSON:      value.RawPromptJSON,
		RawWorkflowJSON:    value.RawWorkflowJSON,
		Parameters:         value.Parameters,
		RawJSON:            rawJSON,
		ParsedAt:           parsedAt.UTC().Format(time.RFC3339),
	}
}

func rawMapFromJSON(raw string) map[string]string {
	var value map[string]string
	if json.Unmarshal([]byte(raw), &value) != nil || value == nil {
		return map[string]string{}
	}
	return value
}

func (c *AppCommands) loadAssetGenerationMetadata(assetID int64, refresh bool) (*AssetGenerationMetadataDTO, error) {
	asset, err := c.assetRepo.GetByID(assetID)
	if err != nil {
		return nil, err
	}
	if asset == nil {
		return nil, fmt.Errorf("generation metadata asset not found: %d", assetID)
	}
	repo := db.NewGenerationMetadataRepo(c.db)
	modifiedKey := generationModifiedKey(asset.ModifiedAtFS)
	if !refresh {
		stored, err := repo.Get(assetID)
		if err != nil {
			return nil, err
		}
		if stored != nil &&
			stored.ParserVersion == generationmeta.ParserVersion &&
			stored.FileSize == asset.FileSize &&
			stored.ModifiedAtFS == modifiedKey {
			var normalized domain.GenerationMetadata
			if json.Unmarshal([]byte(stored.NormalizedJSON), &normalized) == nil {
				if normalized.LoRAs == nil {
					normalized.LoRAs = []domain.GenerationLoRA{}
				}
				if err := c.enrichLoRATriggerKnowledge(&normalized); err != nil {
					return nil, err
				}
				dto := c.generationMetadataDTO(assetID, normalized, stored.RawJSON, stored.ParsedAt)
				return &dto, nil
			}
		}
	}

	normalized, raw, err := generationmeta.Read(asset.FilePath)
	if err != nil {
		return nil, fmt.Errorf("read generation metadata: %w", err)
	}
	if normalized.LoRAs == nil {
		normalized.LoRAs = []domain.GenerationLoRA{}
	}
	if err := c.enrichLoRATriggerKnowledge(&normalized); err != nil {
		return nil, err
	}
	rawBytes, _ := json.Marshal(raw)
	normalizedBytes, err := json.Marshal(normalized)
	if err != nil {
		return nil, err
	}
	stored := domain.StoredGenerationMetadata{
		AssetID:        assetID,
		SchemaVersion:  1,
		ParserVersion:  generationmeta.ParserVersion,
		SourceFormat:   normalized.SourceFormat,
		FileSize:       asset.FileSize,
		ModifiedAtFS:   modifiedKey,
		RawJSON:        string(rawBytes),
		NormalizedJSON: string(normalizedBytes),
	}
	if err := repo.Upsert(stored); err != nil {
		return nil, err
	}
	persisted, err := repo.Get(assetID)
	if err != nil {
		return nil, err
	}
	parsedAt := time.Now()
	if persisted != nil {
		parsedAt = persisted.ParsedAt
	}
	dto := c.generationMetadataDTO(assetID, normalized, string(rawBytes), parsedAt)
	return &dto, nil
}

func (c *AppCommands) GetAssetGenerationMetadata(assetID int64, refresh bool) (*AssetGenerationMetadataDTO, error) {
	return c.loadAssetGenerationMetadata(assetID, refresh)
}

func (c *AppCommands) rememberPromptProjectLoRATriggers(loras []PromptLoRADTO) error {
	repo := db.NewGenerationMetadataRepo(c.db)
	for _, lora := range loras {
		if err := repo.MergeLoRATriggers(lora.Name, lora.TriggerWords); err != nil {
			return err
		}
	}
	return nil
}
