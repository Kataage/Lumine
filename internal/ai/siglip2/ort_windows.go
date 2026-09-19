//go:build windows && amd64

package siglip2

import (
	"archive/zip"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"unsafe"

	"github.com/kataage/lumine/internal/ai"
)

const (
	ortAPIVersion = uintptr(29)

	ortFnGetErrorMessage                  = uintptr(2)
	ortFnCreateEnv                        = uintptr(3)
	ortFnCreateSession                    = uintptr(7)
	ortFnRun                              = uintptr(9)
	ortFnCreateSessionOptions             = uintptr(10)
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
	ortEnableAll      = uintptr(99)
	ortArenaAllocator = uintptr(1)
	ortMemTypeDefault = uintptr(0)
	ortTensorFloat    = uintptr(1)
	ortTensorInt64    = uintptr(7)

	siglipEmbeddingSize = 768
)

type windowsORT struct {
	dll           *syscall.DLL
	api           uintptr
	env           uintptr
	memoryInfo    uintptr
	textSession   uintptr
	visionSession uintptr
}

func newORTBackend(modelRoot string, options ai.LoadOptions) (ortBackend, error) {
	// The first implementation intentionally uses the CPU execution provider.
	// GPU opt-in remains authoritative globally; this engine simply has no GPU
	// provider to activate, so it never consumes GPU resources implicitly.
	_ = options

	dllPath, err := extractORTDLL(modelRoot)
	if err != nil {
		return nil, err
	}
	dll, err := syscall.LoadDLL(dllPath)
	if err != nil {
		return nil, fmt.Errorf("load ONNX Runtime DLL: %w", err)
	}

	backend := &windowsORT{dll: dll}
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
	if !strings.HasPrefix(version, "1.29.") {
		return nil, fmt.Errorf("unexpected ONNX Runtime version %q; expected 1.29.x", version)
	}

	logID := append([]byte("lumine-siglip2"), 0)
	if err := backend.callStatus(
		ortFnCreateEnv,
		ortLoggingWarning,
		uintptr(unsafe.Pointer(&logID[0])),
		uintptr(unsafe.Pointer(&backend.env)),
	); err != nil {
		return nil, fmt.Errorf("create ONNX Runtime environment: %w", err)
	}

	if err := backend.callStatus(
		ortFnCreateCpuMemoryInfo,
		ortArenaAllocator,
		ortMemTypeDefault,
		uintptr(unsafe.Pointer(&backend.memoryInfo)),
	); err != nil {
		return nil, fmt.Errorf("create ONNX Runtime memory info: %w", err)
	}

	optionsPtr, err := backend.createSessionOptions()
	if err != nil {
		return nil, err
	}
	defer backend.release(ortFnReleaseSessionOptions, optionsPtr)

	textPath, err := syscall.UTF16PtrFromString(filepath.Join(modelRoot, textModelPath))
	if err != nil {
		return nil, fmt.Errorf("encode text model path: %w", err)
	}
	if err := backend.callStatus(
		ortFnCreateSession,
		backend.env,
		uintptr(unsafe.Pointer(textPath)),
		optionsPtr,
		uintptr(unsafe.Pointer(&backend.textSession)),
	); err != nil {
		return nil, fmt.Errorf("create SigLIP2 text session: %w", err)
	}

	visionPath, err := syscall.UTF16PtrFromString(filepath.Join(modelRoot, visionModelPath))
	if err != nil {
		return nil, fmt.Errorf("encode vision model path: %w", err)
	}
	if err := backend.callStatus(
		ortFnCreateSession,
		backend.env,
		uintptr(unsafe.Pointer(visionPath)),
		optionsPtr,
		uintptr(unsafe.Pointer(&backend.visionSession)),
	); err != nil {
		return nil, fmt.Errorf("create SigLIP2 vision session: %w", err)
	}

	runtime.KeepAlive(logID)
	runtime.KeepAlive(textPath)
	runtime.KeepAlive(visionPath)
	ok = true
	return backend, nil
}

