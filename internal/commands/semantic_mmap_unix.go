//go:build !windows

package commands

import (
	"os"
	"syscall"
)

func mapReadOnlyFile(file *os.File, size int) ([]byte, func() error, error) {
	data, err := syscall.Mmap(
		int(file.Fd()),
		0,
		size,
		syscall.PROT_READ,
		syscall.MAP_SHARED,
	)
	if err != nil {
		return nil, nil, err
	}
	return data, func() error {
		return syscall.Munmap(data)
	}, nil
}
