package commands

import (
	"crypto/rand"
	"encoding/hex"
	"errors"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/kataage/lumine/internal/promptprofile"
)

var (
	ErrBuiltInModelProfileReadOnly = errors.New("built-in model profile is read-only")
	ErrModelProfileNotFound        = errors.New("model profile not found")
)

type ModelProfileDTO struct {
	ID                   string   `json:"id"`
	Name                 string   `json:"name"`
	Family               string   `json:"family"`
	CheckpointName       string   `json:"checkpointName"`
	PromptStyle          string   `json:"promptStyle"`
	QualityTags          []string `json:"qualityTags"`
	NegativePromptPolicy string   `json:"negativePromptPolicy"`
	TagOrder             []string `json:"tagOrder"`
	TriggerWords         []string `json:"triggerWords"`
	LoRATriggerSyntax    string   `json:"loraTriggerSyntax"`
	WeightSyntax         string   `json:"weightSyntax"`
	SystemGuidance       string   `json:"systemGuidance"`
	Notes                string   `json:"notes"`
	BuiltIn              bool     `json:"builtIn"`
	CreatedAt            string   `json:"createdAt,omitempty"`
	UpdatedAt            string   `json:"updatedAt,omitempty"`
}

type ModelProfileInput struct {
	Name                 string   `json:"name"`
	Family               string   `json:"family"`
	CheckpointName       string   `json:"checkpointName"`
	PromptStyle          string   `json:"promptStyle"`
	QualityTags          []string `json:"qualityTags"`
	NegativePromptPolicy string   `json:"negativePromptPolicy"`
	TagOrder             []string `json:"tagOrder"`
	TriggerWords         []string `json:"triggerWords"`
	LoRATriggerSyntax    string   `json:"loraTriggerSyntax"`
	WeightSyntax         string   `json:"weightSyntax"`
	SystemGuidance       string   `json:"systemGuidance"`
	Notes                string   `json:"notes"`
}

func modelProfileDTO(profile domain.ModelProfile) ModelProfileDTO {
	dto := ModelProfileDTO{
		ID:                   profile.ID,
		Name:                 profile.Name,
		Family:               profile.Family,
		CheckpointName:       profile.CheckpointName,
		PromptStyle:          profile.PromptStyle,
		QualityTags:          append([]string{}, profile.QualityTags...),
		NegativePromptPolicy: profile.NegativePromptPolicy,
		TagOrder:             append([]string{}, profile.TagOrder...),
		TriggerWords:         append([]string{}, profile.TriggerWords...),
		LoRATriggerSyntax:    profile.LoRATriggerSyntax,
		WeightSyntax:         profile.WeightSyntax,
		SystemGuidance:       profile.SystemGuidance,
		Notes:                profile.Notes,
		BuiltIn:              profile.BuiltIn,
	}
	if !profile.CreatedAt.IsZero() {
		dto.CreatedAt = profile.CreatedAt.UTC().Format("2006-01-02T15:04:05Z")
	}
	if !profile.UpdatedAt.IsZero() {
		dto.UpdatedAt = profile.UpdatedAt.UTC().Format("2006-01-02T15:04:05Z")
	}
	if dto.QualityTags == nil {
		dto.QualityTags = []string{}
	}
	if dto.TagOrder == nil {
		dto.TagOrder = []string{}
	}
	if dto.TriggerWords == nil {
		dto.TriggerWords = []string{}
	}
	return dto
}

func cleanProfileList(values []string) []string {
	result := make([]string, 0, len(values))
	seen := make(map[string]struct{}, len(values))
	for _, value := range values {
		value = strings.TrimSpace(value)
		if value == "" {
			continue
		}
		key := strings.ToLower(value)
		if _, exists := seen[key]; exists {
			continue
		}
		seen[key] = struct{}{}
		result = append(result, value)
	}
	return result
}

func profileFromInput(id string, input ModelProfileInput) domain.ModelProfile {
	loraSyntax := strings.TrimSpace(input.LoRATriggerSyntax)
	if loraSyntax == "" {
		loraSyntax = "<lora:{name}:{weight}>"
	}
	weightSyntax := strings.TrimSpace(input.WeightSyntax)
	if weightSyntax == "" {
		weightSyntax = "({text}:{weight})"
	}
	return domain.ModelProfile{
		ID:                   id,
		Name:                 strings.TrimSpace(input.Name),
		Family:               strings.TrimSpace(input.Family),
		CheckpointName:       strings.TrimSpace(input.CheckpointName),
		PromptStyle:          strings.TrimSpace(input.PromptStyle),
		QualityTags:          cleanProfileList(input.QualityTags),
		NegativePromptPolicy: strings.TrimSpace(input.NegativePromptPolicy),
		TagOrder:             cleanProfileList(input.TagOrder),
		TriggerWords:         cleanProfileList(input.TriggerWords),
		LoRATriggerSyntax:    loraSyntax,
		WeightSyntax:         weightSyntax,
		SystemGuidance:       strings.TrimSpace(input.SystemGuidance),
		Notes:                strings.TrimSpace(input.Notes),
	}
}

