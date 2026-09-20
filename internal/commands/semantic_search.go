package commands

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"sync"
	"time"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/siglip2"
	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

var (
	ErrSemanticSearchDisabled = errors.New("Semantic Search is disabled")
	ErrSemanticModelNotReady  = errors.New("Semantic Search model is not loaded")
)

const (
	semanticSearchSessionTTL = 15 * time.Minute
	semanticSearchSessionMax = 8
	semanticSearchCancelTTL  = 5 * time.Minute
)

type semanticSearchSession struct {
	hits          []domain.SemanticSearchHit
	total         int
	coverageReady  int
	coverageTotal  int
	coverageStates db.SemanticCoverageStateCounts
	createdAt      time.Time
}

type semanticSearchState struct {
	mu        sync.Mutex
	seq       uint64
	cancels   map[string]context.CancelFunc
	cancelled map[string]time.Time
	sessions  map[string]semanticSearchSession
}

type SemanticSearchProgressDTO struct {
	RequestID    string `json:"requestId"`
	Stage        string `json:"stage"`
	ScannedCount int    `json:"scannedCount"`
	TotalCount   int    `json:"totalCount"`
	ElapsedMs    int64  `json:"elapsedMs"`
}

func newSemanticSearchState() *semanticSearchState {
	return &semanticSearchState{
		cancels:   make(map[string]context.CancelFunc),
		cancelled: make(map[string]time.Time),
		sessions:  make(map[string]semanticSearchSession),
	}
}

func (s *semanticSearchState) begin(requestID string, parent context.Context) (context.Context, context.CancelFunc) {
	ctx, cancel := context.WithCancel(parent)
	if requestID == "" {
		return ctx, cancel
	}

	s.mu.Lock()
	s.pruneCancelledLocked(time.Now())
	if _, cancelled := s.cancelled[requestID]; cancelled {
		delete(s.cancelled, requestID)
		s.mu.Unlock()
		cancel()
		return ctx, cancel
	}
	if previous := s.cancels[requestID]; previous != nil {
		previous()
	}
	s.cancels[requestID] = cancel
	s.mu.Unlock()
	return ctx, cancel
}

func (s *semanticSearchState) finish(requestID string) {
	if requestID == "" {
		return
	}
	s.mu.Lock()
	delete(s.cancels, requestID)
	s.mu.Unlock()
}

func (s *semanticSearchState) cancel(requestID string) {
	if requestID == "" {
		return
	}
	now := time.Now()
	s.mu.Lock()
	s.pruneCancelledLocked(now)
	cancel := s.cancels[requestID]
	delete(s.cancels, requestID)
	if cancel == nil {
		// Cancellation can arrive before the Wails call reaches begin(). Keep a
		// short-lived tombstone so that stale requests are cancelled on arrival.
		s.cancelled[requestID] = now
	}
	s.mu.Unlock()
	if cancel != nil {
		cancel()
	}
}

func (s *semanticSearchState) pruneCancelledLocked(now time.Time) {
	for id, cancelledAt := range s.cancelled {
		if now.Sub(cancelledAt) > semanticSearchCancelTTL {
			delete(s.cancelled, id)
		}
	}
}
func (s *semanticSearchState) cancelAll() {
	s.mu.Lock()
	cancels := make([]context.CancelFunc, 0, len(s.cancels))
	for _, cancel := range s.cancels {
		if cancel != nil {
			cancels = append(cancels, cancel)
		}
	}
	s.cancels = make(map[string]context.CancelFunc)
	s.cancelled = make(map[string]time.Time)
	s.sessions = make(map[string]semanticSearchSession)
	s.mu.Unlock()

	for _, cancel := range cancels {
		cancel()
	}
}


func (s *semanticSearchState) store(
	hits []domain.SemanticSearchHit,
	total int,
	coverageReady int,
	coverageTotal int,
	coverageStates db.SemanticCoverageStateCounts,
) string {
	s.mu.Lock()
	defer s.mu.Unlock()

	now := time.Now()
	for id, session := range s.sessions {
		if now.Sub(session.createdAt) > semanticSearchSessionTTL {
			delete(s.sessions, id)
		}
	}
	for len(s.sessions) >= semanticSearchSessionMax {
		var oldestID string
		var oldest time.Time
		for id, session := range s.sessions {
			if oldestID == "" || session.createdAt.Before(oldest) {
				oldestID = id
				oldest = session.createdAt
			}
		}
		delete(s.sessions, oldestID)
	}

	s.seq++
	id := fmt.Sprintf("semantic-%x", s.seq)
	s.sessions[id] = semanticSearchSession{
		hits:          append([]domain.SemanticSearchHit(nil), hits...),
		total:         total,
		coverageReady:  coverageReady,
		coverageTotal:  coverageTotal,
		coverageStates: coverageStates,
		createdAt:      now,
	}
	return id
}

