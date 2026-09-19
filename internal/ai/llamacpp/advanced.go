package llamacpp

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"strings"
)

const (
	AdvancedOperationDeep                 = "analyze_deep"
	AdvancedOperationCompare              = "compare_images"
	AdvancedOperationReversePromptSupport = "reverse_prompt_support"
)

type AdvancedVisionResult struct {
	Summary            string   `json:"summary"`
	Subjects           []string `json:"subjects"`
	Environment        string   `json:"environment"`
	Composition        string   `json:"composition"`
	Viewpoint          string   `json:"viewpoint"`
	Actions            []string `json:"actions"`
	Relationships      []string `json:"relationships"`
	Context            string   `json:"context"`
	Differences        []string `json:"differences"`
	Commonalities      []string `json:"commonalities"`
	ReversePromptHints []string `json:"reversePromptHints"`
	VisibleText        []string `json:"visibleText"`
	Notes              []string `json:"notes"`
}

func (e *Engine) inferAdvanced(
	ctx context.Context,
	request ai.InferenceRequest,
) (ai.InferenceResponse, error) {
	switch request.Operation {
	case AdvancedOperationDeep, AdvancedOperationCompare, AdvancedOperationReversePromptSupport:
	default:
		return ai.InferenceResponse{}, fmt.Errorf("unsupported Advanced Vision operation %q", request.Operation)
	}

	filePaths, err := advancedFilePaths(request.Payload)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	if request.Operation == AdvancedOperationCompare && len(filePaths) < 2 {
		return ai.InferenceResponse{}, errors.New("compare_images requires at least two filePaths")
	}

	instruction, _ := request.Payload["instruction"].(string)
	instruction = strings.TrimSpace(instruction)

	content := []any{
		map[string]any{
			"type": "text",
			"text": advancedVisionPrompt(request.Operation, instruction),
		},
	}
	for index, path := range filePaths {
		imageURI, err := fileDataURI(path)
		if err != nil {
			return ai.InferenceResponse{}, fmt.Errorf("prepare Advanced Vision image %d: %w", index+1, err)
		}
		if len(filePaths) > 1 {
			content = append(content, map[string]any{
				"type": "text",
				"text": fmt.Sprintf("Image %d:", index+1),
			})
		}
		content = append(content, map[string]any{
			"type": "image_url",
			"image_url": map[string]any{
				"url": imageURI,
			},
		})
	}

	result, completionTokens, err := e.advancedChat(ctx, content)
	if err != nil {
		return ai.InferenceResponse{}, err
	}

	return ai.InferenceResponse{
		Payload: map[string]any{
			"summary":            result.Summary,
			"subjects":           result.Subjects,
			"environment":        result.Environment,
			"composition":        result.Composition,
			"viewpoint":          result.Viewpoint,
			"actions":            result.Actions,
			"relationships":      result.Relationships,
			"context":            result.Context,
			"differences":        result.Differences,
			"commonalities":      result.Commonalities,
			"reversePromptHints": result.ReversePromptHints,
			"visibleText":        result.VisibleText,
			"notes":              result.Notes,
			"completionTokens":   completionTokens,
		},
	}, nil
}

func advancedFilePaths(payload map[string]any) ([]string, error) {
	var result []string
	switch raw := payload["filePaths"].(type) {
	case []string:
		for _, value := range raw {
			if cleaned := strings.TrimSpace(value); cleaned != "" {
				result = append(result, cleaned)
			}
		}
	case []any:
		for _, item := range raw {
			if value, ok := item.(string); ok {
				if cleaned := strings.TrimSpace(value); cleaned != "" {
					result = append(result, cleaned)
				}
			}
		}
	case nil:
		if value, ok := payload["filePath"].(string); ok {
			if cleaned := strings.TrimSpace(value); cleaned != "" {
				result = append(result, cleaned)
			}
		}
	default:
		return nil, errors.New("filePaths must be an array of strings")
	}
	if len(result) == 0 {
		return nil, errors.New("Advanced Vision requires at least one image file")
	}
	if len(result) > 8 {
		return nil, errors.New("Advanced Vision accepts at most 8 images per request")
	}
	return result, nil
}

func advancedVisionPrompt(operation, instruction string) string {
	base := "Analyze the supplied image or images for a local image-library and image-generation workflow. " +
		"Use only visible evidence and do not invent hidden metadata or identities. " +
		"For clearly adult lawful artwork, analyze visible details neutrally instead of refusing. " +
		"Return only JSON matching the supplied schema. "

	switch operation {
	case AdvancedOperationCompare:
		base += "Compare all images. Put concrete changes in differences and stable shared evidence in commonalities. "
	case AdvancedOperationReversePromptSupport:
		base += "Recover visually grounded attributes useful to a later image-generation prompt while keeping observations separate from inference. "
	default:
		base += "Analyze subjects, environment, composition, viewpoint, actions, relationships, context and visible text in depth. "
	}
	if instruction != "" {
		base += "Follow this user instruction: " + instruction
	}
	return base
}

