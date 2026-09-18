package main

import (
	"context"
	"embed"
	"fmt"
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

var imageContentTypes = map[string]string{
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
	".apng": "image/apng",
	".ico":  "image/x-icon",
}

type localFileHandler struct{}

func resolveLocalImagePath(r *http.Request) (string, string, error) {
	filePath := r.URL.Query().Get("path")

	// Keep the old /local/C:/... form working for already-rendered views while
	// the frontend migrates to the query-string form, which safely handles
	// spaces, #, ?, Unicode, and Windows drive letters.
	if filePath == "" && strings.HasPrefix(r.URL.Path, "/local/") {
		filePath = strings.TrimPrefix(r.URL.Path, "/local/")
	}
	if filePath == "" {
		return "", "", fmt.Errorf("missing image path")
	}

	cleanPath := filepath.Clean(filePath)
	if !filepath.IsAbs(cleanPath) {
		return "", "", fmt.Errorf("image path must be absolute")
	}

	ext := strings.ToLower(filepath.Ext(cleanPath))
	contentType, ok := imageContentTypes[ext]
	if !ok {
		return "", "", fmt.Errorf("unsupported image extension: %s", ext)
	}

	return cleanPath, contentType, nil
}

func (h *localFileHandler) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	filePath, contentType, err := resolveLocalImagePath(r)
	if err != nil {
		http.NotFound(w, r)
		return
	}

	file, err := os.Open(filePath)
	if err != nil {
		http.NotFound(w, r)
		return
	}
	defer file.Close()

	info, err := file.Stat()
	if err != nil || info.IsDir() {
		http.NotFound(w, r)
		return
	}

	w.Header().Set("Content-Type", contentType)
	w.Header().Set("X-Content-Type-Options", "nosniff")
	w.Header().Set("Cross-Origin-Resource-Policy", "same-origin")

	// Do not let WebView2 build a second on-disk image library behind Lumine.
	// Grid/detail previews are cached only in the frontend's bounded in-memory
	// bitmap cache. http.ServeContent still provides byte-range support for the
	// full-resolution viewer.
	w.Header().Set("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
	w.Header().Set("Pragma", "no-cache")
	w.Header().Set("Expires", "0")

	http.ServeContent(w, r, info.Name(), info.ModTime(), file)
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
		Title:     "Lumine",
		Width:     1280,
		Height:    800,
		MinWidth:  800,
		MinHeight: 600,
		AssetServer: &assetserver.Options{
			Assets:     assets,
			Middleware: localFileMiddleware,
		},
		BackgroundColour: &options.RGBA{R: 9, G: 9, B: 11, A: 1},
		OnStartup: func(ctx context.Context) {
			cmd.SetContext(ctx)
			slog.Info("Lumine started")
		},
		Bind: []interface{}{
			cmd,
		},
		// Keep native Windows chrome so resize, Snap Layouts, maximise/restore,
		// and multi-monitor movement retain their normal shell behaviour. The
		// native frame is themed to visually continue Lumine's app header.
		Windows: &windows.Options{
			WebviewIsTransparent: false,
			WindowIsTranslucent:  false,
			Theme:                windows.Dark,
			CustomTheme: &windows.ThemeSettings{
				DarkModeTitleBar:           windows.RGB(9, 9, 11),
				DarkModeTitleBarInactive:   windows.RGB(9, 9, 11),
				DarkModeTitleText:          windows.RGB(228, 228, 231),
				DarkModeTitleTextInactive:  windows.RGB(161, 161, 170),
				DarkModeBorder:             windows.RGB(39, 39, 42),
				DarkModeBorderInactive:     windows.RGB(24, 24, 27),
				LightModeTitleBar:          windows.RGB(9, 9, 11),
				LightModeTitleBarInactive:  windows.RGB(9, 9, 11),
				LightModeTitleText:         windows.RGB(228, 228, 231),
				LightModeTitleTextInactive: windows.RGB(161, 161, 170),
				LightModeBorder:            windows.RGB(39, 39, 42),
				LightModeBorderInactive:    windows.RGB(24, 24, 27),
			},
		},
	})

	if err != nil {
		log.Fatal(err)
	}
}

func localFileMiddleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/local" || strings.HasPrefix(r.URL.Path, "/local/") {
			(&localFileHandler{}).ServeHTTP(w, r)
			return
		}
		next.ServeHTTP(w, r)
	})
}