func (s *semanticSearchState) get(id string) (semanticSearchSession, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	session, ok := s.sessions[id]
	if !ok {
		return semanticSearchSession{}, false
	}
	if time.Since(session.createdAt) > semanticSearchSessionTTL {
		delete(s.sessions, id)
		return semanticSearchSession{}, false
	}
	return session, true
}

func (c *AppCommands) HandleScannedAssetChanges(assetIDs []int64) (int, error) {
	if len(assetIDs) == 0 || c.aiJobQueue == nil {
		return 0, nil
	}

	for _, capability := range []domain.AICapability{
		domain.AICapabilitySemanticSearch,
		domain.AICapabilityTagger,
		domain.AICapabilityLightweightVision,
	} {
		if _, err := c.aiJobQueue.MarkStaleForAssets(capability, assetIDs); err != nil {
			return 0, err
		}
	}

	semanticCreated, err := c.EnqueueAutomaticSemanticAssets(assetIDs)
	if err != nil {
		return semanticCreated, err
	}
	taggerCreated, err := c.EnqueueAutomaticTaggerAssets(assetIDs)
	if err != nil {
		return semanticCreated + taggerCreated, err
	}
	visionCreated, err := c.EnqueueAutomaticLightweightVisionAssets(assetIDs)
	if err != nil {
		return semanticCreated + taggerCreated + visionCreated, err
	}
	return semanticCreated + taggerCreated + visionCreated, nil
}

func (c *AppCommands) rememberSemanticPriorityAssets(assetIDs []int64) {
	if len(assetIDs) == 0 {
		return
	}
	c.semanticPriorityMu.Lock()
	defer c.semanticPriorityMu.Unlock()
	if c.semanticPrioritySeen == nil {
		c.semanticPrioritySeen = make(map[int64]struct{})
	}
	for _, id := range assetIDs {
		if id <= 0 {
			continue
		}
		if _, exists := c.semanticPrioritySeen[id]; exists {
			continue
		}
		c.semanticPrioritySeen[id] = struct{}{}
		c.semanticPriorityPending = append(c.semanticPriorityPending, id)
		if len(c.semanticPriorityPending) >= 1000 {
			break
		}
	}
}

func (c *AppCommands) takeSemanticPriorityAssets() []int64 {
	c.semanticPriorityMu.Lock()
	defer c.semanticPriorityMu.Unlock()
	if len(c.semanticPriorityPending) == 0 {
		return nil
	}
	ids := append([]int64(nil), c.semanticPriorityPending...)
	c.semanticPriorityPending = c.semanticPriorityPending[:0]
	clear(c.semanticPrioritySeen)
	return ids
}

func mergeSemanticPriorityIDs(primary, pending []int64) []int64 {
	if len(primary) == 0 && len(pending) == 0 {
		return nil
	}
	seen := make(map[int64]struct{}, len(primary)+len(pending))
	result := make([]int64, 0, len(primary)+len(pending))
	for _, group := range [][]int64{primary, pending} {
		for _, id := range group {
			if id <= 0 {
				continue
			}
			if _, ok := seen[id]; ok {
				continue
			}
			seen[id] = struct{}{}
			result = append(result, id)
			if len(result) >= 1000 {
				return result
			}
		}
	}
	return result
}

func (c *AppCommands) EnqueueAutomaticSemanticAssets(assetIDs []int64) (int, error) {
	if c.aiJobQueue == nil || c.aiManager == nil {
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
		c.rememberSemanticPriorityAssets(assetIDs)
		return 0, nil
	}
	analysisVersion := siglip2.AnalysisVersion(status.Version)
	assetIDs = mergeSemanticPriorityIDs(assetIDs, c.takeSemanticPriorityAssets())
	if len(assetIDs) == 0 {
		return 0, nil
	}
	needed, err := c.semanticRepo.FilterNeedingEmbeddingIDs(
		context.Background(),
		assetIDs,
		status.Engine,
		status.ModelID,
		analysisVersion,
	)
	if err != nil {
		return 0, err
	}
	if len(needed) == 0 {
		return 0, nil
	}
	return c.aiJobQueue.EnqueueMany(
		needed,
		domain.AICapabilitySemanticSearch,
		250,
		true,
	)
}

