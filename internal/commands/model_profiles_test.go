package commands

import (
	"testing"

	"github.com/kataage/lumine/internal/promptprofile"
)

func TestBuiltInModelProfilesHaveDistinctPromptKnowledge(t *testing.T) {
	illustrious, ok := promptprofile.BuiltIn(promptprofile.IllustriousID)
	if !ok {
		t.Fatal("Illustrious profile missing")
	}
	flux, ok := promptprofile.BuiltIn(promptprofile.FluxID)
	if !ok {
		t.Fatal("FLUX profile missing")
	}
	ilSpec := modelProfilePromptSpec(illustrious)
	fluxSpec := modelProfilePromptSpec(flux)
	if ilSpec["promptStyle"] == fluxSpec["promptStyle"] {
		t.Fatal("different target profiles must carry different prompt guidance")
	}
	if len(illustrious.QualityTags) == 0 {
		t.Fatal("Illustrious profile should expose quality tags")
	}
	if len(flux.QualityTags) != 0 {
		t.Fatal("generic FLUX profile should not inject tag-style quality boilerplate")
	}
}

func TestProfileFromInputNormalizesListsAndSyntax(t *testing.T) {
	profile := profileFromInput("custom-test", ModelProfileInput{
		Name:         " Test ",
		Family:       " custom ",
		QualityTags:  []string{"masterpiece", " masterpiece ", ""},
		TagOrder:     []string{"subject", "subject"},
		TriggerWords: []string{" Trigger ", "trigger"},
	})
	if profile.Name != "Test" || profile.Family != "custom" {
		t.Fatalf("profile = %+v", profile)
	}
	if len(profile.QualityTags) != 1 || len(profile.TagOrder) != 1 || len(profile.TriggerWords) != 1 {
		t.Fatalf("lists were not normalized: %+v", profile)
	}
	if profile.LoRATriggerSyntax == "" || profile.WeightSyntax == "" {
		t.Fatalf("default syntax missing: %+v", profile)
	}
}
