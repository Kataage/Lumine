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

	"github.com/kataage/lumine/internal/ai"
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
	modeOverride string,
) (ai.InferenceResponse, error) {
	filePaths, err := inferenceFilePaths(request.Payload["filePaths"])
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	if len(filePaths) == 0 {
		if single, _ := request.Payload["filePath"].(string); strings.TrimSpace(single) != "" {
			filePaths = []string{strings.TrimSpace(single)}
		}
	}
	if len(filePaths) == 0 {
		return ai.InferenceResponse{}, errors.New("Advanced Vision requires at least one image")
	}
	if len(filePaths) > 8 {
		return ai.InferenceResponse{}, errors.New("Advanced Vision supports at most 8 images per request")
	}

	mode := strings.TrimSpace(modeOverride)
	if mode == "" {
		mode, _ = request.Payload["mode"].(string)
		mode = strings.TrimSpace(mode)
	}
	if mode == "" {
		mode = "deep"
	}
	switch mode {
	case "deep", "compare", "reverse_prompt_support":
	default:
		return ai.InferenceResponse{}, fmt.Errorf("unsupported Advanced Vision mode %q", mode)
	}
	if mode == "compare" && len(filePaths) < 2 {
		return ai.InferenceResponse{}, errors.New("compare mode requires at least two images")
	}
	instruction, _ := request.Payload["instruction"].(string)
	instruction = strings.TrimSpace(instruction)

	content := make([]any, 0, 1+len(filePaths)*2)
	content = append(content, map[string]any{
		"type": "text",
		"text": advancedVisionPrompt(mode, instruction, len(filePaths)),
	})
	for index, path := range filePaths {
		uri, err := fileDataURI(path)
		if err != nil {
			return ai.InferenceResponse{}, fmt.Errorf("image %d: %w", index+1, err)
		}
		if len(filePaths) > 1 {
			content = append(content, map[string]any{
				"type": "text",
				"text": fmt.Sprintf("Image %d:", index+1),
			})
		}
		content = append(content, map[string]any{
			"type": "image_url",
			"image_url": map[string]any{"url": uri},
		})
	}

	e.mu.Lock()
	sidecar := e.sidecar
	baseURL := e.baseURL
	maxTokens := e.config.maxTokens
	e.mu.Unlock()
	if sidecar == nil || baseURL == "" || !sidecar.Running() {
		return ai.InferenceResponse{}, ai.ErrRuntimeNotLoaded
	}
	if maxTokens <= 0 {
		maxTokens = 1024
	}

	payload := map[string]any{
		"temperature": 0,
		"max_tokens": maxTokens,
		"messages": []any{
			map[string]any{
				"role": "user",
				"content": content,
			},
		},
		"response_format": map[string]any{
			"type": "json_schema",
			"schema": advancedVisionJSONSchema(),
		},
	}
	body, err := json.Marshal(payload)
	if err != nil {
		return ai.InferenceResponse{}, fmt.Errorf("encode Advanced Vision request: %w", err)
	}

	httpRequest, err := http.NewRequestWithContext(ctx, http.MethodPost, baseURL+"/v1/chat/completions", bytes.NewReader(body))
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	httpRequest.Header.Set("Content-Type", "application/json")
	response, err := e.client.Do(httpRequest)
	if err != nil {
		return ai.InferenceResponse{}, fmt.Errorf("Advanced Vision request: %w", err)
	}
	defer response.Body.Close()
	responseBody, err := io.ReadAll(io.LimitReader(response.Body, 4*1024*1024))
	if err != nil {
		return ai.InferenceResponse{}, fmt.Errorf("read Advanced Vision response: %w", err)
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return ai.InferenceResponse{}, fmt.Errorf("Advanced Vision HTTP %s: %s", response.Status, strings.TrimSpace(string(responseBody)))
	}

	result, tokens, err := parseAdvancedVisionChatResponse(responseBody)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	encoded, err := json.Marshal(result)
	if err != nil {
		return ai.InferenceResponse{}, fmt.Errorf("encode Advanced Vision result: %w", err)
	}
	var resultMap map[string]any
	if err := json.Unmarshal(encoded, &resultMap); err != nil {
		return ai.InferenceResponse{}, err
	}
	resultMap["completionTokens"] = tokens
	resultMap["mode"] = mode
	resultMap["imageCount"] = len(filePaths)
	return ai.InferenceResponse{Payload: resultMap}, nil
}

