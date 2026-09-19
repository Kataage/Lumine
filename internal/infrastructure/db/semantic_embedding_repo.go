package db

import (
	"container/heap"
	"context"
	"database/sql"
	"encoding/binary"
	"errors"
	"fmt"
	"math"
	"sort"
	"strings"
	"time"

	"github.com/kataage/lumine/internal/domain"
)

var (
	ErrSemanticEmbeddingNotFound = errors.New("semantic embedding not found")
	ErrSemanticVectorInvalid      = errors.New("semantic embedding vector is invalid")
)

type SemanticEmbeddingRepo struct {
	db *DB
}

func NewSemanticEmbeddingRepo(db *DB) *SemanticEmbeddingRepo {
	return &SemanticEmbeddingRepo{db: db}
}

type SemanticSearchQuery struct {
	LibraryID    int64
	FolderPath   string
	Recurse      bool
	Rating       int
	StatusLabel  string
	IsFavorite   *bool
	TagIDs       []int64
	HasNote      *bool
	Extension    string
	ColorLabel   string
	Engine       string
	ModelID      string
	ModelVersion string
	ExcludeID    int64
	Offset       int
	Limit        int
}

type SemanticSearchResult struct {
	Hits       []domain.SemanticSearchHit
	TotalCount int
	RankedHits []domain.SemanticSearchHit
}

type SemanticSearchProgress struct {
	Hits         []domain.SemanticSearchHit
	ScannedCount int
	TotalCount   int
}

type semanticTopHeap []domain.SemanticSearchHit

func (h semanticTopHeap) Len() int { return len(h) }
func (h semanticTopHeap) Less(i, j int) bool {
	if h[i].Score == h[j].Score {
		return h[i].AssetID > h[j].AssetID
	}
	return h[i].Score < h[j].Score
}
func (h semanticTopHeap) Swap(i, j int) { h[i], h[j] = h[j], h[i] }
func (h *semanticTopHeap) Push(value any) {
	*h = append(*h, value.(domain.SemanticSearchHit))
}
func (h *semanticTopHeap) Pop() any {
	old := *h
	last := len(old) - 1
	value := old[last]
	*h = old[:last]
	return value
}

func semanticHitBetter(a, b domain.SemanticSearchHit) bool {
	if a.Score == b.Score {
		return a.AssetID < b.AssetID
	}
	return a.Score > b.Score
}

func snapshotSemanticTop(h semanticTopHeap) []domain.SemanticSearchHit {
	result := append([]domain.SemanticSearchHit(nil), h...)
	sort.Slice(result, func(i, j int) bool {
		return semanticHitBetter(result[i], result[j])
	})
	return result
}

func (r *SemanticEmbeddingRepo) Upsert(
	assetID int64,
	engine string,
	modelID string,
	modelVersion string,
	vector []float32,
) error {
	if assetID <= 0 {
		return errors.New("asset id must be positive")
	}
	if engine == "" || modelID == "" || modelVersion == "" {
		return errors.New("semantic embedding provenance is required")
	}
	normalized, err := normalizeSemanticVector(vector)
	if err != nil {
		return err
	}
	blob := encodeSemanticVector(normalized)

	_, err = r.db.Exec(`
		INSERT INTO ai_semantic_embeddings (
			asset_id, engine, model_id, model_version, dimensions, vector, updated_at
		) VALUES (?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP)
		ON CONFLICT(asset_id) DO UPDATE SET
			engine = excluded.engine,
			model_id = excluded.model_id,
			model_version = excluded.model_version,
			dimensions = excluded.dimensions,
			vector = excluded.vector,
			updated_at = CURRENT_TIMESTAMP
	`, assetID, engine, modelID, modelVersion, len(normalized), blob)
	if err != nil {
		return fmt.Errorf("upsert semantic embedding: %w", err)
	}
	return nil
}

