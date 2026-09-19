package storage

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func writeTestFile(t *testing.T, path, value string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(value), 0644); err != nil {
		t.Fatal(err)
	}
}

func readTestFile(t *testing.T, path string) string {
	t.Helper()
	value, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	return string(value)
}

func TestInstalledLegacyMigrationCopiesToPreferredAndPreservesSource(t *testing.T) {
	base := t.TempDir()
	home := filepath.Join(base, "home")
	local := filepath.Join(base, "local")
	legacy := filepath.Join(home, "lumine")

	writeTestFile(t, filepath.Join(legacy, "lumine.db"), "legacy-db")
	writeTestFile(t, filepath.Join(legacy, "models", "model.gguf"), "legacy-model")
	writeTestFile(t, filepath.Join(legacy, "runtimes", "llama.cpp", "server.exe"), "legacy-runtime")
	writeTestFile(t, filepath.Join(legacy, "semantic-index", "index.bin"), "legacy-index")
	writeTestFile(t, filepath.Join(legacy, "logs", "lumine.log"), "legacy-log")

	layout, err := Resolve(ResolveOptions{
		Mode:         ModeInstalled,
		HomeDir:      home,
		LocalDataDir: local,
	})
	if err != nil {
		t.Fatal(err)
	}
	if !layout.UsingLegacy {
		t.Fatalf("expected legacy compatibility layout: %+v", layout)
	}

	status := LegacyMigrationStatus(layout)
	if !status.Available || status.Pending {
		t.Fatalf("unexpected initial migration status: %+v", status)
	}
	if err := RequestLegacyCopy(layout); err != nil {
		t.Fatal(err)
	}
	status = LegacyMigrationStatus(layout)
	if !status.Pending {
		t.Fatalf("migration request was not persisted: %+v", status)
	}

	migrated, err := ApplyPendingLegacyCopy(layout)
	if err != nil {
		t.Fatal(err)
	}
	if !migrated {
		t.Fatal("scheduled installed migration was not applied")
	}
	completedStatus := LegacyMigrationStatus(layout)
	if completedStatus.Pending {
		t.Fatal("migration marker remained after successful copy")
	}
	if completedStatus.Available {
		t.Fatal("completed legacy migration should not be offered again")
	}

	preferred := filepath.Join(local, "Lumine")
	for path, want := range map[string]string{
		filepath.Join(preferred, "lumine.db"):                         "legacy-db",
		filepath.Join(preferred, "models", "model.gguf"):             "legacy-model",
		filepath.Join(preferred, "runtimes", "llama.cpp", "server.exe"): "legacy-runtime",
		filepath.Join(preferred, "semantic-index", "index.bin"):      "legacy-index",
		filepath.Join(preferred, "logs", "lumine.log"):               "legacy-log",
	} {
		if got := readTestFile(t, path); got != want {
			t.Fatalf("migrated file %s = %q, want %q", path, got, want)
		}
	}

	// Copy migration is intentionally non-destructive.
	if got := readTestFile(t, filepath.Join(legacy, "lumine.db")); got != "legacy-db" {
		t.Fatalf("legacy database changed: %q", got)
	}
	if got := readTestFile(t, filepath.Join(legacy, "models", "model.gguf")); got != "legacy-model" {
		t.Fatalf("legacy model changed: %q", got)
	}

	resolved, err := Resolve(ResolveOptions{
		Mode:         ModeInstalled,
		HomeDir:      home,
		LocalDataDir: local,
	})
	if err != nil {
		t.Fatal(err)
	}
	if resolved.UsingLegacy || resolved.RootDir != preferred {
		t.Fatalf("post-migration installed root = %+v", resolved)
	}
}

