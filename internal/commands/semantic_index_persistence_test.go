package commands

import (
	"context"
	"crypto/sha256"
	"encoding/binary"
	"fmt"
	"io"
	"math"
	"os"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/infrastructure/db"
)

const (
	testSemanticEngine  = "test-semantic-engine"
	testSemanticModel   = "test-semantic-model"
	testSemanticVersion = "1"
)

func seedReadySemanticEmbedding(
	t testing.TB,
	cmd *AppCommands,
	libraryID int64,
	name string,
	vector []float32,
) int64 {
	t.Helper()
	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, err := assetRepo.Create(makeAsset(libraryID, "/tmp/semantic-index", name))
	if err != nil {
		t.Fatalf("create semantic test asset: %v", err)
	}
	if err := cmd.semanticRepo.Upsert(
		assetID,
		testSemanticEngine,
		testSemanticModel,
		testSemanticVersion,
		vector,
	); err != nil {
		t.Fatalf("upsert semantic embedding: %v", err)
	}
	if _, err := cmd.db.Exec(`
		INSERT INTO ai_asset_analysis (
			asset_id, capability, state, engine, model_id, model_version, result_json
		) VALUES (?, 'semantic_search', 'ready', ?, ?, ?, '{}')
	`, assetID, testSemanticEngine, testSemanticModel, testSemanticVersion); err != nil {
		t.Fatalf("mark semantic analysis ready: %v", err)
	}
	return assetID
}

func releaseSemanticTestIndex(index *semanticMemoryIndex) {
	if index == nil {
		return
	}
	index.mu.Lock()
	index.releaseMappingLocked()
	index.mu.Unlock()
}

func buildPersistentSemanticFixture(t *testing.T) (*AppCommands, string, semanticIndexKey, []int64) {
	t.Helper()
	cmd := setupCommands(t)
	libraryRoot := t.TempDir()
	library := createTestLibrary(t, cmd, "Semantic persistence", libraryRoot)
	ids := []int64{
		seedReadySemanticEmbedding(t, cmd, library.ID, "a.png", []float32{1, 0}),
		seedReadySemanticEmbedding(t, cmd, library.ID, "b.png", []float32{0.8, 0.6}),
		seedReadySemanticEmbedding(t, cmd, library.ID, "c.png", []float32{-1, 0}),
	}
	root := t.TempDir()
	key := semanticKey(testSemanticEngine, testSemanticModel, testSemanticVersion)

	index := newSemanticMemoryIndex()
	index.SetStorageRoot(root)
	index.Prepare(key.engine, key.modelID, key.version)
	if err := index.Warm(context.Background(), cmd.semanticRepo, key.engine, key.modelID, key.version); err != nil {
		t.Fatalf("initial SQLite warm: %v", err)
	}
	snapshot, err := writeSemanticPersistentSnapshot(context.Background(), root, cmd.semanticRepo, key)
	if err != nil {
		t.Fatalf("write semantic persistent snapshot: %v", err)
	}
	if snapshot.unmap != nil {
		if err := snapshot.unmap(); err != nil {
			t.Fatalf("unmap written semantic snapshot: %v", err)
		}
	}
	return cmd, root, key, ids
}

func TestSemanticPersistentSnapshotRoundTripExactSearch(t *testing.T) {
	cmd, root, key, ids := buildPersistentSemanticFixture(t)

	index := newSemanticMemoryIndex()
	index.SetStorageRoot(root)
	index.Prepare(key.engine, key.modelID, key.version)
	t.Cleanup(func() { releaseSemanticTestIndex(index) })

	if err := index.Warm(context.Background(), cmd.semanticRepo, key.engine, key.modelID, key.version); err != nil {
		t.Fatalf("persistent warm: %v", err)
	}
	status := index.Status()
	if !status.Persistent {
		t.Fatalf("index did not open persistent mmap snapshot: %+v", status)
	}
	if status.LoadedCount != len(ids) || status.Dimensions != 2 {
		t.Fatalf("unexpected persistent status: %+v", status)
	}

	result, err := index.Search(
		context.Background(),
		[]float32{1, 0},
		ids,
		0,
		10,
		nil,
	)
	if err != nil {
		t.Fatalf("search persistent index: %v", err)
	}
	want := []int64{ids[0], ids[1], ids[2]}
	if len(result.RankedHits) != len(want) {
		t.Fatalf("ranked hits = %d, want %d", len(result.RankedHits), len(want))
	}
	for position, assetID := range want {
		if result.RankedHits[position].AssetID != assetID {
			t.Fatalf("rank %d = %d, want %d", position, result.RankedHits[position].AssetID, assetID)
		}
	}
}

