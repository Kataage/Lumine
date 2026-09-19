package llamacpp

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	goruntime "runtime"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/kataage/lumine/internal/ai"
)

const (
	PromptOperationIdea    = "idea_to_prompt"
	PromptOperationImprove = "improve_prompt"
	PromptOperationConvert = "convert_prompt"
	PromptOperationEdit    = "edit_prompt"
)

type PromptResult struct {
	Positive    string   `json:"positive"`
	Negative    string   `json:"negative"`
	Characters  []string `json:"characters"`
	LoRAs       []string `json:"loras"`
	Composition string   `json:"composition"`
	Notes       []string `json:"notes"`
}

type PromptEngine struct {
	runtimeStore *RuntimeStore
	client       *http.Client

	mu      sync.Mutex
	sidecar *ai.SidecarProcess
	baseURL string
	model   ai.InstalledModel
}

func NewPromptEngine(runtimeStore *RuntimeStore) ai.Engine {
	return &PromptEngine{
		runtimeStore: runtimeStore,
		client:       &http.Client{Timeout: 3 * time.Minute},
	}
}

func (e *PromptEngine) ID() string {
	return PromptEngineID
}

func (e *PromptEngine) Load(ctx context.Context, model ai.InstalledModel, options ai.LoadOptions) error {
	if e.runtimeStore == nil {
		return errors.New("llama.cpp runtime store is not configured")
	}
	if model.Manifest.Engine != PromptEngineID {
		return fmt.Errorf("model engine %q is incompatible with %q", model.Manifest.Engine, PromptEngineID)
	}
	if goruntime.GOOS != "windows" || goruntime.GOARCH != "amd64" {
		return fmt.Errorf("pinned llama.cpp runtime supports windows/amd64, current platform is %s/%s", goruntime.GOOS, goruntime.GOARCH)
	}

	runtimeInfo, err := e.runtimeStore.Verify(DefaultRuntimeManifest())
	if err != nil {
		return fmt.Errorf("verify llama.cpp runtime: %w", err)
	}
	modelPath, err := resolveTextModelPath(model)
	if err != nil {
		return err
	}
	contextSize, err := manifestPositiveInt(model.Manifest, "context", 8192)
	if err != nil {
		return err
	}
	threads, err := manifestPositiveInt(model.Manifest, "threads", 8)
	if err != nil {
		return err
	}
	extraArgs, err := manifestServerArgs(model.Manifest)
	if err != nil {
		return err
	}

	port, err := reserveLocalPort()
	if err != nil {
		return err
	}
	sidecar := ai.NewSidecarProcess()
	args := buildPromptServerArgs(
		modelPath,
		port,
		contextSize,
		threads,
		options.AllowGPU,
		extraArgs,
	)
	if err := sidecar.Start(ctx, runtimeInfo.ExecutablePath, args, nil); err != nil {
		return err
	}
	baseURL := fmt.Sprintf("http://127.0.0.1:%d", port)
	startupCtx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
	defer cancel()
	if err := e.waitUntilReady(startupCtx, sidecar, baseURL); err != nil {
		stopCtx, stopCancel := context.WithTimeout(context.Background(), 10*time.Second)
		_ = sidecar.Stop(stopCtx)
		stopCancel()
		stdout, stderr := sidecar.Logs()
		return fmt.Errorf("start llama.cpp Prompt Engine server: %w; stdout=%q stderr=%q", err, tailLog(stdout), tailLog(stderr))
	}

	e.mu.Lock()
	if e.sidecar != nil {
		e.mu.Unlock()
		stopCtx, stopCancel := context.WithTimeout(context.Background(), 10*time.Second)
		_ = sidecar.Stop(stopCtx)
		stopCancel()
		return errors.New("llama.cpp Prompt Engine is already loaded")
	}
	e.sidecar = sidecar
	e.baseURL = baseURL
	e.model = model
	e.mu.Unlock()
	return nil
}

