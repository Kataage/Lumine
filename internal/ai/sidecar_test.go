package ai

import (
	"context"
	"os"
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


func TestSidecarHelperProcess(t *testing.T) {
	if os.Getenv("LUMINE_SIDECAR_HELPER") != "1" {
		return
	}
	for {
		time.Sleep(time.Hour)
	}
}

func TestSidecarStopTerminatesRunningProcess(t *testing.T) {
	sidecar := NewSidecarProcess()
	if err := sidecar.Start(
		context.Background(),
		os.Args[0],
		[]string{"-test.run=^TestSidecarHelperProcess$"},
		[]string{"LUMINE_SIDECAR_HELPER=1"},
	); err != nil {
		t.Fatalf("start helper sidecar: %v", err)
	}
	if !sidecar.Running() {
		t.Fatal("helper sidecar should be running")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := sidecar.Stop(ctx); err != nil {
		t.Fatalf("stop helper sidecar: %v", err)
	}

	deadline := time.Now().Add(time.Second)
	for sidecar.Running() && time.Now().Before(deadline) {
		time.Sleep(10 * time.Millisecond)
	}
	if sidecar.Running() {
		t.Fatal("sidecar remained running after Stop")
	}
}
