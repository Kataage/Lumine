package aibench

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
)

const (
	SchemaVersion    = 1
	EvaluatorVersion = "lumine-ai-bench-v1"
)

var categorySet = func() map[Category]struct{} {
	result := make(map[Category]struct{}, len(RequiredCategories))
	for _, category := range RequiredCategories {
		result[category] = struct{}{}
	}
	return result
}()

func ValidateCatalog(catalog Catalog) error {
	if catalog.SchemaVersion != SchemaVersion {
		return fmt.Errorf("unsupported catalog schemaVersion %d", catalog.SchemaVersion)
	}
	if strings.TrimSpace(catalog.FixturePack) == "" {
		return errors.New("catalog fixturePack is required")
	}

	seenIDs := make(map[string]struct{}, len(catalog.Fixtures))
	coverage := make(map[Category]int)
	for index, fixture := range catalog.Fixtures {
		if strings.TrimSpace(fixture.ID) == "" {
			return fmt.Errorf("fixture[%d] id is required", index)
		}
		if _, exists := seenIDs[fixture.ID]; exists {
			return fmt.Errorf("duplicate fixture id %q", fixture.ID)
		}
		seenIDs[fixture.ID] = struct{}{}

		if _, ok := categorySet[fixture.Category]; !ok {
			return fmt.Errorf("fixture %q has unknown category %q", fixture.ID, fixture.Category)
		}
		if strings.TrimSpace(fixture.Description) == "" {
			return fmt.Errorf("fixture %q description is required", fixture.ID)
		}
		for _, ref := range fixture.References {
			if strings.TrimSpace(ref.Path) == "" {
				return fmt.Errorf("fixture %q contains an empty reference path", fixture.ID)
			}
			clean := filepath.Clean(filepath.FromSlash(ref.Path))
			if filepath.IsAbs(clean) || clean == "." || clean == ".." ||
				strings.HasPrefix(clean, ".."+string(filepath.Separator)) {
				return fmt.Errorf("fixture %q contains unsafe reference path %q", fixture.ID, ref.Path)
			}
		}
		coverage[fixture.Category]++
	}

	for _, category := range RequiredCategories {
		if coverage[category] == 0 {
			return fmt.Errorf("catalog does not cover required category %q", category)
		}
	}
	return nil
}

func ValidateModelProfile(profile ModelProfile) error {
	if strings.TrimSpace(profile.ID) == "" {
		return errors.New("model id is required")
	}
	if strings.TrimSpace(profile.Version) == "" {
		return errors.New("model version is required")
	}
	if strings.TrimSpace(profile.Engine) == "" {
		return errors.New("model engine is required")
	}
	if profile.ModelSizeBytes < 0 {
		return errors.New("modelSizeBytes cannot be negative")
	}
	if profile.ArtifactSHA256 != "" {
		if len(profile.ArtifactSHA256) != 64 {
			return errors.New("artifactSha256 must contain 64 hex characters")
		}
		if _, err := hex.DecodeString(profile.ArtifactSHA256); err != nil {
			return fmt.Errorf("artifactSha256 is not valid hex: %w", err)
		}
	}
	seenCategories := make(map[Category]struct{}, len(profile.BenchmarkCategories))
	for _, category := range profile.BenchmarkCategories {
		if _, ok := categorySet[category]; !ok {
			return fmt.Errorf("model profile contains unknown benchmark category %q", category)
		}
		if _, exists := seenCategories[category]; exists {
			return fmt.Errorf("model profile contains duplicate benchmark category %q", category)
		}
		seenCategories[category] = struct{}{}
	}
	return nil
}

