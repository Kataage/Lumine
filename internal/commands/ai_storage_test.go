package commands

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/kataage/lumine/internal/infrastructure/storage"
)

func TestAIStorageInfoReflectsResolvedLayoutAndMigrationState(t *testing.T) {
	cmd := setupCommands(t)
	base := t.TempDir()
	legacy := filepath.Join(base, "legacy")
	target := filepath.Join(base, "target")
	if err := os.MkdirAll(legacy, 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(legacy, "lumine.db"), []byte("db"), 0644); err != nil {
		t.Fatal(err)
	}

	layout := storage.Layout{
		Mode:              storage.ModeInstalled,
		RootDir:           legacy,
		DataDir:           legacy,
		LogsDir:           filepath.Join(legacy, "logs"),
		ModelsDir:         filepath.Join(legacy, "models"),
		RuntimesDir:       filepath.Join(legacy, "runtimes"),
		SemanticIndexDir:  filepath.Join(legacy, "semantic-index"),
		WebviewDataDir:    filepath.Join(legacy, "webview2"),
		PreferredRootDir:  target,
		LegacyRootDir:     legacy,
		LegacyDetected:    true,
		UsingLegacy:       true,
	}
	ConfigureStorageLayout(cmd, layout)

	info := cmd.GetAIStorageInfo()
	if info.Mode != "installed" || info.RootPath != legacy || info.DatabasePath != filepath.Join(legacy, "lumine.db") {
		t.Fatalf("unexpected storage info: %+v", info)
	}
	if info.WebviewDataPath != filepath.Join(legacy, "webview2") {
		t.Fatalf("WebView2 storage path = %q", info.WebviewDataPath)
	}
	if !info.UsingLegacy || !info.MigrationAvailable || info.MigrationPending {
		t.Fatalf("unexpected initial migration state: %+v", info)
	}
	if info.MigrationSourcePath != legacy || info.MigrationTargetPath != target || !info.MigrationRequiresRestart {
		t.Fatalf("unexpected migration paths: %+v", info)
	}

	pending, err := cmd.RequestLegacyStorageMigration()
	if err != nil {
		t.Fatal(err)
	}
	if !pending.MigrationPending {
		t.Fatalf("migration request was not reflected in storage info: %+v", pending)
	}

	cancelled, err := cmd.CancelLegacyStorageMigration()
	if err != nil {
		t.Fatal(err)
	}
	if cancelled.MigrationPending {
		t.Fatalf("migration remained pending after cancel: %+v", cancelled)
	}
	if _, err := os.Stat(filepath.Join(target, ".lumine-import-legacy")); !os.IsNotExist(err) {
		t.Fatalf("migration marker still exists after cancel: %v", err)
	}
}

func TestAIStorageMigrationCommandsRequireConfiguredLayout(t *testing.T) {
	cmd := setupCommands(t)

	if _, err := cmd.RequestLegacyStorageMigration(); err == nil {
		t.Fatal("request without configured layout should fail")
	}
	if _, err := cmd.CancelLegacyStorageMigration(); err == nil {
		t.Fatal("cancel without configured layout should fail")
	}
}
