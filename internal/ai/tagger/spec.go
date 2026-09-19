package tagger

import (
	"errors"
	"fmt"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/kataage/lumine/internal/ai"
)

const EngineID = "tagger-onnx"

const (
	familyWDV3       = "wd-v3"
	familyPixAIV09   = "pixai-v0.9"
	familyCamieV2    = "camie-v2"
	roleModel        = "tagger-model"
	roleTags         = "tagger-tags"
	roleORTDirectML  = "onnxruntime-directml"
	roleDirectML     = "directml-redist"
)

type modelConfig struct {
	family             string
	modelPath          string
	tagsPath           string
	inputName          string
	outputName         string
	imageSize          int
	generalThreshold   float64
	characterThreshold float64
	ratingThreshold    float64
	ratingSupported    bool
	activationSigmoid  bool
}

func configFromModel(model ai.InstalledModel) (modelConfig, error) {
	if model.Manifest.Engine != EngineID {
		return modelConfig{}, fmt.Errorf("Tagger engine cannot load manifest engine %q", model.Manifest.Engine)
	}
	modelFile, ok := modelFileByRole(model.Manifest, roleModel)
	if !ok {
		return modelConfig{}, fmt.Errorf("Tagger manifest is missing %q file role", roleModel)
	}
	tagsFile, ok := modelFileByRole(model.Manifest, roleTags)
	if !ok {
		return modelConfig{}, fmt.Errorf("Tagger manifest is missing %q file role", roleTags)
	}
	params := model.Manifest.Parameters
	family := strings.TrimSpace(params["family"])
	switch family {
	case familyWDV3, familyPixAIV09, familyCamieV2:
	default:
		return modelConfig{}, fmt.Errorf("unsupported Tagger family %q", family)
	}

	imageSize, err := requiredPositiveInt(params, "image_size")
	if err != nil {
		return modelConfig{}, err
	}
	inputName := strings.TrimSpace(params["input_name"])
	outputName := strings.TrimSpace(params["output_name"])
	if inputName == "" || outputName == "" {
		return modelConfig{}, errors.New("Tagger manifest requires input_name and output_name")
	}

	general, err := thresholdParam(params, "general_threshold", 0)
	if err != nil {
		return modelConfig{}, err
	}
	character, err := thresholdParam(params, "character_threshold", 0)
	if err != nil {
		return modelConfig{}, err
	}
	rating, err := thresholdParam(params, "rating_threshold", 0)
	if err != nil {
		return modelConfig{}, err
	}
	ratingSupported, err := boolParam(params, "rating_supported", true)
	if err != nil {
		return modelConfig{}, err
	}

	activationSigmoid := family != familyWDV3
	if raw := strings.TrimSpace(params["activation"]); raw != "" {
		switch raw {
		case "probabilities":
			activationSigmoid = false
		case "sigmoid":
			activationSigmoid = true
		default:
			return modelConfig{}, fmt.Errorf("unsupported Tagger activation %q", raw)
		}
	}

	return modelConfig{
		family:             family,
		modelPath:          filepath.Join(model.RootDir, filepath.FromSlash(modelFile.Path)),
		tagsPath:           filepath.Join(model.RootDir, filepath.FromSlash(tagsFile.Path)),
		inputName:          inputName,
		outputName:         outputName,
		imageSize:          imageSize,
		generalThreshold:   general,
		characterThreshold: character,
		ratingThreshold:    rating,
		ratingSupported:    ratingSupported,
		activationSigmoid:  activationSigmoid,
	}, nil
}

func modelFileByRole(manifest ai.ModelManifest, role string) (ai.ModelFile, bool) {
	for _, file := range manifest.Files {
		if file.Role == role {
			return file, true
		}
	}
	return ai.ModelFile{}, false
}

func requiredPositiveInt(params map[string]string, key string) (int, error) {
	value, err := strconv.Atoi(strings.TrimSpace(params[key]))
	if err != nil || value <= 0 {
		return 0, fmt.Errorf("Tagger manifest parameter %s must be a positive integer", key)
	}
	return value, nil
}

func thresholdParam(params map[string]string, key string, fallback float64) (float64, error) {
	raw := strings.TrimSpace(params[key])
	if raw == "" {
		return fallback, nil
	}
	value, err := strconv.ParseFloat(raw, 64)
	if err != nil || value < 0 || value > 1 {
		return 0, fmt.Errorf("Tagger manifest parameter %s must be between 0 and 1", key)
	}
	return value, nil
}

func boolParam(params map[string]string, key string, fallback bool) (bool, error) {
	raw := strings.TrimSpace(params[key])
	if raw == "" {
		return fallback, nil
	}
	value, err := strconv.ParseBool(raw)
	if err != nil {
		return false, fmt.Errorf("Tagger manifest parameter %s must be boolean", key)
	}
	return value, nil
}
