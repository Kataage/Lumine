package ai

import (
	"context"
	"testing"
	"time"
)

func TestSidecarStartFailureIsReported(t *testing.T) {
	sidecar := NewSidecarProcess()
	err := sidecar.Start(context.Background(), "lumine-definitely-missing-sidecar-binary", nil, nil)
	if err == nil {
		t.Fatal("expected missing sidecar executable to fail")
	}
	if sidecar.Running() {
		t.Fatal("failed sidecar must not be marked running")
	}
	if sidecar.LastError() == nil {
		t.Fatal("start failure should be retained for diagnostics")
	}
}

func TestSidecarStopIsNoopWhenNotRunning(t *testing.T) {
	sidecar := NewSidecarProcess()
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if err := sidecar.Stop(ctx); err != nil {
		t.Fatalf("Stop on idle sidecar: %v", err)
	}
}
