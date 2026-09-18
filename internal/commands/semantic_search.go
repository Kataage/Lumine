package commands

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

var (
	ErrSemanticSearchDisabled = errors.New("Semantic Search is disabled")
	ErrSemanticModelNotReady  = errors.New("Semantic Search model is not loaded")
)

func (c *AppCommands) HandleScannedAssetChanges(assetIDs []int64) (int, error) {
	if len(assetIDs) == 0 || c.aiJobQueue == nil {
		return 0, nil
	}
	if _, err := c.aiJobQueue.MarkStaleForAssets(domain.AICapabilitySemanticSearch, assetIDs); err != nil {
		return 0, err
	}
	return c.EnqueueAutomaticSemanticAssets(assetIDs)
}

func (c *AppCommands) EnqueueAutomaticSemanticAssets(assetIDs []int64) (int, error) {
	if len(assetIDs) == 0 || c.aiJobQueue == nil || c.aiManager == nil {
		return 0, nil
	}
	settings, err := c.GetAISettings()
	if err != nil {
		return 0, err
	}
	if !settings.CapabilityEnabled(domain.AICapabilitySemanticSearch) ||
		!settings.CapabilityEnabled(domain.AICapabilityAutoAnalyze) {
		return 0, nil
	}
	status := c.aiManager.Status(domain.AICapabilitySemanticSearch)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return 0, nil
	}
	return c.aiJobQueue.EnqueueMany(
		assetIDs,
		domain.AICapabilitySemanticSearch,
		-50,
		true,
	)
}

func (c *AppCommands) EnqueueSemanticBackfill() (int, error) {
	if c.aiJobQueue == nil || c.aiManager == nil {
		return 0, nil
	}
	if err := c.requireSemanticSearchEnabled(); err != nil {
		return 0, err
	}

	status := c.aiManager.Status(domain.AICapabilitySemanticSearch)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return 0, ErrSemanticModelNotReady
	}
	if status.Engine == "" || status.ModelID == "" || status.Version == "" {
		return 0, errors.New("Semantic Search runtime provenance is incomplete")
	}

	libraries, err := c.libraryRepo.List()
	if err != nil {
		return 0, fmt.Errorf("list libraries for semantic backfill: %w", err)
	}

	total := 0
	for _, library := range libraries {
		if !library.IsEnabled {
			continue
		}
		var afterID int64
		for {
			ids, err := c.semanticRepo.ListNeedingEmbedding(
				library.ID,
				status.Engine,
				status.ModelID,
				status.Version,
				afterID,
				1000,
			)
			if err != nil {
				return total, err
			}
			if len(ids) == 0 {
				break
			}
			created, err := c.aiJobQueue.EnqueueMany(
				ids,
				domain.AICapabilitySemanticSearch,
				-100,
				false,
			)
			if err != nil {
				return total, err
			}
			total += created
			afterID = ids[len(ids)-1]
			if len(ids) < 1000 {
				break
			}
		}
	}
	return total, nil
}

func (c *AppCommands) SemanticAnalysisHandler(ctx context.Context, job domain.AIJob) (ai.AnalysisOutput, error) {
	if c.aiManager == nil {
		return ai.AnalysisOutput{}, errors.New("AI model manager is not available")
	}
	asset, err := c.assetRepo.GetByID(job.AssetID)
	if err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("get semantic asset %d: %w", job.AssetID, err)
	}
	if asset == nil {
		return ai.AnalysisOutput{}, fmt.Errorf("semantic asset not found: %d", job.AssetID)
	}

	status := c.aiManager.Status(domain.AICapabilitySemanticSearch)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return ai.AnalysisOutput{}, fmt.Errorf("%w: %s", ErrSemanticModelNotReady, status.State)
	}
	if status.Engine == "" || status.ModelID == "" || status.Version == "" {
		return ai.AnalysisOutput{}, errors.New("Semantic Search runtime provenance is incomplete")
	}

	response, err := c.aiManager.Infer(ctx, domain.AICapabilitySemanticSearch, ai.InferenceRequest{
		Operation: "embed_image",
		Payload: map[string]any{
			"filePath": asset.FilePath,
		},
	})
	if err != nil {
		return ai.AnalysisOutput{}, fmt.Errorf("embed image %d: %w", asset.ID, err)
	}
	vector, err := semanticVectorFromResponse(response)
	if err != nil {
		return ai.AnalysisOutput{}, err
	}
	if err := c.semanticRepo.Upsert(
		asset.ID,
		status.Engine,
		status.ModelID,
		status.Version,
		vector,
	); err != nil {
		return ai.AnalysisOutput{}, err
	}

	summary, _ := json.Marshal(map[string]any{
		"dimensions": len(vector),
		"kind":       "image_embedding",
	})
	return ai.AnalysisOutput{
		Engine:       status.Engine,
		ModelID:      status.ModelID,
		ModelVersion: status.Version,
		ResultJSON:   string(summary),
	}, nil
}

