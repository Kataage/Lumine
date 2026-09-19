package commands

import (
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"strings"

	"github.com/kataage/lumine/internal/ai"
)

const taggerThresholdOverridesSettingKey = "ai.tagger.threshold_overrides"

type taggerThresholdOverrides struct {
	General   *float64 `json:"general"`
	Character *float64 `json:"character"`
	Rating    *float64 `json:"rating"`
}

func validateTaggerThresholdValue(name string, value *float64) error {
	if value == nil {
		return nil
	}
	if math.IsNaN(*value) || math.IsInf(*value, 0) || *value < 0 || *value > 1 {
		return fmt.Errorf("%s Tagger threshold must be between 0 and 1", name)
	}
	return nil
}

func validateTaggerThresholdOverrides(value taggerThresholdOverrides) error {
	if err := validateTaggerThresholdValue("general", value.General); err != nil {
		return err
	}
	if err := validateTaggerThresholdValue("character", value.Character); err != nil {
		return err
	}
	return validateTaggerThresholdValue("rating", value.Rating)
}

func (c *AppCommands) getTaggerThresholdOverrides() (taggerThresholdOverrides, error) {
	if c.settingRepo == nil {
		return taggerThresholdOverrides{}, errors.New("settings repository is not available")
	}
	setting, err := c.settingRepo.Get(taggerThresholdOverridesSettingKey)
	if err != nil {
		return taggerThresholdOverrides{}, err
	}
	if setting == nil || strings.TrimSpace(setting.ValueJSON) == "" {
		return taggerThresholdOverrides{}, nil
	}
	var value taggerThresholdOverrides
	if err := json.Unmarshal([]byte(setting.ValueJSON), &value); err != nil {
		return taggerThresholdOverrides{}, fmt.Errorf("decode Tagger threshold overrides: %w", err)
	}
	if err := validateTaggerThresholdOverrides(value); err != nil {
		return taggerThresholdOverrides{}, fmt.Errorf("stored Tagger threshold overrides are invalid: %w", err)
	}
	return value, nil
}

func (c *AppCommands) GetTaggerThresholdOverridesJSON() (string, error) {
	value, err := c.getTaggerThresholdOverrides()
	if err != nil {
		return "", err
	}
	encoded, err := json.Marshal(value)
	if err != nil {
		return "", fmt.Errorf("encode Tagger threshold overrides: %w", err)
	}
	return string(encoded), nil
}

func (c *AppCommands) SetTaggerThresholdOverridesJSON(encoded string) error {
	if c.settingRepo == nil {
		return errors.New("settings repository is not available")
	}
	var value taggerThresholdOverrides
	if strings.TrimSpace(encoded) != "" {
		if err := json.Unmarshal([]byte(encoded), &value); err != nil {
			return fmt.Errorf("decode Tagger threshold overrides: %w", err)
		}
	}
	if err := validateTaggerThresholdOverrides(value); err != nil {
		return err
	}
	normalized, err := json.Marshal(value)
	if err != nil {
		return fmt.Errorf("encode Tagger threshold overrides: %w", err)
	}
	return c.settingRepo.Set(taggerThresholdOverridesSettingKey, string(normalized))
}

func validateTaggerThresholdEcho(
	response ai.InferenceResponse,
	overrides taggerThresholdOverrides,
) error {
	checks := []struct {
		name  string
		key   string
		value *float64
	}{
		{name: "general", key: "generalThreshold", value: overrides.General},
		{name: "character", key: "characterThreshold", value: overrides.Character},
		{name: "rating", key: "ratingThreshold", value: overrides.Rating},
	}
	for _, check := range checks {
		if check.value == nil {
			continue
		}
		raw, ok := response.Payload[check.key]
		if !ok {
			return fmt.Errorf("Tagger engine did not report applied %s threshold", check.name)
		}
		actual, ok := numberAsFloat64(raw)
		if !ok || math.IsNaN(actual) || math.IsInf(actual, 0) {
			return fmt.Errorf("Tagger engine returned invalid %s threshold", check.name)
		}
		if math.Abs(actual-*check.value) > 1e-6 {
			return fmt.Errorf(
				"Tagger engine did not honor %s threshold override: got %.6f want %.6f",
				check.name,
				actual,
				*check.value,
			)
		}
	}
	return nil
}

func applyTaggerThresholdOverrides(
	result *TaggerResult,
	overrides taggerThresholdOverrides,
) {
	if result == nil {
		return
	}
	if overrides.General != nil {
		result.GeneralThreshold = *overrides.General
	}
	if overrides.Character != nil {
		result.CharacterThreshold = *overrides.Character
	}
	if overrides.Rating != nil {
		result.RatingThreshold = *overrides.Rating
	}
	result.GeneralTags = filterTaggerScores(result.GeneralTags, result.GeneralThreshold)
	result.CharacterTags = filterTaggerScores(result.CharacterTags, result.CharacterThreshold)
	result.RatingScores = filterTaggerScores(result.RatingScores, result.RatingThreshold)
	if len(result.RatingScores) > 0 {
		result.Rating = result.RatingScores[0].Name
	}
}

func addTaggerThresholdOverridesToPayload(
	payload map[string]any,
	overrides taggerThresholdOverrides,
) {
	if overrides.General != nil {
		payload["generalThreshold"] = *overrides.General
	}
	if overrides.Character != nil {
		payload["characterThreshold"] = *overrides.Character
	}
	if overrides.Rating != nil {
		payload["ratingThreshold"] = *overrides.Rating
	}
}