func (r *windowsORT) createSessionOptions() (uintptr, error) {
	var options uintptr
	if err := r.callStatus(
		ortFnCreateSessionOptions,
		uintptr(unsafe.Pointer(&options)),
	); err != nil {
		return 0, fmt.Errorf("create ONNX Runtime session options: %w", err)
	}
	if err := r.callStatus(
		ortFnSetSessionGraphOptimizationLevel,
		options,
		ortEnableAll,
	); err != nil {
		r.release(ortFnReleaseSessionOptions, options)
		return 0, fmt.Errorf("enable ONNX graph optimizations: %w", err)
	}
	return options, nil
}

func (r *windowsORT) EmbedText(input [siglipTextLength]int64) ([]float32, error) {
	output := make([]float32, siglipEmbeddingSize)
	if err := r.runSingle(
		r.textSession,
		"input_ids",
		uintptr(unsafe.Pointer(&input[0])),
		uintptr(len(input))*unsafe.Sizeof(input[0]),
		[]int64{1, siglipTextLength},
		ortTensorInt64,
		"pooler_output",
		uintptr(unsafe.Pointer(&output[0])),
		uintptr(len(output))*unsafe.Sizeof(output[0]),
		[]int64{1, siglipEmbeddingSize},
		ortTensorFloat,
	); err != nil {
		return nil, fmt.Errorf("run SigLIP2 text encoder: %w", err)
	}
	runtime.KeepAlive(input)
	runtime.KeepAlive(output)
	return output, nil
}

func (r *windowsORT) EmbedImage(input []float32) ([]float32, error) {
	expected := siglipChannels * siglipImageSize * siglipImageSize
	if len(input) != expected {
		return nil, fmt.Errorf("SigLIP2 image tensor has %d values, want %d", len(input), expected)
	}
	output := make([]float32, siglipEmbeddingSize)
	if err := r.runSingle(
		r.visionSession,
		"pixel_values",
		uintptr(unsafe.Pointer(&input[0])),
		uintptr(len(input))*unsafe.Sizeof(input[0]),
		[]int64{1, siglipChannels, siglipImageSize, siglipImageSize},
		ortTensorFloat,
		"pooler_output",
		uintptr(unsafe.Pointer(&output[0])),
		uintptr(len(output))*unsafe.Sizeof(output[0]),
		[]int64{1, siglipEmbeddingSize},
		ortTensorFloat,
	); err != nil {
		return nil, fmt.Errorf("run SigLIP2 vision encoder: %w", err)
	}
	runtime.KeepAlive(input)
	runtime.KeepAlive(output)
	return output, nil
}

func (r *windowsORT) runSingle(
	session uintptr,
	inputName string,
	inputData uintptr,
	inputBytes uintptr,
	inputShape []int64,
	inputType uintptr,
	outputName string,
	outputData uintptr,
	outputBytes uintptr,
	outputShape []int64,
	outputType uintptr,
) error {
	if session == 0 || r.memoryInfo == 0 {
		return errors.New("ONNX Runtime session is not initialized")
	}

	var inputValue, outputValue uintptr
	if err := r.callStatus(
		ortFnCreateTensorWithDataAsOrtValue,
		r.memoryInfo,
		inputData,
		inputBytes,
		uintptr(unsafe.Pointer(&inputShape[0])),
		uintptr(len(inputShape)),
		inputType,
		uintptr(unsafe.Pointer(&inputValue)),
	); err != nil {
		return fmt.Errorf("create input tensor: %w", err)
	}
	defer r.release(ortFnReleaseValue, inputValue)

	if err := r.callStatus(
		ortFnCreateTensorWithDataAsOrtValue,
		r.memoryInfo,
		outputData,
		outputBytes,
		uintptr(unsafe.Pointer(&outputShape[0])),
		uintptr(len(outputShape)),
		outputType,
		uintptr(unsafe.Pointer(&outputValue)),
	); err != nil {
		return fmt.Errorf("create output tensor: %w", err)
	}
	defer r.release(ortFnReleaseValue, outputValue)

	inputNameBytes := append([]byte(inputName), 0)
	outputNameBytes := append([]byte(outputName), 0)
	inputNames := []uintptr{uintptr(unsafe.Pointer(&inputNameBytes[0]))}
	outputNames := []uintptr{uintptr(unsafe.Pointer(&outputNameBytes[0]))}
	inputValues := []uintptr{inputValue}
	outputValues := []uintptr{outputValue}

	err := r.callStatus(
		ortFnRun,
		session,
		0,
		uintptr(unsafe.Pointer(&inputNames[0])),
		uintptr(unsafe.Pointer(&inputValues[0])),
		1,
		uintptr(unsafe.Pointer(&outputNames[0])),
		1,
		uintptr(unsafe.Pointer(&outputValues[0])),
	)
	runtime.KeepAlive(inputShape)
	runtime.KeepAlive(outputShape)
	runtime.KeepAlive(inputNameBytes)
	runtime.KeepAlive(outputNameBytes)
	runtime.KeepAlive(inputNames)
	runtime.KeepAlive(outputNames)
	runtime.KeepAlive(inputValues)
	runtime.KeepAlive(outputValues)
	return err
}

