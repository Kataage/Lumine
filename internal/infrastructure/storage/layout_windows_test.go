//go:build windows

package storage

import (
	"path/filepath"
	"testing"
)

func TestResolveInstalledDefaultUsesLocalAppDataOnWindows(t *testing.T) {
	local := t.TempDir()
	home := t.TempDir()
	t.Setenv("LOCALAPPDATA", local)

	layout, err := Resolve(ResolveOptions{
		Mode:    ModeInstalled,
		HomeDir: home,
	})
	if err != nil {
		t.Fatal(err)
	}
	want := filepath.Join(local, "Lumine")
	if layout.RootDir != want || layout.DataDir != want {
		t.Fatalf("installed default root = %+v, want %q", layout, want)
	}
	if layout.WebviewDataDir != filepath.Join(want, "webview2") {
		t.Fatalf("installed WebView2 path = %q", layout.WebviewDataDir)
	}
}
