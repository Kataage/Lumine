package commands

import (
	"os"
	"testing"

	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/kataage/lumine/internal/infrastructure/scanner"

	"github.com/kataage/lumine/internal/domain"
)

func TestAISettingsDefaultToDisabled(t *testing.T) {
	cmd := setupCommands(t)

	settings, err := cmd.GetAISettings()
	if err != nil {
		t.Fatalf("GetAISettings: %v", err)
	}
	if settings.Enabled {
		t.Fatal("global AI should be disabled by default")
	}

	allowed, err := cmd.IsAICapabilityEnabled(string(domain.AICapabilityPromptEngine))
	if err != nil {
		t.Fatalf("IsAICapabilityEnabled: %v", err)
	}
	if allowed {
		t.Fatal("prompt engine must not be allowed before opt-in")
	}
}

func TestAISettingsPersistAndGateCapabilities(t *testing.T) {
	cmd := setupCommands(t)

	wanted := domain.AISettings{
		Enabled:             true,
		SemanticSearch:      true,
		Tagger:              false,
		LightweightVision:   true,
		AdvancedVision:      false,
		PromptEngine:        true,
		AutoAnalyze:         true,
		GPUAcceleration:     false,
	}
	if _, err := cmd.SetAISettings(wanted); err != nil {
		t.Fatalf("SetAISettings: %v", err)
	}

	got, err := cmd.GetAISettings()
	if err != nil {
		t.Fatalf("GetAISettings: %v", err)
	}
	if got != wanted {
		t.Fatalf("settings round-trip mismatch: got %+v want %+v", got, wanted)
	}

	tests := []struct {
		capability domain.AICapability
		want       bool
	}{
		{domain.AICapabilitySemanticSearch, true},
		{domain.AICapabilityTagger, false},
		{domain.AICapabilityLightweightVision, true},
		{domain.AICapabilityAdvancedVision, false},
		{domain.AICapabilityPromptEngine, true},
		{domain.AICapabilityAutoAnalyze, true},
	}
	for _, tt := range tests {
		got, err := cmd.IsAICapabilityEnabled(string(tt.capability))
		if err != nil {
			t.Fatalf("IsAICapabilityEnabled(%s): %v", tt.capability, err)
		}
		if got != tt.want {
			t.Fatalf("IsAICapabilityEnabled(%s) = %v, want %v", tt.capability, got, tt.want)
		}
	}

	wanted.Enabled = false
	if _, err := cmd.SetAISettings(wanted); err != nil {
		t.Fatalf("disable AI: %v", err)
	}
	allowed, err := cmd.IsAICapabilityEnabled(string(domain.AICapabilitySemanticSearch))
	if err != nil {
		t.Fatalf("IsAICapabilityEnabled after disable: %v", err)
	}
	if allowed {
		t.Fatal("global AI off must override enabled child feature")
	}
}


func TestAISettingsPersistAcrossDatabaseReopen(t *testing.T) {
	dir, err := os.MkdirTemp("", "lumine-ai-settings-reopen-*")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)

	openCommands := func() (*AppCommands, *db.DB) {
		database, err := db.Open(dir)
		if err != nil {
			t.Fatalf("open db: %v", err)
		}
		scanSvc := scanner.NewScanner(
			db.NewAssetRepo(database),
			db.NewLibraryRepo(database),
			db.NewJobLogRepo(database),
		)
		scanSvc.SetSettingRepo(db.NewAppSettingRepo(database))
		return New(database, scanSvc), database
	}

	first, database := openCommands()
	wanted := domain.AISettings{
		Enabled:           true,
		SemanticSearch:    true,
		LightweightVision: true,
		AutoAnalyze:       true,
		GPUAcceleration:   true,
	}
	if _, err := first.SetAISettings(wanted); err != nil {
		t.Fatalf("persist settings: %v", err)
	}
	if err := database.Close(); err != nil {
		t.Fatalf("close first db: %v", err)
	}

	second, reopened := openCommands()
	defer reopened.Close()

	got, err := second.GetAISettings()
	if err != nil {
		t.Fatalf("GetAISettings after reopen: %v", err)
	}
	if got != wanted {
		t.Fatalf("settings after reopen = %+v, want %+v", got, wanted)
	}

	health, err := second.GetAIHealthSnapshot()
	if err != nil {
		t.Fatalf("GetAIHealthSnapshot: %v", err)
	}
	if !health.SettingsPersisted || !health.Settings.Enabled || !health.SemanticSearchEnabled {
		t.Fatalf("unexpected AI health after reopen: %+v", health)
	}
}


func TestPatchAISettingsPreservesUnrelatedFields(t *testing.T) {
	cmd := setupCommands(t)

	initial := domain.AISettings{
		Enabled:           true,
		SemanticSearch:    true,
		LightweightVision: true,
		AdvancedVision:    true,
		PromptEngine:      true,
		AutoAnalyze:       true,
		GPUAcceleration:   true,
	}
	if _, err := cmd.SetAISettings(initial); err != nil {
		t.Fatalf("SetAISettings: %v", err)
	}

	updated, err := cmd.PatchAISettings(map[string]bool{
		"semanticSearch": false,
	})
	if err != nil {
		t.Fatalf("PatchAISettings: %v", err)
	}

	want := initial
	want.SemanticSearch = false
	if updated != want {
		t.Fatalf("patched settings = %+v, want %+v", updated, want)
	}

	reloaded, err := cmd.GetAISettings()
	if err != nil {
		t.Fatalf("GetAISettings: %v", err)
	}
	if reloaded != want {
		t.Fatalf("persisted settings = %+v, want %+v", reloaded, want)
	}
}

func TestPatchAISettingsRejectsUnknownFieldsWithoutMutation(t *testing.T) {
	cmd := setupCommands(t)

	initial := domain.AISettings{
		Enabled:      true,
		PromptEngine: true,
	}
	if _, err := cmd.SetAISettings(initial); err != nil {
		t.Fatalf("SetAISettings: %v", err)
	}

	if _, err := cmd.PatchAISettings(map[string]bool{"unknownField": true}); err == nil {
		t.Fatal("unknown patch field should fail")
	}

	got, err := cmd.GetAISettings()
	if err != nil {
		t.Fatalf("GetAISettings: %v", err)
	}
	if got != initial {
		t.Fatalf("settings mutated after rejected patch: got %+v want %+v", got, initial)
	}
}