func (c *AppCommands) SemanticSearchAssets(req AssetListRequest) (*AssetListResponse, error) {
	queryText := strings.TrimSpace(req.Search)
	if queryText == "" {
		return &AssetListResponse{Assets: []AssetDTO{}, TotalCount: 0}, nil
	}
	if err := c.requireSemanticSearchEnabled(); err != nil {
		return nil, err
	}
	if c.aiManager == nil {
		return nil, ErrSemanticModelNotReady
	}

	status := c.aiManager.Status(domain.AICapabilitySemanticSearch)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		return nil, fmt.Errorf("%w: %s", ErrSemanticModelNotReady, status.State)
	}

	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	response, err := c.aiManager.Infer(ctx, domain.AICapabilitySemanticSearch, ai.InferenceRequest{
		Operation: "embed_text",
		Payload: map[string]any{
			"text": queryText,
		},
	})
	if err != nil {
		return nil, fmt.Errorf("embed semantic query: %w", err)
	}
	vector, err := semanticVectorFromResponse(response)
	if err != nil {
		return nil, err
	}

	result, err := c.semanticRepo.Search(vector, semanticQueryFromAssetRequest(req, status, 0))
	if err != nil {
		return nil, err
	}
	return c.semanticResultToAssets(result)
}

func (c *AppCommands) ListSimilarAssets(assetID int64, req AssetListRequest) (*AssetListResponse, error) {
	if err := c.requireSemanticSearchEnabled(); err != nil {
		return nil, err
	}
	source, err := c.semanticRepo.GetReady(assetID)
	if err != nil {
		return nil, err
	}
	if source == nil {
		return nil, fmt.Errorf("%w for asset %d", db.ErrSemanticEmbeddingNotFound, assetID)
	}

	status := ai.RuntimeStatus{
		Engine:  source.Engine,
		ModelID: source.ModelID,
		Version: source.ModelVersion,
	}
	result, err := c.semanticRepo.Search(source.Vector, semanticQueryFromAssetRequest(req, status, assetID))
	if err != nil {
		return nil, err
	}
	return c.semanticResultToAssets(result)
}

func (c *AppCommands) semanticResultToAssets(result *db.SemanticSearchResult) (*AssetListResponse, error) {
	if result == nil || len(result.Hits) == 0 {
		total := 0
		if result != nil {
			total = result.TotalCount
		}
		return &AssetListResponse{Assets: []AssetDTO{}, TotalCount: total}, nil
	}

	ids := make([]int64, len(result.Hits))
	scoreByID := make(map[int64]float32, len(result.Hits))
	for i, hit := range result.Hits {
		ids[i] = hit.AssetID
		scoreByID[hit.AssetID] = hit.Score
	}
	assets, err := c.assetRepo.GetByIDs(ids)
	if err != nil {
		return nil, err
	}
	dtos := make([]AssetDTO, 0, len(assets))
	for i := range assets {
		dto := toAssetDTO(&assets[i])
		dto.SemanticScore = scoreByID[dto.ID]
		dtos = append(dtos, dto)
	}
	return &AssetListResponse{Assets: dtos, TotalCount: result.TotalCount}, nil
}

func semanticQueryFromAssetRequest(
	req AssetListRequest,
	status ai.RuntimeStatus,
	excludeID int64,
) db.SemanticSearchQuery {
	return db.SemanticSearchQuery{
		LibraryID:    req.LibraryID,
		FolderPath:   req.FolderPath,
		Recurse:      req.Recurse,
		Rating:       req.Rating,
		StatusLabel:  req.StatusLabel,
		IsFavorite:   req.IsFavorite,
		TagIDs:       req.TagIDs,
		HasNote:      req.HasNote,
		Extension:    req.Extension,
		ColorLabel:   req.ColorLabel,
		Engine:       status.Engine,
		ModelID:      status.ModelID,
		ModelVersion: status.Version,
		ExcludeID:    excludeID,
		Offset:       req.Offset,
		Limit:        req.Limit,
	}
}

func (c *AppCommands) requireSemanticSearchEnabled() error {
	settings, err := c.GetAISettings()
	if err != nil {
		return err
	}
	if !settings.CapabilityEnabled(domain.AICapabilitySemanticSearch) {
		return ErrSemanticSearchDisabled
	}
	return nil
}

func semanticVectorFromResponse(response ai.InferenceResponse) ([]float32, error) {
	raw, ok := response.Payload["embedding"]
	if !ok {
		return nil, errors.New("AI response does not contain an embedding")
	}

	switch value := raw.(type) {
	case []float32:
		if len(value) == 0 {
			return nil, errors.New("AI embedding is empty")
		}
		return value, nil
	case []float64:
		result := make([]float32, len(value))
		for i, item := range value {
			result[i] = float32(item)
		}
		return result, nil
	case []any:
		result := make([]float32, len(value))
		for i, item := range value {
			number, ok := item.(float64)
			if !ok {
				return nil, fmt.Errorf("AI embedding element %d is not numeric", i)
			}
			result[i] = float32(number)
		}
		if len(result) == 0 {
			return nil, errors.New("AI embedding is empty")
		}
		return result, nil
	default:
		return nil, fmt.Errorf("AI embedding has unsupported type %T", raw)
	}
}
