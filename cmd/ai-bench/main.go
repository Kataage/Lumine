package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/kataage/lumine/internal/aibench"
)

type multiFlag []string

func (m *multiFlag) String() string { return strings.Join(*m, " ") }
func (m *multiFlag) Set(value string) error {
	*m = append(*m, value)
	return nil
}

func main() {
	if len(os.Args) < 2 {
		usage()
		os.Exit(2)
	}

	var err error
	switch os.Args[1] {
	case "validate-catalog":
		err = validateCatalogCommand(os.Args[2:])
	case "validate-result":
		err = validateResultCommand(os.Args[2:])
	case "validate-adoptions":
		err = validateAdoptionsCommand(os.Args[2:])
	case "run":
		err = runCommand(os.Args[2:])
	case "compare":
		err = compareCommand(os.Args[2:])
	default:
		usage()
		err = fmt.Errorf("unknown command %q", os.Args[1])
	}

	if err != nil {
		fmt.Fprintln(os.Stderr, "ai-bench:", err)
		os.Exit(1)
	}
}

func usage() {
	fmt.Fprintln(os.Stderr, "Usage: go run ./cmd/ai-bench <command> [options]")
	fmt.Fprintln(os.Stderr, "Commands: validate-catalog, validate-result, validate-adoptions, run, compare")
}

func validateCatalogCommand(args []string) error {
	fs := flag.NewFlagSet("validate-catalog", flag.ContinueOnError)
	path := fs.String("catalog", "benchmarks/ai/catalog.json", "fixture catalog")
	fixtureDir := fs.String("fixtures-dir", "", "optional local fixture pack directory")
	if err := fs.Parse(args); err != nil {
		return err
	}

	var catalog aibench.Catalog
	if err := readJSON(*path, &catalog); err != nil {
		return err
	}
	if err := aibench.ValidateCatalog(catalog); err != nil {
		return err
	}
	if *fixtureDir != "" {
		if err := aibench.VerifyFixturePack(catalog, *fixtureDir); err != nil {
			return err
		}
	}
	fmt.Printf("catalog OK: %d fixtures, %d required categories, pack=%s\n",
		len(catalog.Fixtures), len(aibench.RequiredCategories), catalog.FixturePack)
	return nil
}

func validateResultCommand(args []string) error {
	fs := flag.NewFlagSet("validate-result", flag.ContinueOnError)
	catalogPath := fs.String("catalog", "benchmarks/ai/catalog.json", "fixture catalog")
	resultPath := fs.String("result", "", "benchmark result")
	if err := fs.Parse(args); err != nil {
		return err
	}
	if *resultPath == "" {
		return errors.New("-result is required")
	}

	var catalog aibench.Catalog
	var result aibench.BenchmarkResult
	if err := readJSON(*catalogPath, &catalog); err != nil {
		return err
	}
	if err := readJSON(*resultPath, &result); err != nil {
		return err
	}
	if err := aibench.ValidateResult(result, catalog); err != nil {
		return err
	}
	fmt.Printf("result OK: run=%s model=%s@%s cases=%d\n",
		result.RunID, result.Model.ID, result.Model.Version, len(result.Cases))
	return nil
}

func validateAdoptionsCommand(args []string) error {
	fs := flag.NewFlagSet("validate-adoptions", flag.ContinueOnError)
	path := fs.String("file", "benchmarks/ai/adoptions.json", "adoption ledger")
	if err := fs.Parse(args); err != nil {
		return err
	}
	var ledger aibench.AdoptionLedger
	if err := readJSON(*path, &ledger); err != nil {
		return err
	}
	if err := aibench.ValidateAdoptionLedger(ledger); err != nil {
		return err
	}
	fmt.Printf("adoption ledger OK: %d decisions\n", len(ledger.Decisions))
	return nil
}

