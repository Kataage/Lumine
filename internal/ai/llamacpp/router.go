package llamacpp

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/kataage/lumine/internal/ai"
)

const (
	routerCacheRAMMiB     = 256
	routerIdleSleepSecond = 300
	routerFitTargetMiB    = 1024
)

type routerModelConfig struct {
	Alias       string
	ModelPath   string
	MMProjPath  string
	ContextSize int
	Threads     int
	ExtraArgs   []string
}

type sharedRouterRuntime struct {
	sidecar        *ai.SidecarProcess
	baseURL        string
	executablePath string
	provider       string
	warning        string
	policyAllowGPU bool
	presetPath     string
}

func (s *RuntimeStore) lockRouterOperation(ctx context.Context) (func(), error) {
	if ctx == nil {
		ctx = context.Background()
	}
	if s.routerOpMu.TryLock() {
		return s.routerOpMu.Unlock, nil
	}
	ticker := time.NewTicker(10 * time.Millisecond)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return nil, ctx.Err()
		case <-ticker.C:
			if s.routerOpMu.TryLock() {
				return s.routerOpMu.Unlock, nil
			}
		}
	}
}

func (s *RuntimeStore) AcquireRouterModel(
	ctx context.Context,
	config routerModelConfig,
	allowGPU bool,
	lazy bool,
) (string, string, string, error) {
	if s == nil {
		return "", "", "", errors.New("llama.cpp runtime store is not configured")
	}
	if strings.TrimSpace(config.Alias) == "" {
		return "", "", "", errors.New("router model alias is required")
	}
	if strings.TrimSpace(config.ModelPath) == "" {
		return "", "", "", errors.New("router model path is required")
	}
	unlock, err := s.lockRouterOperation(ctx)
	if err != nil {
		return "", "", "", err
	}
	defer unlock()

	s.routerMu.Lock()
	if s.routerModels == nil {
		s.routerModels = make(map[string]routerModelConfig)
	}
	s.routerModels[config.Alias] = config
	s.routerPolicyAllowGPU = allowGPU
	s.routerMu.Unlock()

	if err := s.ensureRouterLocked(ctx, allowGPU); err != nil {
		s.removeRouterModelRegistration(config.Alias)
		return "", "", "", err
	}
	if err := s.writeAndReloadRouterPresetLocked(ctx); err != nil {
		s.removeRouterModelRegistration(config.Alias)
		return "", "", "", err
	}

	if !lazy {
		if _, err := s.ensureRouterModelReadyLocked(ctx, config.Alias); err != nil {
			s.removeRouterModelRegistration(config.Alias)
			_ = s.writeAndReloadRouterPresetLocked(context.Background())
			return "", "", "", err
		}
	}

	diagnostics := s.RouterDiagnostics()
	return config.Alias, diagnostics.ExecutionProvider, diagnostics.Warning, nil
}

func (s *RuntimeStore) ReleaseRouterModel(ctx context.Context, alias string) error {
	if s == nil || strings.TrimSpace(alias) == "" {
		return nil
	}
	unlock, err := s.lockRouterOperation(ctx)
	if err != nil {
		return err
	}
	defer unlock()

	s.routerMu.Lock()
	if s.routerModels != nil {
		delete(s.routerModels, alias)
	}
	remaining := len(s.routerModels)
	s.routerMu.Unlock()

	if remaining == 0 {
		return s.stopRouterLocked(ctx)
	}
	return s.writeAndReloadRouterPresetLocked(ctx)
}

func (s *RuntimeStore) ResetRouter(ctx context.Context) error {
	if s == nil {
		return nil
	}
	unlock, err := s.lockRouterOperation(ctx)
	if err != nil {
		return err
	}
	defer unlock()
	return s.stopRouterLocked(ctx)
}

