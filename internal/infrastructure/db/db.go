package db

import (
	"database/sql"
	"embed"
	"fmt"
	"log/slog"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"

	_ "modernc.org/sqlite"
)

type DB struct {
	*sql.DB
}

//go:embed migrations/*.sql
var migrationsFS embed.FS

func Open(appDir string) (*DB, error) {
	dbPath := filepath.Join(appDir, "lumine.db")
	slog.Info("opening database", "path", dbPath)

	// WAL is useful only when reads are allowed to use connections other than the
	// active writer. The previous SetMaxOpenConns(1) serialized the scanner and
	// the UI even though WAL was enabled, making browsing stall during scans.
	dsn := dbPath + "?_pragma=journal_mode(WAL)&_pragma=foreign_keys(1)&_pragma=busy_timeout(5000)&_pragma=synchronous(NORMAL)&_pragma=temp_store(MEMORY)"
	database, err := sql.Open("sqlite", dsn)
	if err != nil {
		return nil, fmt.Errorf("open db: %w", err)
	}

	// SQLite still has a single writer, but WAL permits concurrent readers. A
	// small bounded pool avoids both the old global bottleneck and an excessive
	// number of SQLite connections on large libraries.
	database.SetMaxOpenConns(4)
	database.SetMaxIdleConns(4)

	if err := database.Ping(); err != nil {
		database.Close()
		return nil, fmt.Errorf("ping db: %w", err)
	}

	if err := migrate(database); err != nil {
		database.Close()
		return nil, fmt.Errorf("migrate: %w", err)
	}

	return &DB{database}, nil
}

func EnsureAppDir() (string, error) {
	home, err := os.UserHomeDir()
	if err != nil {
		return "", fmt.Errorf("user home dir: %w", err)
	}
	appDir := filepath.Join(home, "lumine")

	// Lumine no longer creates thumbnail or temporary image directories. The
	// database and logs are the only persistent app-owned data required here.
	for _, d := range []string{appDir, filepath.Join(appDir, "logs")} {
		if err := os.MkdirAll(d, 0755); err != nil {
			return "", fmt.Errorf("mkdir %s: %w", d, err)
		}
	}

	// Clean up only empty legacy cache directories. Never delete old contents
	// automatically; users may want to inspect/remove them themselves.
	for _, legacy := range []string{
		filepath.Join(appDir, "thumb-cache"),
		filepath.Join(appDir, "tmp"),
	} {
		entries, readErr := os.ReadDir(legacy)
		if readErr == nil && len(entries) == 0 {
			_ = os.Remove(legacy)
		}
	}

	return appDir, nil
}

func migrate(db *sql.DB) error {
	entries, err := migrationsFS.ReadDir("migrations")
	if err != nil {
		return fmt.Errorf("read migrations dir: %w", err)
	}

	var names []string
	for _, e := range entries {
		if strings.HasSuffix(e.Name(), ".sql") {
			names = append(names, e.Name())
		}
	}
	sort.Strings(names)

	_, err = db.Exec("CREATE TABLE IF NOT EXISTS _migrations (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE, applied_at DATETIME DEFAULT CURRENT_TIMESTAMP)")
	if err != nil {
		return fmt.Errorf("create _migrations table: %w", err)
	}

	for _, name := range names {
		var count int
		err := db.QueryRow("SELECT COUNT(*) FROM _migrations WHERE name = ?", name).Scan(&count)
		if err != nil {
			return fmt.Errorf("check migration %s: %w", name, err)
		}
		if count > 0 {
			continue
		}

		content, err := migrationsFS.ReadFile("migrations/" + name)
		if err != nil {
			return fmt.Errorf("read migration %s: %w", name, err)
		}

		slog.Info("applying migration", "name", name)

		tx, err := db.Begin()
		if err != nil {
			return fmt.Errorf("begin tx for %s: %w", name, err)
		}

		if _, err := tx.Exec(string(content)); err != nil {
			tx.Rollback()
			return fmt.Errorf("exec migration %s: %w", name, err)
		}

		if _, err := tx.Exec("INSERT INTO _migrations (name) VALUES (?)", name); err != nil {
			tx.Rollback()
			return fmt.Errorf("record migration %s: %w", name, err)
		}

		if err := tx.Commit(); err != nil {
			return fmt.Errorf("commit migration %s: %w", name, err)
		}
	}

	return nil
}

func ParseVersion(name string) int {
	parts := strings.SplitN(name, "_", 2)
	v, _ := strconv.Atoi(parts[0])
	return v
}
