package commands

import (
	"context"
	"errors"
	"testing"
	"time"
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
