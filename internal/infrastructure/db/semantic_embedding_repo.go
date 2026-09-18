package db

import (
	"encoding/binary"
	"errors"
	"fmt"
	"math"
	"sort"
	"strings"

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
		if strings.Contains(err.Error(), "no rows") {
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

func (r *SemanticEmbeddingRepo) Search(vector []float32, query SemanticSearchQuery) (*SemanticSearchResult, error) {
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

	hits := make([]domain.SemanticSearchHit, 0)
	for rows.Next() {
		var assetID int64
		var dimensions int
		var blob []byte
		if err := rows.Scan(&assetID, &dimensions, &blob); err != nil {
			return nil, fmt.Errorf("scan semantic embedding: %w", err)
		}
		if dimensions != len(needle) {
			continue
		}
		candidate, err := decodeSemanticVector(blob, dimensions)
		if err != nil {
			return nil, fmt.Errorf("decode semantic embedding for asset %d: %w", assetID, err)
		}
		var score float32
		for i := range needle {
			score += needle[i] * candidate[i]
		}
		hits = append(hits, domain.SemanticSearchHit{AssetID: assetID, Score: score})
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}

	sort.SliceStable(hits, func(i, j int) bool {
		if hits[i].Score == hits[j].Score {
			return hits[i].AssetID < hits[j].AssetID
		}
		return hits[i].Score > hits[j].Score
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