func customProfileID() (string, error) {
	var bytes [12]byte
	if _, err := rand.Read(bytes[:]); err != nil {
		return "", fmt.Errorf("generate model profile id: %w", err)
	}
	return "custom-" + hex.EncodeToString(bytes[:]), nil
}

func (c *AppCommands) ListModelProfiles() ([]ModelProfileDTO, error) {
	result := make([]ModelProfileDTO, 0)
	for _, profile := range promptprofile.BuiltIns() {
		result = append(result, modelProfileDTO(profile))
	}
	custom, err := db.NewModelProfileRepo(c.db).List()
	if err != nil {
		return nil, fmt.Errorf("list custom model profiles: %w", err)
	}
	for _, profile := range custom {
		result = append(result, modelProfileDTO(profile))
	}
	return result, nil
}

func (c *AppCommands) GetModelProfile(id string) (*ModelProfileDTO, error) {
	profile, err := c.resolveModelProfile(id)
	if err != nil {
		return nil, err
	}
	dto := modelProfileDTO(profile)
	return &dto, nil
}

func (c *AppCommands) CreateModelProfile(input ModelProfileInput) (*ModelProfileDTO, error) {
	id, err := customProfileID()
	if err != nil {
		return nil, err
	}
	profile := profileFromInput(id, input)
	created, err := db.NewModelProfileRepo(c.db).Create(&profile)
	if err != nil {
		return nil, err
	}
	dto := modelProfileDTO(*created)
	return &dto, nil
}

func (c *AppCommands) UpdateModelProfile(id string, input ModelProfileInput) (*ModelProfileDTO, error) {
	if _, builtIn := promptprofile.BuiltIn(id); builtIn {
		return nil, ErrBuiltInModelProfileReadOnly
	}
	current, err := db.NewModelProfileRepo(c.db).Get(id)
	if err != nil {
		return nil, err
	}
	if current == nil {
		return nil, fmt.Errorf("%w: %s", ErrModelProfileNotFound, id)
	}
	profile := profileFromInput(id, input)
	updated, err := db.NewModelProfileRepo(c.db).Update(&profile)
	if err != nil {
		return nil, err
	}
	dto := modelProfileDTO(*updated)
	return &dto, nil
}

func (c *AppCommands) DuplicateModelProfile(id, newName string) (*ModelProfileDTO, error) {
	source, err := c.resolveModelProfile(id)
	if err != nil {
		return nil, err
	}
	newID, err := customProfileID()
	if err != nil {
		return nil, err
	}
	source.ID = newID
	source.BuiltIn = false
	source.CreatedAt = source.CreatedAt.Add(0)
	source.UpdatedAt = source.UpdatedAt.Add(0)
	source.Name = strings.TrimSpace(newName)
	if source.Name == "" {
		source.Name = modelProfileDTO(source).Name + " Copy"
	}
	created, err := db.NewModelProfileRepo(c.db).Create(&source)
	if err != nil {
		return nil, err
	}
	dto := modelProfileDTO(*created)
	return &dto, nil
}

func (c *AppCommands) DeleteModelProfile(id string) error {
	if _, builtIn := promptprofile.BuiltIn(id); builtIn {
		return ErrBuiltInModelProfileReadOnly
	}
	if err := db.NewModelProfileRepo(c.db).Delete(id); err != nil {
		return err
	}
	return nil
}

func (c *AppCommands) resolveModelProfile(id string) (domain.ModelProfile, error) {
	id = strings.TrimSpace(id)
	if profile, ok := promptprofile.BuiltIn(id); ok {
		return profile, nil
	}
	profile, err := db.NewModelProfileRepo(c.db).Get(id)
	if err != nil {
		return domain.ModelProfile{}, err
	}
	if profile == nil {
		return domain.ModelProfile{}, fmt.Errorf("%w: %s", ErrModelProfileNotFound, id)
	}
	return *profile, nil
}

func modelProfilePromptSpec(profile domain.ModelProfile) map[string]any {
	return map[string]any{
		"id":                   profile.ID,
		"name":                 profile.Name,
		"family":               profile.Family,
		"checkpointName":       profile.CheckpointName,
		"promptStyle":          profile.PromptStyle,
		"qualityTags":          append([]string{}, profile.QualityTags...),
		"negativePromptPolicy": profile.NegativePromptPolicy,
		"tagOrder":             append([]string{}, profile.TagOrder...),
		"triggerWords":         append([]string{}, profile.TriggerWords...),
		"loraTriggerSyntax":    profile.LoRATriggerSyntax,
		"weightSyntax":         profile.WeightSyntax,
		"systemGuidance":       profile.SystemGuidance,
		"notes":                profile.Notes,
	}
}
