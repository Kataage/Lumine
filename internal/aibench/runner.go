package aibench

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"os/exec"
	"runtime"
	"strings"
	"time"
)

type RunOptions struct {
	Adapter     string
	AdapterArgs []string
	FixtureDir  string
	Environment Environment
	CaseTimeout time.Duration
}

func RunAdapter(
	ctx context.Context,
	catalog Catalog,
	model ModelProfile,
	options RunOptions,
) (BenchmarkResult, error) {
	if err := ValidateCatalog(catalog); err != nil {
		return BenchmarkResult{}, err
	}
	if err := ValidateModelProfile(model); err != nil {
		return BenchmarkResult{}, err
	}
	if strings.TrimSpace(options.Adapter) == "" {
		return BenchmarkResult{}, fmt.Errorf("adapter executable is required")
	}
	if strings.TrimSpace(options.Environment.HardwareID) == "" {
		return BenchmarkResult{}, fmt.Errorf("hardwareId is required")
	}
	if options.Environment.OS == "" {
		options.Environment.OS = runtime.GOOS
	}
	if options.Environment.Arch == "" {
		options.Environment.Arch = runtime.GOARCH
	}
	if options.CaseTimeout <= 0 {
		options.CaseTimeout = 5 * time.Minute
	}

	hasReferences := false
	for _, fixture := range catalog.Fixtures {
		if len(fixture.References) > 0 {
			hasReferences = true
			break
		}
	}
	if hasReferences {
		if err := VerifyFixturePack(catalog, options.FixtureDir); err != nil {
			return BenchmarkResult{}, err
		}
	}

	now := time.Now().UTC()
	result := BenchmarkResult{
		SchemaVersion:    SchemaVersion,
		EvaluatorVersion: EvaluatorVersion,
		RunID:            buildRunID(now, model),
		CreatedAt:     now,
		CatalogPack:   catalog.FixturePack,
		Model:         model,
		Environment:   options.Environment,
		Cases:         make([]CaseResult, 0, len(catalog.Fixtures)),
	}

	for _, fixture := range catalog.Fixtures {
		caseResult := runFixture(ctx, fixture, model, options)
		result.Cases = append(result.Cases, caseResult)
	}
	return result, nil
}

func runFixture(
	parent context.Context,
	fixture Fixture,
	model ModelProfile,
	options RunOptions,
) CaseResult {
	value := CaseResult{
		FixtureID: fixture.ID,
		Category:  fixture.Category,
		Status:    CaseStatusError,
		Score:     0,
	}

	request := AdapterRequest{
		SchemaVersion: SchemaVersion,
		FixtureDir:    options.FixtureDir,
		Model:         model,
		Fixture:       fixture,
	}
	requestBytes, err := json.Marshal(request)
	if err != nil {
		value.Error = "encode adapter request: " + err.Error()
		return value
	}

	caseCtx, cancel := context.WithTimeout(parent, options.CaseTimeout)
	defer cancel()

	cmd := exec.CommandContext(caseCtx, options.Adapter, options.AdapterArgs...)
	cmd.Stdin = bytes.NewReader(requestBytes)
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	cmd.Stdout = &stdout
	cmd.Stderr = &stderr

	started := time.Now()
	runErr := cmd.Run()
	wallLatencyMS := float64(time.Since(started).Microseconds()) / 1000.0

	if caseCtx.Err() != nil {
		value.Error = "adapter timeout/cancellation: " + caseCtx.Err().Error()
		return value
	}
	if runErr != nil {
		detail := strings.TrimSpace(stderr.String())
		if len(detail) > 4096 {
			detail = detail[len(detail)-4096:]
		}
		if detail == "" {
			detail = runErr.Error()
		}
		value.Error = "adapter failed: " + detail
		return value
	}

	var response AdapterResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		value.Error = "decode adapter response: " + err.Error()
		return value
	}
	if response.Status == "" {
		response.Status = CaseStatusOK
	}
	value.Status = response.Status
	value.Score = response.Score
	value.Metrics = response.Metrics
	value.Output = response.Output
	value.Error = response.Error
	value.Notes = response.Notes

	if value.Metrics.LatencyMS == 0 {
		value.Metrics.LatencyMS = wallLatencyMS
	}
	if fixture.Category == CategoryColdStart && value.Metrics.ColdStartMS == 0 {
		value.Metrics.ColdStartMS = wallLatencyMS
	}
	if fixture.Category == CategoryModelSize &&
		value.Metrics.ModelSizeMB == 0 &&
		model.ModelSizeBytes > 0 {
		value.Metrics.ModelSizeMB = float64(model.ModelSizeBytes) / (1024 * 1024)
	}
	if value.Status == CaseStatusError && strings.TrimSpace(value.Error) == "" {
		value.Error = "adapter returned error status without details"
	}
	return value
}

func buildRunID(at time.Time, model ModelProfile) string {
	id := strings.NewReplacer(
		"/", "-",
		"\\", "-",
		" ", "-",
		":", "-",
	).Replace(model.ID)
	return at.Format("20060102T150405.000Z") + "-" + id + "-" + model.Version
}
