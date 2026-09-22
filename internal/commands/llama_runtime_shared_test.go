package commands

import (
	"reflect"
	"testing"

	"github.com/kataage/lumine/internal/domain"
)

func TestSharedLlamaCapabilitiesAreExclusiveWithoutSemantic(t *testing.T) {
	got := sharedLlamaCapabilities()
	want := []domain.AICapability{
		domain.AICapabilityLightweightVision,
		domain.AICapabilityAdvancedVision,
		domain.AICapabilityPromptEngine,
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("shared llama capabilities = %v, want %v", got, want)
	}
	for _, capability := range got {
		if capability == domain.AICapabilitySemanticSearch {
			t.Fatal("Semantic Search must not be part of the shared llama sidecar residency group")
		}
	}
}
