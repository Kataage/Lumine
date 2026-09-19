package storage

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
)

type Mode string

const (
	ModeInstalled Mode = "installed"
	ModePortable  Mode = "portable"
)

type Layout struct {
	Mode              Mode
	RootDir           string
	DataDir           string
	LogsDir           string
	ModelsDir         string
	RuntimesDir       string
	SemanticIndexDir  string
	PreferredRootDir  string
	LegacyRootDir     string
	LegacyDetected    bool
	UsingLegacy       bool
	Fresh             bool
}

type ResolveOptions struct {
	Mode           Mode
	ExecutablePath string
	LocalDataDir   string
	HomeDir        string
}

func Resolve(options ResolveOptions) (Layout, error) {
	if options.Mode != ModeInstalled && options.Mode != ModePortable {
		return Layout{}, fmt.Errorf("unsupported Lumine distribution mode %q", options.Mode)
	}

	homeDir := options.HomeDir
	if homeDir == "" {
		value, err := os.UserHomeDir()
		if err != nil {
			return Layout{}, fmt.Errorf("resolve user home: %w", err)
		}
		homeDir = value
	}
	legacyRoot := filepath.Join(homeDir, "lumine")
	legacyDetected := legacyStorageExists(legacyRoot)

	if options.Mode == ModePortable {
		executablePath := options.ExecutablePath
		if executablePath == "" {
			value, err := os.Executable()
			if err != nil {
				return Layout{}, fmt.Errorf("resolve Lumine executable: %w", err)
			}
			executablePath = value
		}
		executablePath, err := filepath.Abs(executablePath)
		if err != nil {
			return Layout{}, fmt.Errorf("resolve absolute Lumine executable path: %w", err)
		}
		root := filepath.Dir(executablePath)
		dataDir := filepath.Join(root, "data")
		layout := Layout{
			Mode:             ModePortable,
			RootDir:          root,
			DataDir:          dataDir,
			LogsDir:          filepath.Join(dataDir, "logs"),
			ModelsDir:        filepath.Join(root, "models"),
			RuntimesDir:      filepath.Join(root, "runtimes"),
			SemanticIndexDir: filepath.Join(dataDir, "semantic-index"),
			PreferredRootDir: root,
			LegacyRootDir:    legacyRoot,
			LegacyDetected:   legacyDetected,
		}
		layout.Fresh = !layoutHasPersistentData(layout)
		return layout, nil
	}

	localDataDir := options.LocalDataDir
	if localDataDir == "" {
		value, err := defaultInstalledDataBase()
		if err != nil {
			return Layout{}, err
		}
		localDataDir = value
	}
	preferredRoot := filepath.Join(localDataDir, "Lumine")
	preferred := installedLayout(preferredRoot)
	preferred.PreferredRootDir = preferredRoot
	preferred.LegacyRootDir = legacyRoot
	preferred.LegacyDetected = legacyDetected

	// Existing users keep using the old root until they explicitly migrate.
	// A pending migration also pins the old root even if a previous copy attempt
	// already produced a target DB; only removing the request marker after a
	// complete copy allows the preferred root to become active.
	migrationPending := fileExists(filepath.Join(preferredRoot, legacyImportMarkerName))
	if (!installedDatabaseExists(preferredRoot) || migrationPending) && installedDatabaseExists(legacyRoot) {
		legacy := installedLayout(legacyRoot)
		legacy.PreferredRootDir = preferredRoot
		legacy.LegacyRootDir = legacyRoot
		legacy.LegacyDetected = true
		legacy.UsingLegacy = true
		legacy.Fresh = false
		return legacy, nil
	}

	preferred.Fresh = !layoutHasPersistentData(preferred)
	return preferred, nil
}

func installedLayout(root string) Layout {
	return Layout{
		Mode:             ModeInstalled,
		RootDir:          root,
		DataDir:          root,
		LogsDir:          filepath.Join(root, "logs"),
		ModelsDir:        filepath.Join(root, "models"),
		RuntimesDir:      filepath.Join(root, "runtimes"),
		SemanticIndexDir: filepath.Join(root, "semantic-index"),
	}
}

func Prepare(layout Layout) error {
	for _, dir := range []string{
		layout.DataDir,
		layout.LogsDir,
		layout.ModelsDir,
		layout.RuntimesDir,
		layout.SemanticIndexDir,
	} {
		if dir == "" {
			return errors.New("Lumine storage layout contains an empty directory")
		}
		if err := os.MkdirAll(dir, 0755); err != nil {
			return fmt.Errorf("create Lumine storage directory %s: %w", dir, err)
		}
	}
	return nil
}

func DatabasePath(layout Layout) string {
	return filepath.Join(layout.DataDir, "lumine.db")
}

func defaultInstalledDataBase() (string, error) {
	if runtime.GOOS == "windows" {
		// Go maps UserCacheDir to %LocalAppData% on Windows. Lumine stores
		// machine-local DB/model/runtime data here rather than roaming it.
		dir, err := os.UserCacheDir()
		if err != nil {
			return "", fmt.Errorf("resolve LocalAppData: %w", err)
		}
		return dir, nil
	}
	dir, err := os.UserConfigDir()
	if err != nil {
		return "", fmt.Errorf("resolve user application data: %w", err)
	}
	return dir, nil
}

func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

func installedDatabaseExists(root string) bool {
	info, err := os.Stat(filepath.Join(root, "lumine.db"))
	return err == nil && !info.IsDir()
}

func legacyStorageExists(root string) bool {
	for _, candidate := range []string{
		filepath.Join(root, "lumine.db"),
		filepath.Join(root, "models"),
		filepath.Join(root, "runtimes"),
		filepath.Join(root, "semantic-index"),
	} {
		if _, err := os.Stat(candidate); err == nil {
			return true
		}
	}
	return false
}

func layoutHasPersistentData(layout Layout) bool {
	for _, candidate := range []string{
		DatabasePath(layout),
		layout.ModelsDir,
		layout.RuntimesDir,
		layout.SemanticIndexDir,
	} {
		if _, err := os.Stat(candidate); err == nil {
			return true
		}
	}
	return false
}
