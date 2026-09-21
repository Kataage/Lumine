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