func TestSemanticPersistentSnapshotCorruptionFallsBackToSQLite(t *testing.T) {
	cmd, root, key, ids := buildPersistentSemanticFixture(t)
	generation, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	path := semanticSnapshotPath(root, key, generation)

	file, err := os.OpenFile(path, os.O_RDWR, 0)
	if err != nil {
		t.Fatal(err)
	}
	vectorOffset := alignSemanticSnapshot(semanticSnapshotHeaderSize + len(ids)*8)
	var one [1]byte
	if _, err := file.ReadAt(one[:], int64(vectorOffset)); err != nil {
		_ = file.Close()
		t.Fatal(err)
	}
	one[0] ^= 0xff
	if _, err := file.WriteAt(one[:], int64(vectorOffset)); err != nil {
		_ = file.Close()
		t.Fatal(err)
	}
	if err := file.Close(); err != nil {
		t.Fatal(err)
	}

	index := newSemanticMemoryIndex()
	index.SetStorageRoot(root)
	index.Prepare(key.engine, key.modelID, key.version)
	if err := index.Warm(context.Background(), cmd.semanticRepo, key.engine, key.modelID, key.version); err != nil {
		t.Fatalf("corrupt snapshot should rebuild from SQLite: %v", err)
	}
	defer releaseSemanticTestIndex(index)

	status := index.Status()
	if status.Persistent {
		t.Fatalf("corrupt snapshot should not remain mapped: %+v", status)
	}
	if status.LoadedCount != len(ids) {
		t.Fatalf("rebuilt count = %d, want %d", status.LoadedCount, len(ids))
	}
	if _, err := os.Stat(path); !os.IsNotExist(err) {
		t.Fatalf("corrupt snapshot was not removed: %v", err)
	}
}

func TestSemanticPersistentSnapshotGenerationChangeRejectsStaleData(t *testing.T) {
	cmd, root, key, ids := buildPersistentSemanticFixture(t)
	libraries, err := cmd.ListLibraries()
	if err != nil || len(libraries) != 1 {
		t.Fatalf("list test library: %v, count=%d", err, len(libraries))
	}
	newID := seedReadySemanticEmbedding(t, cmd, libraries[0].ID, "new.png", []float32{0, 1})

	index := newSemanticMemoryIndex()
	index.SetStorageRoot(root)
	index.Prepare(key.engine, key.modelID, key.version)
	if err := index.Warm(context.Background(), cmd.semanticRepo, key.engine, key.modelID, key.version); err != nil {
		t.Fatalf("stale snapshot rebuild: %v", err)
	}
	defer releaseSemanticTestIndex(index)

	status := index.Status()
	if status.Persistent {
		t.Fatalf("old generation snapshot must not be accepted: %+v", status)
	}
	if status.LoadedCount != len(ids)+1 {
		t.Fatalf("rebuilt count = %d, want %d", status.LoadedCount, len(ids)+1)
	}

	eligible := append(append([]int64(nil), ids...), newID)
	result, err := index.Search(context.Background(), []float32{0, 1}, eligible, 0, 10, nil)
	if err != nil {
		t.Fatal(err)
	}
	if len(result.RankedHits) == 0 || result.RankedHits[0].AssetID != newID {
		t.Fatalf("new generation embedding is not searchable: %+v", result.RankedHits)
	}
}

func TestCleanupSemanticSnapshotsPreservesOtherModels(t *testing.T) {
	root := t.TempDir()
	key := semanticKey("engine", "model", "1")
	otherKey := semanticKey("engine", "other-model", "1")
	oldPath := semanticSnapshotPath(root, key, 10)
	keepPath := semanticSnapshotPath(root, key, 11)
	otherPath := semanticSnapshotPath(root, otherKey, 10)
	for _, path := range []string{oldPath, keepPath, otherPath} {
		if err := os.WriteFile(path, []byte("snapshot"), 0644); err != nil {
			t.Fatal(err)
		}
	}

	cleanupSemanticSnapshots(root, key, keepPath)

	if _, err := os.Stat(oldPath); !os.IsNotExist(err) {
		t.Fatalf("old same-model snapshot was not removed: %v", err)
	}
	if _, err := os.Stat(keepPath); err != nil {
		t.Fatalf("current snapshot was removed: %v", err)
	}
	if _, err := os.Stat(otherPath); err != nil {
		t.Fatalf("other-model snapshot was removed: %v", err)
	}
}