func (s *RuntimeStore) RouterDiagnostics() ai.RuntimeDiagnostics {
	if s == nil {
		return ai.RuntimeDiagnostics{}
	}
	s.routerMu.Lock()
	defer s.routerMu.Unlock()
	if s.router == nil {
		return ai.RuntimeDiagnostics{}
	}
	return ai.RuntimeDiagnostics{
		ExecutionProvider: s.router.provider,
		Warning:           s.router.warning,
	}
}

func (s *RuntimeStore) RouterRunning() bool {
	if s == nil {
		return false
	}
	s.routerMu.Lock()
	defer s.routerMu.Unlock()
	return s.router != nil && s.router.sidecar != nil && s.router.sidecar.Running()
}

func (s *RuntimeStore) RouterLogs() (stdout string, stderr string) {
	if s == nil {
		return "", ""
	}
	s.routerMu.Lock()
	current := s.router
	s.routerMu.Unlock()
	if current == nil || current.sidecar == nil {
		return "", ""
	}
	return current.sidecar.Logs()
}

// PrepareRouterModelForRequest serializes all llama.cpp model activity across
// capabilities, makes the requested model resident, and returns the shared
// router endpoint. The returned release function must be held until the HTTP
// response body has been completely consumed. This avoids model eviction while
// another request is still using the single resident child.
func (s *RuntimeStore) PrepareRouterModelForRequest(
	ctx context.Context,
	alias string,
) (baseURL string, release func(), err error) {
	if s == nil {
		return "", nil, errors.New("llama.cpp runtime store is not configured")
	}
	unlock, err := s.lockRouterOperation(ctx)
	if err != nil {
		return "", nil, err
	}

	s.routerMu.Lock()
	policyAllowGPU := s.routerPolicyAllowGPU
	current := s.router
	s.routerMu.Unlock()
	if current == nil || current.sidecar == nil || !current.sidecar.Running() {
		if err := s.ensureRouterLocked(ctx, policyAllowGPU); err != nil {
			unlock()
			return "", nil, fmt.Errorf("recover llama.cpp router: %w", err)
		}
	}

	baseURL, err = s.ensureRouterModelReadyLocked(ctx, alias)
	if err != nil {
		unlock()
		return "", nil, err
	}
	return baseURL, unlock, nil
}

func (s *RuntimeStore) ensureRouterLocked(ctx context.Context, allowGPU bool) error {
	s.routerMu.Lock()
	current := s.router
	s.routerMu.Unlock()

	if current != nil && current.sidecar != nil && current.sidecar.Running() && current.policyAllowGPU == allowGPU {
		return nil
	}
	if current != nil {
		if err := s.stopRouterLocked(ctx); err != nil {
			return err
		}
	}

	var gpuFailures []string
	for _, candidate := range runtimeCandidates(allowGPU) {
		runtimeInfo, verifyErr := s.Verify(candidate.manifest)
		if verifyErr != nil {
			if candidate.provider == "vulkan" {
				gpuFailures = append(gpuFailures, "verify Vulkan runtime: "+verifyErr.Error())
				continue
			}
			if len(gpuFailures) > 0 {
				return fmt.Errorf(
					"verify llama.cpp CPU fallback: %w; Vulkan attempt: %s",
					verifyErr,
					strings.Join(gpuFailures, " | "),
				)
			}
			return fmt.Errorf("verify llama.cpp CPU runtime: %w", verifyErr)
		}

		warning := ""
		if candidate.provider == "cpu" && len(gpuFailures) > 0 {
			warning = "Vulkan unavailable; using CPU fallback: " + strings.Join(gpuFailures, " | ")
		}
		if err := s.startRouterCandidateLocked(
			ctx,
			runtimeInfo.ExecutablePath,
			candidate.provider,
			allowGPU,
			warning,
		); err != nil {
			if candidate.provider == "vulkan" {
				gpuFailures = append(gpuFailures, "start Vulkan router: "+err.Error())
				continue
			}
			if len(gpuFailures) > 0 {
				return fmt.Errorf("%w; Vulkan attempt: %s", err, strings.Join(gpuFailures, " | "))
			}
			return err
		}
		return nil
	}
	return errors.New("no usable llama.cpp runtime is installed")
}

