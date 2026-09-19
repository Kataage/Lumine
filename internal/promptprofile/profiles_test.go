package promptprofile

import "testing"

func TestBuiltInProfilesValidateAndUseStableUniqueIDs(t *testing.T) {
	profiles := BuiltIns()
	if len(profiles) != 7 {
		t.Fatalf("built-in profile count = %d, want 7", len(profiles))
	}
	seen := make(map[string]struct{}, len(profiles))
	for _, profile := range profiles {
		if !profile.BuiltIn {
			t.Fatalf("%s is not marked built-in", profile.ID)
		}
		if err := Validate(profile); err != nil {
			t.Fatalf("profile %s invalid: %v", profile.ID, err)
		}
		if _, exists := seen[profile.ID]; exists {
			t.Fatalf("duplicate profile id %s", profile.ID)
		}
		seen[profile.ID] = struct{}{}
	}
	for _, id := range []string{IllustriousID, NoobAIID, PonyID, SDXLID, FluxID, SD15ID, CustomID} {
		if _, ok := seen[id]; !ok {
			t.Fatalf("missing built-in profile %s", id)
		}
	}
}

func TestFamilySpecificDefaultsDoNotCrossContaminate(t *testing.T) {
	pony, _ := BuiltIn(PonyID)
	illustrious, _ := BuiltIn(IllustriousID)
	flux, _ := BuiltIn(FluxID)

	if pony.QualityTags[0] != "score_9" {
		t.Fatalf("Pony score defaults = %v", pony.QualityTags)
	}
	for _, tag := range illustrious.QualityTags {
		if tag == "score_9" {
			t.Fatal("Illustrious must not inherit Pony score tags")
		}
	}
	if len(flux.QualityTags) != 0 {
		t.Fatalf("FLUX must not inherit tag-style quality defaults: %v", flux.QualityTags)
	}
}