func ValidateResult(result BenchmarkResult, catalog Catalog) error {
	if err := ValidateCatalog(catalog); err != nil {
		return fmt.Errorf("catalog: %w", err)
	}
	if result.SchemaVersion != SchemaVersion {
		return fmt.Errorf("unsupported result schemaVersion %d", result.SchemaVersion)
	}
	if result.EvaluatorVersion != EvaluatorVersion {
		return fmt.Errorf("unsupported evaluatorVersion %q", result.EvaluatorVersion)
	}
	if strings.TrimSpace(result.RunID) == "" {
		return errors.New("runId is required")
	}
	if result.CreatedAt.IsZero() {
		return errors.New("createdAt is required")
	}
	if result.CatalogPack != catalog.FixturePack {
		return fmt.Errorf("result catalogPack %q does not match catalog %q", result.CatalogPack, catalog.FixturePack)
	}
	if err := ValidateModelProfile(result.Model); err != nil {
		return fmt.Errorf("model: %w", err)
	}
	if strings.TrimSpace(result.Environment.HardwareID) == "" {
		return errors.New("environment.hardwareId is required")
	}
	if strings.TrimSpace(result.Environment.OS) == "" || strings.TrimSpace(result.Environment.Arch) == "" {
		return errors.New("environment.os and environment.arch are required")
	}

	expected := make(map[string]Fixture, len(catalog.Fixtures))
	for _, fixture := range catalog.Fixtures {
		expected[fixture.ID] = fixture
	}
	seen := make(map[string]struct{}, len(result.Cases))
	for _, value := range result.Cases {
		fixture, ok := expected[value.FixtureID]
		if !ok {
			return fmt.Errorf("result contains unknown fixture %q", value.FixtureID)
		}
		if _, exists := seen[value.FixtureID]; exists {
			return fmt.Errorf("result contains duplicate fixture %q", value.FixtureID)
		}
		seen[value.FixtureID] = struct{}{}
		if value.Category != fixture.Category {
			return fmt.Errorf("fixture %q category = %q, want %q", value.FixtureID, value.Category, fixture.Category)
		}
		if value.Status != CaseStatusOK && value.Status != CaseStatusSkipped && value.Status != CaseStatusError {
			return fmt.Errorf("fixture %q has invalid status %q", value.FixtureID, value.Status)
		}
		if value.Score < 0 || value.Score > 1 {
			return fmt.Errorf("fixture %q score %.4f is outside [0,1]", value.FixtureID, value.Score)
		}
		if value.Status == CaseStatusError && strings.TrimSpace(value.Error) == "" {
			return fmt.Errorf("fixture %q error status requires an error message", value.FixtureID)
		}
		if err := validateMetrics(value.FixtureID, value.Metrics); err != nil {
			return err
		}
	}
	if len(seen) != len(expected) {
		var missing []string
		for id := range expected {
			if _, ok := seen[id]; !ok {
				missing = append(missing, id)
			}
		}
		return fmt.Errorf("result is missing %d fixtures: %s", len(missing), strings.Join(missing, ", "))
	}
	return nil
}

func validateMetrics(fixtureID string, metrics Metrics) error {
	if metrics.LatencyMS < 0 ||
		metrics.TokensPerSecond < 0 ||
		metrics.RAMMB < 0 ||
		metrics.ColdStartMS < 0 ||
		metrics.ModelSizeMB < 0 {
		return fmt.Errorf("fixture %q contains a negative performance metric", fixtureID)
	}
	if metrics.RuntimeSuccessRate < 0 || metrics.RuntimeSuccessRate > 1 {
		return fmt.Errorf("fixture %q runtimeSuccessRate is outside [0,1]", fixtureID)
	}
	return nil
}

func ValidateThresholds(thresholds Thresholds) error {
	values := map[string]float64{
		"maxScoreDrop":                   thresholds.MaxScoreDrop,
		"maxLatencyRegressionPercent":    thresholds.MaxLatencyRegressionPercent,
		"maxTokensPerSecondDropPercent":  thresholds.MaxTokensPerSecondDropPercent,
		"maxRAMRegressionPercent":        thresholds.MaxRAMRegressionPercent,
		"maxColdStartRegressionPercent":  thresholds.MaxColdStartRegressionPercent,
		"maxModelSizeRegressionPercent":  thresholds.MaxModelSizeRegressionPercent,
		"maxRuntimeSuccessRateDrop":      thresholds.MaxRuntimeSuccessRateDrop,
	}
	for name, value := range values {
		if value < 0 {
			return fmt.Errorf("%s cannot be negative", name)
		}
	}
	return nil
}