func TestPortableLegacyMigrationBacksUpCurrentDatabase(t *testing.T) {
	base := t.TempDir()
	home := filepath.Join(base, "home")
	local := filepath.Join(base, "local")
	legacy := filepath.Join(home, "lumine")
	portable := filepath.Join(base, "portable")

	writeTestFile(t, filepath.Join(legacy, "lumine.db"), "legacy-db")
	writeTestFile(t, filepath.Join(legacy, "models", "model.gguf"), "legacy-model")
	writeTestFile(t, filepath.Join(portable, "data", "lumine.db"), "portable-before-import")
	writeTestFile(t, filepath.Join(portable, "models", "portable-only.gguf"), "portable-model")

	layout, err := Resolve(ResolveOptions{
		Mode:           ModePortable,
		ExecutablePath: filepath.Join(portable, "Lumine-portable.exe"),
		HomeDir:        home,
		LocalDataDir:   local,
	})
	if err != nil {
		t.Fatal(err)
	}
	if !layout.LegacyDetected {
		t.Fatalf("legacy data was not detected: %+v", layout)
	}
	if err := RequestLegacyCopy(layout); err != nil {
		t.Fatal(err)
	}

	migrated, err := ApplyPendingLegacyCopy(layout)
	if err != nil {
		t.Fatal(err)
	}
	if !migrated {
		t.Fatal("portable legacy import was not applied")
	}

	if got := readTestFile(t, filepath.Join(portable, "data", "lumine.db")); got != "legacy-db" {
		t.Fatalf("portable database after import = %q", got)
	}
	if got := readTestFile(t, filepath.Join(portable, "models", "model.gguf")); got != "legacy-model" {
		t.Fatalf("legacy model was not imported: %q", got)
	}
	if got := readTestFile(t, filepath.Join(portable, "models", "portable-only.gguf")); got != "portable-model" {
		t.Fatalf("unrelated portable model was lost: %q", got)
	}

	backups, err := filepath.Glob(filepath.Join(portable, "data", ".pre-legacy-import-*", "lumine.db"))
	if err != nil {
		t.Fatal(err)
	}
	if len(backups) != 1 {
		t.Fatalf("portable DB backup count = %d, want 1 (%v)", len(backups), backups)
	}
	if got := readTestFile(t, backups[0]); got != "portable-before-import" {
		t.Fatalf("portable pre-import backup = %q", got)
	}

	if got := readTestFile(t, filepath.Join(legacy, "lumine.db")); got != "legacy-db" {
		t.Fatalf("legacy source was mutated: %q", got)
	}
	if status := LegacyMigrationStatus(layout); status.Pending || status.Available {
		t.Fatalf("completed portable import should not be offered again: %+v", status)
	}
}

