package llamacpp

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"mime"
	"net"
	"net/http"
	"os"
	"path/filepath"
	goruntime "runtime"
	"strings"
	"sync"
	"time"

	"github.com/kataage/lumine/internal/ai"
)

type VisionResult struct {
	ShortCaption    string   `json:"shortCaption"`
	DetailedCaption string   `json:"detailedCaption"`
	Subject         string   `json:"subject"`
	Background      string   `json:"background"`
	Composition     string   `json:"composition"`
	Viewpoint       string   `json:"viewpoint"`
	VisibleText     []string `json:"visibleText"`
}

type Engine struct {
	runtimeStore *RuntimeStore
	client       *http.Client

	mu      sync.Mutex
	sidecar *ai.SidecarProcess
	baseURL string
	model   ai.InstalledModel
}

func NewEngine(runtimeStore *RuntimeStore) ai.Engine {
	return &Engine{
		runtimeStore: runtimeStore,
		client:       &http.Client{Timeout: 3 * time.Minute},
	}
}

func (e *Engine) ID() string {
	return EngineID
}

func (e *Engine) Load(ctx context.Context, model ai.InstalledModel, _ ai.LoadOptions) error {
	if e.runtimeStore == nil {
		return errors.New("llama.cpp runtime store is not configured")
	}
	if model.Manifest.Engine != EngineID {
		return fmt.Errorf("model engine %q is incompatible with %q", model.Manifest.Engine, EngineID)
	}
	if goruntime.GOOS != "windows" || goruntime.GOARCH != "amd64" {
		return fmt.Errorf("pinned llama.cpp runtime supports windows/amd64, current platform is %s/%s", goruntime.GOOS, goruntime.GOARCH)
	}

	runtimeInfo, err := e.runtimeStore.Verify(DefaultRuntimeManifest())
	if err != nil {
		return fmt.Errorf("verify llama.cpp runtime: %w", err)
	}
	modelPath := filepath.Join(model.RootDir, defaultVisionModelFile)
	mmprojPath := filepath.Join(model.RootDir, defaultVisionMMProjFile)
	for _, path := range []string{modelPath, mmprojPath} {
		info, statErr := os.Stat(path)
		if statErr != nil || info.IsDir() {
			if statErr == nil {
				statErr = errors.New("path is a directory")
			}
			return fmt.Errorf("required VLM file %s is unavailable: %w", filepath.Base(path), statErr)
		}
	}

	port, err := reserveLocalPort()
	if err != nil {
		return err
	}
	sidecar := ai.NewSidecarProcess()
	args := []string{
		"-m", modelPath,
		"--mmproj", mmprojPath,
		"--host", "127.0.0.1",
		"--port", fmt.Sprintf("%d", port),
		"--ctx-size", "4096",
		"--threads", "8",
		"--parallel", "1",
		"--no-mmproj-offload",
		"-ngl", "0",
	}
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
		return fmt.Errorf("start llama.cpp VLM server: %w; stdout=%q stderr=%q", err, tailLog(stdout), tailLog(stderr))
	}

	e.mu.Lock()
	if e.sidecar != nil {
		e.mu.Unlock()
		stopCtx, stopCancel := context.WithTimeout(context.Background(), 10*time.Second)
		_ = sidecar.Stop(stopCtx)
		stopCancel()
		return errors.New("llama.cpp VLM engine is already loaded")
	}
	e.sidecar = sidecar
	e.baseURL = baseURL
	e.model = model
	e.mu.Unlock()
	return nil
}

