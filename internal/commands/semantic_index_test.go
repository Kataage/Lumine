package commands

import (
	"context"
	"fmt"
	"testing"
)

func readySemanticIndex(vectors map[int64][]float32) *semanticMemoryIndex {
	index := newSemanticMemoryIndex()
	index.key = semanticKey("engine", "model", "1")
	index.ready = true
	index.stage = "ready"
	for assetID, vector := range vectors {
		index.Upsert(assetID, "engine", "model", "1", vector)
	}
	index.total = len(index.positions)
	index.loaded = len(index.positions)
	return index
}

func TestSemanticMemoryIndexSearchPreservesExactRanking(t *testing.T) {
	index := readySemanticIndex(map[int64][]float32{
		1: {1, 0},
		2: {0.8, 0.6},
		3: {-1, 0},
		4: {0, 1},
	})

	result, err := index.Search(
		context.Background(),
		[]float32{1, 0},
		[]int64{1, 2, 3, 4},
		0,
		10,
		nil,
	)
	if err != nil {
		t.Fatal(err)
	}
	if result.TotalCount != 4 || len(result.RankedHits) != 4 {
		t.Fatalf("unexpected result size: %+v", result)
	}
	want := []int64{1, 2, 4, 3}
	for position, id := range want {
		if result.RankedHits[position].AssetID != id {
			t.Fatalf("rank %d = %d, want %d; hits=%+v", position, result.RankedHits[position].AssetID, id, result.RankedHits)
		}
	}
}

func TestSemanticMemoryIndexSearchRespectsEligibleIDsAndPaging(t *testing.T) {
	index := readySemanticIndex(map[int64][]float32{
		10: {1, 0},
		20: {0.9, 0.1},
		30: {0.8, 0.2},
		40: {0.7, 0.3},
	})

	result, err := index.Search(
		context.Background(),
		[]float32{1, 0},
		[]int64{10, 30, 40},
		1,
		1,
		nil,
	)
	if err != nil {
		t.Fatal(err)
	}
	if result.TotalCount != 3 || len(result.Hits) != 1 || result.Hits[0].AssetID != 30 {
		t.Fatalf("unexpected filtered page: %+v", result)
	}
}

func TestSemanticMemoryIndexUpsertUpdatesReadyIndex(t *testing.T) {
	index := readySemanticIndex(map[int64][]float32{
		1: {1, 0},
	})
	index.Upsert(2, "engine", "model", "1", []float32{0.95, 0.05})
	index.Upsert(3, "other", "model", "1", []float32{1, 0})

	result, err := index.Search(
		context.Background(),
		[]float32{1, 0},
		[]int64{1, 2, 3},
		0,
		10,
		nil,
	)
	if err != nil {
		t.Fatal(err)
	}
	if result.TotalCount != 2 {
		t.Fatalf("total = %d, want 2", result.TotalCount)
	}
	if result.RankedHits[0].AssetID != 1 || result.RankedHits[1].AssetID != 2 {
		t.Fatalf("unexpected ranking after upsert: %+v", result.RankedHits)
	}
}

func benchmarkSemanticMemoryIndexSearch(b *testing.B, count int) {
	const dims = 768
	vectors := make(map[int64][]float32, count)
	eligible := make([]int64, count)
	for asset := 0; asset < count; asset++ {
		vector := make([]float32, dims)
		vector[asset%dims] = 1
		id := int64(asset + 1)
		vectors[id] = vector
		eligible[asset] = id
	}
	index := readySemanticIndex(vectors)
	query := make([]float32, dims)
	query[0] = 1

	b.ReportAllocs()
	b.ResetTimer()
	for iteration := 0; iteration < b.N; iteration++ {
		result, err := index.Search(context.Background(), query, eligible, 0, 200, nil)
		if err != nil {
			b.Fatal(err)
		}
		if len(result.Hits) != 200 {
			b.Fatal(fmt.Sprintf("hits = %d", len(result.Hits)))
		}
	}
}

func BenchmarkSemanticMemoryIndexSearch20K(b *testing.B) {
	benchmarkSemanticMemoryIndexSearch(b, 20_000)
}

func BenchmarkSemanticMemoryIndexSearch100K(b *testing.B) {
	benchmarkSemanticMemoryIndexSearch(b, 100_000)
}


func TestSemanticMemoryIndexPrepareBuffersEarlyUpserts(t *testing.T) {
	index := newSemanticMemoryIndex()
	index.Prepare("engine", "model", "1")
	index.Upsert(42, "engine", "model", "1", []float32{1, 0})

	index.mu.RLock()
	defer index.mu.RUnlock()
	vector, ok := index.pending[42]
	if !ok {
		t.Fatal("embedding completed before warm start was dropped")
	}
	if len(vector) != 2 {
		t.Fatalf("pending vector length = %d, want 2", len(vector))
	}
}


func TestSemanticMemoryIndexPrepareInvalidatesInFlightWarm(t *testing.T) {
	index := newSemanticMemoryIndex()
	oldKey := semanticKey("engine", "old-model", "1")
	index.key = oldKey
	index.generation = 7
	index.warming = true
	index.wait = make(chan struct{})
	wait := index.wait

	index.Prepare("engine", "new-model", "2")

	select {
	case <-wait:
	default:
		t.Fatal("model switch did not wake old warm waiters")
	}

	index.mu.RLock()
	defer index.mu.RUnlock()
	if index.warming {
		t.Fatal("index remained warming after model switch")
	}
	if index.wait != nil {
		t.Fatal("stale warm wait channel was retained")
	}
	if index.key != semanticKey("engine", "new-model", "2") {
		t.Fatalf("index key = %+v, want new model", index.key)
	}
	if index.generation != 8 {
		t.Fatalf("generation = %d, want 8", index.generation)
	}
	if index.ready {
		t.Fatal("new model index must not inherit ready state")
	}
}

func TestSemanticMemoryIndexStaleWarmCannotPublishErrorState(t *testing.T) {
	index := newSemanticMemoryIndex()
	oldKey := semanticKey("engine", "old-model", "1")
	index.key = oldKey
	index.generation = 3
	index.warming = true
	index.wait = make(chan struct{})
	oldWait := index.wait

	index.Prepare("engine", "new-model", "2")
	if index.finishWarm(oldWait, oldKey, 3, fmt.Errorf("old warm failed")) {
		t.Fatal("superseded warm unexpectedly finalized current index")
	}

	status := index.Status()
	if status.State != "idle" || status.ModelID != "new-model" || status.Error != "" {
		t.Fatalf("stale warm mutated new model status: %+v", status)
	}
}
