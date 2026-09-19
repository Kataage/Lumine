package commands

import (
	"context"
	"encoding/binary"
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

var (
	errSemanticIndexNotReady   = errors.New("semantic memory index is not ready")
	errSemanticIndexSuperseded = errors.New("semantic memory index warm was superseded")
)

type semanticIndexKey struct {
	engine  string
	modelID string
	version string
}

type SemanticIndexStatus struct {
	State        string `json:"state"`
	LoadedCount  int    `json:"loadedCount"`
	TotalCount   int    `json:"totalCount"`
	Dimensions   int    `json:"dimensions"`
	ElapsedMs    int64  `json:"elapsedMs"`
	UpdatedAgoMs int64  `json:"updatedAgoMs"`
	ModelID      string `json:"modelId,omitempty"`
	Error        string `json:"error,omitempty"`
}

type semanticMemoryIndex struct {
	mu sync.RWMutex

	key        semanticIndexKey
	positions  map[int64]int
	data       []float32
	dimensions int

	ready      bool
	warming    bool
	wait       chan struct{}
	generation uint64
	pending    map[int64][]float32
	lastErr    error

	stage      string
	loaded     int
	total      int
	startedAt  time.Time
	finishedAt time.Time
	updatedAt  time.Time
}

func newSemanticMemoryIndex() *semanticMemoryIndex {
	return &semanticMemoryIndex{
		positions: make(map[int64]int),
		pending:   make(map[int64][]float32),
		stage:     "idle",
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
	for index, value := range vector {
		result[index] = value * scale
	}
	return result, nil
}

func decodeIndexBlobInto(target []float32, blob []byte, dimensions int) error {
	if dimensions <= 0 || len(target) != dimensions || len(blob) != dimensions*4 {
		return errors.New("invalid semantic index blob")
	}
	for index := 0; index < dimensions; index++ {
		value := math.Float32frombits(binary.LittleEndian.Uint32(blob[index*4:]))
		if math.IsNaN(float64(value)) || math.IsInf(float64(value), 0) {
			return errors.New("semantic index blob contains a non-finite value")
		}
		target[index] = value
	}
	return nil
}

func (i *semanticMemoryIndex) setProgressLocked(stage string, loaded, total int) {
	i.stage = stage
	i.loaded = loaded
	i.total = total
	i.updatedAt = time.Now()
}

func (i *semanticMemoryIndex) Status() SemanticIndexStatus {
	i.mu.RLock()
	defer i.mu.RUnlock()

	now := time.Now()
	var elapsedMs int64
	if !i.startedAt.IsZero() {
		end := now
		if !i.finishedAt.IsZero() {
			end = i.finishedAt
		}
		elapsedMs = end.Sub(i.startedAt).Milliseconds()
	}
	var updatedAgoMs int64
	if !i.updatedAt.IsZero() {
		updatedAgoMs = now.Sub(i.updatedAt).Milliseconds()
	}
	status := SemanticIndexStatus{
		State:        i.stage,
		LoadedCount:  i.loaded,
		TotalCount:   i.total,
		Dimensions:   i.dimensions,
		ElapsedMs:    elapsedMs,
		UpdatedAgoMs: updatedAgoMs,
		ModelID:      i.key.modelID,
	}
	if i.lastErr != nil {
		status.Error = i.lastErr.Error()
	}
	return status
}

func (i *semanticMemoryIndex) Prepare(engine, modelID, version string) {
	key := semanticKey(engine, modelID, version)
	i.mu.Lock()
	defer i.mu.Unlock()
	if i.key == key {
		if i.pending == nil {
			i.pending = make(map[int64][]float32)
		}
		return
	}
	// A model/version switch invalidates an in-flight warm. Wake waiters now;
	// the old loader checks generation/key before every shared-state write and
	// will return errSemanticIndexSuperseded without publishing old vectors.
	if i.warming {
		i.warming = false
		if i.wait != nil {
			close(i.wait)
			i.wait = nil
		}
	}
	i.generation++

	// Semantic Search currently has one active model. Selecting the model
	// synchronously before background warm/backfill prevents early completed
	// embeddings from being dropped before Warm gets CPU time.
	i.key = key
	i.ready = false
	i.stage = "idle"
	i.positions = make(map[int64]int)
	i.data = nil
	i.dimensions = 0
	i.pending = make(map[int64][]float32)
	i.lastErr = nil
	i.loaded = 0
	i.total = 0
	i.startedAt = time.Time{}
	i.finishedAt = time.Time{}
	i.updatedAt = time.Now()
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
	if i.warming || !i.ready {
		if i.pending == nil {
			i.pending = make(map[int64][]float32)
		}
		i.pending[assetID] = normalized
		return
	}
	if i.dimensions == 0 {
		i.dimensions = len(normalized)
	}
	if len(normalized) != i.dimensions {
		return
	}
	if position, ok := i.positions[assetID]; ok {
		start := position * i.dimensions
		copy(i.data[start:start+i.dimensions], normalized)
	} else {
		position := len(i.positions)
		i.positions[assetID] = position
		i.data = append(i.data, normalized...)
	}
	i.loaded = len(i.positions)
	if i.total < i.loaded {
		i.total = i.loaded
	}
	i.updatedAt = time.Now()
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

	for {
		i.mu.Lock()
		if i.ready && i.key == key {
			i.mu.Unlock()
			return nil
		}
		if i.warming {
			wait := i.wait
			i.mu.Unlock()
			select {
			case <-ctx.Done():
				return ctx.Err()
			case <-wait:
				i.mu.RLock()
				superseded := i.key != key
				i.mu.RUnlock()
				if superseded {
					return errSemanticIndexSuperseded
				}
				continue
			}
		}

		if i.key != key {
			i.generation++
			i.key = key
			i.pending = make(map[int64][]float32)
		} else if i.pending == nil {
			i.pending = make(map[int64][]float32)
		}
		i.ready = false
		i.warming = true
		i.lastErr = nil
		i.positions = make(map[int64]int)
		i.data = nil
		i.dimensions = 0
		i.wait = make(chan struct{})
		i.startedAt = time.Now()
		i.finishedAt = time.Time{}
		i.setProgressLocked("counting", 0, 0)
		wait := i.wait
		generation := i.generation
		i.mu.Unlock()

		slog.Info("semantic index warm started", "model", modelID)

		total, err := repo.CountReadyEmbeddings(ctx, engine, modelID, version)
		if err != nil {
			if !i.finishWarm(wait, key, generation, err) {
				return errSemanticIndexSuperseded
			}
			return err
		}

		i.mu.Lock()
		if i.generation != generation || i.key != key || i.wait != wait {
			i.mu.Unlock()
			return errSemanticIndexSuperseded
		}
		i.positions = make(map[int64]int, total)
		i.setProgressLocked("loading", 0, total)
		i.mu.Unlock()
		slog.Info("semantic index ready embeddings counted", "model", modelID, "total", total)

		loaded := 0
		lastLog := time.Now()
		err = repo.WalkReadyEmbeddingBlobs(ctx, engine, modelID, version, func(assetID int64, dimensions int, blob []byte) error {
			i.mu.Lock()
			defer i.mu.Unlock()
			if i.generation != generation || i.key != key || i.wait != wait {
				return errSemanticIndexSuperseded
			}

			if i.dimensions == 0 {
				i.dimensions = dimensions
				if total > 0 && dimensions > 0 {
					maxInt := int(^uint(0) >> 1)
					if total <= maxInt/dimensions {
						i.data = make([]float32, 0, total*dimensions)
					}
				}
			}
			if dimensions != i.dimensions {
				return fmt.Errorf("semantic embedding dimensions changed from %d to %d", i.dimensions, dimensions)
			}

			position := len(i.positions)
			i.positions[assetID] = position
			start := len(i.data)
			end := start + dimensions
			if end <= cap(i.data) {
				i.data = i.data[:end]
			} else {
				i.data = append(i.data, make([]float32, dimensions)...)
			}
			if err := decodeIndexBlobInto(i.data[start:end], blob, dimensions); err != nil {
				return fmt.Errorf("decode semantic embedding for asset %d: %w", assetID, err)
			}
			loaded++
			if loaded == total || loaded%256 == 0 || time.Since(i.updatedAt) >= 250*time.Millisecond {
				i.setProgressLocked("loading", loaded, total)
			}
			if time.Since(lastLog) >= 2*time.Second {
				elapsed := time.Since(i.startedAt)
				rate := float64(loaded) / math.Max(elapsed.Seconds(), 0.001)
				slog.Info("semantic index warming",
					"model", modelID,
					"loaded", loaded,
					"total", total,
					"rate_per_sec", math.Round(rate),
					"elapsed", elapsed.Round(time.Millisecond),
				)
				lastLog = time.Now()
			}
			return nil
		})

		i.mu.Lock()
		if i.generation != generation || i.key != key || i.wait != wait {
			i.mu.Unlock()
			return errSemanticIndexSuperseded
		}
		if err == nil {
			for assetID, vector := range i.pending {
				if i.dimensions == 0 {
					i.dimensions = len(vector)
				}
				if len(vector) != i.dimensions {
					continue
				}
				if position, ok := i.positions[assetID]; ok {
					start := position * i.dimensions
					copy(i.data[start:start+i.dimensions], vector)
				} else {
					position := len(i.positions)
					i.positions[assetID] = position
					i.data = append(i.data, vector...)
				}
			}
			i.ready = true
			i.lastErr = nil
			i.finishedAt = time.Now()
			i.setProgressLocked("ready", len(i.positions), len(i.positions))
		} else {
			i.lastErr = err
			i.finishedAt = time.Now()
			i.setProgressLocked("error", loaded, total)
		}
		i.pending = make(map[int64][]float32)
		i.warming = false
		close(wait)
		i.wait = nil
		elapsed := time.Since(i.startedAt)
		count := len(i.positions)
		i.mu.Unlock()

		if err != nil {
			slog.Warn("semantic index warm failed",
				"model", modelID,
				"loaded", loaded,
				"total", total,
				"elapsed", elapsed.Round(time.Millisecond),
				"error", err,
			)
			return err
		}
		slog.Info("semantic memory index ready",
			"vectors", count,
			"dimensions", i.Status().Dimensions,
			"elapsed", elapsed.Round(time.Millisecond),
			"model", modelID,
		)
		return nil
	}
}

func (i *semanticMemoryIndex) finishWarm(
	wait chan struct{},
	key semanticIndexKey,
	generation uint64,
	err error,
) bool {
	i.mu.Lock()
	defer i.mu.Unlock()
	if i.wait != wait || i.key != key || i.generation != generation {
		return false
	}
	i.lastErr = err
	i.warming = false
	i.finishedAt = time.Now()
	i.stage = "error"
	i.updatedAt = time.Now()
	close(wait)
	i.wait = nil
	return true
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
	if i.dimensions == 0 || len(needle) != i.dimensions {
		return nil, fmt.Errorf("semantic query dimensions %d do not match index dimensions %d", len(needle), i.dimensions)
	}

	hits := make([]domain.SemanticSearchHit, 0, len(eligibleIDs))
	lastProgress := time.Now()
	for eligiblePosition, assetID := range eligibleIDs {
		if eligiblePosition%512 == 0 {
			select {
			case <-ctx.Done():
				return nil, ctx.Err()
			default:
			}
		}
		position, ok := i.positions[assetID]
		if !ok {
			continue
		}
		start := position * i.dimensions
		candidate := i.data[start : start+i.dimensions]
		var score float32
		for dimension, queryValue := range needle {
			score += queryValue * candidate[dimension]
		}
		hits = append(hits, domain.SemanticSearchHit{AssetID: assetID, Score: score})

		if onProgress != nil && time.Since(lastProgress) >= 120*time.Millisecond {
			onProgress(eligiblePosition+1, len(eligibleIDs))
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
	return i.ready, len(i.positions), fmt.Sprintf("%s/%s/%s", i.key.engine, i.key.modelID, i.key.version)
}

func (c *AppCommands) GetSemanticIndexStatus() SemanticIndexStatus {
	if c.semanticIndex == nil {
		return SemanticIndexStatus{State: "unavailable"}
	}
	return c.semanticIndex.Status()
}