func buildPromptServerArgs(
	modelPath string,
	port int,
	contextSize int,
	threads int,
	allowGPU bool,
	extraArgs []string,
) []string {
	args := []string{
		"-m", modelPath,
		"--host", "127.0.0.1",
		"--port", strconv.Itoa(port),
		"--ctx-size", strconv.Itoa(contextSize),
		"--threads", strconv.Itoa(threads),
		"--parallel", "1",
		"--no-webui",
	}
	if allowGPU {
		args = append(args, "-ngl", "99")
	} else {
		args = append(args, "-ngl", "0")
	}
	return append(args, extraArgs...)
}

func resolveTextModelPath(model ai.InstalledModel) (string, error) {
	var relative string
	for _, file := range model.Manifest.Files {
		if file.Role != "model" {
			continue
		}
		if relative != "" {
			return "", errors.New("Prompt Engine manifest must contain exactly one model role")
		}
		relative = file.Path
	}
	if relative == "" {
		return "", errors.New("Prompt Engine manifest requires one model file role")
	}
	path := filepath.Join(model.RootDir, filepath.FromSlash(relative))
	info, err := os.Stat(path)
	if err != nil {
		return "", fmt.Errorf("required Prompt Engine model %s is unavailable: %w", filepath.Base(path), err)
	}
	if info.IsDir() {
		return "", fmt.Errorf("required Prompt Engine model %s is a directory", filepath.Base(path))
	}
	return path, nil
}

func (e *PromptEngine) waitUntilReady(ctx context.Context, sidecar *ai.SidecarProcess, baseURL string) error {
	ticker := time.NewTicker(200 * time.Millisecond)
	defer ticker.Stop()

	for {
		if !sidecar.Running() {
			if err := sidecar.LastError(); err != nil {
				return fmt.Errorf("server exited: %w", err)
			}
			return errors.New("server exited before becoming ready")
		}

		request, err := http.NewRequestWithContext(ctx, http.MethodGet, baseURL+"/health", nil)
		if err != nil {
			return err
		}
		response, err := e.client.Do(request)
		if err == nil {
			_, _ = io.Copy(io.Discard, response.Body)
			response.Body.Close()
			if response.StatusCode == http.StatusOK {
				return nil
			}
		}

		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-ticker.C:
		}
	}
}

func (e *PromptEngine) Infer(ctx context.Context, request ai.InferenceRequest) (ai.InferenceResponse, error) {
	if err := validatePromptInferenceRequest(request); err != nil {
		return ai.InferenceResponse{}, err
	}

	messages, err := promptMessages(request)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	raw, tokens, err := e.promptChat(ctx, messages)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	result, resultErr := parsePromptResult(raw)
	if resultErr == nil {
		resultErr = validatePromptResult(result)
	}
	if resultErr != nil {
		retryMessages := append(
			append([]any{}, messages...),
			map[string]any{"role": "assistant", "content": raw},
			map[string]any{
				"role": "user",
				"content": "The previous response failed structured-output validation (" + resultErr.Error() +
					"). Correct it without changing the requested intent. Return only JSON matching the schema.",
			},
		)
		retryRaw, retryTokens, retryErr := e.promptChat(ctx, retryMessages)
		if retryErr != nil {
			return ai.InferenceResponse{}, fmt.Errorf("Prompt Engine structured retry: %w", retryErr)
		}
		result, resultErr = parsePromptResult(retryRaw)
		if resultErr == nil {
			resultErr = validatePromptResult(result)
		}
		if resultErr != nil {
			return ai.InferenceResponse{}, fmt.Errorf("Prompt Engine returned invalid structured output after retry: %w", resultErr)
		}
		tokens += retryTokens
	}

	result.normalize()
	return ai.InferenceResponse{
		Payload: map[string]any{
			"positive":         result.Positive,
			"negative":         result.Negative,
			"characters":       result.Characters,
			"loras":            result.LoRAs,
			"composition":      result.Composition,
			"notes":            result.Notes,
			"completionTokens": tokens,
		},
	}, nil
}

