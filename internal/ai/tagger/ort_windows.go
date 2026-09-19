//go:build windows && amd64

package tagger

import (
	"archive/zip"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"syscall"
	"unsafe"

	"github.com/kataage/lumine/internal/ai"
)

const (
	ortAPIVersion = uintptr(24)

	ortFnGetErrorMessage                  = uintptr(2)
	ortFnCreateEnv                        = uintptr(3)
	ortFnCreateSession                    = uintptr(7)
	ortFnRun                              = uintptr(9)
	ortFnCreateSessionOptions             = uintptr(10)
	ortFnSetSessionExecutionMode          = uintptr(13)
	ortFnDisableMemPattern                = uintptr(17)
	ortFnSetSessionGraphOptimizationLevel = uintptr(23)
	ortFnCreateTensorWithDataAsOrtValue   = uintptr(49)
	ortFnCreateCpuMemoryInfo              = uintptr(69)
	ortFnReleaseEnv                       = uintptr(92)
	ortFnReleaseStatus                    = uintptr(93)
	ortFnReleaseMemoryInfo                = uintptr(94)
	ortFnReleaseSession                   = uintptr(95)
	ortFnReleaseValue                     = uintptr(96)
	ortFnReleaseSessionOptions            = uintptr(100)

	ortLoggingWarning = uintptr(2)
	ortSequential     = uintptr(0)
	ortEnableAll      = uintptr(99)
	ortArenaAllocator = uintptr(1)
	ortMemTypeDefault = uintptr(0)
	ortTensorFloat    = uintptr(1)

	ortRuntimeVersion = "1.24.4"

	ortDLLArchivePath      = "runtimes/win-x64/native/onnxruntime.dll"
	ortSharedArchivePath   = "runtimes/win-x64/native/onnxruntime_providers_shared.dll"
	directMLDLLArchivePath = "bin/x64-win/DirectML.dll"

	ortDLLSize       = uint64(17328152)
	ortSharedDLLSize = uint64(22040)
	directMLDLLSize  = uint64(18527776)

	ortDLLSHA256       = "e7eedec6a6f26dc39dc948276a75ef6d2bee3fff944d874ceed0bbd3b97bff40"
	ortSharedDLLSHA256 = "265c8daf29637cb259cac8be9f08f2cd45f3883f0f0e4949cbfddd5b4cbec3b6"
	directMLDLLSHA256  = "9c9e6d822561c6c41b90e6994b3e8857cf1d66dbfb1e0c4c799c7c89b4e92da1"
)

var ortExtractMu sync.Mutex

type windowsORT struct {
	dll         *syscall.DLL
	directMLDLL *syscall.DLL
	api         uintptr
	env         uintptr
	memoryInfo  uintptr
	session     uintptr
	runMu       sync.Mutex
	inputName   string
	outputName  string
	provider    string
	warning     string
}

