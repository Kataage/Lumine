package ai

import (
	"bytes"
	"context"
	"fmt"
	"os"
	"os/exec"
	"sync"
	"time"
)

const defaultSidecarLogLimit = 256 * 1024

type lockedLimitedBuffer struct {
	mu    sync.Mutex
	data  []byte
	limit int
}

func newLockedLimitedBuffer(limit int) *lockedLimitedBuffer {
	if limit <= 0 {
		limit = defaultSidecarLogLimit
	}
	return &lockedLimitedBuffer{limit: limit}
}

func (b *lockedLimitedBuffer) Write(p []byte) (int, error) {
	b.mu.Lock()
	defer b.mu.Unlock()

	b.data = append(b.data, p...)
	if len(b.data) > b.limit {
		b.data = append([]byte(nil), b.data[len(b.data)-b.limit:]...)
	}
	return len(p), nil
}

func (b *lockedLimitedBuffer) String() string {
	b.mu.Lock()
	defer b.mu.Unlock()
	return string(bytes.Clone(b.data))
}

type SidecarProcess struct {
	mu sync.Mutex

	cmd       *exec.Cmd
	cancel    context.CancelFunc
	done      chan error
	stdout    *lockedLimitedBuffer
	stderr    *lockedLimitedBuffer
	lastError error
}

func NewSidecarProcess() *SidecarProcess {
	return &SidecarProcess{
		stdout: newLockedLimitedBuffer(defaultSidecarLogLimit),
		stderr: newLockedLimitedBuffer(defaultSidecarLogLimit),
	}
}

func (s *SidecarProcess) Start(ctx context.Context, executable string, args []string, env []string) error {
	s.mu.Lock()
	if s.cmd != nil {
		s.mu.Unlock()
		return fmt.Errorf("sidecar is already running")
	}

	processCtx, cancel := context.WithCancel(ctx)
	cmd := exec.CommandContext(processCtx, executable, args...)
	if len(env) > 0 {
		cmd.Env = append(os.Environ(), env...)
	}
	stdout := newLockedLimitedBuffer(defaultSidecarLogLimit)
	stderr := newLockedLimitedBuffer(defaultSidecarLogLimit)
	cmd.Stdout = stdout
	cmd.Stderr = stderr
	done := make(chan error, 1)

	if err := cmd.Start(); err != nil {
		cancel()
		s.lastError = err
		s.mu.Unlock()
		return fmt.Errorf("start sidecar %s: %w", executable, err)
	}

	s.cmd = cmd
	s.cancel = cancel
	s.done = done
	s.stdout = stdout
	s.stderr = stderr
	s.lastError = nil
	s.mu.Unlock()

	go func() {
		err := cmd.Wait()
		s.mu.Lock()
		if s.cmd == cmd {
			s.lastError = err
			s.cmd = nil
			s.cancel = nil
			s.done = nil
		}
		s.mu.Unlock()
		done <- err
		close(done)
	}()
	return nil
}

func (s *SidecarProcess) Stop(ctx context.Context) error {
	s.mu.Lock()
	cmd := s.cmd
	cancel := s.cancel
	done := s.done
	s.mu.Unlock()

	if cmd == nil {
		return nil
	}
	if cancel != nil {
		cancel()
	}

	waitResult := func(err error) error {
		if err != nil && !errorsIsContextTermination(err) {
			return fmt.Errorf("sidecar exit: %w", err)
		}
		return nil
	}

	select {
	case err := <-done:
		return waitResult(err)
	case <-ctx.Done():
		if cmd.Process != nil {
			_ = cmd.Process.Kill()
		}

		// Process.Kill is asynchronous on some platforms. Do not return while a
		// Lumine-owned child may still be alive: wait briefly for cmd.Wait to
		// observe the forced termination.
		select {
		case err := <-done:
			if stopErr := waitResult(err); stopErr != nil {
				return stopErr
			}
			return ctx.Err()
		case <-time.After(2 * time.Second):
			return fmt.Errorf("sidecar did not exit after forced kill: %w", ctx.Err())
		}
	}
}

func (s *SidecarProcess) Running() bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.cmd != nil
}

func (s *SidecarProcess) Logs() (stdout string, stderr string) {
	s.mu.Lock()
	out := s.stdout
	errOut := s.stderr
	s.mu.Unlock()
	return out.String(), errOut.String()
}

func (s *SidecarProcess) LastError() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.lastError
}

func errorsIsContextTermination(err error) bool {
	if err == nil {
		return false
	}
	// CommandContext terminates the child process when its context is cancelled.
	// The platform-specific *ExitError is expected during an explicit Stop.
	_, ok := err.(*exec.ExitError)
	return ok
}