func runCommand(args []string) error {
	fs := flag.NewFlagSet("run", flag.ContinueOnError)
	catalogPath := fs.String("catalog", "benchmarks/ai/catalog.json", "fixture catalog")
	profilePath := fs.String("profile", "", "model profile JSON")
	adapter := fs.String("adapter", "", "adapter executable")
	fixtureDir := fs.String("fixtures-dir", "", "local fixture pack directory")
	out := fs.String("out", "", "output result JSON")
	hardwareID := fs.String("hardware-id", "", "stable identifier for the benchmark machine/config")
	cpu := fs.String("cpu", "", "CPU description")
	gpu := fs.String("gpu", "", "GPU description")
	ramBytes := fs.Int64("ram-bytes", 0, "system RAM in bytes")
	lumineVersion := fs.String("lumine-version", "", "Lumine version/commit")
	timeout := fs.Duration("case-timeout", 5*time.Minute, "timeout per fixture")
	var adapterArgs multiFlag
	fs.Var(&adapterArgs, "adapter-arg", "adapter argument; repeat as needed")
	if err := fs.Parse(args); err != nil {
		return err
	}
	if *profilePath == "" || *adapter == "" || *out == "" || *hardwareID == "" {
		return errors.New("-profile, -adapter, -out and -hardware-id are required")
	}

	var catalog aibench.Catalog
	var profile aibench.ModelProfile
	if err := readJSON(*catalogPath, &catalog); err != nil {
		return err
	}
	if err := readJSON(*profilePath, &profile); err != nil {
		return err
	}

	result, err := aibench.RunAdapter(context.Background(), catalog, profile, aibench.RunOptions{
		Adapter:     *adapter,
		AdapterArgs: adapterArgs,
		FixtureDir:  *fixtureDir,
		CaseTimeout: *timeout,
		Environment: aibench.Environment{
			HardwareID:    *hardwareID,
			CPU:           *cpu,
			GPU:           *gpu,
			RAMBytes:      *ramBytes,
			LumineVersion: *lumineVersion,
		},
	})
	if err != nil {
		return err
	}
	if err := aibench.ValidateResult(result, catalog); err != nil {
		return fmt.Errorf("generated result is invalid: %w", err)
	}
	if err := writeJSON(*out, result); err != nil {
		return err
	}
	fmt.Printf("wrote %s (%d cases)\n", *out, len(result.Cases))
	return nil
}

func compareCommand(args []string) error {
	fs := flag.NewFlagSet("compare", flag.ContinueOnError)
	catalogPath := fs.String("catalog", "benchmarks/ai/catalog.json", "fixture catalog")
	thresholdPath := fs.String("thresholds", "benchmarks/ai/thresholds.json", "regression thresholds")
	baselinePath := fs.String("baseline", "", "baseline result JSON")
	candidatePath := fs.String("candidate", "", "candidate result JSON")
	out := fs.String("out", "", "optional comparison report JSON")
	allowHardwareMismatch := fs.Bool("allow-hardware-mismatch", false, "allow comparisons produced on different hardware")
	if err := fs.Parse(args); err != nil {
		return err
	}
	if *baselinePath == "" || *candidatePath == "" {
		return errors.New("-baseline and -candidate are required")
	}

	var catalog aibench.Catalog
	var thresholds aibench.Thresholds
	var baseline aibench.BenchmarkResult
	var candidate aibench.BenchmarkResult
	for path, target := range map[string]any{
		*catalogPath:   &catalog,
		*thresholdPath: &thresholds,
		*baselinePath:  &baseline,
		*candidatePath: &candidate,
	} {
		if err := readJSON(path, target); err != nil {
			return err
		}
	}

	report, err := aibench.Compare(
		baseline,
		candidate,
		catalog,
		thresholds,
		aibench.CompareOptions{AllowHardwareMismatch: *allowHardwareMismatch},
	)
	if err != nil {
		return err
	}
	if *out != "" {
		if err := writeJSON(*out, report); err != nil {
			return err
		}
	}

	fmt.Printf("compared=%d regressions=%d passed=%v\n",
		report.ComparedCases, len(report.Regressions), report.Passed)
	for _, regression := range report.Regressions {
		fmt.Printf("- %s [%s] %s baseline=%.4f candidate=%.4f delta=%.4f limit=%.4f\n",
			regression.FixtureID,
			regression.Category,
			regression.Metric,
			regression.Baseline,
			regression.Candidate,
			regression.Delta,
			regression.Limit,
		)
	}
	if !report.Passed {
		return errors.New("benchmark regression threshold exceeded")
	}
	return nil
}

func readJSON(path string, target any) error {
	data, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("read %s: %w", path, err)
	}
	if err := json.Unmarshal(data, target); err != nil {
		return fmt.Errorf("decode %s: %w", path, err)
	}
	return nil
}

func writeJSON(path string, value any) error {
	data, err := json.MarshalIndent(value, "", "  ")
	if err != nil {
		return err
	}
	data = append(data, '\n')
	if dir := filepath.Dir(path); dir != "." {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			return err
		}
	}
	if err := os.WriteFile(path, data, 0o644); err != nil {
		return fmt.Errorf("write %s: %w", path, err)
	}
	return nil
}