func inferenceFilePaths(raw any) ([]string, error) {
	var values []string
	switch typed := raw.(type) {
	case nil:
		return nil, nil
	case []string:
		values = typed
	case []any:
		for _, item := range typed {
			value, ok := item.(string)
			if !ok {
				return nil, errors.New("filePaths must contain only strings")
			}
			values = append(values, value)
		}
	default:
		return nil, errors.New("filePaths must be a string array")
	}
	result := make([]string, 0, len(values))
	for _, value := range values {
		value = strings.TrimSpace(value)
		if value == "" {
			return nil, errors.New("filePaths cannot contain empty paths")
		}
		result = append(result, value)
	}
	return result, nil
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
		return AdvancedVisionResult{}, 0, fmt.Errorf("decode Advanced Vision response envelope: %w", err)
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
	normalizeAdvancedVisionResult(&result)
	return result, envelope.Usage.CompletionTokens, nil
}

func normalizeAdvancedVisionResult(result *AdvancedVisionResult) {
	result.Summary = strings.TrimSpace(result.Summary)
	result.Environment = strings.TrimSpace(result.Environment)
	result.Composition = strings.TrimSpace(result.Composition)
	result.Viewpoint = strings.TrimSpace(result.Viewpoint)
	result.Context = strings.TrimSpace(result.Context)
	if result.Subjects == nil { result.Subjects = []string{} }
	if result.Actions == nil { result.Actions = []string{} }
	if result.Relationships == nil { result.Relationships = []string{} }
	if result.Differences == nil { result.Differences = []string{} }
	if result.Commonalities == nil { result.Commonalities = []string{} }
	if result.ReversePromptHints == nil { result.ReversePromptHints = []string{} }
	if result.VisibleText == nil { result.VisibleText = []string{} }
	if result.Notes == nil { result.Notes = []string{} }
}

func advancedVisionPrompt(mode, instruction string, imageCount int) string {
	var task string
	switch mode {
	case "compare":
		task = "Compare the supplied images. Identify concrete commonalities and differences while preserving unchanged context."
	case "reverse_prompt_support":
		task = "Analyze visible details that would be useful as grounded input for a later image-generation prompt. Do not invent hidden prompt metadata."
	default:
		task = "Analyze the image deeply: subjects, environment, composition, viewpoint, actions, relationships, context, and visible text."
	}
	if instruction != "" {
		task += " User instruction: " + instruction
	}
	if imageCount > 1 {
		task += fmt.Sprintf(" There are %d images; refer to them by their presented order.", imageCount)
	}
	return task +
		" Describe only visible evidence. If the content is lawful adult-only artwork, analyze it neutrally rather than refusing merely because it is adult. " +
		"Return only JSON matching the supplied schema."
}

func advancedVisionJSONSchema() map[string]any {
	stringProperty := func() map[string]any { return map[string]any{"type": "string"} }
	stringArray := func() map[string]any {
		return map[string]any{"type": "array", "items": stringProperty()}
	}
	return map[string]any{
		"type": "object",
		"properties": map[string]any{
			"summary": stringProperty(),
			"subjects": stringArray(),
			"environment": stringProperty(),
			"composition": stringProperty(),
			"viewpoint": stringProperty(),
			"actions": stringArray(),
			"relationships": stringArray(),
			"context": stringProperty(),
			"differences": stringArray(),
			"commonalities": stringArray(),
			"reversePromptHints": stringArray(),
			"visibleText": stringArray(),
			"notes": stringArray(),
		},
		"required": []string{
			"summary", "subjects", "environment", "composition", "viewpoint",
			"actions", "relationships", "context", "differences", "commonalities",
			"reversePromptHints", "visibleText", "notes",
		},
		"additionalProperties": false,
	}
}