func (r *windowsORT) Close() error {
	if r == nil {
		return nil
	}
	if r.visionSession != 0 {
		r.release(ortFnReleaseSession, r.visionSession)
		r.visionSession = 0
	}
	if r.textSession != 0 {
		r.release(ortFnReleaseSession, r.textSession)
		r.textSession = 0
	}
	if r.memoryInfo != 0 {
		r.release(ortFnReleaseMemoryInfo, r.memoryInfo)
		r.memoryInfo = 0
	}
	if r.env != 0 {
		r.release(ortFnReleaseEnv, r.env)
		r.env = 0
	}
	if r.dll != nil {
		err := r.dll.Release()
		r.dll = nil
		return err
	}
	return nil
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
	if status == 0 {
		return nil
	}

	messageFn := *(*uintptr)(unsafe.Pointer(r.api + ortFnGetErrorMessage*unsafe.Sizeof(uintptr(0))))
	messagePtr, _, _ := syscall.SyscallN(messageFn, status)
	message := readCString(messagePtr)
	releaseStatusFn := *(*uintptr)(unsafe.Pointer(r.api + ortFnReleaseStatus*unsafe.Sizeof(uintptr(0))))
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

func extractORTDLL(modelRoot string) (string, error) {
	zipPath := filepath.Join(modelRoot, runtimeZipPath)
	reader, err := zip.OpenReader(zipPath)
	if err != nil {
		return "", fmt.Errorf("open ONNX Runtime archive: %w", err)
	}
	defer reader.Close()

	var source *zip.File
	for _, file := range reader.File {
		name := strings.ReplaceAll(file.Name, "\\", "/")
		if strings.HasSuffix(strings.ToLower(name), "/lib/onnxruntime.dll") {
			source = file
			break
		}
	}
	if source == nil {
		return "", errors.New("onnxruntime.dll was not found in runtime archive")
	}

	runtimeDir := filepath.Join(modelRoot, ".runtime")
	if err := os.MkdirAll(runtimeDir, 0o755); err != nil {
		return "", fmt.Errorf("create ONNX Runtime directory: %w", err)
	}
	target := filepath.Join(runtimeDir, "onnxruntime.dll")
	temp := target + ".tmp"

	input, err := source.Open()
	if err != nil {
		return "", fmt.Errorf("open ONNX Runtime DLL from archive: %w", err)
	}
	output, err := os.Create(temp)
	if err != nil {
		input.Close()
		return "", fmt.Errorf("create extracted ONNX Runtime DLL: %w", err)
	}
	_, copyErr := io.Copy(output, input)
	closeOutErr := output.Close()
	closeInErr := input.Close()
	if copyErr != nil {
		_ = os.Remove(temp)
		return "", fmt.Errorf("extract ONNX Runtime DLL: %w", copyErr)
	}
	if closeOutErr != nil {
		_ = os.Remove(temp)
		return "", closeOutErr
	}
	if closeInErr != nil {
		_ = os.Remove(temp)
		return "", closeInErr
	}
	_ = os.Remove(target)
	if err := os.Rename(temp, target); err != nil {
		_ = os.Remove(temp)
		return "", fmt.Errorf("commit ONNX Runtime DLL extraction: %w", err)
	}
	return target, nil
}