func newORTBackend(
	model ai.InstalledModel,
	config modelConfig,
	options ai.LoadOptions,
) (runtimeBackend, error) {
	dllPath, directMLPath, extractWarning, err := extractORTRuntime(model, options.AllowGPU)
	if err != nil {
		return nil, err
	}

	backend := &windowsORT{
		inputName:  config.inputName,
		outputName: config.outputName,
		warning:    extractWarning,
	}
	if options.AllowGPU && directMLPath != "" {
		directMLDLL, loadErr := syscall.LoadDLL(directMLPath)
		if loadErr != nil {
			backend.warning = joinWarning(
				backend.warning,
				fmt.Sprintf("DirectML runtime could not be loaded; using CPU fallback: %v", loadErr),
			)
		} else {
			backend.directMLDLL = directMLDLL
		}
	}

	dll, err := syscall.LoadDLL(dllPath)
	if err != nil {
		if backend.directMLDLL != nil {
			_ = backend.directMLDLL.Release()
			backend.directMLDLL = nil
		}
		return nil, fmt.Errorf("load Tagger ONNX Runtime DLL: %w", err)
	}
	backend.dll = dll
	ok := false
	defer func() {
		if !ok {
			_ = backend.Close()
		}
	}()

	getBase, err := dll.FindProc("OrtGetApiBase")
	if err != nil {
		return nil, fmt.Errorf("find OrtGetApiBase: %w", err)
	}
	base, _, _ := getBase.Call()
	if base == 0 {
		return nil, errors.New("OrtGetApiBase returned nil")
	}

	getAPIFn := *(*uintptr)(unsafe.Pointer(base))
	versionFn := *(*uintptr)(unsafe.Pointer(base + unsafe.Sizeof(uintptr(0))))
	apiPtr, _, _ := syscall.SyscallN(getAPIFn, ortAPIVersion)
	if apiPtr == 0 {
		return nil, fmt.Errorf("ONNX Runtime does not support C API version %d", ortAPIVersion)
	}
	backend.api = apiPtr

	versionPtr, _, _ := syscall.SyscallN(versionFn)
	version := readCString(versionPtr)
	if version != ortRuntimeVersion {
		return nil, fmt.Errorf(
			"unexpected ONNX Runtime version %q; expected %s",
			version,
			ortRuntimeVersion,
		)
	}

	logID := append([]byte("lumine-tagger"), 0)
	if err := backend.callStatus(
		ortFnCreateEnv,
		ortLoggingWarning,
		uintptr(unsafe.Pointer(&logID[0])),
		uintptr(unsafe.Pointer(&backend.env)),
	); err != nil {
		return nil, fmt.Errorf("create Tagger ONNX Runtime environment: %w", err)
	}
	runtime.KeepAlive(logID)

	if err := backend.callStatus(
		ortFnCreateCpuMemoryInfo,
		ortArenaAllocator,
		ortMemTypeDefault,
		uintptr(unsafe.Pointer(&backend.memoryInfo)),
	); err != nil {
		return nil, fmt.Errorf("create Tagger ONNX Runtime memory info: %w", err)
	}

	if options.AllowGPU && backend.directMLDLL != nil {
		if gpuErr := backend.createSession(config.modelPath, true); gpuErr == nil {
			backend.provider = "directml"
			ok = true
			return backend, nil
		} else {
			backend.warning = joinWarning(
				backend.warning,
				fmt.Sprintf("DirectML unavailable; using CPU fallback: %v", gpuErr),
			)
		}
	}

	if err := backend.createSession(config.modelPath, false); err != nil {
		if backend.warning != "" {
			return nil, fmt.Errorf("%s; CPU fallback failed: %w", backend.warning, err)
		}
		return nil, err
	}
	backend.provider = "cpu"
	ok = true
	return backend, nil
}

func (r *windowsORT) createSession(modelPath string, useDirectML bool) error {
	r.releaseSession()

	options, err := r.createSessionOptions(useDirectML)
	if err != nil {
		return err
	}
	defer r.release(ortFnReleaseSessionOptions, options)

	path, err := syscall.UTF16PtrFromString(modelPath)
	if err != nil {
		return fmt.Errorf("encode Tagger model path: %w", err)
	}
	if err := r.callStatus(
		ortFnCreateSession,
		r.env,
		uintptr(unsafe.Pointer(path)),
		options,
		uintptr(unsafe.Pointer(&r.session)),
	); err != nil {
		return fmt.Errorf("create Tagger ONNX session: %w", err)
	}
	runtime.KeepAlive(path)
	return nil
}

func (r *windowsORT) createSessionOptions(useDirectML bool) (uintptr, error) {
	var options uintptr
	if err := r.callStatus(
		ortFnCreateSessionOptions,
		uintptr(unsafe.Pointer(&options)),
	); err != nil {
		return 0, fmt.Errorf("create Tagger session options: %w", err)
	}
	releaseOnError := func(err error) (uintptr, error) {
		r.release(ortFnReleaseSessionOptions, options)
		return 0, err
	}

	if err := r.callStatus(
		ortFnSetSessionGraphOptimizationLevel,
		options,
		ortEnableAll,
	); err != nil {
		return releaseOnError(fmt.Errorf("enable ONNX graph optimizations: %w", err))
	}

	if useDirectML {
		if err := r.callStatus(ortFnDisableMemPattern, options); err != nil {
			return releaseOnError(fmt.Errorf("disable memory pattern for DirectML: %w", err))
		}
		if err := r.callStatus(ortFnSetSessionExecutionMode, options, ortSequential); err != nil {
			return releaseOnError(fmt.Errorf("set sequential execution for DirectML: %w", err))
		}
		if err := r.appendDirectML(options); err != nil {
			return releaseOnError(fmt.Errorf("enable DirectML execution provider: %w", err))
		}
	}
	return options, nil
}

