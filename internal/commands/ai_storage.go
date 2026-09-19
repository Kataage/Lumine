package commands

import (
	"path/filepath"

	"github.com/kataage/lumine/internal/infrastructure/storage"
)

type AIStorageInfo struct {
	Mode              string `json:"mode"`
	RootPath          string `json:"rootPath"`
	DataPath          string `json:"dataPath"`
	DatabasePath      string `json:"databasePath"`
	LogsPath          string `json:"logsPath"`
	ModelsPath        string `json:"modelsPath"`
	RuntimesPath      string `json:"runtimesPath"`
	SemanticIndexPath string `json:"semanticIndexPath"`
	PreferredRootPath string `json:"preferredRootPath,omitempty"`
	LegacyPath        string `json:"legacyPath,omitempty"`
	LegacyDetected    bool   `json:"legacyDetected"`
	UsingLegacy       bool   `json:"usingLegacy"`
}

func (c *AppCommands) SetStorageLayout(layout storage.Layout) {
	c.storageInfo = AIStorageInfo{
		Mode:              string(layout.Mode),
		RootPath:          layout.RootDir,
		DataPath:          layout.DataDir,
		DatabasePath:      storage.DatabasePath(layout),
		LogsPath:          layout.LogsDir,
		ModelsPath:        layout.ModelsDir,
		RuntimesPath:      filepath.Join(layout.RuntimesDir, "llama.cpp"),
		SemanticIndexPath: layout.SemanticIndexDir,
		PreferredRootPath: layout.PreferredRootDir,
		LegacyPath:        layout.LegacyRootDir,
		LegacyDetected:    layout.LegacyDetected,
		UsingLegacy:       layout.UsingLegacy,
	}
}

func (c *AppCommands) GetAIStorageInfo() AIStorageInfo {
	info := c.storageInfo
	if c.aiManager != nil && c.aiManager.Store() != nil {
		info.ModelsPath = c.aiManager.Store().Root()
	}
	if c.llamaRuntimeStore != nil {
		info.RuntimesPath = c.llamaRuntimeStore.Root()
	}
	return info
}