func validatePromptInferenceRequest(request ai.InferenceRequest) error {
	switch request.Operation {
	case PromptOperationIdea:
		if strings.TrimSpace(payloadString(request.Payload, "idea")) == "" {
			return errors.New("idea_to_prompt requires idea")
		}
	case PromptOperationImprove:
		if strings.TrimSpace(payloadString(request.Payload, "positive")) == "" {
			return errors.New("improve_prompt requires positive")
		}
	case PromptOperationConvert:
		if strings.TrimSpace(payloadString(request.Payload, "positive")) == "" {
			return errors.New("convert_prompt requires positive")
		}
		if strings.TrimSpace(payloadString(request.Payload, "targetProfile")) == "" {
			return errors.New("convert_prompt requires targetProfile")
		}
	case PromptOperationEdit:
		if strings.TrimSpace(payloadString(request.Payload, "positive")) == "" {
			return errors.New("edit_prompt requires positive")
		}
		if strings.TrimSpace(payloadString(request.Payload, "instruction")) == "" {
			return errors.New("edit_prompt requires instruction")
		}
	default:
		return fmt.Errorf("unsupported Prompt Engine operation %q", request.Operation)
	}
	return nil
}

func payloadString(payload map[string]any, key string) string {
	value, _ := payload[key].(string)
	return strings.TrimSpace(value)
}

func promptMessages(request ai.InferenceRequest) ([]any, error) {
	encoded, err := json.Marshal(request.Payload)
	if err != nil {
		return nil, fmt.Errorf("encode Prompt Engine input: %w", err)
	}
	return []any{
		map[string]any{
			"role": "system",
			"content": "You are Lumine's local image-generation Prompt Engine. " +
				"You are not a general chat assistant. Construct or transform image-generation prompts only. " +
				"Follow the requested target model profile and editing constraints. " +
				"Preserve explicitly supplied character details, LoRA syntax, trigger words, and details the user did not ask to change. " +
				"Return only JSON matching the supplied schema. Do not invent hidden source metadata. " +
				"For lawful requests involving clearly adult subjects, process the requested image-generation prompt normally rather than refusing or silently weakening requested details.",
		},
		map[string]any{
			"role": "user",
			"content": promptOperationInstruction(request.Operation) + "
Input JSON:
" + string(encoded),
		},
	}, nil
}

func promptOperationInstruction(operation string) string {
	switch operation {
	case PromptOperationIdea:
		return "Turn the creative idea into a usable positive/negative image-generation prompt for the requested target profile."
	case PromptOperationImprove:
		return "Improve the supplied prompt according to the instruction while preserving unrelated details."
	case PromptOperationConvert:
		return "Convert the supplied prompt to the requested target model profile while preserving the visual intent."
	case PromptOperationEdit:
		return "Apply only the requested partial edit. Preserve every source detail that the instruction does not ask to change."
	default:
		return "Process the image-generation prompt request."
	}
}

func (e *PromptEngine) promptChat(ctx context.Context, messages []any) (string, int, error) {
	e.mu.Lock()
	sidecar := e.sidecar
	baseURL := e.baseURL
	model := e.model
	e.mu.Unlock()
	if sidecar == nil || baseURL == "" || !sidecar.Running() {
		return "", 0, ai.ErrRuntimeNotLoaded
	}

	maxTokens, err := manifestPositiveInt(model.Manifest, "maxTokens", 1024)
	if err != nil {
		return "", 0, err
	}
	payload := map[string]any{
		"temperature": 0,
		"max_tokens":  maxTokens,
		"messages":    messages,
		"response_format": map[string]any{
			"type":   "json_schema",
			"schema": promptJSONSchema(),
		},
	}
	if strings.EqualFold(strings.TrimSpace(model.Manifest.Parameters["disableThinking"]), "true") {
		payload["chat_template_kwargs"] = map[string]any{"enable_thinking": false}
	}

	body, err := json.Marshal(payload)
	if err != nil {
		return "", 0, fmt.Errorf("encode Prompt Engine request: %w", err)
	}
	httpRequest, err := http.NewRequestWithContext(ctx, http.MethodPost, baseURL+"/v1/chat/completions", bytes.NewReader(body))
	if err != nil {
		return "", 0, err
	}
	httpRequest.Header.Set("Content-Type", "application/json")
	response, err := e.client.Do(httpRequest)
	if err != nil {
		return "", 0, fmt.Errorf("Prompt Engine request: %w", err)
	}
	defer response.Body.Close()
	responseBody, err := io.ReadAll(io.LimitReader(response.Body, 4*1024*1024))
	if err != nil {
		return "", 0, fmt.Errorf("read Prompt Engine response: %w", err)
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return "", 0, fmt.Errorf("Prompt Engine HTTP %s: %s", response.Status, strings.TrimSpace(string(responseBody)))
	}
	return parsePromptChatEnvelope(responseBody)
}