func (e *Engine) waitUntilReady(
	ctx context.Context,
	sidecar *ai.SidecarProcess,
	baseURL string,
) error {
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

func (e *Engine) Infer(
	ctx context.Context,
	request ai.InferenceRequest,
) (ai.InferenceResponse, error) {
	if request.Operation != "analyze_image" {
		return ai.InferenceResponse{}, fmt.Errorf("unsupported llama.cpp VLM operation %q", request.Operation)
	}
	filePath, _ := request.Payload["filePath"].(string)
	filePath = strings.TrimSpace(filePath)
	if filePath == "" {
		return ai.InferenceResponse{}, errors.New("analyze_image requires filePath")
	}
	mode, _ := request.Payload["mode"].(string)
	if mode == "" {
		mode = "detailed"
	}

	imageURI, err := fileDataURI(filePath)
	if err != nil {
		return ai.InferenceResponse{}, err
	}

	e.mu.Lock()
	sidecar := e.sidecar
	baseURL := e.baseURL
	e.mu.Unlock()
	if sidecar == nil || baseURL == "" || !sidecar.Running() {
		return ai.InferenceResponse{}, ai.ErrRuntimeNotLoaded
	}

	payload := map[string]any{
		"temperature": 0,
		"max_tokens":  512,
		"messages": []any{
			map[string]any{
				"role": "user",
				"content": []any{
					map[string]any{
						"type": "text",
						"text": visionPrompt(mode),
					},
					map[string]any{
						"type": "image_url",
						"image_url": map[string]any{
							"url": imageURI,
						},
					},
				},
			},
		},
		"response_format": map[string]any{
			"type":   "json_schema",
			"schema": visionJSONSchema(),
		},
	}
	body, err := json.Marshal(payload)
	if err != nil {
		return ai.InferenceResponse{}, fmt.Errorf("encode VLM request: %w", err)
	}

	httpRequest, err := http.NewRequestWithContext(
		ctx,
		http.MethodPost,
		baseURL+"/v1/chat/completions",
		bytes.NewReader(body),
	)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	httpRequest.Header.Set("Content-Type", "application/json")
	response, err := e.client.Do(httpRequest)
	if err != nil {
		return ai.InferenceResponse{}, fmt.Errorf("VLM request: %w", err)
	}
	defer response.Body.Close()
	responseBody, err := io.ReadAll(io.LimitReader(response.Body, 2*1024*1024))
	if err != nil {
		return ai.InferenceResponse{}, fmt.Errorf("read VLM response: %w", err)
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return ai.InferenceResponse{}, fmt.Errorf("VLM HTTP %s: %s", response.Status, strings.TrimSpace(string(responseBody)))
	}

	result, completionTokens, err := parseVisionChatResponse(responseBody)
	if err != nil {
		return ai.InferenceResponse{}, err
	}
	return ai.InferenceResponse{
		Payload: map[string]any{
			"shortCaption":     result.ShortCaption,
			"detailedCaption":  result.DetailedCaption,
			"subject":          result.Subject,
			"background":       result.Background,
			"composition":      result.Composition,
			"viewpoint":        result.Viewpoint,
			"visibleText":      result.VisibleText,
			"completionTokens": completionTokens,
		},
	}, nil
}

func (e *Engine) Unload(_ context.Context) error {
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

func parseVisionChatResponse(body []byte) (VisionResult, int, error) {
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
		return VisionResult{}, 0, fmt.Errorf("decode VLM response envelope: %w", err)
	}
	if envelope.Error != nil {
		return VisionResult{}, 0, fmt.Errorf("VLM returned error: %v", envelope.Error)
	}
	if len(envelope.Choices) == 0 {
		return VisionResult{}, 0, errors.New("VLM response contains no choices")
	}
	content, err := chatContentString(envelope.Choices[0].Message.Content)
	if err != nil {
		return VisionResult{}, 0, err
	}
	content = stripJSONFence(content)

	var result VisionResult
	if err := json.Unmarshal([]byte(content), &result); err != nil {
		return VisionResult{}, 0, fmt.Errorf("decode structured VLM result: %w; content=%q", err, content)
	}
	result.ShortCaption = strings.TrimSpace(result.ShortCaption)
	result.DetailedCaption = strings.TrimSpace(result.DetailedCaption)
	result.Subject = strings.TrimSpace(result.Subject)
	result.Background = strings.TrimSpace(result.Background)
	result.Composition = strings.TrimSpace(result.Composition)
	result.Viewpoint = strings.TrimSpace(result.Viewpoint)
	if result.VisibleText == nil {
		result.VisibleText = []string{}
	}
	return result, envelope.Usage.CompletionTokens, nil
}

func chatContentString(value any) (string, error) {
	switch content := value.(type) {
	case string:
		return content, nil
	case []any:
		var builder strings.Builder
		for _, item := range content {
			part, ok := item.(map[string]any)
			if !ok {
				continue
			}
			if text, ok := part["text"].(string); ok {
				builder.WriteString(text)
			}
		}
		if builder.Len() == 0 {
			return "", errors.New("VLM response content array contains no text")
		}
		return builder.String(), nil
	default:
		return "", fmt.Errorf("unsupported VLM response content type %T", value)
	}
}

func stripJSONFence(value string) string {
	value = strings.TrimSpace(value)
	if !strings.HasPrefix(value, "```") {
		return value
	}
	value = strings.TrimPrefix(value, "```json")
	value = strings.TrimPrefix(value, "```JSON")
	value = strings.TrimPrefix(value, "```")
	value = strings.TrimSuffix(value, "```")
	return strings.TrimSpace(value)
}

func fileDataURI(path string) (string, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return "", fmt.Errorf("read image for VLM: %w", err)
	}
	if len(data) == 0 {
		return "", errors.New("image for VLM is empty")
	}
	contentType := mime.TypeByExtension(strings.ToLower(filepath.Ext(path)))
	if contentType == "" {
		contentType = http.DetectContentType(data[:min(len(data), 512)])
	}
	if !strings.HasPrefix(contentType, "image/") {
		return "", fmt.Errorf("unsupported VLM image content type %q", contentType)
	}
	return "data:" + contentType + ";base64," + base64.StdEncoding.EncodeToString(data), nil
}

func reserveLocalPort() (int, error) {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		return 0, fmt.Errorf("reserve local VLM port: %w", err)
	}
	defer listener.Close()
	return listener.Addr().(*net.TCPAddr).Port, nil
}

func visionPrompt(mode string) string {
	detail := "Be specific about the subject, background, composition, camera/viewpoint, and visible text."
	if mode == "short" {
		detail = "Keep the captions concise while still filling every JSON field."
	}
	return "Analyze this image for a local image-library application. " + detail +
		" Describe only what is visibly present; do not invent hidden details. " +
		"If the image is lawful adult-only artwork, describe it neutrally instead of refusing. " +
		"Return only JSON matching the supplied schema."
}

func visionJSONSchema() map[string]any {
	stringProperty := func() map[string]any { return map[string]any{"type": "string"} }
	return map[string]any{
		"type": "object",
		"properties": map[string]any{
			"shortCaption":    stringProperty(),
			"detailedCaption": stringProperty(),
			"subject":         stringProperty(),
			"background":      stringProperty(),
			"composition":     stringProperty(),
			"viewpoint":       stringProperty(),
			"visibleText": map[string]any{
				"type":  "array",
				"items": stringProperty(),
			},
		},
		"required": []string{
			"shortCaption",
			"detailedCaption",
			"subject",
			"background",
			"composition",
			"viewpoint",
			"visibleText",
		},
		"additionalProperties": false,
	}
}

func tailLog(value string) string {
	const limit = 4096
	value = strings.TrimSpace(value)
	if len(value) <= limit {
		return value
	}
	return value[len(value)-limit:]
}
