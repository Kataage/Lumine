package storage

import (
	"os"
	"path/filepath"
	"testing"
)

func TestResolvePortableUsesExecutableDirectoryOnly(t *testing.T) {
	base := t.TempDir()
	portableDir := filepath.Join(base, "portable")
	homeDir := filepath.Join(base, "home")
	localData := filepath.Join(base, "local")
	if err := os.MkdirAll(portableDir, 0755); err != nil {
		t.Fatal(err)
	}

	layout, err := Resolve(ResolveOptions{
		Mode:           ModePortable,
		ExecutablePath: filepath.Join(portableDir, "Lumine-portable.exe"),
		HomeDir:        homeDir,
		LocalDataDir:   localData,
	})
	if err != nil {
		t.Fatal(err)
	}

	if layout.RootDir != portableDir {
		t.Fatalf("portable root = %q, want %q", layout.RootDir, portableDir)
	}
	if layout.DataDir != filepath.Join(portableDir, "data") {
		t.Fatalf("portable data = %q", layout.DataDir)
	}
	if layout.ModelsDir != filepath.Join(portableDir, "models") {
		t.Fatalf("portable models = %q", layout.ModelsDir)
	}
	if layout.RuntimesDir != filepath.Join(portableDir, "runtimes") {
		t.Fatalf("portable runtimes = %q", layout.RuntimesDir)
	}
	if layout.SemanticIndexDir != filepath.Join(portableDir, "data", "semantic-index") {
		t.Fatalf("portable semantic index = %q", layout.SemanticIndexDir)
	}

	if err := Prepare(layout); err != nil {
		t.Fatal(err)
	}
	for _, path := range []string{
		layout.DataDir,
		layout.LogsDir,
		layout.ModelsDir,
		layout.RuntimesDir,
		layout.SemanticIndexDir,
	} {
		if info, err := os.Stat(path); err != nil || !info.IsDir() {
			t.Fatalf("portable directory %q was not created: %v", path, err)
		}
	}
	if _, err := os.Stat(filepath.Join(homeDir, "lumine")); !os.IsNotExist(err) {
		t.Fatalf("portable prepare touched legacy user root: %v", err)
	}
	if _, err := os.Stat(filepath.Join(localData, "Lumine")); !os.IsNotExist(err) {
		t.Fatalf("portable prepare touched installed data root: %v", err)
	}
}

func TestResolveInstalledUsesPreferredLocalDataForNewUser(t *testing.T) {
	base := t.TempDir()
	homeDir := filepath.Join(base, "home")
	localData := filepath.Join(base, "local")

	layout, err := Resolve(ResolveOptions{
		Mode:         ModeInstalled,
		HomeDir:      homeDir,
		LocalDataDir: localData,
	})
	if err != nil {
		t.Fatal(err)
	}
	wantRoot := filepath.Join(localData, "Lumine")
	if layout.RootDir != wantRoot || layout.DataDir != wantRoot {
		t.Fatalf("installed root = %+v, want %q", layout, wantRoot)
	}
	if layout.UsingLegacy {
		t.Fatal("new installed user unexpectedly entered legacy mode")
	}
	if !layout.Fresh {
		t.Fatal("new installed root should be marked fresh")
	}

	if err := Prepare(layout); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(wantRoot); err != nil {
		t.Fatalf("preferred installed root was not created: %v", err)
	}
	if _, err := os.Stat(filepath.Join(homeDir, "lumine")); !os.IsNotExist(err) {
		t.Fatalf("new installed startup created legacy root: %v", err)
	}
}

