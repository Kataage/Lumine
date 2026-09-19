package aibench

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func fullTestCatalog() Catalog {
	fixtures := make([]Fixture, 0, len(RequiredCategories))
	for index, category := range RequiredCategories {
		fixtures = append(fixtures, Fixture{
			ID:          string(category) + "-fixture",
			Category:    category,
			Description: "test fixture",
			Input:       map[string]any{"index": index},
		})
	}
	return Catalog{
		SchemaVersion: SchemaVersion,
		FixturePack:   "test-pack-v1",
		Fixtures:      fixtures,
	}
}

func fullTestResult(catalog Catalog, runID, hardwareID string) BenchmarkResult {
	cases := make([]CaseResult, 0, len(catalog.Fixtures))
	for _, fixture := range catalog.Fixtures {
		cases = append(cases, CaseResult{
			FixtureID: fixture.ID,
			Category:  fixture.Category,
			Status:    CaseStatusOK,
			Score:     0.9,
		})
	}
	return BenchmarkResult{
		SchemaVersion:    SchemaVersion,
		EvaluatorVersion: EvaluatorVersion,
		RunID:            runID,
		CreatedAt:     time.Date(2026, 9, 19, 0, 0, 0, 0, time.UTC),
		CatalogPack:   catalog.FixturePack,
		Model: ModelProfile{
			ID:      "model",
			Version: "1",
			Engine:  "engine",
		},
		Environment: Environment{
			HardwareID: hardwareID,
			OS:         "windows",
			Arch:       "amd64",
		},
		Cases: cases,
	}
}

func TestCatalogRequiresEveryBenchmarkCategory(t *testing.T) {
	catalog := fullTestCatalog()
	if err := ValidateCatalog(catalog); err != nil {
		t.Fatalf("ValidateCatalog: %v", err)
	}

	catalog.Fixtures = catalog.Fixtures[:len(catalog.Fixtures)-1]
	if err := ValidateCatalog(catalog); err == nil {
		t.Fatal("catalog missing a required category should fail")
	}
}

func TestCatalogCanScopeRequiredCategories(t *testing.T) {
	catalog := Catalog{
		SchemaVersion:      SchemaVersion,
		FixturePack:        "vision-only-v1",
		RequiredCategories: []Category{CategoryLightweightVision, CategoryCPULatency},
		Fixtures: []Fixture{
			{ID: "vision", Category: CategoryLightweightVision, Description: "vision"},
			{ID: "cpu", Category: CategoryCPULatency, Description: "cpu"},
		},
	}
	if err := ValidateCatalog(catalog); err != nil {
		t.Fatalf("ValidateCatalog scoped: %v", err)
	}

	catalog.RequiredCategories = append(catalog.RequiredCategories, CategoryRAM)
	if err := ValidateCatalog(catalog); err == nil {
		t.Fatal("scoped catalog missing declared RAM category should fail")
	}
}


func TestResultValidationRequiresCompleteStableFixtureCoverage(t *testing.T) {
	catalog := fullTestCatalog()
	result := fullTestResult(catalog, "baseline", "machine-a")
	if err := ValidateResult(result, catalog); err != nil {
		t.Fatalf("ValidateResult: %v", err)
	}

	result.Cases = result.Cases[:len(result.Cases)-1]
	if err := ValidateResult(result, catalog); err == nil {
		t.Fatal("incomplete result should fail validation")
	}
}

func TestCompareDetectsQualityPerformanceAndStatusRegressions(t *testing.T) {
	catalog := fullTestCatalog()
	baseline := fullTestResult(catalog, "baseline", "machine-a")
	candidate := fullTestResult(catalog, "candidate", "machine-a")

	baseline.Cases[0].Score = 0.95
	candidate.Cases[0].Score = 0.80

	baseline.Cases[1].Metrics.LatencyMS = 100
	candidate.Cases[1].Metrics.LatencyMS = 130

	baseline.Cases[2].Metrics.TokensPerSecond = 100
	candidate.Cases[2].Metrics.TokensPerSecond = 70

	candidate.Cases[3].Status = CaseStatusError
	candidate.Cases[3].Error = "runtime crashed"

	report, err := Compare(
		baseline,
		candidate,
		catalog,
		Thresholds{
			MaxScoreDrop:                  0.03,
			MaxLatencyRegressionPercent:   15,
			MaxTokensPerSecondDropPercent: 15,
			MaxRAMRegressionPercent:       15,
			MaxColdStartRegressionPercent: 20,
			MaxModelSizeRegressionPercent: 10,
			MaxRuntimeSuccessRateDrop:     0.01,
		},
		CompareOptions{},
	)
	if err != nil {
		t.Fatalf("Compare: %v", err)
	}
	if report.Passed {
		t.Fatal("regressing candidate should fail")
	}

	metrics := make(map[string]bool)
	for _, regression := range report.Regressions {
		metrics[regression.Metric] = true
	}
	for _, expected := range []string{"score", "latencyMs", "tokensPerSecond", "status"} {
		if !metrics[expected] {
			t.Fatalf("missing %s regression: %+v", expected, report.Regressions)
		}
	}
}

