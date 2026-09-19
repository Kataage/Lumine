//go:build portable

package main

import (
	"testing"

	"github.com/kataage/lumine/internal/infrastructure/storage"
)

func TestDistributionModePortable(t *testing.T) {
	if distributionMode != storage.ModePortable {
		t.Fatalf("distribution mode = %q, want portable", distributionMode)
	}
}
