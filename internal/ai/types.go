package ai

import (
	"context"

	"github.com/kataage/lumine/internal/domain"
)

type ModelFile struct {
	Path      string `json:"path"`
	URL       string `json:"url"`
	SHA256    string `json:"sha256"`
	SizeBytes int64  `json:"sizeBytes"`
}

type ModelManifest struct {
	ID          string      `json:"id"`
	Version     string      `json:"version"`
	Engine      string      `json:"engine"`
	DisplayName string      `json:"displayName"`
	License     string      `json:"license"`
	SizeBytes   int64       `json:"sizeBytes"`
	Files       []ModelFile `json:"files"`
}

type InstalledModel struct {
	Manifest ModelManifest `json:"manifest"`
	RootDir  string        `json:"rootDir"`
}

type InstalledModelInfo struct {
	ID          string `json:"id"`
	Version     string `json:"version"`
	Engine      string `json:"engine"`
	DisplayName string `json:"displayName"`
	License     string `json:"license"`
	SizeBytes   int64  `json:"sizeBytes"`
	RootDir     string `json:"rootDir"`
}

type DownloadProgress struct {
	ModelID         string `json:"modelId"`
	Version         string `json:"version"`
	FilePath        string `json:"filePath"`
	FileIndex       int    `json:"fileIndex"`
	FileCount       int    `json:"fileCount"`
	BytesDownloaded int64  `json:"bytesDownloaded"`
	BytesTotal      int64  `json:"bytesTotal"`
	Done            bool   `json:"done"`
}

type ProgressFunc func(DownloadProgress)

type LoadOptions struct {
	AllowGPU bool `json:"allowGpu"`
}

type InferenceRequest struct {
	Operation string         `json:"operation"`
	Payload   map[string]any `json:"payload,omitempty"`
}

type InferenceResponse struct {
	Payload map[string]any `json:"payload,omitempty"`
}

type Engine interface {
	ID() string
	Load(ctx context.Context, model InstalledModel, options LoadOptions) error
	Infer(ctx context.Context, request InferenceRequest) (InferenceResponse, error)
	Unload(ctx context.Context) error
}

type EngineFactory func() Engine

type CapabilityPolicy func(domain.AICapability) (bool, error)

type RuntimeState string

const (
	RuntimeStateDisabled          RuntimeState = "disabled"
	RuntimeStateModelNotInstalled RuntimeState = "model_not_installed"
	RuntimeStateReady             RuntimeState = "ready"
	RuntimeStateRunning           RuntimeState = "running"
	RuntimeStateError             RuntimeState = "error"
)

type RuntimeStatus struct {
	Capability domain.AICapability `json:"capability"`
	State      RuntimeState        `json:"state"`
	ModelID    string              `json:"modelId,omitempty"`
	Version    string              `json:"version,omitempty"`
	Engine     string              `json:"engine,omitempty"`
	Error      string              `json:"error,omitempty"`
}
