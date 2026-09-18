package commands

import (
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

type CreativeAssetRefDTO struct {
	ID       int64  `json:"id"`
	FileName string `json:"fileName"`
	FilePath string `json:"filePath"`
}

type WorkDTO struct {
	ID           int64                 `json:"id"`
	Title        string                `json:"title"`
	Description  string                `json:"description"`
	CoverAssetID int64                 `json:"coverAssetId,omitempty"`
	AssetIDs     []int64               `json:"assetIds"`
	Assets       []CreativeAssetRefDTO `json:"assets"`
	CreatedAt    string                `json:"createdAt"`
	UpdatedAt    string                `json:"updatedAt"`
}

type GenerationGroupDTO struct {
	ID             int64                 `json:"id"`
	WorkID         int64                 `json:"workId,omitempty"`
	Name           string                `json:"name"`
	Prompt         string                `json:"prompt"`
	NegativePrompt string                `json:"negativePrompt"`
	ModelName      string                `json:"modelName"`
	Sampler        string                `json:"sampler"`
	Scheduler      string                `json:"scheduler"`
	Steps          int                   `json:"steps"`
	CFGScale       float64               `json:"cfgScale"`
	WorkflowJSON   string                `json:"workflowJson"`
	Notes          string                `json:"notes"`
	AssetIDs       []int64               `json:"assetIds"`
	Assets         []CreativeAssetRefDTO `json:"assets"`
	CreatedAt      string                `json:"createdAt"`
	UpdatedAt      string                `json:"updatedAt"`
}

type AssetRelationDTO struct {
	ID             int64  `json:"id"`
	ParentAssetID  int64  `json:"parentAssetId"`
	ParentFileName string `json:"parentFileName"`
	ParentFilePath string `json:"parentFilePath"`
	ChildAssetID   int64  `json:"childAssetId"`
	ChildFileName  string `json:"childFileName"`
	ChildFilePath  string `json:"childFilePath"`
	RelationType   string `json:"relationType"`
	Note           string `json:"note"`
	CreatedAt      string `json:"createdAt"`
}

type AssetCreativeContextDTO struct {
	Works     []WorkDTO          `json:"works"`
	Groups    []GenerationGroupDTO `json:"groups"`
	Relations []AssetRelationDTO `json:"relations"`
}

type CreateGenerationGroupRequest struct {
	AssetIDs       []int64 `json:"assetIds"`
	WorkID         int64   `json:"workId,omitempty"`
	Name           string  `json:"name"`
	Prompt         string  `json:"prompt"`
	NegativePrompt string  `json:"negativePrompt"`
	ModelName      string  `json:"modelName"`
	Sampler        string  `json:"sampler"`
	Scheduler      string  `json:"scheduler"`
	Steps          int     `json:"steps"`
	CFGScale       float64 `json:"cfgScale"`
	WorkflowJSON   string  `json:"workflowJson"`
	Notes          string  `json:"notes"`
}

func (c *AppCommands) creativeAssetRefs(ids []int64) ([]CreativeAssetRefDTO, error) {
	refs := make([]CreativeAssetRefDTO, 0, len(ids))
	for _, id := range ids {
		asset, err := c.assetRepo.GetByID(id)
		if err != nil {
			return nil, fmt.Errorf("load creative asset %d: %w", id, err)
		}
		if asset == nil {
			return nil, fmt.Errorf("creative asset not found: %d", id)
		}
		refs = append(refs, CreativeAssetRefDTO{ID: asset.ID, FileName: asset.FileName, FilePath: asset.FilePath})
	}
	return refs, nil
}

func (c *AppCommands) toWorkDTO(repo *db.CreativeRepo, work domain.Work) (WorkDTO, error) {
	ids, err := repo.GetWorkAssetIDs(work.ID)
	if err != nil {
		return WorkDTO{}, fmt.Errorf("get work %d assets: %w", work.ID, err)
	}
	refs, err := c.creativeAssetRefs(ids)
	if err != nil {
		return WorkDTO{}, err
	}
	dto := WorkDTO{
		ID:          work.ID,
		Title:       work.Title,
		Description: work.Description,
		AssetIDs:    ids,
		Assets:      refs,
		CreatedAt:   work.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt:   work.UpdatedAt.Format("2006-01-02T15:04:05Z"),
	}
	if work.CoverAssetID != nil {
		dto.CoverAssetID = *work.CoverAssetID
	}
	return dto, nil
}

func (c *AppCommands) toGenerationGroupDTO(repo *db.CreativeRepo, group domain.GenerationGroup) (GenerationGroupDTO, error) {
	ids, err := repo.GetGenerationGroupAssetIDs(group.ID)
	if err != nil {
		return GenerationGroupDTO{}, fmt.Errorf("get generation group %d assets: %w", group.ID, err)
	}
	refs, err := c.creativeAssetRefs(ids)
	if err != nil {
		return GenerationGroupDTO{}, err
	}
	dto := GenerationGroupDTO{
		ID:             group.ID,
		Name:           group.Name,
		Prompt:         group.Prompt,
		NegativePrompt: group.NegativePrompt,
		ModelName:      group.ModelName,
		Sampler:        group.Sampler,
		Scheduler:      group.Scheduler,
		Steps:          group.Steps,
		CFGScale:       group.CFGScale,
		WorkflowJSON:   group.WorkflowJSON,
		Notes:          group.Notes,
		AssetIDs:       ids,
		Assets:         refs,
		CreatedAt:      group.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt:      group.UpdatedAt.Format("2006-01-02T15:04:05Z"),
	}
	if group.WorkID != nil {
		dto.WorkID = *group.WorkID
	}
	return dto, nil
}

func (c *AppCommands) toAssetRelationDTO(relation domain.AssetRelation) (AssetRelationDTO, error) {
	dto := AssetRelationDTO{
		ID:            relation.ID,
		ParentAssetID: relation.ParentAssetID,
		ChildAssetID:  relation.ChildAssetID,
		RelationType:  relation.RelationType,
		Note:          relation.Note,
		CreatedAt:     relation.CreatedAt.Format("2006-01-02T15:04:05Z"),
	}
	parent, err := c.assetRepo.GetByID(relation.ParentAssetID)
	if err != nil {
		return AssetRelationDTO{}, fmt.Errorf("load relation parent asset %d: %w", relation.ParentAssetID, err)
	}
	if parent == nil {
		return AssetRelationDTO{}, fmt.Errorf("relation parent asset not found: %d", relation.ParentAssetID)
	}
	dto.ParentFileName = parent.FileName
	dto.ParentFilePath = parent.FilePath

	child, err := c.assetRepo.GetByID(relation.ChildAssetID)
	if err != nil {
		return AssetRelationDTO{}, fmt.Errorf("load relation child asset %d: %w", relation.ChildAssetID, err)
	}
	if child == nil {
		return AssetRelationDTO{}, fmt.Errorf("relation child asset not found: %d", relation.ChildAssetID)
	}
	dto.ChildFileName = child.FileName
	dto.ChildFilePath = child.FilePath
	return dto, nil
}

func (c *AppCommands) ListWorks(limit int) ([]WorkDTO, error) {
	repo := db.NewCreativeRepo(c.db)
	works, err := repo.ListWorks(limit)
	if err != nil {
		return nil, fmt.Errorf("list works: %w", err)
	}
	result := make([]WorkDTO, 0, len(works))
	for _, work := range works {
		dto, err := c.toWorkDTO(repo, work)
		if err != nil {
			return nil, err
		}
		result = append(result, dto)
	}
	return result, nil
}

func (c *AppCommands) CreateWork(title, description string, assetIDs []int64) (*WorkDTO, error) {
	repo := db.NewCreativeRepo(c.db)
	work, err := repo.CreateWork(title, description, assetIDs)
	if err != nil {
		return nil, fmt.Errorf("create work: %w", err)
	}
	dto, err := c.toWorkDTO(repo, *work)
	if err != nil {
		return nil, err
	}
	return &dto, nil
}

func (c *AppCommands) AddAssetsToWork(workID int64, assetIDs []int64) error {
	return db.NewCreativeRepo(c.db).AddAssetsToWork(workID, assetIDs)
}

func (c *AppCommands) ListGenerationGroups(limit int) ([]GenerationGroupDTO, error) {
	repo := db.NewCreativeRepo(c.db)
	groups, err := repo.ListGenerationGroups(limit)
	if err != nil {
		return nil, fmt.Errorf("list generation groups: %w", err)
	}
	result := make([]GenerationGroupDTO, 0, len(groups))
	for _, group := range groups {
		dto, err := c.toGenerationGroupDTO(repo, group)
		if err != nil {
			return nil, err
		}
		result = append(result, dto)
	}
	return result, nil
}

func (c *AppCommands) CreateGenerationGroup(req CreateGenerationGroupRequest) (*GenerationGroupDTO, error) {
	repo := db.NewCreativeRepo(c.db)
	group := &domain.GenerationGroup{
		Name:           strings.TrimSpace(req.Name),
		Prompt:         req.Prompt,
		NegativePrompt: req.NegativePrompt,
		ModelName:      req.ModelName,
		Sampler:        req.Sampler,
		Scheduler:      req.Scheduler,
		Steps:          req.Steps,
		CFGScale:       req.CFGScale,
		WorkflowJSON:   req.WorkflowJSON,
		Notes:          req.Notes,
	}
	if req.WorkID > 0 {
		value := req.WorkID
		group.WorkID = &value
	}
	created, err := repo.CreateGenerationGroup(group, req.AssetIDs)
	if err != nil {
		return nil, fmt.Errorf("create generation group: %w", err)
	}
	dto, err := c.toGenerationGroupDTO(repo, *created)
	if err != nil {
		return nil, err
	}
	return &dto, nil
}

func (c *AppCommands) AddAssetsToGenerationGroup(groupID int64, assetIDs []int64) error {
	return db.NewCreativeRepo(c.db).AddAssetsToGenerationGroup(groupID, assetIDs)
}

func (c *AppCommands) CreateAssetRelation(parentAssetID, childAssetID int64, relationType, note string) (*AssetRelationDTO, error) {
	relation, err := db.NewCreativeRepo(c.db).CreateRelation(parentAssetID, childAssetID, relationType, note)
	if err != nil {
		return nil, fmt.Errorf("create asset relation: %w", err)
	}
	dto, err := c.toAssetRelationDTO(*relation)
	if err != nil {
		return nil, err
	}
	return &dto, nil
}

func (c *AppCommands) DeleteAssetRelation(id int64) error {
	return db.NewCreativeRepo(c.db).DeleteRelation(id)
}

func (c *AppCommands) GetAssetCreativeContext(assetID int64) (*AssetCreativeContextDTO, error) {
	repo := db.NewCreativeRepo(c.db)
	works, err := repo.GetWorksByAsset(assetID)
	if err != nil {
		return nil, fmt.Errorf("get works for asset %d: %w", assetID, err)
	}
	groups, err := repo.GetGenerationGroupsByAsset(assetID)
	if err != nil {
		return nil, fmt.Errorf("get generation groups for asset %d: %w", assetID, err)
	}
	relations, err := repo.GetRelationsByAsset(assetID)
	if err != nil {
		return nil, fmt.Errorf("get relations for asset %d: %w", assetID, err)
	}

	result := &AssetCreativeContextDTO{
		Works:     make([]WorkDTO, 0, len(works)),
		Groups:    make([]GenerationGroupDTO, 0, len(groups)),
		Relations: make([]AssetRelationDTO, 0, len(relations)),
	}
	for _, work := range works {
		dto, err := c.toWorkDTO(repo, work)
		if err != nil {
			return nil, err
		}
		result.Works = append(result.Works, dto)
	}
	for _, group := range groups {
		dto, err := c.toGenerationGroupDTO(repo, group)
		if err != nil {
			return nil, err
		}
		result.Groups = append(result.Groups, dto)
	}
	for _, relation := range relations {
		dto, err := c.toAssetRelationDTO(relation)
		if err != nil {
			return nil, err
		}
		result.Relations = append(result.Relations, dto)
	}
	return result, nil
}