func ValidateAdoptionLedger(ledger AdoptionLedger) error {
	if ledger.SchemaVersion != SchemaVersion {
		return fmt.Errorf("unsupported adoption schemaVersion %d", ledger.SchemaVersion)
	}
	seen := make(map[string]struct{}, len(ledger.Decisions))
	for _, decision := range ledger.Decisions {
		if strings.TrimSpace(decision.Capability) == "" || strings.TrimSpace(decision.ModelID) == "" {
			return errors.New("adoption capability and modelId are required")
		}
		key := decision.Capability + "\x00" + string(decision.Status) + "\x00" + decision.ModelID
		if _, exists := seen[key]; exists {
			return fmt.Errorf("duplicate adoption decision for %s / %s / %s", decision.Capability, decision.Status, decision.ModelID)
		}
		seen[key] = struct{}{}
		switch decision.Status {
		case AdoptionCandidate, AdoptionAdopted, AdoptionRejected:
		default:
			return fmt.Errorf("invalid adoption status %q", decision.Status)
		}
		if strings.TrimSpace(decision.Rationale) == "" {
			return fmt.Errorf("adoption decision for %s requires rationale", decision.ModelID)
		}
		if decision.Status == AdoptionAdopted {
			if decision.Version == "" || decision.Engine == "" || decision.Quantization == "" {
				return fmt.Errorf("adopted model %s must record version, engine and quantization", decision.ModelID)
			}
			if len(decision.EvidenceResults) == 0 {
				return fmt.Errorf("adopted model %s must reference benchmark evidence", decision.ModelID)
			}
		}
	}
	return nil
}

func BuildFixturePackManifest(catalog Catalog, fixtureDir string) (FixturePackManifest, error) {
	if err := ValidateCatalog(catalog); err != nil {
		return FixturePackManifest{}, err
	}
	if fixtureDir == "" {
		return FixturePackManifest{}, errors.New("fixture directory is required")
	}

	manifest := FixturePackManifest{
		SchemaVersion: SchemaVersion,
		PackID:        catalog.FixturePack,
	}
	seen := make(map[string]struct{})
	for _, fixture := range catalog.Fixtures {
		for _, ref := range fixture.References {
			if _, ok := seen[ref.Path]; ok {
				continue
			}
			path := filepath.Join(fixtureDir, filepath.FromSlash(ref.Path))
			file, err := os.Open(path)
			if err != nil {
				return FixturePackManifest{}, fmt.Errorf("open fixture %q: %w", ref.Path, err)
			}
			info, statErr := file.Stat()
			if statErr != nil {
				_ = file.Close()
				return FixturePackManifest{}, fmt.Errorf("stat fixture %q: %w", ref.Path, statErr)
			}
			if info.IsDir() {
				_ = file.Close()
				return FixturePackManifest{}, fmt.Errorf("fixture %q is a directory", ref.Path)
			}
			hasher := sha256.New()
			if _, err := io.Copy(hasher, file); err != nil {
				_ = file.Close()
				return FixturePackManifest{}, fmt.Errorf("hash fixture %q: %w", ref.Path, err)
			}
			if err := file.Close(); err != nil {
				return FixturePackManifest{}, fmt.Errorf("close fixture %q: %w", ref.Path, err)
			}
			manifest.Files = append(manifest.Files, FixturePackFile{
				Path:      ref.Path,
				SHA256:    hex.EncodeToString(hasher.Sum(nil)),
				SizeBytes: info.Size(),
			})
			seen[ref.Path] = struct{}{}
		}
	}
	return manifest, nil
}

func VerifyFixturePack(catalog Catalog, fixtureDir string) error {
	return verifyFixturePack(catalog, fixtureDir, nil)
}

