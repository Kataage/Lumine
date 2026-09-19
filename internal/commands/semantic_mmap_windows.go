//go:build windows

package commands

import (
	"os"
	"unsafe"

	"golang.org/x/sys/windows"
)

func mapReadOnlyFile(file *os.File, size int) ([]byte, func() error, error) {
	mapping, err := windows.CreateFileMapping(
		windows.Handle(file.Fd()),
		nil,
		windows.PAGE_READONLY,
		0,
		0,
		nil,
	)
	if err != nil {
		return nil, nil, err
	}
	addr, err := windows.MapViewOfFile(
		mapping,
		windows.FILE_MAP_READ,
		0,
		0,
		uintptr(size),
	)
	_ = windows.CloseHandle(mapping)
	if err != nil {
		return nil, nil, err
	}
	data := unsafe.Slice((*byte)(unsafe.Pointer(addr)), size)
	return data, func() error {
		return windows.UnmapViewOfFile(addr)
	}, nil
}
