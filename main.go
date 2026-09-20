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
	"sync"
	"time"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/ai/llamacpp"
	"github.com/kataage/lumine/internal/ai/siglip2"
	"github.com/kataage/lumine/internal/ai/tagger"
	"github.com/kataage/lumine/internal/commands"
	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/kataage/lumine/internal/infrastructure/scanner"
	"github.com/kataage/lumine/internal/infrastructure/storage"
	"github.com/wailsapp/wails/v2"
	"github.com/wailsapp/wails/v2/pkg/options"
	"github.com/wailsapp/wails/v2/pkg/runtime"
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

	storageOptions := storage.ResolveOptions{Mode: distributionMode}
	storageLayout, err := storage.Resolve(storageOptions)
	if err != nil {
		log.Fatal("failed to resolve Lumine storage:", err)
	}
	if migrated, migrationErr := storage.ApplyPendingLegacyCopy(storageLayout); migrationErr != nil {
		slog.Error("scheduled Lumine storage migration failed; continuing with current storage", "error", migrationErr)
	} else if migrated {
		storageLayout, err = storage.Resolve(storageOptions)
		if err != nil {
			log.Fatal("failed to resolve migrated Lumine storage:", err)
		}
		slog.Info("scheduled Lumine storage migration completed", "root", storageLayout.RootDir)
	}
	if err := storage.Prepare(storageLayout); err != nil {
		log.Fatal("failed to prepare Lumine storage:", err)
	}
	slog.Info(
		"Lumine storage resolved",
		"mode", storageLayout.Mode,
		"root", storageLayout.RootDir,
		"data", storageLayout.DataDir,
		"using_legacy", storageLayout.UsingLegacy,
	)

	database, err := db.Open(storageLayout.DataDir)
	if err != nil {
		log.Fatal("failed to open database:", err)
	}
	defer database.Close()

	appCtx, appCancel := context.WithCancel(context.Background())
	defer appCancel()

	scanSvc := scanner.NewScanner(
		db.NewAssetRepo(database),
		db.NewLibraryRepo(database),
		db.NewJobLogRepo(database),
	)
	cmd := commands.New(database, scanSvc)
	commands.ConfigureStorageLayout(cmd, storageLayout)
	commands.ConfigureSemanticIndexRoot(cmd, storageLayout.SemanticIndexDir)
	aiManager := ai.NewManager(storageLayout.ModelsDir, cmd.GetAISettings)
	cmd.SetAIManager(aiManager)
	if err := aiManager.RegisterEngine(siglip2.EngineID, siglip2.NewEngine); err != nil {
		log.Fatal("failed to register SigLIP2 engine:", err)
	}

	if err := aiManager.RegisterEngine(tagger.EngineID, tagger.NewEngine); err != nil {
		log.Fatal("failed to register Tagger ONNX engine:", err)
	}

	llamaRuntimeStore := llamacpp.NewRuntimeStore(filepath.Join(storageLayout.RuntimesDir, "llama.cpp"))
	cmd.SetLlamaRuntimeStore(llamaRuntimeStore)
	if err := aiManager.RegisterEngine(llamacpp.EngineID, func() ai.Engine {
		return llamacpp.NewEngine(llamaRuntimeStore)
	}); err != nil {
		log.Fatal("failed to register llama.cpp VLM engine:", err)
	}
	if err := aiManager.RegisterEngine(llamacpp.AdvancedEngineID, func() ai.Engine {
		return llamacpp.NewAdvancedEngine(llamaRuntimeStore)
	}); err != nil {
		log.Fatal("failed to register llama.cpp Advanced Vision engine:", err)
	}
	if err := aiManager.RegisterEngine(llamacpp.PromptEngineID, func() ai.Engine {
		return llamacpp.NewPromptEngine(llamaRuntimeStore)
	}); err != nil {
		log.Fatal("failed to register llama.cpp Prompt Engine:", err)
	}

	aiJobQueue := ai.NewJobQueue(db.NewAIAnalysisRepo(database), cmd.GetAISettings, 1)
	cmd.SetAIJobQueue(aiJobQueue)
	if err := aiJobQueue.RegisterHandler(domain.AICapabilitySemanticSearch, cmd.SemanticAnalysisHandler); err != nil {
		log.Fatal("failed to register Semantic Search job handler:", err)
	}
	if err := aiJobQueue.RegisterHandler(domain.AICapabilityTagger, cmd.TaggerAnalysisHandler); err != nil {
		log.Fatal("failed to register Tagger job handler:", err)
	}
	if err := aiJobQueue.RegisterHandler(domain.AICapabilityLightweightVision, cmd.LightweightVisionAnalysisHandler); err != nil {
		log.Fatal("failed to register Lightweight Vision job handler:", err)
	}
	aiManager.SetModelActivatedHook(aiJobQueue.HandleModelActivated)
	// Recover durable queue state immediately, but never claim model-backed
	// work until startup restore has had a chance to establish runtimes.
	aiJobQueue.SetStartupHold(true)
	if err := aiJobQueue.Start(appCtx); err != nil {
		log.Fatal("failed to start AI job queue:", err)
	}

	scanSvc.SetAssetChangeHandler(func(assetIDs []int64) {
		if _, err := cmd.HandleScannedAssetChanges(assetIDs); err != nil {
			slog.Debug("semantic asset-change handling skipped", "error", err)
		}
	})

	var shutdownOnce sync.Once
	shutdown := func(source string) {
		shutdownOnce.Do(func() {
			slog.Info("shutting down Lumine", "source", source)

			// Cancel application-owned contexts first so active inference and
			// workers can stop before we join background goroutines.
			appCancel()

			backgroundCtx, backgroundCancel := context.WithTimeout(context.Background(), 6*time.Second)
			if err := cmd.ShutdownBackground(backgroundCtx); err != nil {
				slog.Warn("background work did not stop cleanly", "error", err)
			}
			backgroundCancel()

			queueCtx, queueCancel := context.WithTimeout(context.Background(), 6*time.Second)
			if err := aiJobQueue.Stop(queueCtx); err != nil {
				slog.Warn("AI job queue did not stop cleanly", "error", err)
			}
			queueCancel()

			runtimeCtx, runtimeCancel := context.WithTimeout(context.Background(), 12*time.Second)
			if err := aiManager.Close(runtimeCtx); err != nil {
				slog.Warn("AI runtimes did not stop cleanly", "error", err)
			}
			runtimeCancel()

			slog.Info("Lumine shutdown cleanup completed")
		})
	}

	// OnShutdown is the primary path. This defer is a fallback for startup/run
	// failures so child runtimes and workers are still cleaned up exactly once.
	defer shutdown("main-return")

	// Model verification/session construction can stream gigabytes of data.
	// Start automatic restore only after the viewer has been continuously idle;
	// visible image decode must own the cold-start disk bandwidth.
	var restoreOnce sync.Once
	var restoreTimerMu sync.Mutex
	var restoreTimer *time.Timer
	startRestore := func() {
		restoreOnce.Do(func() {
			if !cmd.StartAIRestore() {
				slog.Warn("AI restore was not started because Lumine is shutting down")
			}
		})
	}
	scheduleRestore := func(delay time.Duration) {
		restoreTimerMu.Lock()
		defer restoreTimerMu.Unlock()
		if restoreTimer != nil {
			restoreTimer.Stop()
		}
		restoreTimer = time.AfterFunc(delay, startRestore)
	}
	cancelRestoreSchedule := func() {
		restoreTimerMu.Lock()
		defer restoreTimerMu.Unlock()
		if restoreTimer != nil {
			restoreTimer.Stop()
			restoreTimer = nil
		}
	}

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
		OnDomReady: func(ctx context.Context) {
			// Give the initial grid a real cold-load window before any model
			// verification/session construction begins. If the viewer reports
			// activity, restart the idle countdown from zero.
			const restoreIdleDelay = 3 * time.Second
			scheduleRestore(restoreIdleDelay)

			runtime.EventsOff(ctx, "viewer:activity")
			runtime.EventsOn(ctx, "viewer:activity", func(data ...interface{}) {
				if len(data) == 0 {
					return
				}
				active, ok := data[0].(bool)
				if !ok {
					return
				}
				aiJobQueue.SetInteractiveUIActive(active)
				if active {
					cancelRestoreSchedule()
					return
				}
				scheduleRestore(restoreIdleDelay)
			})
		},
		OnShutdown: func(_ context.Context) {
			shutdown("wails")
		},
		Bind: []interface{}{
			cmd,
		},
		// Keep native Windows chrome so resize, Snap Layouts, maximise/restore,
		// and multi-monitor movement retain their normal shell behaviour. The
		// native frame is themed to visually continue Lumine's app header.
		Windows: &windows.Options{
			WebviewUserDataPath:  storageLayout.WebviewDataDir,
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
