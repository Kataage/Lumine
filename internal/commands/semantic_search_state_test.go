package commands

import (
	"context"
	"errors"
	"reflect"
	"testing"
	"time"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
)

func TestSemanticSearchCancelBeforeBeginCancelsArrivingRequest(t *testing.T) {
	state := newSemanticSearchState()
	state.cancel("request-before-begin")

	ctx, cancel := state.begin("request-before-begin", context.Background())
	defer cancel()

	select {
	case <-ctx.Done():
		if !errors.Is(ctx.Err(), context.Canceled) {
			t.Fatalf("context error = %v, want canceled", ctx.Err())
		}
	case <-time.After(time.Second):
		t.Fatal("pre-cancelled semantic request was allowed to start")
	}
}

func TestSemanticSearchCancelStopsActiveRequest(t *testing.T) {
	state := newSemanticSearchState()
	ctx, cancel := state.begin("active-request", context.Background())
	defer cancel()

	state.cancel("active-request")

	select {
	case <-ctx.Done():
		if !errors.Is(ctx.Err(), context.Canceled) {
			t.Fatalf("context error = %v, want canceled", ctx.Err())
		}
	case <-time.After(time.Second):
		t.Fatal("active semantic request was not cancelled")
	}
}

func TestSemanticSearchFinishDoesNotLeaveActiveCancel(t *testing.T) {
	state := newSemanticSearchState()
	_, cancel := state.begin("finished-request", context.Background())
	cancel()
	state.finish("finished-request")

	state.mu.Lock()
	defer state.mu.Unlock()
	if _, ok := state.cancels["finished-request"]; ok {
		t.Fatal("finished semantic request remained in active cancel map")
	}
}


func TestSemanticSearchCoverageUpdateDoesNotMutateRanking(t *testing.T) {
	state := newSemanticSearchState()
	hits := []domain.SemanticSearchHit{
		{AssetID: 3, Score: 0.9},
		{AssetID: 1, Score: 0.8},
	}
	query := db.SemanticSearchQuery{
		LibraryID:    7,
		Engine:       "siglip2-onnx",
		ModelID:      "siglip2",
		ModelVersion: "revision-1",
	}
	sessionID := state.store(
		hits,
		len(hits),
		1,
		10,
		db.SemanticCoverageStateCounts{Queued: 8, Running: 1},
		query,
		[]float32{1, 0},
	)

	updated, ok := state.updateCoverage(
		sessionID,
		4,
		db.SemanticCoverageStateCounts{Queued: 4, Running: 2},
	)
	if !ok {
		t.Fatal("session disappeared during coverage update")
	}
	if updated.coverageReady != 4 ||
		updated.coverageStates.Queued != 4 ||
		updated.coverageStates.Running != 2 {
		t.Fatalf("coverage was not updated: %+v", updated)
	}
	if len(updated.hits) != 2 ||
		updated.hits[0].AssetID != 3 ||
		updated.hits[1].AssetID != 1 ||
		updated.hits[0].Score != 0.9 ||
		updated.hits[1].Score != 0.8 {
		t.Fatalf("coverage refresh changed frozen ranking: %+v", updated.hits)
	}
	if !reflect.DeepEqual(updated.coverageQuery, query) {
		t.Fatalf("coverage query changed: got %+v want %+v", updated.coverageQuery, query)
	}
}


func TestSimilarRankingSessionIsStableAcrossPages(t *testing.T) {
	state := newSemanticSearchState()
	hits := make([]domain.SemanticSearchHit, 450)
	for i := range hits {
		hits[i] = domain.SemanticSearchHit{
			AssetID: int64(1000 - i),
			Score:   1 - float32(i)/1000,
		}
	}
	sessionID := state.store(
		hits,
		len(hits),
		len(hits),
		len(hits),
		db.SemanticCoverageStateCounts{},
		db.SemanticSearchQuery{},
		nil,
	)

	session, ok := state.get(sessionID)
	if !ok {
		t.Fatal("similar ranking session missing")
	}
	page1 := append([]domain.SemanticSearchHit(nil), session.hits[:200]...)
	page2 := append([]domain.SemanticSearchHit(nil), session.hits[200:400]...)
	page3 := append([]domain.SemanticSearchHit(nil), session.hits[400:]...)

	if page1[0].AssetID != hits[0].AssetID ||
		page2[0].AssetID != hits[200].AssetID ||
		page3[0].AssetID != hits[400].AssetID {
		t.Fatalf("session paging changed ranking: p1=%d p2=%d p3=%d",
			page1[0].AssetID, page2[0].AssetID, page3[0].AssetID)
	}

	// Simulate newer background progress. Similar-image sessions deliberately do
	// not carry a coverageQuery, so paging remains the frozen first-request
	// ranking rather than silently incorporating new candidates.
	if session.coverageQuery.Engine != "" ||
		session.coverageQuery.ModelID != "" ||
		session.coverageQuery.ModelVersion != "" {
		t.Fatalf("similar session unexpectedly has live coverage query: %+v", session.coverageQuery)
	}
}