func TestSemanticPersistentSnapshotPathIsGenerationAddressed(t *testing.T) {
	root := t.TempDir()
	key := semanticKey("engine", "model", "1")
	first := semanticSnapshotPath(root, key, 10)
	second := semanticSnapshotPath(root, key, 11)
	otherModel := semanticSnapshotPath(root, semanticKey("engine", "other", "1"), 10)
	if first == second {
		t.Fatal("snapshot path did not change with DB generation")
	}
	if first == otherModel {
		t.Fatal("snapshot path did not include model provenance")
	}
}

func TestSemanticPersistWorkerWaitsUntilIncrementalEmbeddingIsReady(t *testing.T) {
	cmd, root, key, ids := buildPersistentSemanticFixture(t)

	index := newSemanticMemoryIndex()
	index.persistDebounce = 10 * time.Millisecond
	index.SetStorageRoot(root)
	index.Prepare(key.engine, key.modelID, key.version)
	if err := index.Warm(context.Background(), cmd.semanticRepo, key.engine, key.modelID, key.version); err != nil {
		t.Fatalf("open initial persistent snapshot: %v", err)
	}
	defer releaseSemanticTestIndex(index)
	if !index.Status().Persistent {
		t.Fatal("initial snapshot was not memory-mapped")
	}

	libraries, err := cmd.ListLibraries()
	if err != nil || len(libraries) != 1 {
		t.Fatalf("list test library: %v count=%d", err, len(libraries))
	}
	assetRepo := db.NewAssetRepo(cmd.db)
	newID, err := assetRepo.Create(makeAsset(libraries[0].ID, "/tmp/semantic-index", "incremental.png"))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := cmd.db.Exec(`
		INSERT INTO ai_asset_analysis (
			asset_id, capability, state, engine, model_id, model_version, result_json
		) VALUES (?, 'semantic_search', 'running', ?, ?, ?, '{}')
	`, newID, key.engine, key.modelID, key.version); err != nil {
		t.Fatal(err)
	}

	generationBeforeEmbedding, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	vector := []float32{0, 1}
	if err := cmd.semanticRepo.Upsert(newID, key.engine, key.modelID, key.version, vector); err != nil {
		t.Fatal(err)
	}
	generationWhileRunning, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if generationWhileRunning != generationBeforeEmbedding {
		t.Fatalf("non-ready embedding unexpectedly invalidated snapshot: before=%d after=%d", generationBeforeEmbedding, generationWhileRunning)
	}

	index.Upsert(newID, key.engine, key.modelID, key.version, vector)
	if !index.MarkPersistDirty(key.engine, key.modelID, key.version) {
		t.Fatal("incremental embedding did not start persistence worker")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	errCh := make(chan error, 1)
	go func() {
		errCh <- index.PersistWhenStable(ctx, cmd.semanticRepo, key.engine, key.modelID, key.version)
	}()

	// Let at least one persistence pass observe the embedding while its
	// analysis row is still non-ready. The overlay must survive that pass.
	time.Sleep(35 * time.Millisecond)
	index.mu.RLock()
	_, overlayStillPresent := index.overlay[newID]
	index.mu.RUnlock()
	if !overlayStillPresent {
		t.Fatal("non-ready incremental overlay was dropped before DB ready transition")
	}

	if _, err := cmd.db.Exec(
		"UPDATE ai_asset_analysis SET state = 'ready' WHERE asset_id = ? AND capability = 'semantic_search'",
		newID,
	); err != nil {
		t.Fatal(err)
	}

	select {
	case err := <-errCh:
		if err != nil {
			t.Fatalf("persist incremental embedding: %v", err)
		}
	case <-ctx.Done():
		t.Fatalf("persist worker did not converge after ready transition: %v", ctx.Err())
	}

	status := index.Status()
	if !status.Persistent {
		t.Fatalf("final incremental index is not persistent: %+v", status)
	}
	if status.OverlayCount != 0 {
		t.Fatalf("represented overlay was not compacted: %+v", status)
	}
	if status.LoadedCount != len(ids)+1 {
		t.Fatalf("persistent count = %d, want %d", status.LoadedCount, len(ids)+1)
	}

	currentGeneration, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(semanticSnapshotPath(root, key, currentGeneration)); err != nil {
		t.Fatalf("current generation snapshot missing: %v", err)
	}

	eligible := append(append([]int64(nil), ids...), newID)
	result, err := index.Search(context.Background(), []float32{0, 1}, eligible, 0, 10, nil)
	if err != nil {
		t.Fatal(err)
	}
	if len(result.RankedHits) == 0 || result.RankedHits[0].AssetID != newID {
		t.Fatalf("incremental embedding is not searchable after compaction: %+v", result.RankedHits)
	}
}

func TestSemanticIndexMarkPersistDirtyAfterIncrementalUpsert(t *testing.T) {
	index := newSemanticMemoryIndex()
	index.SetStorageRoot(t.TempDir())
	index.key = semanticKey(testSemanticEngine, testSemanticModel, testSemanticVersion)
	index.ready = true
	index.mappedReadOnly = true
	index.positions = map[int64]int{1: 0}
	index.data = []float32{1, 0}
	index.dimensions = 2
	index.overlay = make(map[int64][]float32)

	if index.MarkPersistDirty(testSemanticEngine, testSemanticModel, testSemanticVersion) {
		t.Fatal("clean mapped snapshot should not start a persistence worker")
	}
	index.Upsert(2, testSemanticEngine, testSemanticModel, testSemanticVersion, []float32{0, 1})
	if !index.MarkPersistDirty(testSemanticEngine, testSemanticModel, testSemanticVersion) {
		t.Fatal("incremental overlay did not start a new persistence worker")
	}
	index.cancelPersistWorkerStart()
}

func writeSyntheticSemanticSnapshot(
	tb testing.TB,
	root string,
	key semanticIndexKey,
	generation uint64,
	count int,
	dimensions int,
) (string, int64) {
	tb.Helper()
	if err := os.MkdirAll(root, 0755); err != nil {
		tb.Fatal(err)
	}
	vectorsOffset := alignSemanticSnapshot(semanticSnapshotHeaderSize + count*8)
	header := semanticSnapshotHeader{
		key:           key,
		generation:    generation,
		count:         count,
		dimensions:    dimensions,
		vectorsOffset: vectorsOffset,
	}
	size, err := semanticSnapshotExpectedSize(header)
	if err != nil {
		tb.Fatal(err)
	}
	path := semanticSnapshotPath(root, key, generation)
	file, err := os.Create(path)
	if err != nil {
		tb.Fatal(err)
	}
	defer file.Close()
	if err := file.Truncate(int64(size)); err != nil {
		tb.Fatal(err)
	}

	vector := make([]byte, dimensions*4)
	if dimensions > 0 {
		binary.LittleEndian.PutUint32(vector[:4], math.Float32bits(1))
	}
	for position := 0; position < count; position++ {
		var id [8]byte
		binary.LittleEndian.PutUint64(id[:], uint64(position+1))
		if _, err := file.WriteAt(id[:], int64(semanticSnapshotHeaderSize+position*8)); err != nil {
			tb.Fatal(err)
		}
		if _, err := file.WriteAt(vector, int64(vectorsOffset+position*dimensions*4)); err != nil {
			tb.Fatal(err)
		}
	}
	if _, err := file.Seek(semanticSnapshotHeaderSize, io.SeekStart); err != nil {
		tb.Fatal(err)
	}
	hasher := sha256.New()
	if _, err := io.Copy(hasher, file); err != nil {
		tb.Fatal(err)
	}
	copy(header.checksum[:], hasher.Sum(nil))
	encoded, err := encodeSemanticSnapshotHeader(header)
	if err != nil {
		tb.Fatal(err)
	}
	if _, err := file.WriteAt(encoded, 0); err != nil {
		tb.Fatal(err)
	}
	if err := file.Sync(); err != nil {
		tb.Fatal(err)
	}
	return path, int64(size)
}

func benchmarkSemanticPersistentSnapshotOpen(b *testing.B, count int) {
	const dimensions = 768
	root := b.TempDir()
	key := semanticKey(testSemanticEngine, testSemanticModel, testSemanticVersion)
	_, size := writeSyntheticSemanticSnapshot(b, root, key, 42, count, dimensions)
	b.ReportAllocs()
	b.SetBytes(size)
	b.ReportMetric(float64(size)/(1024*1024), "MiB/index")
	b.ResetTimer()
	for iteration := 0; iteration < b.N; iteration++ {
		snapshot, err := openSemanticPersistentSnapshot(root, key, 42)
		if err != nil {
			b.Fatal(err)
		}
		if snapshot.count != count || snapshot.dimensions != dimensions {
			b.Fatalf("snapshot metadata mismatch: count=%d dims=%d", snapshot.count, snapshot.dimensions)
		}
		if err := snapshot.unmap(); err != nil {
			b.Fatal(err)
		}
	}
}

func BenchmarkSemanticPersistentSnapshotOpen20K(b *testing.B) {
	benchmarkSemanticPersistentSnapshotOpen(b, 20_000)
}

func BenchmarkSemanticPersistentSnapshotOpen100K(b *testing.B) {
	benchmarkSemanticPersistentSnapshotOpen(b, 100_000)
}

func TestSemanticIndexGenerationIgnoresNonReadyLifecycle(t *testing.T) {
	cmd := setupCommands(t)
	library := createTestLibrary(t, cmd, "Semantic generation lifecycle", t.TempDir())
	assetRepo := db.NewAssetRepo(cmd.db)
	assetID, err := assetRepo.Create(makeAsset(library.ID, "/tmp/semantic-index", "lifecycle.png"))
	if err != nil {
		t.Fatal(err)
	}

	before, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if _, err := cmd.db.Exec(`
		INSERT INTO ai_asset_analysis (
			asset_id, capability, state, engine, model_id, model_version, result_json
		) VALUES (?, 'semantic_search', 'queued', ?, ?, ?, '{}')
	`, assetID, testSemanticEngine, testSemanticModel, testSemanticVersion); err != nil {
		t.Fatal(err)
	}
	if _, err := cmd.db.Exec(
		"UPDATE ai_asset_analysis SET state = 'running' WHERE asset_id = ? AND capability = 'semantic_search'",
		assetID,
	); err != nil {
		t.Fatal(err)
	}
	if err := cmd.semanticRepo.Upsert(
		assetID,
		testSemanticEngine,
		testSemanticModel,
		testSemanticVersion,
		[]float32{1, 0},
	); err != nil {
		t.Fatal(err)
	}
	if _, err := cmd.db.Exec(
		"UPDATE ai_asset_analysis SET state = 'failed' WHERE asset_id = ? AND capability = 'semantic_search'",
		assetID,
	); err != nil {
		t.Fatal(err)
	}

	nonReadyGeneration, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if nonReadyGeneration != before {
		t.Fatalf("non-ready lifecycle invalidated semantic snapshot: before=%d after=%d", before, nonReadyGeneration)
	}

	if _, err := cmd.db.Exec(
		"UPDATE ai_asset_analysis SET state = 'ready' WHERE asset_id = ? AND capability = 'semantic_search'",
		assetID,
	); err != nil {
		t.Fatal(err)
	}
	readyGeneration, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if readyGeneration <= nonReadyGeneration {
		t.Fatalf("ready transition did not invalidate semantic snapshot: before=%d after=%d", nonReadyGeneration, readyGeneration)
	}
}

func TestSemanticIndexGenerationAdvancesForReadyChanges(t *testing.T) {
	cmd := setupCommands(t)
	library := createTestLibrary(t, cmd, "Semantic generation", t.TempDir())
	before, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	seedReadySemanticEmbedding(t, cmd, library.ID, "generation.png", []float32{1, 0})
	after, err := cmd.semanticRepo.SemanticIndexGeneration(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if after <= before {
		t.Fatalf("semantic DB generation did not advance: before=%d after=%d", before, after)
	}
}

func TestSemanticPersistentSnapshotFileSizeFormula(t *testing.T) {
	header := semanticSnapshotHeader{
		key:           semanticKey("e", "m", "v"),
		generation:    1,
		count:         100_000,
		dimensions:    768,
		vectorsOffset: alignSemanticSnapshot(semanticSnapshotHeaderSize + 100_000*8),
	}
	size, err := semanticSnapshotExpectedSize(header)
	if err != nil {
		t.Fatal(err)
	}
	wantVectorBytes := 100_000 * 768 * 4
	if size < wantVectorBytes {
		t.Fatalf("snapshot size %d is smaller than vector payload %d", size, wantVectorBytes)
	}
	t.Logf("100k x 768 exact snapshot disk footprint: %.1f MiB", float64(size)/(1024*1024))
}

func Example_semanticSnapshotPath() {
	fmt.Println("generation-addressed snapshots avoid replacing a live mmap")
	// Output: generation-addressed snapshots avoid replacing a live mmap
}
