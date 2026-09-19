package storage

import (
	"errors"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"time"
)

const (
	legacyImportMarkerName   = ".lumine-import-legacy"
	legacyImportCompleteName = ".lumine-legacy-import-complete"
)

type MigrationStatus struct {
	Available       bool
	Pending         bool
	SourceRoot      string
	TargetRoot      string
	RequiresRestart bool
}

func LegacyMigrationStatus(layout Layout) MigrationStatus {
	source := layout.LegacyRootDir
	target := migrationTargetRoot(layout)
	completed := fileExists(legacyImportCompletePath(layout))
	available := layout.LegacyDetected && !completed && source != "" && target != "" && !samePath(source, target)
	_, markerErr := os.Stat(legacyImportMarkerPath(layout))
	return MigrationStatus{
		Available:       available,
		Pending:         markerErr == nil,
		SourceRoot:      source,
		TargetRoot:      target,
		RequiresRestart: true,
	}
}

func RequestLegacyCopy(layout Layout) error {
	status := LegacyMigrationStatus(layout)
	if !status.Available {
		return errors.New("legacy Lumine storage is not available for migration")
	}
	marker := legacyImportMarkerPath(layout)
	if err := os.MkdirAll(filepath.Dir(marker), 0755); err != nil {
		return fmt.Errorf("create migration target directory: %w", err)
	}
	content := []byte(status.SourceRoot + "\n")
	if err := os.WriteFile(marker, content, 0644); err != nil {
		return fmt.Errorf("schedule legacy storage migration: %w", err)
	}
	return nil
}

func CancelLegacyCopy(layout Layout) error {
	err := os.Remove(legacyImportMarkerPath(layout))
	if err != nil && !errors.Is(err, os.ErrNotExist) {
		return fmt.Errorf("cancel legacy storage migration: %w", err)
	}
	return nil
}

// ApplyPendingLegacyCopy runs before Lumine opens SQLite or starts any AI
// runtime. It copies legacy state into the current distribution's preferred
// root, leaves the source untouched, and removes the request marker only after
// the copy has completed successfully.
func ApplyPendingLegacyCopy(layout Layout) (bool, error) {
	status := LegacyMigrationStatus(layout)
	if !status.Pending {
		return false, nil
	}
	if fileExists(legacyImportCompletePath(layout)) {
		if err := os.Remove(legacyImportMarkerPath(layout)); err != nil && !errors.Is(err, os.ErrNotExist) {
			return false, fmt.Errorf("remove stale completed migration request: %w", err)
		}
		return true, nil
	}
	if !status.Available {
		return false, errors.New("scheduled legacy migration no longer has a valid source and target")
	}

	sourceInfo, err := os.Stat(status.SourceRoot)
	if err != nil {
		return false, fmt.Errorf("stat legacy Lumine storage: %w", err)
	}
	if !sourceInfo.IsDir() {
		return false, errors.New("legacy Lumine storage is not a directory")
	}

	target := migrationTargetLayout(layout)
	if target.DataDir == "" || target.ModelsDir == "" || target.RuntimesDir == "" {
		return false, errors.New("migration target layout is incomplete")
	}
	for _, dir := range []string{
		target.DataDir,
		target.LogsDir,
		target.ModelsDir,
		target.RuntimesDir,
		target.SemanticIndexDir,
	} {
		if err := os.MkdirAll(dir, 0755); err != nil {
			return false, fmt.Errorf("prepare migration target %s: %w", dir, err)
		}
	}

	// Copy large reusable assets first. The database is switched last so a
	// partial model/runtime copy never makes an incomplete target look active.
	mappings := [][2]string{
		{filepath.Join(status.SourceRoot, "models"), target.ModelsDir},
		{filepath.Join(status.SourceRoot, "runtimes"), target.RuntimesDir},
		{filepath.Join(status.SourceRoot, "semantic-index"), target.SemanticIndexDir},
		{filepath.Join(status.SourceRoot, "logs"), target.LogsDir},
	}
	for _, mapping := range mappings {
		if err := copyTreeIfPresent(mapping[0], mapping[1]); err != nil {
			return false, err
		}
	}

	if err := backupCurrentDatabase(target.DataDir); err != nil {
		return false, err
	}
	if err := copyLegacyDatabase(status.SourceRoot, target.DataDir); err != nil {
		return false, err
	}

	if err := os.WriteFile(legacyImportCompletePath(layout), []byte(status.SourceRoot+"\n"), 0644); err != nil {
		return false, fmt.Errorf("record completed legacy migration: %w", err)
	}
	if err := os.Remove(legacyImportMarkerPath(layout)); err != nil && !errors.Is(err, os.ErrNotExist) {
		return false, fmt.Errorf("remove completed migration request: %w", err)
	}
	return true, nil
}

func migrationTargetRoot(layout Layout) string {
	if layout.Mode == ModePortable {
		return layout.RootDir
	}
	if layout.PreferredRootDir != "" {
		return layout.PreferredRootDir
	}
	return layout.RootDir
}

func migrationTargetLayout(layout Layout) Layout {
	root := migrationTargetRoot(layout)
	if layout.Mode == ModePortable {
		dataDir := filepath.Join(root, "data")
		return Layout{
			Mode:             ModePortable,
			RootDir:          root,
			DataDir:          dataDir,
			LogsDir:          filepath.Join(dataDir, "logs"),
			ModelsDir:        filepath.Join(root, "models"),
			RuntimesDir:      filepath.Join(root, "runtimes"),
			SemanticIndexDir: filepath.Join(dataDir, "semantic-index"),
		}
	}
	return installedLayout(root)
}

func legacyImportMarkerPath(layout Layout) string {
	return filepath.Join(migrationTargetRoot(layout), legacyImportMarkerName)
}