func (c *AppCommands) EnqueueSemanticBackfill() (int, error) {
	ctx := c.ctx
	if ctx == nil {
		ctx = context.Background()
	}
	return c.enqueueSemanticBackfillContext(ctx)
}

func (c *AppCommands) waitForSemanticBackgroundWindow(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	for c.aiJobQueue != nil && c.aiJobQueue.InteractiveUIActive() {
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-time.After(250 * time.Millisecond):
		}
	}
	return ctx.Err()
}

func (c *AppCommands) enqueueSemanticBackfillContext(ctx context.Context) (int, error) {
	if c.aiJobQueue == nil || c.aiManager == nil {
		return 0, nil
	}
	if ctx == nil {
		ctx = context.Background()
	}
	if err := ctx.Err(); err != nil {
		return 0, err
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
	analysisVersion := siglip2.AnalysisVersion(status.Version)

	libraries, err := c.libraryRepo.List()
	if err != nil {
		return 0, fmt.Errorf("list libraries for semantic backfill: %w", err)
	}

	settings, err := c.GetAISettings()
	if err != nil {
		return 0, err
	}
	if !settings.CapabilityEnabled(domain.AICapabilityAutoAnalyze) {
		return 0, nil
	}

	total := 0
	for _, library := range libraries {
		if err := ctx.Err(); err != nil {
			return total, err
		}
		if !library.IsEnabled {
			continue
		}
		var beforeModifiedAt string
		var beforeID int64
		for {
			if err := ctx.Err(); err != nil {
				return total, err
			}
			if err := c.waitForSemanticBackgroundWindow(ctx); err != nil {
				return total, err
			}
			candidates, err := c.semanticRepo.ListNeedingEmbeddingNewestContext(
				ctx,
				library.ID,
				status.Engine,
				status.ModelID,
				analysisVersion,
				beforeModifiedAt,
				beforeID,
				1000,
			)
			if err != nil {
				return total, err
			}
			if len(candidates) == 0 {
				break
			}
			ids := make([]int64, len(candidates))
			for i, candidate := range candidates {
				ids[i] = candidate.AssetID
			}
			if err := c.waitForSemanticBackgroundWindow(ctx); err != nil {
				return total, err
			}
			created, err := c.aiJobQueue.EnqueueMany(
				ids,
				domain.AICapabilitySemanticSearch,
				-100,
				true,
			)
			if err != nil {
				return total, err
			}
			total += created
			last := candidates[len(candidates)-1]
			beforeModifiedAt = last.ModifiedAtFS
			beforeID = last.AssetID
			if len(candidates) < 1000 {
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
	analysisVersion := siglip2.AnalysisVersion(status.Version)

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
		analysisVersion,
		vector,
	); err != nil {
		return ai.AnalysisOutput{}, err
	}
	if c.semanticIndex != nil {
		c.semanticIndex.Upsert(asset.ID, status.Engine, status.ModelID, analysisVersion, vector)
		c.scheduleSemanticIndexPersist(status.Engine, status.ModelID, analysisVersion)
	}

	if c.ctx != nil {
		runtime.EventsEmit(c.ctx, "semantic:embedding-updated", map[string]any{
			"assetId":      asset.ID,
			"modelVersion": analysisVersion,
		})
	}

	summary, _ := json.Marshal(map[string]any{
		"dimensions": len(vector),
		"kind":       "image_embedding",
	})
	return ai.AnalysisOutput{
		Engine:       status.Engine,
		ModelID:      status.ModelID,
		ModelVersion: analysisVersion,
		ResultJSON:   string(summary),
	}, nil
}

func (c *AppCommands) SemanticSearchAssets(req AssetListRequest) (*AssetListResponse, error) {
	return c.SemanticSearchAssetsWithID(req, "")
}

func (c *AppCommands) SemanticSearchAssetsWithID(req AssetListRequest, requestID string) (*AssetListResponse, error) {
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

	parent := c.ctx
	if parent == nil {
		parent = context.Background()
	}
	searchCtx, cancel := c.semanticSearchState.begin(requestID, parent)
	defer func() {
		cancel()
		c.semanticSearchState.finish(requestID)
	}()
	if err := searchCtx.Err(); err != nil {
		return nil, err
	}

	status := c.aiManager.Status(domain.AICapabilitySemanticSearch)
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		if err := c.ensureSemanticSearchReadyContext(searchCtx); err != nil {
			return nil, fmt.Errorf("%w: %v", ErrSemanticModelNotReady, err)
		}
		status = c.aiManager.Status(domain.AICapabilitySemanticSearch)
	}
	if status.State != ai.RuntimeStateReady && status.State != ai.RuntimeStateRunning {
		if status.Error != "" {
			return nil, fmt.Errorf("%w: %s: %s", ErrSemanticModelNotReady, status.State, status.Error)
		}
		return nil, fmt.Errorf("%w: %s", ErrSemanticModelNotReady, status.State)
	}

	searchStarted := time.Now()
	emitProgress := func(stage string, scanned, total int) {
		if requestID == "" || c.ctx == nil || searchCtx.Err() != nil {
			return
		}
		runtime.EventsEmit(c.ctx, "semantic-search:progress", SemanticSearchProgressDTO{
			RequestID:    requestID,
			Stage:        stage,
			ScannedCount: scanned,
			TotalCount:   total,
			ElapsedMs:    time.Since(searchStarted).Milliseconds(),
		})
	}

	resumeBackground := func() {}
	if c.aiJobQueue != nil {
		resumeBackground = c.aiJobQueue.PauseCapabilityForForeground(domain.AICapabilitySemanticSearch)
	}
	defer resumeBackground()

	emitProgress("embedding_query", 0, 0)
	response, err := c.aiManager.Infer(searchCtx, domain.AICapabilitySemanticSearch, ai.InferenceRequest{
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

	searchStatus := status
	searchStatus.Version = siglip2.AnalysisVersion(status.Version)
	searchQuery := semanticQueryFromAssetRequest(req, searchStatus, 0)
	coverageTotal, _ := c.semanticScopeTotal(req)
	coverageReady := 0
	coverageStates, _ := c.semanticRepo.CountSemanticAnalysisStates(searchCtx, searchQuery)
	var result *db.SemanticSearchResult
	if c.semanticIndex != nil {
		if !c.semanticIndex.IsReady(searchStatus.Engine, searchStatus.ModelID, searchStatus.Version) {
			emitProgress("warming_index", 0, 0)
			if err := c.semanticIndex.Warm(searchCtx, c.semanticRepo, searchStatus.Engine, searchStatus.ModelID, searchStatus.Version); err != nil {
				return nil, fmt.Errorf("prepare semantic memory index: %w", err)
			}
			c.scheduleSemanticIndexPersist(searchStatus.Engine, searchStatus.ModelID, searchStatus.Version)
		}
		emitProgress("filtering", 0, 0)
		eligibleIDs, err := c.semanticRepo.ListEligibleSemanticAssetIDs(searchCtx, searchQuery)
		if err != nil {
			return nil, err
		}
		coverageReady = len(eligibleIDs)
		emitProgress("scoring", 0, len(eligibleIDs))
		result, err = c.semanticIndex.Search(
			searchCtx,
			vector,
			eligibleIDs,
			searchQuery.Offset,
			searchQuery.Limit,
			func(scanned, total int) {
				emitProgress("scoring", scanned, total)
			},
		)
		if err != nil {
			return nil, err
		}
	} else {
		result, err = c.semanticRepo.SearchWithProgress(searchCtx, vector, searchQuery, 0, nil)
		if err != nil {
			return nil, err
		}
		coverageReady = result.TotalCount
	}

	emitProgress("formatting", len(result.Hits), result.TotalCount)
	sessionID := c.semanticSearchState.store(
		result.RankedHits,
		result.TotalCount,
		coverageReady,
		coverageTotal,
		coverageStates,
	)
	return c.semanticHitsToAssets(
		result.Hits,
		result.TotalCount,
		sessionID,
		coverageReady,
		coverageTotal,
		coverageStates,
	)
}

func (c *AppCommands) SemanticSearchPage(sessionID string, offset, limit int) (*AssetListResponse, error) {
	session, ok := c.semanticSearchState.get(strings.TrimSpace(sessionID))
	if !ok {
		return nil, errors.New("semantic search session expired")
	}
	if offset < 0 {
		offset = 0
	}
	if limit <= 0 {
		limit = 100
	}
	if limit > 500 {
		limit = 500
	}
	if offset > len(session.hits) {
		offset = len(session.hits)
	}
	end := offset + limit
	if end > len(session.hits) {
		end = len(session.hits)
	}
	return c.semanticHitsToAssets(
		session.hits[offset:end],
		session.total,
		sessionID,
		session.coverageReady,
		session.coverageTotal,
		session.coverageStates,
	)
}

func (c *AppCommands) CancelSemanticSearch(requestID string) {
	c.semanticSearchState.cancel(strings.TrimSpace(requestID))
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
	searchQuery := semanticQueryFromAssetRequest(req, status, assetID)
	var result *db.SemanticSearchResult
	if c.semanticIndex != nil {
		ctx := c.ctx
		if ctx == nil {
			ctx = context.Background()
		}
		if !c.semanticIndex.IsReady(status.Engine, status.ModelID, status.Version) {
			if err := c.semanticIndex.Warm(ctx, c.semanticRepo, status.Engine, status.ModelID, status.Version); err != nil {
				return nil, fmt.Errorf("prepare semantic memory index: %w", err)
			}
			c.scheduleSemanticIndexPersist(status.Engine, status.ModelID, status.Version)
		}
		eligibleIDs, err := c.semanticRepo.ListEligibleSemanticAssetIDs(ctx, searchQuery)
		if err != nil {
			return nil, err
		}
		result, err = c.semanticIndex.Search(ctx, source.Vector, eligibleIDs, searchQuery.Offset, searchQuery.Limit, nil)
		if err != nil {
			return nil, err
		}
	} else {
		var err error
		result, err = c.semanticRepo.Search(source.Vector, searchQuery)
		if err != nil {
			return nil, err
		}
	}
	return c.semanticResultToAssets(result)
}

func (c *AppCommands) semanticResultToAssets(result *db.SemanticSearchResult) (*AssetListResponse, error) {
	if result == nil {
		return &AssetListResponse{Assets: []AssetDTO{}, TotalCount: 0}, nil
	}
	return c.semanticHitsToAssets(
		result.Hits,
		result.TotalCount,
		"",
		result.TotalCount,
		result.TotalCount,
		db.SemanticCoverageStateCounts{},
	)
}

func (c *AppCommands) semanticHitsToAssets(
	hits []domain.SemanticSearchHit,
	total int,
	sessionID string,
	coverageReady int,
	coverageTotal int,
	coverageStates db.SemanticCoverageStateCounts,
) (*AssetListResponse, error) {
	if len(hits) == 0 {
		return &AssetListResponse{
			Assets:                     []AssetDTO{},
			TotalCount:                 total,
			SemanticSearchSessionID:    sessionID,
			SemanticCoverageReadyCount:   coverageReady,
			SemanticCoverageTotalCount:   coverageTotal,
			SemanticCoverageQueuedCount:  coverageStates.Queued,
			SemanticCoverageRunningCount: coverageStates.Running,
			SemanticCoverageFailedCount:  coverageStates.Failed,
			SemanticCoverageStaleCount:   coverageStates.Stale,
		}, nil
	}

	ids := make([]int64, len(hits))
	scoreByID := make(map[int64]float32, len(hits))
	for i, hit := range hits {
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
	return &AssetListResponse{
		Assets:                     dtos,
		TotalCount:                 total,
		SemanticSearchSessionID:    sessionID,
		SemanticCoverageReadyCount:   coverageReady,
		SemanticCoverageTotalCount:   coverageTotal,
		SemanticCoverageQueuedCount:  coverageStates.Queued,
		SemanticCoverageRunningCount: coverageStates.Running,
		SemanticCoverageFailedCount:  coverageStates.Failed,
		SemanticCoverageStaleCount:   coverageStates.Stale,
	}, nil
}

func (c *AppCommands) semanticScopeTotal(req AssetListRequest) (int, error) {
	result, err := c.assetRepo.List(db.AssetQuery{
		LibraryID:   req.LibraryID,
		FolderPath:  req.FolderPath,
		Recurse:     req.Recurse,
		Rating:      req.Rating,
		StatusLabel: req.StatusLabel,
		IsFavorite:  req.IsFavorite,
		TagIDs:      req.TagIDs,
		HasNote:     req.HasNote,
		Extension:   req.Extension,
		ColorLabel:  req.ColorLabel,
		SortBy:      req.SortBy,
		SortDesc:    req.SortDesc,
		Offset:      0,
		Limit:       1,
	})
	if err != nil {
		return 0, err
	}
	return result.TotalCount, nil
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
