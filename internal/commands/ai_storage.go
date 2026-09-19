package commands

import (
	"fmt"
	"path/filepath"

	"github.com/kataage/lumine/internal/infrastructure/storage"
)

type AIStorageInfo struct {
	Mode                      string `json:"mode"`
	RootPath                  string `json:"rootPath"`
	DataPath                  string `json:"dataPath"`
	DatabasePath              string `json:"databasePath"`
	LogsPath                  string `json:"logsPath"`
	ModelsPath                string `json:"modelsPath"`
	RuntimesPath              string `json:"runtimesPath"`
	SemanticIndexPath         string `json:"semanticIndexPath"`
	PreferredRootPath         string `json:"preferredRootPath,omitempty"`
	LegacyPath                string `json:"legacyPath,omitempty"`
	LegacyDetected            bool   `json:"legacyDetected"`
	UsingLegacy               bool   `json:"usingLegacy"`
	MigrationAvailable        bool   `json:"migrationAvailable"`
	MigrationPending          bool   `json:"migrationPending"`
	MigrationSourcePath       string `json:"migrationSourcePath,omitempty"`
	MigrationTargetPath       string `json:"migrationTargetPath,omitempty"`
	MigrationRequiresRestart  bool   `json:"migrationRequiresRestart"`
}

func ConfigureStorageLayout(c *AppCommands, layout storage.Layout) {
	if c == nil {
		return
	}
	c.storageLayout = layout
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
	applyStorageMigrationStatus(&c.storageInfo, storage.LegacyMigrationStatus(layout))
}

func applyStorageMigrationStatus(info *AIStorageInfo, status storage.MigrationStatus) {
	if info == nil {
		return
	}
	info.MigrationAvailable = status.Available
	info.MigrationPending = status.Pending
	info.MigrationSourcePath = status.SourceRoot
	info.MigrationTargetPath = status.TargetRoot
	info.MigrationRequiresRestart = status.RequiresRestart
}

func (c *AppCommands) GetAIStorageInfo() AIStorageInfo {
	info := c.storageInfo
	if c.storageLayout.Mode != "" {
		applyStorageMigrationStatus(&info, storage.LegacyMigrationStatus(c.storageLayout))
	}
	if c.aiManager != nil && c.aiManager.Store() != nil {
		info.ModelsPath = c.aiManager.Store().Root()
	}
	if c.llamaRuntimeStore != nil {
		info.RuntimesPath = c.llamaRuntimeStore.Root()
	}
	return info
}

func (c *AppCommands) RequestLegacyStorageMigration() (AIStorageInfo, error) {
	if c.storageLayout.Mode == "" {
		return AIStorageInfo{}, fmt.Errorf("Lumine storage layout is unavailable")
	}
	if err := storage.RequestLegacyCopy(c.storageLayout); err != nil {
		return AIStorageInfo{}, err
	}
	return c.GetAIStorageInfo(), nil
}

func (c *AppCommands) CancelLegacyStorageMigration() (AIStorageInfo, error) {
	if c.storageLayout.Mode == "" {
		return AIStorageInfo{}, fmt.Errorf("Lumine storage layout is unavailable")
	}
	if err := storage.CancelLegacyCopy(c.storageLayout); err != nil {
		return AIStorageInfo{}, err
	}
	return c.GetAIStorageInfo(), nil
}
