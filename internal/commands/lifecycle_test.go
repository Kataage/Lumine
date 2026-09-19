package commands

import (
	"context"
	"testing"
	"time"
)

func TestShutdownBackgroundCancelsWaitsAndRejectsNewWork(t *testing.T) {
	commands := &AppCommands{
		semanticSearchState: newSemanticSearchState(),
	}
	commands.SetContext(context.Background())

	started := make(chan struct{})
	finished := make(chan struct{})
	if !commands.startBackgroundTask(func(ctx context.Context) {
		close(started)
		<-ctx.Done()
		close(finished)
	}) {
		t.Fatal("background task was not started")
	}

	select {
	case <-started:
	case <-time.After(time.Second):
		t.Fatal("background task did not start")
	}

	shutdownCtx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	if err := commands.ShutdownBackground(shutdownCtx); err != nil {
		t.Fatalf("ShutdownBackground: %v", err)
	}

	select {
	case <-finished:
	default:
		t.Fatal("ShutdownBackground returned before background task finished")
	}
	if !commands.IsShuttingDown() {
		t.Fatal("commands should be marked shutting down")
	}
	if commands.startBackgroundTask(func(context.Context) {}) {
		t.Fatal("new background work must be rejected after shutdown starts")
	}

	// Shutdown is intentionally idempotent.
	if err := commands.ShutdownBackground(shutdownCtx); err != nil {
		t.Fatalf("second ShutdownBackground: %v", err)
	}
}