func (r *windowsORT) appendDirectML(options uintptr) error {
	if r.dll == nil {
		return errors.New("ONNX Runtime DLL is not loaded")
	}
	proc, err := r.dll.FindProc("OrtSessionOptionsAppendExecutionProvider_DML")
	if err != nil {
		return fmt.Errorf("find DirectML provider factory: %w", err)
	}
	status, _, _ := proc.Call(options, 0)
	return r.consumeStatus(status)
}

func (r *windowsORT) RuntimeDiagnostics() ai.RuntimeDiagnostics {
	if r == nil {
		return ai.RuntimeDiagnostics{}
	}
	return ai.RuntimeDiagnostics{
		ExecutionProvider: r.provider,
		Warning:           r.warning,
	}
}

func (r *windowsORT) Run(input imageTensor, outputSize int) ([]float32, error) {
	r.runMu.Lock()
	defer r.runMu.Unlock()

	if r.session == 0 || r.memoryInfo == 0 {
		return nil, errors.New("Tagger ONNX Runtime session is not initialized")
	}
	if len(input.Data) == 0 || len(input.Shape) == 0 {
		return nil, errors.New("Tagger input tensor is empty")
	}
	if outputSize <= 0 {
		return nil, errors.New("Tagger output size must be positive")
	}

	output := make([]float32, outputSize)
	outputShape := []int64{1, int64(outputSize)}

	var inputValue, outputValue uintptr
	if err := r.callStatus(
		ortFnCreateTensorWithDataAsOrtValue,
		r.memoryInfo,
		uintptr(unsafe.Pointer(&input.Data[0])),
		uintptr(len(input.Data))*unsafe.Sizeof(input.Data[0]),
		uintptr(unsafe.Pointer(&input.Shape[0])),
		uintptr(len(input.Shape)),
		ortTensorFloat,
		uintptr(unsafe.Pointer(&inputValue)),
	); err != nil {
		return nil, fmt.Errorf("create Tagger input tensor: %w", err)
	}
	defer r.release(ortFnReleaseValue, inputValue)

	if err := r.callStatus(
		ortFnCreateTensorWithDataAsOrtValue,
		r.memoryInfo,
		uintptr(unsafe.Pointer(&output[0])),
		uintptr(len(output))*unsafe.Sizeof(output[0]),
		uintptr(unsafe.Pointer(&outputShape[0])),
		uintptr(len(outputShape)),
		ortTensorFloat,
		uintptr(unsafe.Pointer(&outputValue)),
	); err != nil {
		return nil, fmt.Errorf("create Tagger output tensor: %w", err)
	}
	defer r.release(ortFnReleaseValue, outputValue)

	inputNameBytes := append([]byte(r.inputName), 0)
	outputNameBytes := append([]byte(r.outputName), 0)
	inputNames := []uintptr{uintptr(unsafe.Pointer(&inputNameBytes[0]))}
	outputNames := []uintptr{uintptr(unsafe.Pointer(&outputNameBytes[0]))}
	inputValues := []uintptr{inputValue}
	outputValues := []uintptr{outputValue}

	err := r.callStatus(
		ortFnRun,
		r.session,
		0,
		uintptr(unsafe.Pointer(&inputNames[0])),
		uintptr(unsafe.Pointer(&inputValues[0])),
		1,
		uintptr(unsafe.Pointer(&outputNames[0])),
		1,
		uintptr(unsafe.Pointer(&outputValues[0])),
	)
	runtime.KeepAlive(input.Data)
	runtime.KeepAlive(input.Shape)
	runtime.KeepAlive(output)
	runtime.KeepAlive(outputShape)
	runtime.KeepAlive(inputNameBytes)
	runtime.KeepAlive(outputNameBytes)
	runtime.KeepAlive(inputNames)
	runtime.KeepAlive(outputNames)
	runtime.KeepAlive(inputValues)
	runtime.KeepAlive(outputValues)
	if err != nil {
		return nil, err
	}
	return output, nil
}

func (r *windowsORT) releaseSession() {
	if r.session != 0 {
		r.release(ortFnReleaseSession, r.session)
		r.session = 0
	}
}