func (r *SemanticEmbeddingRepo) Get(assetID int64) (*domain.SemanticEmbedding, error) {
	var value domain.SemanticEmbedding
	var blob []byte
	err := r.db.QueryRow(`
		SELECT asset_id, engine, model_id, model_version, dimensions, vector
		FROM ai_semantic_embeddings
		WHERE asset_id = ?
	`, assetID).Scan(
		&value.AssetID,
		&value.Engine,
		&value.ModelID,
		&value.ModelVersion,
		&value.Dimensions,
		&blob,
	)
	if err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return nil, nil
		}
		return nil, fmt.Errorf("get semantic embedding: %w", err)
	}
	vector, err := decodeSemanticVector(blob, value.Dimensions)
	if err != nil {
		return nil, fmt.Errorf("decode semantic embedding: %w", err)
	}
	value.Vector = vector
	return &value, nil
}

func (r *SemanticEmbeddingRepo) GetReady(assetID int64) (*domain.SemanticEmbedding, error) {
	var value domain.SemanticEmbedding
	var blob []byte
	err := r.db.QueryRow(`
		SELECT e.asset_id, e.engine, e.model_id, e.model_version, e.dimensions, e.vector
		FROM ai_semantic_embeddings e
		JOIN ai_asset_analysis aa
		  ON aa.asset_id = e.asset_id
		 AND aa.capability = 'semantic_search'
		 AND aa.state = 'ready'
		 AND aa.engine = e.engine
		 AND aa.model_id = e.model_id
		 AND aa.model_version = e.model_version
		WHERE e.asset_id = ?
	`, assetID).Scan(
		&value.AssetID,
		&value.Engine,
		&value.ModelID,
		&value.ModelVersion,
		&value.Dimensions,
		&blob,
	)
	if err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return nil, nil
		}
		return nil, fmt.Errorf("get ready semantic embedding: %w", err)
	}
	vector, err := decodeSemanticVector(blob, value.Dimensions)
	if err != nil {
		return nil, fmt.Errorf("decode ready semantic embedding: %w", err)
	}
	value.Vector = vector
	return &value, nil
}

func (r *SemanticEmbeddingRepo) ListNeedingEmbedding(
	libraryID int64,
	engine string,
	modelID string,
	modelVersion string,
	afterID int64,
	limit int,
) ([]int64, error) {
	return r.ListNeedingEmbeddingContext(
		context.Background(),
		libraryID,
		engine,
		modelID,
		modelVersion,
		afterID,
		limit,
	)
}