func TestResolveInstalledKeepsExistingLegacyDataWithoutCreatingPreferredRoot(t *testing.T) {
	base := t.TempDir()
	homeDir := filepath.Join(base, "home")
	localData := filepath.Join(base, "local")
	legacyRoot := filepath.Join(homeDir, "lumine")
	if err := os.MkdirAll(filepath.Join(legacyRoot, "models"), 0755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(legacyRoot, "lumine.db"), []byte("legacy"), 0644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(legacyRoot, "models", "model.gguf"), []byte("model"), 0644); err != nil {
		t.Fatal(err)
	}

	layout, err := Resolve(ResolveOptions{
		Mode:         ModeInstalled,
		HomeDir:      homeDir,
		LocalDataDir: localData,
	})
	if err != nil {
		t.Fatal(err)
	}
	if !layout.UsingLegacy || !layout.LegacyDetected {
		t.Fatalf("legacy installation not detected: %+v", layout)
	}
	if layout.RootDir != legacyRoot {
		t.Fatalf("active root = %q, want legacy %q", layout.RootDir, legacyRoot)
	}
	if layout.PreferredRootDir != filepath.Join(localData, "Lumine") {
		t.Fatalf("preferred migration target = %q", layout.PreferredRootDir)
	}

	if err := Prepare(layout); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(layout.PreferredRootDir); !os.IsNotExist(err) {
		t.Fatalf("compatibility startup created preferred root before migration: %v", err)
	}
	modelBytes, err := os.ReadFile(filepath.Join(layout.ModelsDir, "model.gguf"))
	if err != nil || string(modelBytes) != "model" {
		t.Fatalf("legacy model was not reusable: %v", err)
	}
}

func TestResolveInstalledPrefersMigratedLocalDataWhenBothExist(t *testing.T) {
	base := t.TempDir()
	homeDir := filepath.Join(base, "home")
	localData := filepath.Join(base, "local")
	legacyRoot := filepath.Join(homeDir, "lumine")
	preferredRoot := filepath.Join(localData, "Lumine")
	for _, root := range []string{legacyRoot, preferredRoot} {
		if err := os.MkdirAll(root, 0755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(filepath.Join(root, "lumine.db"), []byte(root), 0644); err != nil {
			t.Fatal(err)
		}
	}

	layout, err := Resolve(ResolveOptions{
		Mode:         ModeInstalled,
		HomeDir:      homeDir,
		LocalDataDir: localData,
	})
	if err != nil {
		t.Fatal(err)
	}
	if layout.UsingLegacy {
		t.Fatal("preferred LocalAppData DB should win after migration")
	}
	if layout.RootDir != preferredRoot {
		t.Fatalf("active root = %q, want %q", layout.RootDir, preferredRoot)
	}
	if !layout.LegacyDetected {
		t.Fatal("legacy data should remain detectable without being selected")
	}
}

func TestPortableFolderMovePreservesRelativeState(t *testing.T) {
	base := t.TempDir()
	homeDir := filepath.Join(base, "home")
	localData := filepath.Join(base, "local")
	first := filepath.Join(base, "Lumine-A")
	if err := os.MkdirAll(first, 0755); err != nil {
		t.Fatal(err)
	}

	firstLayout, err := Resolve(ResolveOptions{
		Mode:           ModePortable,
		ExecutablePath: filepath.Join(first, "Lumine-portable.exe"),
		HomeDir:        homeDir,
		LocalDataDir:   localData,
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := Prepare(firstLayout); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(DatabasePath(firstLayout), []byte("portable-db"), 0644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(firstLayout.ModelsDir, "model.gguf"), []byte("model"), 0644); err != nil {
		t.Fatal(err)
	}

	second := filepath.Join(base, "Lumine-B")
	if err := os.Rename(first, second); err != nil {
		t.Fatal(err)
	}
	secondLayout, err := Resolve(ResolveOptions{
		Mode:           ModePortable,
		ExecutablePath: filepath.Join(second, "Lumine-portable.exe"),
		HomeDir:        homeDir,
		LocalDataDir:   localData,
	})
	if err != nil {
		t.Fatal(err)
	}
	if secondLayout.RootDir != second {
		t.Fatalf("moved portable root = %q, want %q", secondLayout.RootDir, second)
	}
	if data, err := os.ReadFile(DatabasePath(secondLayout)); err != nil || string(data) != "portable-db" {
		t.Fatalf("portable DB did not move with folder: data=%q err=%v", data, err)
	}
	if data, err := os.ReadFile(filepath.Join(secondLayout.ModelsDir, "model.gguf")); err != nil || string(data) != "model" {
		t.Fatalf("portable model did not move with folder: data=%q err=%v", data, err)
	}
}