func (s *RuntimeStore) startRouterCandidateLocked(
	ctx context.Context,
	executablePath string,
	provider string,
	policyAllowGPU bool,
	warning string,
) error {
	s.routerMu.Lock()
	if s.routerPort == 0 {
		port, err := reserveLocalPort()
		if err != nil {
			s.routerMu.Unlock()
			return err
		}
		s.routerPort = port
	}
	port := s.routerPort
	presetPath := filepath.Join(s.root, "router-state", "models.ini")
	s.routerMu.Unlock()

	if err := os.MkdirAll(filepath.Dir(presetPath), 0o755); err != nil {
		return fmt.Errorf("create llama.cpp router state: %w", err)
	}
	if err := s.writeRouterPresetForProvider(presetPath, provider); err != nil {
		return err
	}

	sidecar := ai.NewSidecarProcess()
	args := buildRouterServerArgs(presetPath, port, provider)
	if err := sidecar.Start(ctx, executablePath, args, nil); err != nil {
		return err
	}
	baseURL := fmt.Sprintf("http://127.0.0.1:%d", port)

	startupCtx := ctx
	if startupCtx == nil {
		startupCtx = context.Background()
	}
	startupCtx, cancel := context.WithTimeout(startupCtx, 30*time.Second)
	defer cancel()
	if err := waitRouterReady(startupCtx, sidecar, baseURL); err != nil {
		stopCtx, stopCancel := context.WithTimeout(context.Background(), 10*time.Second)
		_ = sidecar.Stop(stopCtx)
		stopCancel()
		stdout, stderr := sidecar.Logs()
		return fmt.Errorf(
			"start llama.cpp %s router: %w; stdout=%q stderr=%q",
			provider,
			err,
			tailLog(stdout),
			tailLog(stderr),
		)
	}

	s.routerMu.Lock()
	s.router = &sharedRouterRuntime{
		sidecar:        sidecar,
		baseURL:        baseURL,
		executablePath: executablePath,
		provider:       provider,
		warning:        warning,
		policyAllowGPU: policyAllowGPU,
		presetPath:     presetPath,
	}
	s.routerMu.Unlock()
	return nil
}

func buildRouterServerArgs(presetPath string, port int, provider string) []string {
	args := []string{
		"--host", "127.0.0.1",
		"--port", strconv.Itoa(port),
		"--models-preset", presetPath,
		"--models-max", "1",
		"--models-autoload",
		"--parallel", "1",
		"--cache-ram", strconv.Itoa(routerCacheRAMMiB),
		"--sleep-idle-seconds", strconv.Itoa(routerIdleSleepSecond),
		"--no-webui",
	}
	if provider == "vulkan" {
		args = append(args,
			"--fit", "on",
			"--fit-target", strconv.Itoa(routerFitTargetMiB),
			"--gpu-layers", "auto",
		)
	} else {
		args = append(args,
			"--device", "none",
			"--gpu-layers", "0",
			"--no-mmproj-offload",
		)
	}
	return args
}

func waitRouterReady(ctx context.Context, sidecar *ai.SidecarProcess, baseURL string) error {
	client := &http.Client{Timeout: 2 * time.Second}
	ticker := time.NewTicker(200 * time.Millisecond)
	defer ticker.Stop()
	for {
		if !sidecar.Running() {
			if err := sidecar.LastError(); err != nil {
				return fmt.Errorf("router exited: %w", err)
			}
			return errors.New("router exited before becoming ready")
		}
		request, err := http.NewRequestWithContext(ctx, http.MethodGet, baseURL+"/health", nil)
		if err != nil {
			return err
		}
		response, err := client.Do(request)
		if err == nil {
			_, _ = io.Copy(io.Discard, response.Body)
			response.Body.Close()
			if response.StatusCode == http.StatusOK {
				return nil
			}
		}
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-ticker.C:
		}
	}
}