func (r *windowsORT) Close() error {
	if r == nil {
		return nil
	}
	r.runMu.Lock()
	defer r.runMu.Unlock()

	r.releaseSession()
	if r.memoryInfo != 0 {
		r.release(ortFnReleaseMemoryInfo, r.memoryInfo)
		r.memoryInfo = 0
	}
	if r.env != 0 {
		r.release(ortFnReleaseEnv, r.env)
		r.env = 0
	}
	var closeErr error
	if r.dll != nil {
		closeErr = r.dll.Release()
		r.dll = nil
	}
	if r.directMLDLL != nil {
		if err := r.directMLDLL.Release(); err != nil && closeErr == nil {
			closeErr = err
		}
		r.directMLDLL = nil
	}
	return closeErr
}

func (r *windowsORT) callStatus(index uintptr, args ...uintptr) error {
	if r.api == 0 {
		return errors.New("ONNX Runtime API is not initialized")
	}
	fn := *(*uintptr)(unsafe.Pointer(r.api + index*unsafe.Sizeof(uintptr(0))))
	if fn == 0 {
		return fmt.Errorf("ONNX Runtime API function %d is unavailable", index)
	}
	status, _, _ := syscall.SyscallN(fn, args...)
	return r.consumeStatus(status)
}

func (r *windowsORT) consumeStatus(status uintptr) error {
	if status == 0 {
		return nil
	}
	if r.api == 0 {
		return errors.New("ONNX Runtime returned an error before API initialization")
	}
	messageFn := *(*uintptr)(unsafe.Pointer(
		r.api + ortFnGetErrorMessage*unsafe.Sizeof(uintptr(0)),
	))
	messagePtr, _, _ := syscall.SyscallN(messageFn, status)
	message := readCString(messagePtr)
	releaseStatusFn := *(*uintptr)(unsafe.Pointer(
		r.api + ortFnReleaseStatus*unsafe.Sizeof(uintptr(0)),
	))
	_, _, _ = syscall.SyscallN(releaseStatusFn, status)
	if message == "" {
		message = "unknown ONNX Runtime error"
	}
	return errors.New(message)
}

func (r *windowsORT) release(index uintptr, value uintptr) {
	if r == nil || r.api == 0 || value == 0 {
		return
	}
	fn := *(*uintptr)(unsafe.Pointer(r.api + index*unsafe.Sizeof(uintptr(0))))
	if fn != 0 {
		_, _, _ = syscall.SyscallN(fn, value)
	}
}

func readCString(pointer uintptr) string {
	if pointer == 0 {
		return ""
	}
	data := make([]byte, 0, 128)
	for offset := uintptr(0); offset < 64*1024; offset++ {
		value := *(*byte)(unsafe.Pointer(pointer + offset))
		if value == 0 {
			break
		}
		data = append(data, value)
	}
	return string(data)
}

type runtimeDLLSpec struct {
	archivePath string
	targetName  string
	size        uint64
	sha256      string
}

func extractORTRuntime(
	model ai.InstalledModel,
	wantGPU bool,
) (dllPath string, directMLPath string, warning string, err error) {
	ortPackage, ok := modelFileByRole(model.Manifest, roleORTDirectML)
	if !ok {
		return "", "", "", fmt.Errorf(
			"Tagger manifest is missing %q runtime file role",
			roleORTDirectML,
		)
	}

	ortExtractMu.Lock()
	defer ortExtractMu.Unlock()

	runtimeDir := filepath.Join(model.RootDir, ".runtime")
	if err := os.MkdirAll(runtimeDir, 0o755); err != nil {
		return "", "", "", fmt.Errorf("create Tagger ONNX Runtime directory: %w", err)
	}

	if err := extractPinnedRuntimeFiles(
		filepath.Join(model.RootDir, filepath.FromSlash(ortPackage.Path)),
		runtimeDir,
		[]runtimeDLLSpec{
			{
				archivePath: ortDLLArchivePath,
				targetName:  "onnxruntime.dll",
				size:        ortDLLSize,
				sha256:      ortDLLSHA256,
			},
			{
				archivePath: ortSharedArchivePath,
				targetName:  "onnxruntime_providers_shared.dll",
				size:        ortSharedDLLSize,
				sha256:      ortSharedDLLSHA256,
			},
		},
	); err != nil {
		return "", "", "", err
	}

	dllPath = filepath.Join(runtimeDir, "onnxruntime.dll")
	if !wantGPU {
		return dllPath, "", "", nil
	}

	directMLPackage, ok := modelFileByRole(model.Manifest, roleDirectML)
	if !ok {
		return dllPath, "", "DirectML package is not installed; using CPU fallback", nil
	}
	if err := extractPinnedRuntimeFiles(
		filepath.Join(model.RootDir, filepath.FromSlash(directMLPackage.Path)),
		runtimeDir,
		[]runtimeDLLSpec{
			{
				archivePath: directMLDLLArchivePath,
				targetName:  "DirectML.dll",
				size:        directMLDLLSize,
				sha256:      directMLDLLSHA256,
			},
		},
	); err != nil {
		return dllPath, "", fmt.Sprintf(
			"DirectML runtime extraction failed; using CPU fallback: %v",
			err,
		), nil
	}
	return dllPath, filepath.Join(runtimeDir, "DirectML.dll"), "", nil
}