func (e *Engine) advancedChat(ctx context.Context, content []any) (AdvancedVisionResult, int, error) {
	e.mu.Lock()
	sidecar := e.sidecar
	baseURL := e.baseURL
	e.mu.Unlock()
	if sidecar == nil || baseURL == "" || !sidecar.Running() {
		return AdvancedVisionResult{}, 0, ai.ErrRuntimeNotLoaded
	}

	payload := map[string]any{
		"temperature": 0,
		"max_tokens":  1024,
		"messages": []any{
			map[string]any{
				"role":    "user",
				"content": content,
			},
		},
		"response_format": map[string]any{
			"type":   "json_schema",
			"schema": advancedVisionJSONSchema(),
		},
	}
	body, err := json.Marshal(payload)
	if err != nil {
		return AdvancedVisionResult{}, 0, fmt.Errorf("encode Advanced Vision request: %w", err)
	}
	httpRequest, err := http.NewRequestWithContext(
		ctx,
		http.MethodPost,
		baseURL+"/v1/chat/completions",
		bytes.NewReader(body),
	)
	if err != nil {
		return AdvancedVisionResult{}, 0, err
	}
	httpRequest.Header.Set("Content-Type", "application/json")

	response, err := e.client.Do(httpRequest)
	if err != nil {
		return AdvancedVisionResult{}, 0, fmt.Errorf("Advanced Vision request: %w", err)
	}
	defer response.Body.Close()
	responseBody, err := io.ReadAll(io.LimitReader(response.Body, 4*1024*1024))
	if err != nil {
		return AdvancedVisionResult{}, 0, fmt.Errorf("read Advanced Vision response: %w", err)
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return AdvancedVisionResult{}, 0, fmt.Errorf(
			"Advanced Vision HTTP %s: %s",
			response.Status,
			strings.TrimSpace(string(responseBody)),
		)
	}
	return parseAdvancedVisionChatResponse(responseBody)
}

func parseAdvancedVisionChatResponse(body []byte) (AdvancedVisionResult, int, error) {
	var envelope struct {
		Choices []struct {
			Message struct {
				Content any `json:"content"`
			} `json:"message"`
		} `json:"choices"`
		Usage struct {
			CompletionTokens int `json:"completion_tokens"`
		} `json:"usage"`
		Error any `json:"error"`
	}
	if err := json.Unmarshal(body, &envelope); err != nil {
		return AdvancedVisionResult{}, 0, fmt.Errorf("decode Advanced Vision response: %w", err)
	}
	if envelope.Error != nil {
		return AdvancedVisionResult{}, 0, fmt.Errorf("Advanced Vision returned error: %v", envelope.Error)
	}
	if len(envelope.Choices) == 0 {
		return AdvancedVisionResult{}, 0, errors.New("Advanced Vision response contains no choices")
	}

	content, err := chatContentString(envelope.Choices[0].Message.Content)
	if err != nil {
		return AdvancedVisionResult{}, 0, err
	}
	content = stripJSONFence(content)

	var result AdvancedVisionResult
	if err := json.Unmarshal([]byte(content), &result); err != nil {
		return AdvancedVisionResult{}, 0, fmt.Errorf("decode structured Advanced Vision result: %w; content=%q", err, content)
	}
	result.normalize()
	return result, envelope.Usage.CompletionTokens, nil
}

func (r *AdvancedVisionResult) normalize() {
	r.Summary = strings.TrimSpace(r.Summary)
	r.Environment = strings.TrimSpace(r.Environment)
	r.Composition = strings.TrimSpace(r.Composition)
	r.Viewpoint = strings.TrimSpace(r.Viewpoint)
	r.Context = strings.TrimSpace(r.Context)
	r.Subjects = cleanStringSlice(r.Subjects)
	r.Actions = cleanStringSlice(r.Actions)
	r.Relationships = cleanStringSlice(r.Relationships)
	r.Differences = cleanStringSlice(r.Differences)
	r.Commonalities = cleanStringSlice(r.Commonalities)
	r.ReversePromptHints = cleanStringSlice(r.ReversePromptHints)
	r.VisibleText = cleanStringSlice(r.VisibleText)
	r.Notes = cleanStringSlice(r.Notes)
}

func cleanStringSlice(values []string) []string {
	if values == nil {
		return []string{}
	}
	result := make([]string, 0, len(values))
	for _, value := range values {
		if cleaned := strings.TrimSpace(value); cleaned != "" {
			result = append(result, cleaned)
		}
	}
	return result
}

func advancedVisionJSONSchema() map[string]any {
	stringArray := func() map[string]any {
		return map[string]any{
			"type":  "array",
			"items": map[string]any{"type": "string"},
		}
	}
	return map[string]any{
		"type": "object",
		"properties": map[string]any{
			"summary":            map[string]any{"type": "string"},
			"subjects":           stringArray(),
			"environment":        map[string]any{"type": "string"},
			"composition":        map[string]any{"type": "string"},
			"viewpoint":          map[string]any{"type": "string"},
			"actions":            stringArray(),
			"relationships":      stringArray(),
			"context":            map[string]any{"type": "string"},
			"differences":        stringArray(),
			"commonalities":      stringArray(),
			"reversePromptHints": stringArray(),
			"visibleText":        stringArray(),
			"notes":              stringArray(),
		},
		"required": []string{
			"summary",
			"subjects",
			"environment",
			"composition",
			"viewpoint",
			"actions",
			"relationships",
			"context",
			"differences",
			"commonalities",
			"reversePromptHints",
			"visibleText",
			"notes",
		},
		"additionalProperties": false,
	}
}
