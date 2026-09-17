package commands

import (
	"log/slog"
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

func (c *AppCommands) creativeAssetRefs(ids []int64) []CreativeAssetRefDTO {
	refs := make([]CreativeAssetRefDTO, 0, len(ids))
	for _, id := range ids {
		asset, err := c.assetRepo.GetByID(id)
		if err != nil || asset == nil {
			continue
		}
		refs = append(refs, CreativeAssetRefDTO{ID: asset.ID, FileName: asset.FileName, FilePath: asset.FilePath})
	}
	return refs
}

func (c *AppCommands) toWorkDTO(repo *db.CreativeRepo, work domain.Work) WorkDTO {
	ids, _ := repo.GetWorkAssetIDs(work.ID)
	dto := WorkDTO{
		ID:          work.ID,
		Title:       work.Title,
		Description: work.Description,
		AssetIDs:    ids,
		Assets:      c.creativeAssetRefs(ids),
		CreatedAt:   work.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt:   work.UpdatedAt.Format("2006-01-02T15:04:05Z"),
	}
	if work.CoverAssetID != nil {
		dto.CoverAssetID = *work.CoverAssetID
	}
	return dto
}

func (c *AppCommands) toGenerationGroupDTO(repo *db.CreativeRepo, group domain.GenerationGroup) GenerationGroupDTO {
	ids, _ := repo.GetGenerationGroupAssetIDs(group.ID)
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
		Assets:         c.creativeAssetRefs(ids),
		CreatedAt:      group.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt:      group.UpdatedAt.Format("2006-01-02T15:04:05Z"),
	}
	if group.WorkID != nil {
		dto.WorkID = *group.WorkID
	}
	return dto
}

func (c *AppCommands) toAssetRelationDTO(relation domain.AssetRelation) AssetRelationDTO {
	dto := AssetRelationDTO{
		ID:            relation.ID,
		ParentAssetID: relation.ParentAssetID,
		ChildAssetID:  relation.ChildAssetID,
		RelationType:  relation.RelationType,
		Note:          relation.Note,
		CreatedAt:     relation.CreatedAt.Format("2006-01-02T15:04:05Z"),
	}
	if asset, _ := c.assetRepo.GetByID(relation.ParentAssetID); asset != nil {
		dto.ParentFileName = asset.FileName
		dto.ParentFilePath = asset.FilePath
	}
	if asset, _ := c.assetRepo.GetByID(relation.ChildAssetID); asset != nil {
		dto.ChildFileName = asset.FileName
		dto.ChildFilePath = asset.FilePath
	}
	return dto
}

func (c *AppCommands) ListWorks(limit int) []WorkDTO {
	repo := db.NewCreativeRepo(c.db)
	works, err := repo.ListWorks(limit)
	if err != nil {
		slog.Error("ListWorks", "error", err)
		return nil
	}
	result := make([]WorkDTO, 0, len(works))
	for _, work := range works {
		result = append(result, c.toWorkDTO(repo, work))
	}
	return result
}

func (c *AppCommands) CreateWork(title, description string, assetIDs []int64) *WorkDTO {
	repo := db.NewCreativeRepo(c.db)
	work, err := repo.CreateWork(title, description, assetIDs)
	if err != nil {
		slog.Error("CreateWork", "error", err)
		return nil
	}
	dto := c.toWorkDTO(repo, *work)
	return &dto
}

func (c *AppCommands) AddAssetsToWork(workID int64, assetIDs []int64) error {
	return db.NewCreativeRepo(c.db).AddAssetsToWork(workID, assetIDs)
}

func (c *AppCommands) ListGenerationGroups(limit int) []GenerationGroupDTO {
	repo := db.NewCreativeRepo(c.db)
	groups, err := repo.ListGenerationGroups(limit)
	if err != nil {
		slog.Error("ListGenerationGroups", "error", err)
		return nil
	}
	result := make([]GenerationGroupDTO, 0, len(groups))
	for _, group := range groups {
		result = append(result, c.toGenerationGroupDTO(repo, group))
	}
	return result
}

func (c *AppCommands) CreateGenerationGroup(req CreateGenerationGroupRequest) *GenerationGroupDTO {
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
		slog.Error("CreateGenerationGroup", "error", err)
		return nil
	}
	dto := c.toGenerationGroupDTO(repo, *created)
	return &dto
}

func (c *AppCommands) AddAssetsToGenerationGroup(groupID int64, assetIDs []int64) error {
	return db.NewCreativeRepo(c.db).AddAssetsToGenerationGroup(groupID, assetIDs)
}

func (c *AppCommands) CreateAssetRelation(parentAssetID, childAssetID int64, relationType, note string) *AssetRelationDTO {
	relation, err := db.NewCreativeRepo(c.db).CreateRelation(parentAssetID, childAssetID, relationType, note)
	if err != nil {
		slog.Error("CreateAssetRelation", "error", err)
		return nil
	}
	dto := c.toAssetRelationDTO(*relation)
	return &dto
}

func (c *AppCommands) DeleteAssetRelation(id int64) error {
	return db.NewCreativeRepo(c.db).DeleteRelation(id)
}

func (c *AppCommands) GetAssetCreativeContext(assetID int64) *AssetCreativeContextDTO {
	repo := db.NewCreativeRepo(c.db)
	works, err := repo.GetWorksByAsset(assetID)
	if err != nil {
		slog.Error("GetAssetCreativeContext works", "assetID", assetID, "error", err)
		return nil
	}
	groups, err := repo.GetGenerationGroupsByAsset(assetID)
	if err != nil {
		slog.Error("GetAssetCreativeContext groups", "assetID", assetID, "error", err)
		return nil
	}
	relations, err := repo.GetRelationsByAsset(assetID)
	if err != nil {
		slog.Error("GetAssetCreativeContext relations", "assetID", assetID, "error", err)
		return nil
	}

	result := &AssetCreativeContextDTO{
		Works:     make([]WorkDTO, 0, len(works)),
		Groups:    make([]GenerationGroupDTO, 0, len(groups)),
		Relations: make([]AssetRelationDTO, 0, len(relations)),
	}
	for _, work := range works {
		result.Works = append(result.Works, c.toWorkDTO(repo, work))
	}
	for _, group := range groups {
		result.Groups = append(result.Groups, c.toGenerationGroupDTO(repo, group))
	}
	for _, relation := range relations {
		result.Relations = append(result.Relations, c.toAssetRelationDTO(relation))
	}
	return result
}