func extractedDLLMatches(path string, expectedSize uint64, expectedSHA256 string) bool {
	info, err := os.Stat(path)
	if err != nil || !info.Mode().IsRegular() || uint64(info.Size()) != expectedSize {
		return false
	}
	input, err := os.Open(path)
	if err != nil {
		return false
	}
	defer input.Close()
	hasher := sha256.New()
	if _, err := io.Copy(hasher, input); err != nil {
		return false
	}
	return strings.EqualFold(hex.EncodeToString(hasher.Sum(nil)), expectedSHA256)
}

func extractPinnedRuntimeFiles(
	archivePath string,
	runtimeDir string,
	specs []runtimeDLLSpec,
) error {
	reader, err := zip.OpenReader(archivePath)
	if err != nil {
		return fmt.Errorf("open Tagger runtime package %s: %w", filepath.Base(archivePath), err)
	}
	defer reader.Close()

	byName := make(map[string]*zip.File, len(reader.File))
	for _, file := range reader.File {
		normalized := strings.ToLower(strings.ReplaceAll(file.Name, "\\", "/"))
		byName[normalized] = file
	}

	for _, spec := range specs {
		target := filepath.Join(runtimeDir, spec.targetName)
		if extractedDLLMatches(target, spec.size, spec.sha256) {
			continue
		}

		source := byName[strings.ToLower(spec.archivePath)]
		if source == nil {
			return fmt.Errorf(
				"%s was not found in %s",
				spec.archivePath,
				filepath.Base(archivePath),
			)
		}
		if source.UncompressedSize64 != spec.size {
			return fmt.Errorf(
				"runtime package entry %s has size %d, want %d",
				spec.archivePath,
				source.UncompressedSize64,
				spec.size,
			)
		}

		if _, err := os.Stat(target); err == nil {
			if err := os.Remove(target); err != nil {
				return fmt.Errorf(
					"remove invalid runtime DLL %s before repair: %w",
					spec.targetName,
					err,
				)
			}
		} else if !errors.Is(err, os.ErrNotExist) {
			return fmt.Errorf("inspect runtime DLL %s: %w", spec.targetName, err)
		}

		input, err := source.Open()
		if err != nil {
			return fmt.Errorf("open %s from runtime package: %w", spec.archivePath, err)
		}
		output, err := os.CreateTemp(runtimeDir, spec.targetName+"-*.tmp")
		if err != nil {
			_ = input.Close()
			return fmt.Errorf("create temporary runtime DLL %s: %w", spec.targetName, err)
		}
		temp := output.Name()
		copyErr := func() error {
			defer input.Close()
			if _, err := io.Copy(output, input); err != nil {
				return err
			}
			if err := output.Sync(); err != nil {
				return err
			}
			return output.Close()
		}()
		if copyErr != nil {
			_ = output.Close()
			_ = os.Remove(temp)
			return fmt.Errorf("extract runtime DLL %s: %w", spec.targetName, copyErr)
		}
		if !extractedDLLMatches(temp, spec.size, spec.sha256) {
			_ = os.Remove(temp)
			return fmt.Errorf("runtime DLL integrity check failed for %s", spec.targetName)
		}

		if extractedDLLMatches(target, spec.size, spec.sha256) {
			_ = os.Remove(temp)
			continue
		}
		if err := os.Rename(temp, target); err != nil {
			if extractedDLLMatches(target, spec.size, spec.sha256) {
				_ = os.Remove(temp)
				continue
			}
			_ = os.Remove(temp)
			return fmt.Errorf("publish runtime DLL %s: %w", spec.targetName, err)
		}
	}
	return nil
}

func joinWarning(current, next string) string {
	current = strings.TrimSpace(current)
	next = strings.TrimSpace(next)
	switch {
	case current == "":
		return next
	case next == "":
		return current
	default:
		return current + "; " + next
	}
}