func (s *RuntimeStore) writeAndReloadRouterPresetLocked(ctx context.Context) error {
	s.routerMu.Lock()
	current := s.router
	s.routerMu.Unlock()
	if current == nil || current.sidecar == nil || !current.sidecar.Running() {
		return nil
	}
	if err := s.writeRouterPresetForProvider(current.presetPath, current.provider); err != nil {
		return err
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, current.baseURL+"/models?reload=1", nil)
	if err != nil {
		return err
	}
	response, err := s.routerHTTPClient().Do(request)
	if err != nil {
		return fmt.Errorf("reload llama.cpp router presets: %w", err)
	}
	defer response.Body.Close()
	body, readErr := io.ReadAll(io.LimitReader(response.Body, 2*1024*1024))
	if readErr != nil {
		return fmt.Errorf("read llama.cpp router reload response: %w", readErr)
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return fmt.Errorf("reload llama.cpp router presets: HTTP %s: %s", response.Status, strings.TrimSpace(string(body)))
	}
	return nil
}

func (s *RuntimeStore) writeRouterPresetForProvider(path string, provider string) error {
	s.routerMu.Lock()
	models := make([]routerModelConfig, 0, len(s.routerModels))
	for _, model := range s.routerModels {
		models = append(models, model)
	}
	s.routerMu.Unlock()
	sort.Slice(models, func(i, j int) bool { return models[i].Alias < models[j].Alias })

	var builder strings.Builder
	builder.WriteString("version = 1\n\n")
	for _, model := range models {
		for name, value := range map[string]string{
			"model":  model.ModelPath,
			"mmproj": model.MMProjPath,
		} {
			if value == "" {
				continue
			}
			if strings.ContainsAny(value, "\r\n;#") {
				return fmt.Errorf("llama.cpp router %s path contains unsupported INI characters", name)
			}
		}
		builder.WriteString("[")
		builder.WriteString(model.Alias)
		builder.WriteString("]\n")
		builder.WriteString("model = ")
		builder.WriteString(model.ModelPath)
		builder.WriteString("\n")
		if model.MMProjPath != "" {
			builder.WriteString("mmproj = ")
			builder.WriteString(model.MMProjPath)
			builder.WriteString("\n")
		}
		if model.ContextSize > 0 {
			fmt.Fprintf(&builder, "c = %d\n", model.ContextSize)
		}
		if model.Threads > 0 {
			fmt.Fprintf(&builder, "threads = %d\n", model.Threads)
		}
		for _, line := range presetLinesForExtraArgs(model.ExtraArgs) {
			builder.WriteString(line)
			builder.WriteByte('\n')
		}
		builder.WriteByte('\n')
	}

	if err := os.WriteFile(path, []byte(builder.String()), 0o600); err != nil {
		return fmt.Errorf("write llama.cpp router preset: %w", err)
	}
	return nil
}

func presetLinesForExtraArgs(args []string) []string {
	lines := make([]string, 0, len(args))
	for index := 0; index < len(args); index++ {
		arg := strings.TrimSpace(args[index])
		if !strings.HasPrefix(arg, "-") {
			continue
		}
		key := strings.TrimLeft(arg, "-")
		if key == "" {
			continue
		}
		value := "true"
		if index+1 < len(args) && !strings.HasPrefix(args[index+1], "-") {
			value = args[index+1]
			index++
		}
		lines = append(lines, key+" = "+value)
	}
	return lines
}