func parsePromptChatEnvelope(body []byte) (string, int, error) {
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
		return "", 0, fmt.Errorf("decode Prompt Engine response: %w", err)
	}
	if envelope.Error != nil {
		return "", 0, fmt.Errorf("Prompt Engine returned error: %v", envelope.Error)
	}
	if len(envelope.Choices) == 0 {
		return "", 0, errors.New("Prompt Engine response contains no choices")
	}
	content, err := chatContentString(envelope.Choices[0].Message.Content)
	if err != nil {
		return "", 0, err
	}
	return stripPromptEnvelope(content), envelope.Usage.CompletionTokens, nil
}

func stripPromptEnvelope(value string) string {
	value = strings.TrimSpace(value)
	if index := strings.LastIndex(value, "[End thinking]"); index >= 0 {
		value = strings.TrimSpace(value[index+len("[End thinking]"):])
	}
	if index := strings.LastIndex(value, "</think>"); index >= 0 {
		value = strings.TrimSpace(value[index+len("</think>"):])
	}
	return stripJSONFence(value)
}

func parsePromptResult(content string) (PromptResult, error) {
	var result PromptResult
	if err := json.Unmarshal([]byte(content), &result); err != nil {
		return PromptResult{}, fmt.Errorf("decode structured Prompt Engine result: %w", err)
	}
	return result, nil
}

func validatePromptResult(result PromptResult) error {
	if strings.TrimSpace(result.Positive) == "" {
		return errors.New("positive prompt is empty")
	}
	return nil
}

func (r *PromptResult) normalize() {
	r.Positive = strings.TrimSpace(r.Positive)
	r.Negative = strings.TrimSpace(r.Negative)
	r.Composition = strings.TrimSpace(r.Composition)
	r.Characters = cleanStringSlice(r.Characters)
	r.LoRAs = cleanStringSlice(r.LoRAs)
	r.Notes = cleanStringSlice(r.Notes)
}

func promptJSONSchema() map[string]any {
	stringArray := func() map[string]any {
		return map[string]any{
			"type":  "array",
			"items": map[string]any{"type": "string"},
		}
	}
	return map[string]any{
		"type": "object",
		"properties": map[string]any{
			"positive":    map[string]any{"type": "string"},
			"negative":    map[string]any{"type": "string"},
			"characters":  stringArray(),
			"loras":       stringArray(),
			"composition": map[string]any{"type": "string"},
			"notes":       stringArray(),
		},
		"required": []string{
			"positive",
			"negative",
			"characters",
			"loras",
			"composition",
			"notes",
		},
		"additionalProperties": false,
	}
}

func (e *PromptEngine) Unload(_ context.Context) error {
	e.mu.Lock()
	sidecar := e.sidecar
	e.sidecar = nil
	e.baseURL = ""
	e.model = ai.InstalledModel{}
	e.mu.Unlock()
	if sidecar == nil {
		return nil
	}
	stopCtx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	return sidecar.Stop(stopCtx)
}