func (r *SemanticEmbeddingRepo) ListNeedingEmbeddingContext(
	ctx context.Context,
	libraryID int64,
	engine string,
	modelID string,
	modelVersion string,
	afterID int64,
	limit int,
) ([]int64, error) {
	if libraryID <= 0 {
		return nil, errors.New("library id must be positive")
	}
	if engine == "" || modelID == "" || modelVersion == "" {
		return nil, errors.New("semantic embedding provenance is required")
	}
	if limit <= 0 || limit > 5000 {
		limit = 1000
	}
	if ctx == nil {
		ctx = context.Background()
	}

	rows, err := r.db.QueryContext(ctx, `
		SELECT a.id
		FROM assets a
		WHERE a.library_id = ?
		  AND a.id > ?
		  AND NOT EXISTS (
			SELECT 1
			FROM ai_asset_analysis aa
			JOIN ai_semantic_embeddings e ON e.asset_id = aa.asset_id
			WHERE aa.asset_id = a.id
			  AND aa.capability = 'semantic_search'
			  AND aa.state = 'ready'
			  AND aa.engine = ?
			  AND aa.model_id = ?
			  AND aa.model_version = ?
			  AND e.engine = aa.engine
			  AND e.model_id = aa.model_id
			  AND e.model_version = aa.model_version
		  )
		ORDER BY a.id ASC
		LIMIT ?
	`, libraryID, afterID, engine, modelID, modelVersion, limit)
	if err != nil {
		return nil, fmt.Errorf("list assets needing semantic embedding: %w", err)
	}
	defer rows.Close()

	ids := make([]int64, 0, limit)
	for rows.Next() {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		var id int64
		if err := rows.Scan(&id); err != nil {
			return nil, fmt.Errorf("scan asset needing semantic embedding: %w", err)
		}
		ids = append(ids, id)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	return ids, nil
}



type SemanticIndexSnapshotInfo struct {
	Generation uint64
	Count      int
	Dimensions int
}

func (r *SemanticEmbeddingRepo) SemanticIndexGeneration(ctx context.Context) (uint64, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	var generation uint64
	if err := r.db.QueryRowContext(
		ctx,
		"SELECT generation FROM ai_semantic_index_generation WHERE id = 1",
	).Scan(&generation); err != nil {
		return 0, fmt.Errorf("read semantic index generation: %w", err)
	}
	return generation, nil
}

func (r *SemanticEmbeddingRepo) ReadyEmbeddingSnapshotInfo(
	ctx context.Context,
	engine string,
	modelID string,
	modelVersion string,
) (SemanticIndexSnapshotInfo, error) {
	if engine == "" || modelID == "" || modelVersion == "" {
		return SemanticIndexSnapshotInfo{}, errors.New("semantic embedding provenance is required")
	}
	if ctx == nil {
		ctx = context.Background()
	}

	generation, err := r.SemanticIndexGeneration(ctx)
	if err != nil {
		return SemanticIndexSnapshotInfo{}, err
	}

	var count int
	var minDimensions, maxDimensions sql.NullInt64
	if err := r.db.QueryRowContext(ctx, `
		SELECT COUNT(*), MIN(e.dimensions), MAX(e.dimensions)
		FROM ai_semantic_embeddings e
		JOIN ai_asset_analysis aa
		  ON aa.asset_id = e.asset_id
		 AND aa.capability = 'semantic_search'
		 AND aa.state = 'ready'
		 AND aa.engine = e.engine
		 AND aa.model_id = e.model_id
		 AND aa.model_version = e.model_version
		WHERE e.engine = ? AND e.model_id = ? AND e.model_version = ?
	`, engine, modelID, modelVersion).Scan(&count, &minDimensions, &maxDimensions); err != nil {
		return SemanticIndexSnapshotInfo{}, fmt.Errorf("read semantic snapshot info: %w", err)
	}

	dimensions := 0
	if count > 0 {
		if !minDimensions.Valid || !maxDimensions.Valid || minDimensions.Int64 <= 0 || minDimensions.Int64 != maxDimensions.Int64 {
			return SemanticIndexSnapshotInfo{}, ErrSemanticVectorInvalid
		}
		dimensions = int(minDimensions.Int64)
	}
	return SemanticIndexSnapshotInfo{
		Generation: generation,
		Count:      count,
		Dimensions: dimensions,
	}, nil
}

func (r *SemanticEmbeddingRepo) CountReadyEmbeddings(
	ctx context.Context,
	engine string,
	modelID string,
	modelVersion string,
) (int, error) {
	if engine == "" || modelID == "" || modelVersion == "" {
		return 0, errors.New("semantic embedding provenance is required")
	}
	var count int
	err := r.db.QueryRowContext(ctx, `
		SELECT COUNT(*)
		FROM ai_semantic_embeddings e
		JOIN ai_asset_analysis aa
		  ON aa.asset_id = e.asset_id
		 AND aa.capability = 'semantic_search'
		 AND aa.state = 'ready'
		 AND aa.engine = e.engine
		 AND aa.model_id = e.model_id
		 AND aa.model_version = e.model_version
		WHERE e.engine = ? AND e.model_id = ? AND e.model_version = ?
	`, engine, modelID, modelVersion).Scan(&count)
	if err != nil {
		return 0, fmt.Errorf("count ready semantic embeddings: %w", err)
	}
	return count, nil
}

func (r *SemanticEmbeddingRepo) WalkReadyEmbeddingBlobs(
	ctx context.Context,
	engine string,
	modelID string,
	modelVersion string,
	visit func(assetID int64, dimensions int, blob []byte) error,
) error {
	if engine == "" || modelID == "" || modelVersion == "" {
		return errors.New("semantic embedding provenance is required")
	}
	if visit == nil {
		return errors.New("semantic embedding blob visitor is required")
	}

	rows, err := r.db.QueryContext(ctx, `
		SELECT e.asset_id, e.dimensions, e.vector
		FROM ai_semantic_embeddings e
		JOIN ai_asset_analysis aa
		  ON aa.asset_id = e.asset_id
		 AND aa.capability = 'semantic_search'
		 AND aa.state = 'ready'
		 AND aa.engine = e.engine
		 AND aa.model_id = e.model_id
		 AND aa.model_version = e.model_version
		WHERE e.engine = ? AND e.model_id = ? AND e.model_version = ?
		ORDER BY e.asset_id
	`, engine, modelID, modelVersion)
	if err != nil {
		return fmt.Errorf("walk ready semantic embedding blobs: %w", err)
	}
	defer rows.Close()

	for rows.Next() {
		if err := ctx.Err(); err != nil {
			return err
		}
		var assetID int64
		var dimensions int
		var blob []byte
		if err := rows.Scan(&assetID, &dimensions, &blob); err != nil {
			return fmt.Errorf("scan ready semantic embedding blob: %w", err)
		}
		if dimensions <= 0 || len(blob) != dimensions*4 {
			return fmt.Errorf("invalid semantic embedding blob for asset %d: %w", assetID, ErrSemanticVectorInvalid)
		}
		if err := visit(assetID, dimensions, blob); err != nil {
			return err
		}
	}
	return rows.Err()
}

func (r *SemanticEmbeddingRepo) WalkReadyEmbeddings(
	ctx context.Context,
	engine string,
	modelID string,
	modelVersion string,
	visit func(assetID int64, vector []float32) error,
) error {
	if engine == "" || modelID == "" || modelVersion == "" {
		return errors.New("semantic embedding provenance is required")
	}
	if visit == nil {
		return errors.New("semantic embedding visitor is required")
	}

	rows, err := r.db.QueryContext(ctx, `
		SELECT e.asset_id, e.dimensions, e.vector
		FROM ai_semantic_embeddings e
		JOIN ai_asset_analysis aa
		  ON aa.asset_id = e.asset_id
		 AND aa.capability = 'semantic_search'
		 AND aa.state = 'ready'
		 AND aa.engine = e.engine
		 AND aa.model_id = e.model_id
		 AND aa.model_version = e.model_version
		WHERE e.engine = ? AND e.model_id = ? AND e.model_version = ?
		ORDER BY e.asset_id
	`, engine, modelID, modelVersion)
	if err != nil {
		return fmt.Errorf("walk ready semantic embeddings: %w", err)
	}
	defer rows.Close()

	for rows.Next() {
		if err := ctx.Err(); err != nil {
			return err
		}
		var assetID int64
		var dimensions int
		var blob []byte
		if err := rows.Scan(&assetID, &dimensions, &blob); err != nil {
			return fmt.Errorf("scan ready semantic embedding: %w", err)
		}
		vector, err := decodeSemanticVector(blob, dimensions)
		if err != nil {
			return fmt.Errorf("decode ready semantic embedding for asset %d: %w", assetID, err)
		}
		if err := visit(assetID, vector); err != nil {
			return err
		}
	}
	return rows.Err()
}

func (r *SemanticEmbeddingRepo) ListEligibleSemanticAssetIDs(
	ctx context.Context,
	query SemanticSearchQuery,
) ([]int64, error) {
	if query.Engine == "" || query.ModelID == "" || query.ModelVersion == "" {
		return nil, errors.New("semantic search provenance is required")
	}
	where, args := semanticSearchWhere(query)
	rows, err := r.db.QueryContext(ctx, fmt.Sprintf(`
		SELECT e.asset_id
		FROM ai_semantic_embeddings e
		JOIN ai_asset_analysis aa ON aa.asset_id = e.asset_id
		JOIN assets a ON a.id = e.asset_id
		%s
		ORDER BY e.asset_id
	`, where), args...)
	if err != nil {
		return nil, fmt.Errorf("list eligible semantic assets: %w", err)
	}
	defer rows.Close()

	ids := make([]int64, 0, 1024)
	for rows.Next() {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		var id int64
		if err := rows.Scan(&id); err != nil {
			return nil, fmt.Errorf("scan eligible semantic asset: %w", err)
		}
		ids = append(ids, id)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	return ids, nil
}

func (r *SemanticEmbeddingRepo) Search(vector []float32, query SemanticSearchQuery) (*SemanticSearchResult, error) {
	return r.SearchWithProgress(context.Background(), vector, query, 0, nil)
}

func semanticSearchWhere(query SemanticSearchQuery) (string, []any) {
	where := `WHERE e.engine = ? AND e.model_id = ? AND e.model_version = ?
		AND aa.capability = 'semantic_search'
		AND aa.state = 'ready'
		AND aa.engine = e.engine
		AND aa.model_id = e.model_id
		AND aa.model_version = e.model_version`
	args := []any{query.Engine, query.ModelID, query.ModelVersion}

	if query.LibraryID > 0 {
		where += " AND a.library_id = ?"
		args = append(args, query.LibraryID)
	}
	if query.FolderPath != "" {
		if query.Recurse {
			where += " AND (a.folder_path = ? OR a.folder_path LIKE ? OR a.folder_path LIKE ?)"
			args = append(args, query.FolderPath, query.FolderPath+"/%", query.FolderPath+"\\%")
		} else {
			where += " AND a.folder_path = ?"
			args = append(args, query.FolderPath)
		}
	}
	if query.Rating > 0 {
		where += " AND a.rating = ?"
		args = append(args, query.Rating)
	}
	if query.StatusLabel != "" {
		where += " AND a.status_label = ?"
		args = append(args, query.StatusLabel)
	}
	if query.IsFavorite != nil {
		where += " AND a.is_favorite = ?"
		args = append(args, *query.IsFavorite)
	}
	if query.Extension != "" {
		where += " AND a.extension = ?"
		args = append(args, query.Extension)
	}
	if query.ColorLabel != "" {
		where += " AND a.color_label = ?"
		args = append(args, query.ColorLabel)
	}
	if query.HasNote != nil {
		if *query.HasNote {
			where += " AND EXISTS (SELECT 1 FROM asset_notes n WHERE n.asset_id = a.id)"
		} else {
			where += " AND NOT EXISTS (SELECT 1 FROM asset_notes n WHERE n.asset_id = a.id)"
		}
	}
	if query.ExcludeID > 0 {
		where += " AND a.id <> ?"
		args = append(args, query.ExcludeID)
	}
	if len(query.TagIDs) > 0 {
		placeholders := make([]string, len(query.TagIDs))
		for i, tagID := range query.TagIDs {
			placeholders[i] = "?"
			args = append(args, tagID)
		}
		where += fmt.Sprintf(
			" AND a.id IN (SELECT at2.asset_id FROM asset_tags at2 WHERE at2.tag_id IN (%s))",
			strings.Join(placeholders, ","),
		)
	}
	return where, args
}

func semanticDotBlob(needle []float32, blob []byte, dimensions int) (float32, error) {
	if dimensions != len(needle) || dimensions <= 0 || len(blob) != dimensions*4 {
		return 0, ErrSemanticVectorInvalid
	}
	var score float32
	for i, value := range needle {
		candidate := math.Float32frombits(binary.LittleEndian.Uint32(blob[i*4:]))
		if math.IsNaN(float64(candidate)) || math.IsInf(float64(candidate), 0) {
			return 0, ErrSemanticVectorInvalid
		}
		score += value * candidate
	}
	return score, nil
}

func (r *SemanticEmbeddingRepo) SearchWithProgress(
	ctx context.Context,
	vector []float32,
	query SemanticSearchQuery,
	previewLimit int,
	onProgress func(SemanticSearchProgress),
) (*SemanticSearchResult, error) {
	if query.Engine == "" || query.ModelID == "" || query.ModelVersion == "" {
		return nil, errors.New("semantic search provenance is required")
	}
	needle, err := normalizeSemanticVector(vector)
	if err != nil {
		return nil, err
	}
	if query.Limit <= 0 {
		query.Limit = 100
	}
	if query.Limit > 500 {
		query.Limit = 500
	}
	if query.Offset < 0 {
		query.Offset = 0
	}
	if previewLimit < 0 {
		previewLimit = 0
	}
	if previewLimit > 200 {
		previewLimit = 200
	}

	where, args := semanticSearchWhere(query)
	var eligibleCount int
	if err := r.db.QueryRow(fmt.Sprintf(`
		SELECT COUNT(*)
		FROM ai_semantic_embeddings e
		JOIN ai_asset_analysis aa ON aa.asset_id = e.asset_id
		JOIN assets a ON a.id = e.asset_id
		%s
	`, where), args...).Scan(&eligibleCount); err != nil {
		return nil, fmt.Errorf("count semantic embeddings: %w", err)
	}

	rows, err := r.db.Query(fmt.Sprintf(`
		SELECT e.asset_id, e.dimensions, e.vector
		FROM ai_semantic_embeddings e
		JOIN ai_asset_analysis aa ON aa.asset_id = e.asset_id
		JOIN assets a ON a.id = e.asset_id
		%s
	`, where), args...)
	if err != nil {
		return nil, fmt.Errorf("query semantic embeddings: %w", err)
	}
	defer rows.Close()

	hits := make([]domain.SemanticSearchHit, 0, eligibleCount)
	top := &semanticTopHeap{}
	if previewLimit > 0 {
		heap.Init(top)
	}
	scanned := 0
	lastProgress := time.Now()

	emitProgress := func(force bool) {
		if onProgress == nil || previewLimit == 0 || len(*top) == 0 {
			return
		}
		if !force && scanned < 256 && time.Since(lastProgress) < 120*time.Millisecond {
			return
		}
		if !force && scanned%2048 != 0 && time.Since(lastProgress) < 150*time.Millisecond {
			return
		}
		lastProgress = time.Now()
		onProgress(SemanticSearchProgress{
			Hits:         snapshotSemanticTop(*top),
			ScannedCount: scanned,
			TotalCount:   eligibleCount,
		})
	}

	for rows.Next() {
		if scanned%256 == 0 {
			select {
			case <-ctx.Done():
				return nil, ctx.Err()
			default:
			}
		}

		var assetID int64
		var dimensions int
		var blob []byte
		if err := rows.Scan(&assetID, &dimensions, &blob); err != nil {
			return nil, fmt.Errorf("scan semantic embedding: %w", err)
		}
		scanned++
		if dimensions != len(needle) {
			emitProgress(false)
			continue
		}

		score, err := semanticDotBlob(needle, blob, dimensions)
		if err != nil {
			return nil, fmt.Errorf("score semantic embedding for asset %d: %w", assetID, err)
		}
		hit := domain.SemanticSearchHit{AssetID: assetID, Score: score}
		hits = append(hits, hit)

		if previewLimit > 0 {
			if top.Len() < previewLimit {
				heap.Push(top, hit)
			} else if semanticHitBetter(hit, (*top)[0]) {
				(*top)[0] = hit
				heap.Fix(top, 0)
			}
		}
		emitProgress(false)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}

	emitProgress(true)
	sort.Slice(hits, func(i, j int) bool {
		return semanticHitBetter(hits[i], hits[j])
	})

	total := len(hits)
	start := query.Offset
	if start > total {
		start = total
	}
	end := start + query.Limit
	if end > total {
		end = total
	}
	return &SemanticSearchResult{
		Hits:       hits[start:end],
		TotalCount: total,
		RankedHits: hits,
	}, nil
}

func normalizeSemanticVector(vector []float32) ([]float32, error) {
	if len(vector) == 0 {
		return nil, ErrSemanticVectorInvalid
	}
	var sum float64
	for _, value := range vector {
		if math.IsNaN(float64(value)) || math.IsInf(float64(value), 0) {
			return nil, ErrSemanticVectorInvalid
		}
		sum += float64(value) * float64(value)
	}
	if sum <= 0 {
		return nil, ErrSemanticVectorInvalid
	}
	scale := float32(1 / math.Sqrt(sum))
	result := make([]float32, len(vector))
	for i, value := range vector {
		result[i] = value * scale
	}
	return result, nil
}

func encodeSemanticVector(vector []float32) []byte {
	blob := make([]byte, len(vector)*4)
	for i, value := range vector {
		binary.LittleEndian.PutUint32(blob[i*4:], math.Float32bits(value))
	}
	return blob
}

func decodeSemanticVector(blob []byte, dimensions int) ([]float32, error) {
	if dimensions <= 0 || len(blob) != dimensions*4 {
		return nil, ErrSemanticVectorInvalid
	}
	vector := make([]float32, dimensions)
	for i := range vector {
		vector[i] = math.Float32frombits(binary.LittleEndian.Uint32(blob[i*4:]))
	}
	return vector, nil
}
