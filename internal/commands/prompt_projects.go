package commands

import (
	"encoding/json"
	"errors"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

var (
	ErrPromptProjectNotFound = errors.New("prompt project not found")
	ErrPromptVariantNotFound = errors.New("prompt variant not found")
	ErrPromptVersionNotFound = errors.New("prompt version not found")
)

type PromptLoRADTO struct {
	Name         string   `json:"name"`
	Weight       float64  `json:"weight"`
	TriggerWords []string `json:"triggerWords"`
}

type PromptVersionDTO struct {
	ID                  int64  `json:"id"`
	VariantID           int64  `json:"variantId"`
	ParentVersionID     int64  `json:"parentVersionId,omitempty"`
	Positive            string `json:"positive"`
	Negative            string `json:"negative"`
	Source              string `json:"source"`
	ChangeInstruction   string `json:"changeInstruction"`
	ProfileID           string `json:"profileId"`
	ProfileSnapshotJSON string `json:"profileSnapshotJson"`
	AIEngine            string `json:"aiEngine"`
	AIModelID           string `json:"aiModelId"`
	AIModelVersion      string `json:"aiModelVersion"`
	MetadataJSON        string `json:"metadataJson"`
	CreatedAt           string `json:"createdAt"`
}

type PromptVariantDTO struct {
	ID        int64              `json:"id"`
	ProjectID int64              `json:"projectId"`
	Name      string             `json:"name"`
	Versions  []PromptVersionDTO `json:"versions"`
	CreatedAt string             `json:"createdAt"`
}

type PromptProjectDTO struct {
	ID                int64              `json:"id"`
	Title             string             `json:"title"`
	Idea              string             `json:"idea"`
	Notes             string             `json:"notes"`
	TargetProfileID   string             `json:"targetProfileId"`
	Characters        []string           `json:"characters"`
	LoRAs             []PromptLoRADTO    `json:"loras"`
	ReferenceAssetIDs []int64            `json:"referenceAssetIds"`
	RelatedAssetIDs   []int64            `json:"relatedAssetIds"`
	Variants          []PromptVariantDTO `json:"variants,omitempty"`
	Deleted           bool               `json:"deleted"`
	CreatedAt         string             `json:"createdAt"`
	UpdatedAt         string             `json:"updatedAt"`
}

type PromptProjectInput struct {
	Title             string          `json:"title"`
	Idea              string          `json:"idea"`
	Notes             string          `json:"notes"`
	TargetProfileID   string          `json:"targetProfileId"`
	Characters        []string        `json:"characters"`
	LoRAs             []PromptLoRADTO `json:"loras"`
	ReferenceAssetIDs []int64         `json:"referenceAssetIds"`
	RelatedAssetIDs   []int64         `json:"relatedAssetIds"`
}

type PromptVersionInput struct {
	VariantID        int64  `json:"variantId"`
	ParentVersionID  int64  `json:"parentVersionId,omitempty"`
	Positive         string `json:"positive"`
	Negative         string `json:"negative"`
	Source           string `json:"source"`
	ChangeInstruction string `json:"changeInstruction"`
	ProfileID        string `json:"profileId"`
	AIEngine         string `json:"aiEngine"`
	AIModelID        string `json:"aiModelId"`
	AIModelVersion   string `json:"aiModelVersion"`
	MetadataJSON     string `json:"metadataJson"`
}

func cleanPromptProjectCharacters(values []string) []string {
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

func promptLoRAsFromDTO(values []PromptLoRADTO) []domain.PromptLoRA {
	result := make([]domain.PromptLoRA, 0, len(values))
	for _, value := range values {
		name := strings.TrimSpace(value.Name)
		if name == "" {
			continue
		}
		result = append(result, domain.PromptLoRA{
			Name: name,
			Weight: value.Weight,
			TriggerWords: cleanPromptProjectCharacters(value.TriggerWords),
		})
	}
	return result
}

func promptLoRADTOs(values []domain.PromptLoRA) []PromptLoRADTO {
	result := make([]PromptLoRADTO, 0, len(values))
	for _, value := range values {
		result = append(result, PromptLoRADTO{
			Name: value.Name,
			Weight: value.Weight,
			TriggerWords: append([]string{}, value.TriggerWords...),
		})
	}
	return result
}

func promptVersionDTO(version domain.PromptVersion) PromptVersionDTO {
	dto := PromptVersionDTO{
		ID: version.ID,
		VariantID: version.VariantID,
		Positive: version.Positive,
		Negative: version.Negative,
		Source: version.Source,
		ChangeInstruction: version.ChangeInstruction,
		ProfileID: version.ProfileID,
		ProfileSnapshotJSON: version.ProfileSnapshotJSON,
		AIEngine: version.AIEngine,
		AIModelID: version.AIModelID,
		AIModelVersion: version.AIModelVersion,
		MetadataJSON: version.MetadataJSON,
		CreatedAt: version.CreatedAt.UTC().Format("2006-01-02T15:04:05Z"),
	}
	if version.ParentVersionID != nil {
		dto.ParentVersionID = *version.ParentVersionID
	}
	return dto
}

func (c *AppCommands) promptProjectDTO(repo *db.PromptProjectRepo, project domain.PromptProject, includeHistory bool) (PromptProjectDTO, error) {
	referenceIDs, err := repo.GetProjectAssetIDs(project.ID, "reference")
	if err != nil {
		return PromptProjectDTO{}, err
	}
	relatedIDs, err := repo.GetProjectAssetIDs(project.ID, "generated")
	if err != nil {
		return PromptProjectDTO{}, err
	}
	dto := PromptProjectDTO{
		ID: project.ID,
		Title: project.Title,
		Idea: project.Idea,
		Notes: project.Notes,
		TargetProfileID: project.TargetProfileID,
		Characters: append([]string{}, project.Characters...),
		LoRAs: promptLoRADTOs(project.LoRAs),
		ReferenceAssetIDs: referenceIDs,
		RelatedAssetIDs: relatedIDs,
		Deleted: project.DeletedAt != nil,
		CreatedAt: project.CreatedAt.UTC().Format("2006-01-02T15:04:05Z"),
		UpdatedAt: project.UpdatedAt.UTC().Format("2006-01-02T15:04:05Z"),
	}
	if dto.Characters == nil {
		dto.Characters = []string{}
	}
	if dto.LoRAs == nil {
		dto.LoRAs = []PromptLoRADTO{}
	}
	if dto.ReferenceAssetIDs == nil {
		dto.ReferenceAssetIDs = []int64{}
	}
	if dto.RelatedAssetIDs == nil {
		dto.RelatedAssetIDs = []int64{}
	}
	if !includeHistory {
		return dto, nil
	}
	variants, err := repo.ListVariants(project.ID)
	if err != nil {
		return PromptProjectDTO{}, err
	}
	dto.Variants = make([]PromptVariantDTO, 0, len(variants))
	for _, variant := range variants {
		versions, err := repo.ListVersions(variant.ID)
		if err != nil {
			return PromptProjectDTO{}, err
		}
		variantDTO := PromptVariantDTO{
			ID: variant.ID,
			ProjectID: variant.ProjectID,
			Name: variant.Name,
			Versions: make([]PromptVersionDTO, 0, len(versions)),
			CreatedAt: variant.CreatedAt.UTC().Format("2006-01-02T15:04:05Z"),
		}
		for _, version := range versions {
			variantDTO.Versions = append(variantDTO.Versions, promptVersionDTO(version))
		}
		dto.Variants = append(dto.Variants, variantDTO)
	}
	return dto, nil
}

func validatePromptProjectInput(input PromptProjectInput) error {
	if strings.TrimSpace(input.Title) == "" {
		return errors.New("prompt project title is required")
	}
	if len([]rune(input.Title)) > 300 || len([]rune(input.Idea)) > 24000 || len([]rune(input.Notes)) > 24000 {
		return errors.New("prompt project text is too long")
	}
	if len(input.Characters) > 128 || len(input.LoRAs) > 128 ||
		len(input.ReferenceAssetIDs) > 512 || len(input.RelatedAssetIDs) > 512 {
		return errors.New("prompt project contains too many items")
	}
	return nil
}

func (c *AppCommands) CreatePromptProject(input PromptProjectInput) (*PromptProjectDTO, error) {
	if err := validatePromptProjectInput(input); err != nil {
		return nil, err
	}
	if profileID := strings.TrimSpace(input.TargetProfileID); profileID != "" {
		if _, err := c.resolveModelProfile(profileID); err != nil {
			return nil, fmt.Errorf("resolve target model profile: %w", err)
		}
	}
	repo := db.NewPromptProjectRepo(c.db)
	project, err := repo.Create(&domain.PromptProject{
		Title: strings.TrimSpace(input.Title),
		Idea: strings.TrimSpace(input.Idea),
		Notes: strings.TrimSpace(input.Notes),
		TargetProfileID: strings.TrimSpace(input.TargetProfileID),
		Characters: cleanPromptProjectCharacters(input.Characters),
		LoRAs: promptLoRAsFromDTO(input.LoRAs),
	})
	if err != nil {
		return nil, err
	}
	if err := repo.SetProjectAssets(project.ID, "reference", input.ReferenceAssetIDs); err != nil {
		return nil, err
	}
	if err := repo.SetProjectAssets(project.ID, "generated", input.RelatedAssetIDs); err != nil {
		return nil, err
	}
	if _, err := repo.CreateVariant(project.ID, "Main"); err != nil {
		return nil, err
	}
	project, err = repo.GetProject(project.ID, true)
	if err != nil || project == nil {
		return nil, fmt.Errorf("reload prompt project: %w", err)
	}
	dto, err := c.promptProjectDTO(repo, *project, true)
	if err != nil {
		return nil, err
	}
	return &dto, nil
}

func (c *AppCommands) UpdatePromptProject(id int64, input PromptProjectInput) (*PromptProjectDTO, error) {
	if err := validatePromptProjectInput(input); err != nil {
		return nil, err
	}
	repo := db.NewPromptProjectRepo(c.db)
	current, err := repo.GetProject(id, true)
	if err != nil {
		return nil, err
	}
	if current == nil {
		return nil, fmt.Errorf("%w: %d", ErrPromptProjectNotFound, id)
	}
	if profileID := strings.TrimSpace(input.TargetProfileID); profileID != "" {
		if _, err := c.resolveModelProfile(profileID); err != nil {
			return nil, fmt.Errorf("resolve target model profile: %w", err)
		}
	}
	current.Title = strings.TrimSpace(input.Title)
	current.Idea = strings.TrimSpace(input.Idea)
	current.Notes = strings.TrimSpace(input.Notes)
	current.TargetProfileID = strings.TrimSpace(input.TargetProfileID)
	current.Characters = cleanPromptProjectCharacters(input.Characters)
	current.LoRAs = promptLoRAsFromDTO(input.LoRAs)
	if _, err := repo.Update(current); err != nil {
		return nil, err
	}
	if err := repo.SetProjectAssets(id, "reference", input.ReferenceAssetIDs); err != nil {
		return nil, err
	}
	if err := repo.SetProjectAssets(id, "generated", input.RelatedAssetIDs); err != nil {
		return nil, err
	}
	updated, err := repo.GetProject(id, true)
	if err != nil || updated == nil {
		return nil, fmt.Errorf("reload prompt project: %w", err)
	}
	dto, err := c.promptProjectDTO(repo, *updated, true)
	if err != nil {
		return nil, err
	}
	return &dto, nil
}

func (c *AppCommands) ListPromptProjects(includeDeleted bool, limit int) ([]PromptProjectDTO, error) {
	repo := db.NewPromptProjectRepo(c.db)
	projects, err := repo.ListProjects(includeDeleted, limit)
	if err != nil {
		return nil, err
	}
	result := make([]PromptProjectDTO, 0, len(projects))
	for _, project := range projects {
		dto, err := c.promptProjectDTO(repo, project, false)
		if err != nil {
			return nil, err
		}
		result = append(result, dto)
	}
	return result, nil
}

func (c *AppCommands) GetPromptProject(id int64, includeDeleted bool) (*PromptProjectDTO, error) {
	repo := db.NewPromptProjectRepo(c.db)
	project, err := repo.GetProject(id, includeDeleted)
	if err != nil {
		return nil, err
	}
	if project == nil {
		return nil, fmt.Errorf("%w: %d", ErrPromptProjectNotFound, id)
	}
	dto, err := c.promptProjectDTO(repo, *project, true)
	if err != nil {
		return nil, err
	}
	return &dto, nil
}

func (c *AppCommands) DeletePromptProject(id int64) error {
	return db.NewPromptProjectRepo(c.db).SetDeleted(id, true)
}

func (c *AppCommands) RestorePromptProject(id int64) error {
	return db.NewPromptProjectRepo(c.db).SetDeleted(id, false)
}

func (c *AppCommands) CreatePromptVariant(projectID int64, name string, fromVersionID int64) (*PromptVariantDTO, error) {
	repo := db.NewPromptProjectRepo(c.db)
	project, err := repo.GetProject(projectID, false)
	if err != nil {
		return nil, err
	}
	if project == nil {
		return nil, fmt.Errorf("%w: %d", ErrPromptProjectNotFound, projectID)
	}
	variant, err := repo.CreateVariant(projectID, name)
	if err != nil {
		return nil, err
	}
	dto := PromptVariantDTO{
		ID: variant.ID, ProjectID: variant.ProjectID, Name: variant.Name,
		Versions: []PromptVersionDTO{}, CreatedAt: variant.CreatedAt.UTC().Format("2006-01-02T15:04:05Z"),
	}
	if fromVersionID > 0 {
		parent, err := repo.GetVersion(fromVersionID)
		if err != nil {
			return nil, err
		}
		if parent == nil {
			return nil, fmt.Errorf("%w: %d", ErrPromptVersionNotFound, fromVersionID)
		}
		parentVariant, err := repo.GetVariant(parent.VariantID)
		if err != nil || parentVariant == nil || parentVariant.ProjectID != projectID {
			return nil, errors.New("branch source version belongs to another prompt project")
		}
		version, err := repo.CreateVersion(&domain.PromptVersion{
			VariantID: variant.ID,
			ParentVersionID: &parent.ID,
			Positive: parent.Positive,
			Negative: parent.Negative,
			Source: "derived",
			ChangeInstruction: "Branched from version",
			ProfileID: parent.ProfileID,
			ProfileSnapshotJSON: parent.ProfileSnapshotJSON,
			AIEngine: parent.AIEngine,
			AIModelID: parent.AIModelID,
			AIModelVersion: parent.AIModelVersion,
			MetadataJSON: parent.MetadataJSON,
		})
		if err != nil {
			return nil, err
		}
		dto.Versions = append(dto.Versions, promptVersionDTO(*version))
	}
	return &dto, nil
}

func validPromptVersionSource(source string) bool {
	switch source {
	case "manual", "llm", "vlm", "tagger", "derived", "metadata":
		return true
	default:
		return false
	}
}

func normalizeJSONObject(raw string) (string, error) {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return "{}", nil
	}
	var value any
	if err := json.Unmarshal([]byte(raw), &value); err != nil {
		return "", fmt.Errorf("invalid JSON: %w", err)
	}
	object, ok := value.(map[string]any)
	if !ok {
		return "", errors.New("metadata JSON must be an object")
	}
	encoded, err := json.Marshal(object)
	if err != nil {
		return "", err
	}
	return string(encoded), nil
}

func (c *AppCommands) CreatePromptVersion(input PromptVersionInput) (*PromptVersionDTO, error) {
	repo := db.NewPromptProjectRepo(c.db)
	variant, err := repo.GetVariant(input.VariantID)
	if err != nil {
		return nil, err
	}
	if variant == nil {
		return nil, fmt.Errorf("%w: %d", ErrPromptVariantNotFound, input.VariantID)
	}
	source := strings.ToLower(strings.TrimSpace(input.Source))
	if !validPromptVersionSource(source) {
		return nil, fmt.Errorf("unsupported prompt version source %q", input.Source)
	}
	metadataJSON, err := normalizeJSONObject(input.MetadataJSON)
	if err != nil {
		return nil, fmt.Errorf("normalize prompt version metadata: %w", err)
	}
	profileID := strings.TrimSpace(input.ProfileID)
	profileSnapshotJSON := "{}"
	if profileID != "" {
		profile, err := c.resolveModelProfile(profileID)
		if err != nil {
			return nil, fmt.Errorf("resolve prompt version profile: %w", err)
		}
		encoded, err := json.Marshal(modelProfilePromptSpec(profile))
		if err != nil {
			return nil, err
		}
		profileSnapshotJSON = string(encoded)
	}
	version := &domain.PromptVersion{
		VariantID: input.VariantID,
		Positive: input.Positive,
		Negative: input.Negative,
		Source: source,
		ChangeInstruction: strings.TrimSpace(input.ChangeInstruction),
		ProfileID: profileID,
		ProfileSnapshotJSON: profileSnapshotJSON,
		AIEngine: strings.TrimSpace(input.AIEngine),
		AIModelID: strings.TrimSpace(input.AIModelID),
		AIModelVersion: strings.TrimSpace(input.AIModelVersion),
		MetadataJSON: metadataJSON,
	}
	if input.ParentVersionID > 0 {
		value := input.ParentVersionID
		version.ParentVersionID = &value
	}
	if len([]rune(version.Positive)) > 64000 || len([]rune(version.Negative)) > 32000 ||
		len([]rune(version.ChangeInstruction)) > 12000 || len([]rune(version.MetadataJSON)) > 256000 {
		return nil, errors.New("prompt version payload is too large")
	}
	created, err := repo.CreateVersion(version)
	if err != nil {
		return nil, err
	}
	dto := promptVersionDTO(*created)
	return &dto, nil
}
