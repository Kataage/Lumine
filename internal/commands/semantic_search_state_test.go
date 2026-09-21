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


func TestSemanticSearchCoverageUpdatePreservesFrozenRanking(t *testing.T) {
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
	)

	state.mu.Lock()
	defer state.mu.Unlock()
	if len(state.sessions) != 1 {
		t.Fatalf("expired sessions were not pruned: count=%d", len(state.sessions))
	}
}
