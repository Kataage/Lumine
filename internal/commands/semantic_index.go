package commands

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"math"
	"sort"
	"sync"
	"time"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

var errSemanticIndexNotReady = errors.New("semantic memory index is not ready")

type semanticIndexKey struct {
	engine  string
	modelID string
	version string
}

type semanticMemoryIndex struct {
	mu sync.RWMutex

	key     semanticIndexKey
	vectors map[int64][]float32
	ready   bool
	warming bool
	wait    chan struct{}
	pending map[int64][]float32
	lastErr error
}

func newSemanticMemoryIndex() *semanticMemoryIndex {
	return &semanticMemoryIndex{
		vectors: make(map[int64][]float32),
		pending: make(map[int64][]float32),
	}
}

func semanticKey(engine, modelID, version string) semanticIndexKey {
	return semanticIndexKey{engine: engine, modelID: modelID, version: version}
}

func normalizeIndexVector(vector []float32) ([]float32, error) {
	if len(vector) == 0 {
		return nil, errors.New("semantic index vector is empty")
	}
	var sum float64
	for _, value := range vector {
		if math.IsNaN(float64(value)) || math.IsInf(float64(value), 0) {
			return nil, errors.New("semantic index vector contains a non-finite value")
		}
		sum += float64(value) * float64(value)
	}
	if sum <= 0 {
		return nil, errors.New("semantic index vector has zero length")
	}
	scale := float32(1 / math.Sqrt(sum))
	result := make([]float32, len(vector))
	for i, value := range vector {
		result[i] = value * scale
	}
	return result, nil
}

func (i *semanticMemoryIndex) Upsert(
	assetID int64,
	engine string,
	modelID string,
	version string,
	vector []float32,
) {
	normalized, err := normalizeIndexVector(vector)
	if err != nil {
		return
	}
	key := semanticKey(engine, modelID, version)

	i.mu.Lock()
	defer i.mu.Unlock()
	if i.key != key {
		return
	}
	if i.warming {
		i.pending[assetID] = normalized
	}
	if i.ready {
		i.vectors[assetID] = normalized
	}
}

func (i *semanticMemoryIndex) IsReady(engine, modelID, version string) bool {
	key := semanticKey(engine, modelID, version)
	i.mu.RLock()
	defer i.mu.RUnlock()
	return i.ready && i.key == key
}

func (i *semanticMemoryIndex) Warm(
	ctx context.Context,
	repo *db.SemanticEmbeddingRepo,
	engine string,
	modelID string,
	version string,
) error {
	if repo == nil {
		return errors.New("semantic embedding repository is unavailable")
	}
	key := semanticKey(engine, modelID, version)

	i.mu.Lock()
	if i.ready && i.key == key {
		i.mu.Unlock()
		return nil
	}
	if i.warming && i.key == key {
		wait := i.wait
		i.mu.Unlock()
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-wait:
			i.mu.RLock()
			err := i.lastErr
			ready := i.ready
			i.mu.RUnlock()
			if err != nil {
				return err
			}
			if !ready {
				return errSemanticIndexNotReady
			}
			return nil
		}
	}

	i.key = key
	i.ready = false
	i.warming = true
	i.lastErr = nil
	i.pending = make(map[int64][]float32)
	i.wait = make(chan struct{})
	wait := i.wait
	i.mu.Unlock()

	started := time.Now()
	loaded := make(map[int64][]float32)
	err := repo.WalkReadyEmbeddings(ctx, engine, modelID, version, func(assetID int64, vector []float32) error {
		loaded[assetID] = vector
		return nil
	})

	i.mu.Lock()
	if err == nil && i.key == key {
		for assetID, vector := range i.pending {
			loaded[assetID] = vector
		}
		i.vectors = loaded
		i.ready = true
		i.lastErr = nil
	} else if err != nil {
		i.lastErr = err
	}
	i.pending = make(map[int64][]float32)
	i.warming = false
	close(wait)
	i.mu.Unlock()

	if err != nil {
		return err
	}
	slog.Info("semantic memory index ready",
		"vectors", len(loaded),
		"elapsed", time.Since(started),
		"model", modelID,
	)
	return nil
}

func (i *semanticMemoryIndex) Search(
	ctx context.Context,
	vector []float32,
	eligibleIDs []int64,
	offset int,
	limit int,
	onProgress func(scanned, total int),
) (*db.SemanticSearchResult, error) {
	needle, err := normalizeIndexVector(vector)
	if err != nil {
		return nil, err
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

	i.mu.RLock()
	defer i.mu.RUnlock()
	if !i.ready {
		return nil, errSemanticIndexNotReady
	}

	hits := make([]domain.SemanticSearchHit, 0, len(eligibleIDs))
	lastProgress := time.Now()
	for position, assetID := range eligibleIDs {
		if position%512 == 0 {
			select {
			case <-ctx.Done():
				return nil, ctx.Err()
			default:
			}
		}
		candidate := i.vectors[assetID]
		if len(candidate) != len(needle) {
			continue
		}
		var score float32
		for dimension, queryValue := range needle {
			score += queryValue * candidate[dimension]
		}
		hits = append(hits, domain.SemanticSearchHit{AssetID: assetID, Score: score})

		if onProgress != nil && time.Since(lastProgress) >= 120*time.Millisecond {
			onProgress(position+1, len(eligibleIDs))
			lastProgress = time.Now()
		}
	}
	if onProgress != nil && len(eligibleIDs) > 0 {
		onProgress(len(eligibleIDs), len(eligibleIDs))
	}

	sort.Slice(hits, func(a, b int) bool {
		if hits[a].Score == hits[b].Score {
			return hits[a].AssetID < hits[b].AssetID
		}
		return hits[a].Score > hits[b].Score
	})

	total := len(hits)
	start := offset
	if start > total {
		start = total
	}
	end := start + limit
	if end > total {
		end = total
	}
	return &db.SemanticSearchResult{
		Hits:       hits[start:end],
		TotalCount: total,
		RankedHits: hits,
	}, nil
}

func (i *semanticMemoryIndex) Stats() (ready bool, count int, key string) {
	i.mu.RLock()
	defer i.mu.RUnlock()
	return i.ready, len(i.vectors), fmt.Sprintf("%s/%s/%s", i.key.engine, i.key.modelID, i.key.version)
}