func legacyImportCompletePath(layout Layout) string {
	return filepath.Join(migrationTargetRoot(layout), legacyImportCompleteName)
}

func backupCurrentDatabase(targetDataDir string) error {
	dbPath := filepath.Join(targetDataDir, "lumine.db")
	if _, err := os.Stat(dbPath); errors.Is(err, os.ErrNotExist) {
		return nil
	} else if err != nil {
		return fmt.Errorf("stat current Lumine database: %w", err)
	}

	backupDir := filepath.Join(
		targetDataDir,
		".pre-legacy-import-"+time.Now().UTC().Format("20060102T150405.000000000Z"),
	)
	if err := os.MkdirAll(backupDir, 0755); err != nil {
		return fmt.Errorf("create pre-import database backup: %w", err)
	}
	for _, name := range []string{"lumine.db", "lumine.db-wal"} {
		source := filepath.Join(targetDataDir, name)
		if _, err := os.Stat(source); errors.Is(err, os.ErrNotExist) {
			continue
		} else if err != nil {
			return fmt.Errorf("stat current database file %s: %w", name, err)
		}
		if err := copyFileAtomic(source, filepath.Join(backupDir, name)); err != nil {
			return fmt.Errorf("backup current database file %s: %w", name, err)
		}
	}
	return nil
}

func copyLegacyDatabase(sourceRoot, targetDataDir string) error {
	sourceDB := filepath.Join(sourceRoot, "lumine.db")
	if _, err := os.Stat(sourceDB); errors.Is(err, os.ErrNotExist) {
		// A legacy install can contain only downloaded models/runtimes. That is
		// still useful to import and does not require a database replacement.
		return nil
	} else if err != nil {
		return fmt.Errorf("stat legacy database: %w", err)
	}

	for _, stale := range []string{"lumine.db-wal", "lumine.db-shm"} {
		if err := os.Remove(filepath.Join(targetDataDir, stale)); err != nil && !errors.Is(err, os.ErrNotExist) {
			return fmt.Errorf("remove stale target database sidecar %s: %w", stale, err)
		}
	}
	if err := copyFileAtomic(sourceDB, filepath.Join(targetDataDir, "lumine.db")); err != nil {
		return fmt.Errorf("copy legacy database: %w", err)
	}
	sourceWAL := filepath.Join(sourceRoot, "lumine.db-wal")
	if _, err := os.Stat(sourceWAL); err == nil {
		if err := copyFileAtomic(sourceWAL, filepath.Join(targetDataDir, "lumine.db-wal")); err != nil {
			return fmt.Errorf("copy legacy database WAL: %w", err)
		}
	} else if !errors.Is(err, os.ErrNotExist) {
		return fmt.Errorf("stat legacy database WAL: %w", err)
	}
	return nil
}

func copyTreeIfPresent(sourceRoot, targetRoot string) error {
	sourceInfo, err := os.Stat(sourceRoot)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil {
		return fmt.Errorf("stat migration source %s: %w", sourceRoot, err)
	}
	if !sourceInfo.IsDir() {
		return fmt.Errorf("migration source %s is not a directory", sourceRoot)
	}

	return filepath.WalkDir(sourceRoot, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		relative, err := filepath.Rel(sourceRoot, path)
		if err != nil {
			return err
		}
		target := targetRoot
		if relative != "." {
			target = filepath.Join(targetRoot, relative)
		}

		if entry.Type()&os.ModeSymlink != 0 {
			return fmt.Errorf("legacy migration does not follow symbolic link %s", path)
		}
		if entry.IsDir() {
			return os.MkdirAll(target, 0755)
		}
		info, err := entry.Info()
		if err != nil {
			return err
		}
		if !info.Mode().IsRegular() {
			return fmt.Errorf("legacy migration only supports regular files: %s", path)
		}
		if err := copyFileAtomic(path, target); err != nil {
			return fmt.Errorf("copy legacy file %s: %w", path, err)
		}
		return nil
	})
}

func copyFileAtomic(source, target string) error {
	input, err := os.Open(source)
	if err != nil {
		return err
	}
	defer input.Close()

	info, err := input.Stat()
	if err != nil {
		return err
	}
	if !info.Mode().IsRegular() {
		return fmt.Errorf("source is not a regular file: %s", source)
	}
	if err := os.MkdirAll(filepath.Dir(target), 0755); err != nil {
		return err
	}

	temp, err := os.CreateTemp(filepath.Dir(target), ".lumine-copy-*")
	if err != nil {
		return err
	}
	tempPath := temp.Name()
	closed := false
	defer func() {
		if !closed {
			_ = temp.Close()
		}
		_ = os.Remove(tempPath)
	}()

	if _, err := io.Copy(temp, input); err != nil {
		return err
	}
	if err := temp.Sync(); err != nil {
		return err
	}
	if err := temp.Chmod(info.Mode().Perm()); err != nil {
		return err
	}
	if err := temp.Close(); err != nil {
		return err
	}
	closed = true

	if err := os.Rename(tempPath, target); err != nil {
		// Windows does not replace an existing destination with os.Rename.
		if removeErr := os.Remove(target); removeErr != nil && !errors.Is(removeErr, os.ErrNotExist) {
			return err
		}
		if retryErr := os.Rename(tempPath, target); retryErr != nil {
			return retryErr
		}
	}
	return nil
}

func samePath(a, b string) bool {
	if a == "" || b == "" {
		return false
	}
	absA, errA := filepath.Abs(a)
	absB, errB := filepath.Abs(b)
	if errA != nil || errB != nil {
		return filepath.Clean(a) == filepath.Clean(b)
	}
	return filepath.Clean(absA) == filepath.Clean(absB)
}
