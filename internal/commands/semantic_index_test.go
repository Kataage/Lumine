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
	index.vectors = vectors
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

func BenchmarkSemanticMemoryIndexSearch20K(b *testing.B) {
	const (
		count = 20_000
		dims  = 768
	)
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
