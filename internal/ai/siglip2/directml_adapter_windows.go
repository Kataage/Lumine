//go:build windows && amd64

package siglip2

import (
	"errors"
	"fmt"
	"strings"
	"syscall"
	"unsafe"
)

const (
	dxgiAdapterFlagSoftware = uint32(2)
	dxgiErrorNotFound       = uint32(0x887A0002)
)

type dxgiGUID struct {
	Data1 uint32
	Data2 uint16
	Data3 uint16
	Data4 [8]byte
}

var iidIDXGIFactory1 = dxgiGUID{
	Data1: 0x770AAE78,
	Data2: 0xF26F,
	Data3: 0x4DBA,
	Data4: [8]byte{0xA8, 0x29, 0x25, 0x3C, 0x83, 0xD1, 0xB3, 0x87},
}

type dxgiAdapterDesc1 struct {
	Description          [128]uint16
	VendorID             uint32
	DeviceID             uint32
	SubSysID             uint32
	Revision             uint32
	DedicatedVideoMemory uintptr
	DedicatedSystemMemory uintptr
	SharedSystemMemory   uintptr
	AdapterLuid          int64
	Flags                uint32
}

func enumerateDirectMLAdapters() ([]directMLAdapterCandidate, error) {
	dll, err := syscall.LoadDLL("dxgi.dll")
	if err != nil {
		return nil, fmt.Errorf("load dxgi.dll: %w", err)
	}
	defer dll.Release()

	createFactory, err := dll.FindProc("CreateDXGIFactory1")
	if err != nil {
		return nil, fmt.Errorf("find CreateDXGIFactory1: %w", err)
	}

	var factory uintptr
	hr, _, _ := createFactory.Call(
		uintptr(unsafe.Pointer(&iidIDXGIFactory1)),
		uintptr(unsafe.Pointer(&factory)),
	)
	if failedHRESULT(hr) || factory == 0 {
		return nil, fmt.Errorf("CreateDXGIFactory1 failed: HRESULT 0x%08X", uint32(hr))
	}
	defer releaseCOM(factory)

	candidates := make([]directMLAdapterCandidate, 0, 4)
	for index := uint32(0); ; index++ {
		var adapter uintptr
		hr = callCOM(factory, 12, uintptr(index), uintptr(unsafe.Pointer(&adapter)))
		if uint32(hr) == dxgiErrorNotFound {
			break
		}
		if failedHRESULT(hr) {
			return nil, fmt.Errorf("IDXGIFactory1::EnumAdapters1(%d) failed: HRESULT 0x%08X", index, uint32(hr))
		}
		if adapter == 0 {
			return nil, fmt.Errorf("IDXGIFactory1::EnumAdapters1(%d) returned nil adapter", index)
		}

		var desc dxgiAdapterDesc1
		descHR := callCOM(adapter, 10, uintptr(unsafe.Pointer(&desc)))
		releaseCOM(adapter)
		if failedHRESULT(descHR) {
			return nil, fmt.Errorf("IDXGIAdapter1::GetDesc1(%d) failed: HRESULT 0x%08X", index, uint32(descHR))
		}
		name := strings.TrimSpace(syscall.UTF16ToString(desc.Description[:]))
		if name == "" {
			name = fmt.Sprintf("DXGI adapter %d", index)
		}
		software := desc.Flags&dxgiAdapterFlagSoftware != 0 ||
			strings.EqualFold(name, "Microsoft Basic Render Driver")
		candidates = append(candidates, directMLAdapterCandidate{
			ID:                   int(index),
			Name:                 name,
			DedicatedVideoMemory: uint64(desc.DedicatedVideoMemory),
			Software:             software,
		})
	}
	if len(candidates) == 0 {
		return nil, errors.New("DXGI reported no display adapters")
	}
	return candidates, nil
}

func selectDirectMLAdapter() (directMLAdapterCandidate, error) {
	candidates, err := enumerateDirectMLAdapters()
	if err != nil {
		return directMLAdapterCandidate{}, err
	}
	selected, ok := chooseDirectMLAdapter(candidates)
	if !ok {
		return directMLAdapterCandidate{}, errors.New("DXGI reported no hardware adapter suitable for DirectML")
	}
	return selected, nil
}

func failedHRESULT(value uintptr) bool {
	return int32(uint32(value)) < 0
}

func callCOM(object uintptr, method int, args ...uintptr) uintptr {
	if object == 0 {
		return uintptr(uint32(0x80004003))
	}
	vtable := *(*uintptr)(unsafe.Pointer(object))
	fn := *(*uintptr)(unsafe.Pointer(vtable + uintptr(method)*unsafe.Sizeof(uintptr(0))))
	callArgs := make([]uintptr, 0, len(args)+1)
	callArgs = append(callArgs, object)
	callArgs = append(callArgs, args...)
	result, _, _ := syscall.SyscallN(fn, callArgs...)
	return result
}

func releaseCOM(object uintptr) {
	if object != 0 {
		_ = callCOM(object, 2)
	}
}