func TestCompareRejectsEvaluatorMismatch(t *testing.T) {
	catalog := fullTestCatalog()
	baseline := fullTestResult(catalog, "baseline", "machine-a")
	candidate := fullTestResult(catalog, "candidate", "machine-a")
	candidate.EvaluatorVersion = "future-evaluator"

	if _, err := Compare(baseline, candidate, catalog, Thresholds{}, CompareOptions{}); err == nil {
		t.Fatal("evaluator mismatch should fail")
	}
}

func TestCompareRejectsHardwareMismatchByDefault(t *testing.T) {
	catalog := fullTestCatalog()
	baseline := fullTestResult(catalog, "baseline", "machine-a")
	candidate := fullTestResult(catalog, "candidate", "machine-b")

	if _, err := Compare(baseline, candidate, catalog, Thresholds{}, CompareOptions{}); err == nil {
		t.Fatal("hardware mismatch should fail by default")
	}
	if _, err := Compare(
		baseline,
		candidate,
		catalog,
		Thresholds{},
		CompareOptions{AllowHardwareMismatch: true},
	); err != nil {
		t.Fatalf("explicit hardware mismatch override should work: %v", err)
	}
}

func TestFixturePackManifestBuildAndVerify(t *testing.T) {
	catalog := fullTestCatalog()
	catalog.Fixtures[0].References = []FixtureReference{{
		Path: "semantic/image.bin",
		Role: "image",
	}}

	dir := t.TempDir()
	path := filepath.Join(dir, "semantic", "image.bin")
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	data := []byte("stable fixture bytes")
	if err := os.WriteFile(path, data, 0o644); err != nil {
		t.Fatal(err)
	}

	manifest, err := BuildFixturePackManifest(catalog, dir)
	if err != nil {
		t.Fatalf("BuildFixturePackManifest: %v", err)
	}
	if len(manifest.Files) != 1 {
		t.Fatalf("manifest files = %d, want 1", len(manifest.Files))
	}
	sum := sha256.Sum256(data)
	if manifest.Files[0].SHA256 != hex.EncodeToString(sum[:]) {
		t.Fatalf("unexpected fixture hash: %+v", manifest.Files[0])
	}

	manifestBytes, _ := json.Marshal(manifest)
	if err := os.WriteFile(filepath.Join(dir, "manifest.json"), manifestBytes, 0o644); err != nil {
		t.Fatal(err)
	}
	if err := VerifyFixturePack(catalog, dir); err != nil {
		t.Fatalf("VerifyFixturePack: %v", err)
	}

	if err := os.WriteFile(path, []byte("changed"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := VerifyFixturePack(catalog, dir); err == nil {
		t.Fatal("modified fixture should fail hash verification")
	}
}

func TestRepositoryBenchmarkDefinitionsValidate(t *testing.T) {
	root := filepath.Join("..", "..", "benchmarks", "ai")

	var catalog Catalog
	readJSONForTest(t, filepath.Join(root, "catalog.json"), &catalog)
	if err := ValidateCatalog(catalog); err != nil {
		t.Fatalf("repository catalog: %v", err)
	}

	var lightweightCatalog Catalog
	readJSONForTest(t, filepath.Join(root, "catalogs", "lightweight-vision-v1.json"), &lightweightCatalog)
	if err := ValidateCatalog(lightweightCatalog); err != nil {
		t.Fatalf("repository lightweight vision catalog: %v", err)
	}

	var advancedCatalog Catalog
	readJSONForTest(t, filepath.Join(root, "catalogs", "advanced-vision-v1.json"), &advancedCatalog)
	if err := ValidateCatalog(advancedCatalog); err != nil {
		t.Fatalf("repository advanced vision catalog: %v", err)
	}

	var thresholds Thresholds
	readJSONForTest(t, filepath.Join(root, "thresholds.json"), &thresholds)
	if err := ValidateThresholds(thresholds); err != nil {
		t.Fatalf("repository thresholds: %v", err)
	}

	var ledger AdoptionLedger
	readJSONForTest(t, filepath.Join(root, "adoptions.json"), &ledger)
	if err := ValidateAdoptionLedger(ledger); err != nil {
		t.Fatalf("repository adoption ledger: %v", err)
	}

	for _, name := range []string{
		"example.json",
		"wd-vit-tagger-v3.json",
		"pixai-tagger-v0.9.json",
		"florence-2-base.json",
		"smolvlm-500m-q8.json",
		"advanced-qwen3-vl-2b-q4.json",
		"advanced-qwen3-vl-2b-abliterated-q4.json",
		"advanced-qwen3-vl-2b-heretic-q4.json",
		"advanced-internvl3.5-2b-q4.json",
		"advanced-smolvlm2-2.2b-q4.json",
		"advanced-minicpm-v4.6-q4.json",
	} {
		var profile ModelProfile
		readJSONForTest(t, filepath.Join(root, "profiles", name), &profile)
		if err := ValidateModelProfile(profile); err != nil {
			t.Fatalf("repository profile %s: %v", name, err)
		}
	}
}

func readJSONForTest(t *testing.T, path string, target any) {
	t.Helper()
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read %s: %v", path, err)
	}
	if err := json.Unmarshal(data, target); err != nil {
		t.Fatalf("decode %s: %v", path, err)
	}
}