func TestCancelLegacyMigrationRemovesOnlyRequest(t *testing.T) {
	base := t.TempDir()
	home := filepath.Join(base, "home")
	local := filepath.Join(base, "local")
	legacy := filepath.Join(home, "lumine")
	writeTestFile(t, filepath.Join(legacy, "lumine.db"), "legacy")

	layout, err := Resolve(ResolveOptions{
		Mode:         ModeInstalled,
		HomeDir:      home,
		LocalDataDir: local,
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := RequestLegacyCopy(layout); err != nil {
		t.Fatal(err)
	}
	if !LegacyMigrationStatus(layout).Pending {
		t.Fatal("migration was not pending before cancel")
	}
	if err := CancelLegacyCopy(layout); err != nil {
		t.Fatal(err)
	}
	if LegacyMigrationStatus(layout).Pending {
		t.Fatal("migration remained pending after cancel")
	}
	if got := readTestFile(t, filepath.Join(legacy, "lumine.db")); got != "legacy" {
		t.Fatalf("cancel mutated legacy data: %q", got)
	}
}

func TestLegacyMigrationWithoutDatabaseStillReusesModels(t *testing.T) {
	base := t.TempDir()
	home := filepath.Join(base, "home")
	local := filepath.Join(base, "local")
	legacy := filepath.Join(home, "lumine")
	writeTestFile(t, filepath.Join(legacy, "models", "large-model.gguf"), strings.Repeat("m", 128))

	layout, err := Resolve(ResolveOptions{
		Mode:         ModeInstalled,
		HomeDir:      home,
		LocalDataDir: local,
	})
	if err != nil {
		t.Fatal(err)
	}
	if layout.UsingLegacy {
		t.Fatal("models-only legacy root should not replace the new installed DB root")
	}
	if !layout.LegacyDetected {
		t.Fatal("models-only legacy root should still be importable")
	}

	// Simulate the new install having already created its own DB.
	writeTestFile(t, DatabasePath(layout), "new-db")
	if err := RequestLegacyCopy(layout); err != nil {
		t.Fatal(err)
	}
	if migrated, err := ApplyPendingLegacyCopy(layout); err != nil || !migrated {
		t.Fatalf("models-only migration = %v, %v", migrated, err)
	}
	if got := readTestFile(t, DatabasePath(layout)); got != "new-db" {
		t.Fatalf("models-only import replaced current DB: %q", got)
	}
	if got := readTestFile(t, filepath.Join(layout.ModelsDir, "large-model.gguf")); len(got) != 128 {
		t.Fatalf("legacy model was not copied: len=%d", len(got))
	}
}


func TestFailedInstalledMigrationKeepsLegacyRootSelected(t *testing.T) {
	base := t.TempDir()
	home := filepath.Join(base, "home")
	local := filepath.Join(base, "local")
	legacy := filepath.Join(home, "lumine")
	writeTestFile(t, filepath.Join(legacy, "lumine.db"), "legacy-db")
	// Force asset-copy failure before the DB switch.
	writeTestFile(t, filepath.Join(legacy, "models"), "not-a-directory")

	layout, err := Resolve(ResolveOptions{Mode: ModeInstalled, HomeDir: home, LocalDataDir: local})
	if err != nil {
		t.Fatal(err)
	}
	if err := RequestLegacyCopy(layout); err != nil {
		t.Fatal(err)
	}
	if migrated, err := ApplyPendingLegacyCopy(layout); err == nil || migrated {
		t.Fatalf("expected migration failure, got migrated=%v err=%v", migrated, err)
	}

	preferred := filepath.Join(local, "Lumine")
	if _, err := os.Stat(filepath.Join(preferred, "lumine.db")); !os.IsNotExist(err) {
		t.Fatalf("failed migration unexpectedly switched target DB: %v", err)
	}

	resolved, err := Resolve(ResolveOptions{Mode: ModeInstalled, HomeDir: home, LocalDataDir: local})
	if err != nil {
		t.Fatal(err)
	}
	if !resolved.UsingLegacy || resolved.RootDir != legacy {
		t.Fatalf("pending failed migration did not pin legacy root: %+v", resolved)
	}
	if !LegacyMigrationStatus(resolved).Pending {
		t.Fatal("failed migration request should remain pending for retry/cancel")
	}
}

func TestFailedPortableMigrationKeepsCurrentDatabase(t *testing.T) {
	base := t.TempDir()
	home := filepath.Join(base, "home")
	portable := filepath.Join(base, "portable")
	legacy := filepath.Join(home, "lumine")
	writeTestFile(t, filepath.Join(legacy, "lumine.db"), "legacy-db")
	writeTestFile(t, filepath.Join(legacy, "models"), "not-a-directory")
	writeTestFile(t, filepath.Join(portable, "data", "lumine.db"), "portable-db")

	layout, err := Resolve(ResolveOptions{
		Mode:           ModePortable,
		ExecutablePath: filepath.Join(portable, "Lumine-portable.exe"),
		HomeDir:        home,
		LocalDataDir:   filepath.Join(base, "local"),
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := RequestLegacyCopy(layout); err != nil {
		t.Fatal(err)
	}
	if migrated, err := ApplyPendingLegacyCopy(layout); err == nil || migrated {
		t.Fatalf("expected portable migration failure, got migrated=%v err=%v", migrated, err)
	}
	if got := readTestFile(t, filepath.Join(portable, "data", "lumine.db")); got != "portable-db" {
		t.Fatalf("failed portable migration changed current DB: %q", got)
	}
	if !LegacyMigrationStatus(layout).Pending {
		t.Fatal("failed portable migration request should remain pending")
	}
}
