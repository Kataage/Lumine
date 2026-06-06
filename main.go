package main

import (
	"context"
	"embed"
	"log"
	"log/slog"
	"net/http"
	"os"
	"path/filepath"
	"strings"

	"github.com/kataage/lumine/internal/commands"
	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/kataage/lumine/internal/infrastructure/scanner"
	"github.com/wailsapp/wails/v2"
	"github.com/wailsapp/wails/v2/pkg/options"
	"github.com/wailsapp/wails/v2/pkg/options/assetserver"
	"github.com/wailsapp/wails/v2/pkg/options/windows"
)

//go:embed all:frontend/dist
var assets embed.FS

type localFileHandler struct{}

func (h *localFileHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	path := r.URL.Path
	if !strings.HasPrefix(path, "/local/") {
		http.NotFound(w, r)
		return
	}

	filePath := strings.TrimPrefix(path, "/local/")
	absPath, err := filepath.Abs(filepath.Clean(filePath))
	if err != nil {
		http.NotFound(w, r)
		return
	}
	if strings.Contains(absPath, "..") {
		http.NotFound(w, r)
		return
	}

	info, err := os.Stat(absPath)
	if err != nil {
		http.NotFound(w, r)
		return
	}

	if info.IsDir() {
		http.NotFound(w, r)
		return
	}

	ext := strings.ToLower(filepath.Ext(absPath))
	contentTypes := map[string]string{
		".jpg":  "image/jpeg",
		".jpeg": "image/jpeg",
		".png":  "image/png",
		".gif":  "image/gif",
		".bmp":  "image/bmp",
		".webp": "image/webp",
		".tiff": "image/tiff",
		".tif":  "image/tiff",
		".svg":  "image/svg+xml",
		".avif": "image/avif",
		".ico":  "image/x-icon",
	}

	if ct, ok := contentTypes[ext]; ok {
		w.Header().Set("Content-Type", ct)
	}
	w.Header().Set("Cache-Control", "public, max-age=86400")

	http.ServeFile(w, r, absPath)
}

func main() {
	slog.Info("starting Lumine")

	appDir, err := db.EnsureAppDir()
	if err != nil {
		log.Fatal("failed to create app directory:", err)
	}

	database, err := db.Open(appDir)
	if err != nil {
		log.Fatal("failed to open database:", err)
	}
	defer database.Close()

	scanSvc := scanner.NewScanner(
		db.NewAssetRepo(database),
		db.NewLibraryRepo(database),
		db.NewJobLogRepo(database),
	)
	cmd := commands.New(database, scanSvc)

	err = wails.Run(&options.App{
		Title:  "Lumine",
		Width:  1280,
		Height: 800,
		AssetServer: &assetserver.Options{
			Assets:     assets,
			Middleware: localFileMiddleware,
		},
		BackgroundColour: &options.RGBA{R: 27, G: 27, B: 30, A: 1},
		OnStartup: func(ctx context.Context) {
			cmd.SetContext(ctx)
			slog.Info("Lumine started")
		},
		Bind: []interface{}{
			cmd,
		},
		Windows: &windows.Options{
			WebviewIsTransparent: false,
			WindowIsTranslucent:  false,
		},
	})

	if err != nil {
		log.Fatal(err)
	}
}

func localFileMiddleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if strings.HasPrefix(r.URL.Path, "/local/") {
			handler := &localFileHandler{}
			handler.ServeHTTP(w, r)
			return
		}
		next.ServeHTTP(w, r)
	})
}
