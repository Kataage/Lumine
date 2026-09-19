//go:build !portable

package main

import (
	"testing"

	"github.com/kataage/lumine/internal/infrastructure/storage"
)

func TestDistributionModeInstalled(t *testing.T) {
	if distributionMode != storage.ModeInstalled {
		t.Fatalf("distribution mode = %q, want installed", distributionMode)
	}
}
