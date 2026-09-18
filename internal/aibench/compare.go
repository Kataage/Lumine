package aibench

import (
	"errors"
	"fmt"
)

type CompareOptions struct {
	AllowHardwareMismatch bool
}

func Compare(
	baseline BenchmarkResult,
	candidate BenchmarkResult,
	catalog Catalog,
	thresholds Thresholds,
	options CompareOptions,
) (ComparisonReport, error) {
	if err := ValidateResult(baseline, catalog); err != nil {
		return ComparisonReport{}, fmt.Errorf("baseline: %w", err)
	}
	if err := ValidateResult(candidate, catalog); err != nil {
		return ComparisonReport{}, fmt.Errorf("candidate: %w", err)
	}
	if err := ValidateThresholds(thresholds); err != nil {
		return ComparisonReport{}, fmt.Errorf("thresholds: %w", err)
	}
	if baseline.Environment.HardwareID != candidate.Environment.HardwareID && !options.AllowHardwareMismatch {
		return ComparisonReport{}, errors.New("hardwareId mismatch; performance regressions are not comparable")
	}

	baseByID := make(map[string]CaseResult, len(baseline.Cases))
	for _, value := range baseline.Cases {
		baseByID[value.FixtureID] = value
	}

	report := ComparisonReport{
		SchemaVersion:  SchemaVersion,
		BaselineRunID:  baseline.RunID,
		CandidateRunID: candidate.RunID,
		HardwareID:     candidate.Environment.HardwareID,
	}
	for _, current := range candidate.Cases {
		previous := baseByID[current.FixtureID]
		if previous.Status != CaseStatusOK {
			continue
		}
		report.ComparedCases++
		if current.Status != CaseStatusOK {
			report.Regressions = append(report.Regressions, Regression{
				FixtureID: current.FixtureID,
				Category:  current.Category,
				Metric:    "status",
				Baseline:  1,
				Candidate: 0,
				Delta:     1,
				Limit:     0,
			})
			continue
		}
		report.Regressions = append(report.Regressions, compareCase(previous, current, thresholds)...)
	}
	report.Passed = len(report.Regressions) == 0
	return report, nil
}

func compareCase(baseline, candidate CaseResult, thresholds Thresholds) []Regression {
	var regressions []Regression
	scoreDrop := baseline.Score - candidate.Score
	if scoreDrop > thresholds.MaxScoreDrop {
		regressions = append(regressions, Regression{
			FixtureID: candidate.FixtureID,
			Category:  candidate.Category,
			Metric:    "score",
			Baseline:  baseline.Score,
			Candidate: candidate.Score,
			Delta:     scoreDrop,
			Limit:     thresholds.MaxScoreDrop,
		})
	}

	regressions = append(regressions,
		compareHigherIsWorse(candidate, "latencyMs", baseline.Metrics.LatencyMS, candidate.Metrics.LatencyMS, thresholds.MaxLatencyRegressionPercent)...,
	)
	regressions = append(regressions,
		compareLowerIsWorse(candidate, "tokensPerSecond", baseline.Metrics.TokensPerSecond, candidate.Metrics.TokensPerSecond, thresholds.MaxTokensPerSecondDropPercent)...,
	)
	regressions = append(regressions,
		compareHigherIsWorse(candidate, "ramMb", baseline.Metrics.RAMMB, candidate.Metrics.RAMMB, thresholds.MaxRAMRegressionPercent)...,
	)
	regressions = append(regressions,
		compareHigherIsWorse(candidate, "coldStartMs", baseline.Metrics.ColdStartMS, candidate.Metrics.ColdStartMS, thresholds.MaxColdStartRegressionPercent)...,
	)
	regressions = append(regressions,
		compareHigherIsWorse(candidate, "modelSizeMb", baseline.Metrics.ModelSizeMB, candidate.Metrics.ModelSizeMB, thresholds.MaxModelSizeRegressionPercent)...,
	)

	if baseline.Metrics.RuntimeSuccessRate > 0 || candidate.Metrics.RuntimeSuccessRate > 0 {
		drop := baseline.Metrics.RuntimeSuccessRate - candidate.Metrics.RuntimeSuccessRate
		if drop > thresholds.MaxRuntimeSuccessRateDrop {
			regressions = append(regressions, Regression{
				FixtureID: candidate.FixtureID,
				Category:  candidate.Category,
				Metric:    "runtimeSuccessRate",
				Baseline:  baseline.Metrics.RuntimeSuccessRate,
				Candidate: candidate.Metrics.RuntimeSuccessRate,
				Delta:     drop,
				Limit:     thresholds.MaxRuntimeSuccessRateDrop,
			})
		}
	}
	return regressions
}

func compareHigherIsWorse(
	current CaseResult,
	metric string,
	baseline float64,
	candidate float64,
	limitPercent float64,
) []Regression {
	if baseline <= 0 || candidate <= 0 {
		return nil
	}
	regressionPercent := ((candidate - baseline) / baseline) * 100
	if regressionPercent <= limitPercent {
		return nil
	}
	return []Regression{{
		FixtureID: current.FixtureID,
		Category:  current.Category,
		Metric:    metric,
		Baseline:  baseline,
		Candidate: candidate,
		Delta:     regressionPercent,
		Limit:     limitPercent,
	}}
}

func compareLowerIsWorse(
	current CaseResult,
	metric string,
	baseline float64,
	candidate float64,
	limitPercent float64,
) []Regression {
	if baseline <= 0 || candidate <= 0 {
		return nil
	}
	dropPercent := ((baseline - candidate) / baseline) * 100
	if dropPercent <= limitPercent {
		return nil
	}
	return []Regression{{
		FixtureID: current.FixtureID,
		Category:  current.Category,
		Metric:    metric,
		Baseline:  baseline,
		Candidate: candidate,
		Delta:     dropPercent,
		Limit:     limitPercent,
	}}
}