func VerifyFixturePackForCategories(catalog Catalog, fixtureDir string, categories []Category) error {
	selected := make(map[Category]struct{}, len(categories))
	for _, category := range categories {
		if _, ok := categorySet[category]; !ok {
			return fmt.Errorf("unknown benchmark category %q", category)
		}
		selected[category] = struct{}{}
	}
	return verifyFixturePack(catalog, fixtureDir, selected)
}

func verifyFixturePack(catalog Catalog, fixtureDir string, selected map[Category]struct{}) error {
	if fixtureDir == "" {
		return errors.New("fixture directory is required")
	}

	manifestPath := filepath.Join(fixtureDir, "manifest.json")
	manifestBytes, err := os.ReadFile(manifestPath)
	if err != nil {
		return fmt.Errorf("read fixture pack manifest: %w", err)
	}
	var manifest FixturePackManifest
	if err := json.Unmarshal(manifestBytes, &manifest); err != nil {
		return fmt.Errorf("decode fixture pack manifest: %w", err)
	}
	if manifest.SchemaVersion != SchemaVersion {
		return fmt.Errorf("unsupported fixture pack schemaVersion %d", manifest.SchemaVersion)
	}
	if manifest.PackID != catalog.FixturePack {
		return fmt.Errorf("fixture pack id %q does not match catalog %q", manifest.PackID, catalog.FixturePack)
	}

	files := make(map[string]FixturePackFile, len(manifest.Files))
	for _, file := range manifest.Files {
		clean := filepath.Clean(filepath.FromSlash(file.Path))
		if filepath.IsAbs(clean) || clean == "." || clean == ".." ||
			strings.HasPrefix(clean, ".."+string(filepath.Separator)) {
			return fmt.Errorf("fixture pack contains unsafe path %q", file.Path)
		}
		if len(file.SHA256) != 64 {
			return fmt.Errorf("fixture pack file %q has invalid sha256 length", file.Path)
		}
		if _, err := hex.DecodeString(file.SHA256); err != nil {
			return fmt.Errorf("fixture pack file %q has invalid sha256: %w", file.Path, err)
		}
		if file.SizeBytes < 0 {
			return fmt.Errorf("fixture pack file %q has negative size", file.Path)
		}
		if _, exists := files[file.Path]; exists {
			return fmt.Errorf("fixture pack contains duplicate path %q", file.Path)
		}
		files[file.Path] = file
	}

	verified := make(map[string]struct{})
	for _, fixture := range catalog.Fixtures {
		if selected != nil {
			if _, ok := selected[fixture.Category]; !ok {
				continue
			}
		}
		for _, ref := range fixture.References {
			if _, ok := verified[ref.Path]; ok {
				continue
			}
			expected, ok := files[ref.Path]
			if !ok {
				return fmt.Errorf("fixture %q reference %q is missing from pack manifest", fixture.ID, ref.Path)
			}
			path := filepath.Join(fixtureDir, filepath.FromSlash(ref.Path))
			if err := verifyFixtureFile(path, expected); err != nil {
				return fmt.Errorf("fixture %q reference %q: %w", fixture.ID, ref.Path, err)
			}
			verified[ref.Path] = struct{}{}
		}
	}
	return nil
}

func verifyFixtureFile(path string, expected FixturePackFile) error {
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	defer file.Close()

	info, err := file.Stat()
	if err != nil {
		return err
	}
	if info.IsDir() {
		return errors.New("reference is a directory")
	}
	if expected.SizeBytes > 0 && info.Size() != expected.SizeBytes {
		return fmt.Errorf("size mismatch: got %d want %d", info.Size(), expected.SizeBytes)
	}

	hasher := sha256.New()
	if _, err := io.Copy(hasher, file); err != nil {
		return err
	}
	actual := hex.EncodeToString(hasher.Sum(nil))
	if !strings.EqualFold(actual, expected.SHA256) {
		return fmt.Errorf("sha256 mismatch: got %s want %s", actual, expected.SHA256)
	}
	return nil
}