func TestSemanticSearchSessionsStayBoundedAndExpire(t *testing.T) {
	state := newSemanticSearchState()
	for i := 0; i < semanticSearchSessionMax+3; i++ {
		state.store(
			[]domain.SemanticSearchHit{{AssetID: int64(i + 1), Score: 1}},
			1,
			1,
			1,
			db.SemanticCoverageStateCounts{},
			db.SemanticSearchQuery{},
			nil,
		)
	}
	state.mu.Lock()
	if len(state.sessions) != semanticSearchSessionMax {
		got := len(state.sessions)
		state.mu.Unlock()
		t.Fatalf("session count = %d, want %d", got, semanticSearchSessionMax)
	}
	for id, session := range state.sessions {
		session.createdAt = time.Now().Add(-semanticSearchSessionTTL - time.Second)
		state.sessions[id] = session
	}
	state.mu.Unlock()

	state.store(
		[]domain.SemanticSearchHit{{AssetID: 999, Score: 1}},
		1,
		1,
		1,
		db.SemanticCoverageStateCounts{},
		db.SemanticSearchQuery{},
		nil,
	)

	state.mu.Lock()
	defer state.mu.Unlock()
	if len(state.sessions) != 1 {
		t.Fatalf("expired sessions were not pruned: count=%d", len(state.sessions))
	}
}


func TestSemanticSearchLiveMergeAddsAndReranksNewHits(t *testing.T) {
	state := newSemanticSearchState()
	sessionID := state.store(
		[]domain.SemanticSearchHit{
			{AssetID: 10, Score: 0.8},
			{AssetID: 20, Score: 0.4},
		},
		2,
		2,
		100,
		db.SemanticCoverageStateCounts{},
		db.SemanticSearchQuery{Engine: "engine", ModelID: "model", ModelVersion: "1"},
		[]float32{1, 0},
	)

	updated, ok := state.mergeLiveHits(
		sessionID,
		[]domain.SemanticSearchHit{
			{AssetID: 30, Score: 0.9},
			{AssetID: 40, Score: 0.4},
		},
		4,
		db.SemanticCoverageStateCounts{Queued: 96},
	)
	if !ok {
		t.Fatal("live session disappeared")
	}
	if updated.total != 4 || updated.coverageReady != 4 {
		t.Fatalf("updated counts = total:%d ready:%d", updated.total, updated.coverageReady)
	}
	if updated.coverageStates.Queued != 96 {
		t.Fatalf("queued = %d, want 96", updated.coverageStates.Queued)
	}
	want := []int64{30, 10, 20, 40}
	if len(updated.hits) != len(want) {
		t.Fatalf("hits = %+v", updated.hits)
	}
	for index, assetID := range want {
		if updated.hits[index].AssetID != assetID {
			t.Fatalf("rank %d = %d, want %d; hits=%+v", index, updated.hits[index].AssetID, assetID, updated.hits)
		}
	}
}

func TestSemanticSearchLiveMergeDeduplicatesConcurrentRefresh(t *testing.T) {
	state := newSemanticSearchState()
	sessionID := state.store(
		[]domain.SemanticSearchHit{{AssetID: 1, Score: 0.7}},
		1,
		1,
		10,
		db.SemanticCoverageStateCounts{},
		db.SemanticSearchQuery{},
		[]float32{1},
	)

	for attempt := 0; attempt < 2; attempt++ {
		if _, ok := state.mergeLiveHits(
			sessionID,
			[]domain.SemanticSearchHit{{AssetID: 2, Score: 0.8}},
			2,
			db.SemanticCoverageStateCounts{},
		); !ok {
			t.Fatal("live session disappeared")
		}
	}

	session, ok := state.get(sessionID)
	if !ok {
		t.Fatal("session missing")
	}
	if session.total != 2 || len(session.hits) != 2 {
		t.Fatalf("duplicate live result was retained: total=%d hits=%+v", session.total, session.hits)
	}
	if session.hits[0].AssetID != 2 || session.hits[1].AssetID != 1 {
		t.Fatalf("unexpected live ranking: %+v", session.hits)
	}
}

func TestSemanticSearchStoreCopiesQueryVector(t *testing.T) {
	state := newSemanticSearchState()
	queryVector := []float32{1, 2, 3}
	sessionID := state.store(
		nil,
		0,
		0,
		10,
		db.SemanticCoverageStateCounts{},
		db.SemanticSearchQuery{},
		queryVector,
	)
	queryVector[0] = 99

	session, ok := state.get(sessionID)
	if !ok {
		t.Fatal("session missing")
	}
	if len(session.queryVector) != 3 || session.queryVector[0] != 1 {
		t.Fatalf("query vector alias leaked into session: %v", session.queryVector)
	}
}