func (s *RuntimeStore) ensureRouterModelReadyLocked(ctx context.Context, alias string) (string, error) {
	s.routerMu.Lock()
	current := s.router
	_, registered := s.routerModels[alias]
	s.routerMu.Unlock()
	if !registered {
		return "", fmt.Errorf("llama.cpp router model %q is not registered", alias)
	}
	if current == nil || current.sidecar == nil || !current.sidecar.Running() {
		return "", ai.ErrRuntimeNotLoaded
	}
	if err := s.probeRouterModelReady(ctx, current.baseURL, alias); err == nil {
		return current.baseURL, nil
	} else if current.provider != "vulkan" || !current.policyAllowGPU {
		return "", err
	} else {
		vulkanErr := err
		if stopErr := s.stopRouterLocked(ctx); stopErr != nil {
			return "", errors.Join(vulkanErr, stopErr)
		}
		cpuInfo, verifyErr := s.Verify(CPURuntimeManifest())
		if verifyErr != nil {
			return "", errors.Join(vulkanErr, fmt.Errorf("verify CPU fallback: %w", verifyErr))
		}
		warning := "Vulkan model load failed; using CPU fallback: " + vulkanErr.Error()
		if startErr := s.startRouterCandidateLocked(ctx, cpuInfo.ExecutablePath, "cpu", true, warning); startErr != nil {
			return "", errors.Join(vulkanErr, startErr)
		}
		s.routerMu.Lock()
		fallback := s.router
		s.routerMu.Unlock()
		if fallback == nil {
			return "", errors.Join(vulkanErr, ai.ErrRuntimeNotLoaded)
		}
		if retryErr := s.probeRouterModelReady(ctx, fallback.baseURL, alias); retryErr != nil {
			return "", errors.Join(vulkanErr, fmt.Errorf("CPU fallback model load: %w", retryErr))
		}
		return fallback.baseURL, nil
	}
}

func (s *RuntimeStore) probeRouterModelReady(ctx context.Context, baseURL, alias string) error {
	loadCtx := ctx
	if loadCtx == nil {
		loadCtx = context.Background()
	}
	loadCtx, cancel := context.WithTimeout(loadCtx, 2*time.Minute)
	defer cancel()
	endpoint := baseURL + "/props?autoload=true&model=" + url.QueryEscape(alias)
	request, err := http.NewRequestWithContext(loadCtx, http.MethodGet, endpoint, nil)
	if err != nil {
		return err
	}
	response, err := s.routerHTTPClient().Do(request)
	if err != nil {
		return fmt.Errorf("activate llama.cpp router model %s: %w", alias, err)
	}
	defer response.Body.Close()
	body, readErr := io.ReadAll(io.LimitReader(response.Body, 2*1024*1024))
	if readErr != nil {
		return fmt.Errorf("read llama.cpp router model activation: %w", readErr)
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return fmt.Errorf(
			"activate llama.cpp router model %s: HTTP %s: %s",
			alias,
			response.Status,
			strings.TrimSpace(string(body)),
		)
	}
	return nil
}

func (s *RuntimeStore) removeRouterModelRegistration(alias string) {
	s.routerMu.Lock()
	defer s.routerMu.Unlock()
	if s.routerModels != nil {
		delete(s.routerModels, alias)
	}
}

func (s *RuntimeStore) stopRouterLocked(ctx context.Context) error {
	s.routerMu.Lock()
	current := s.router
	s.router = nil
	s.routerMu.Unlock()
	if current == nil || current.sidecar == nil {
		return nil
	}
	stopCtx := ctx
	if stopCtx == nil {
		stopCtx = context.Background()
	}
	stopCtx, cancel := context.WithTimeout(stopCtx, 15*time.Second)
	defer cancel()
	return current.sidecar.Stop(stopCtx)
}

func (s *RuntimeStore) routerHTTPClient() *http.Client {
	s.routerMu.Lock()
	defer s.routerMu.Unlock()
	if s.routerClient == nil {
		s.routerClient = &http.Client{Timeout: 3 * time.Minute}
	}
	return s.routerClient
}

func encodeRouterJSON(payload any) (*bytes.Reader, error) {
	body, err := json.Marshal(payload)
	if err != nil {
		return nil, err
	}
	return bytes.NewReader(body), nil
}
