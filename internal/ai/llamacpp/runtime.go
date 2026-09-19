package llamacpp

import (
	"archive/zip"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"strings"
)

const runtimeInstallManifestName = "runtime.json"

var safeRuntimeComponentPattern = regexp.MustCompile(`^[A-Za-z0-9._-]+$`)

type RuntimeManifest struct {
	ID               string `json:"id"`
	Version          string `json:"version"`
	URL              string `json:"url"`
	SHA256           string `json:"sha256"`
	SizeBytes        int64  `json:"sizeBytes"`
	ExecutableName   string `json:"executableName"`
	Platform         string `json:"platform"`
	Architecture     string `json:"architecture"`
}

type InstalledRuntime struct {
	ID                 string `json:"id"`
	Version            string `json:"version"`
	RootDir            string `json:"rootDir"`
	ExecutablePath     string `json:"executablePath"`
	ArchiveSHA256      string `json:"archiveSha256"`
	ExecutableSHA256   string `json:"executableSha256"`
	Platform           string `json:"platform"`
	Architecture       string `json:"architecture"`
}

type RuntimeDownloadProgress struct {
	RuntimeID       string `json:"runtimeId"`
	Version         string `json:"version"`
	BytesDownloaded int64  `json:"bytesDownloaded"`
	BytesTotal      int64  `json:"bytesTotal"`
	Done            bool   `json:"done"`
}

type RuntimeProgressFunc func(RuntimeDownloadProgress)

type RuntimeStore struct {
	root   string
	client *http.Client
}

func DefaultRuntimeManifest() RuntimeManifest {
	return RuntimeManifest{
		ID:             "llama.cpp-win-cpu-x64",
		Version:        "b10964",
		URL:            "https://github.com/ggml-org/llama.cpp/releases/download/b10964/llama-b10964-bin-win-cpu-x64.zip",
		SHA256:         "917f39c076402c421224824607397af20f53625a60defc20e8dd22446bf4c5d7",
		SizeBytes:      18427629,
		ExecutableName: "llama-server.exe",
		Platform:       "windows",
		Architecture:   "amd64",
	}
}

func VulkanRuntimeManifest() RuntimeManifest {
	return RuntimeManifest{
		ID:             "llama.cpp-win-vulkan-x64",
		Version:        "b10964",
		URL:            "https://github.com/ggml-org/llama.cpp/releases/download/b10964/llama-b10964-bin-win-vulkan-x64.zip",
		SHA256:         "1ee3ad952f4ba71f438bd6d7bebef19e1c7af04adcaa35d08b4ddabb27d4c642",
		SizeBytes:      0,
		ExecutableName: "llama-server.exe",
		Platform:       "windows",
		Architecture:   "amd64",
	}
}

func NewRuntimeStore(root string) *RuntimeStore {
	return &RuntimeStore{
		root:   root,
		client: &http.Client{},
	}
}

func (s *RuntimeStore) Root() string {
	return s.root
}

func (s *RuntimeStore) SetHTTPClient(client *http.Client) {
	if client != nil {
		s.client = client
	}
}

func ValidateRuntimeManifest(manifest RuntimeManifest) error {
	if !safeRuntimeComponentPattern.MatchString(manifest.ID) {
		return fmt.Errorf("invalid runtime id %q", manifest.ID)
	}
	if !safeRuntimeComponentPattern.MatchString(manifest.Version) {
		return fmt.Errorf("invalid runtime version %q", manifest.Version)
	}
	parsedURL, err := url.Parse(manifest.URL)
	if err != nil {
		return fmt.Errorf("invalid runtime URL: %w", err)
	}
	if parsedURL.Scheme != "http" && parsedURL.Scheme != "https" {
		return fmt.Errorf("unsupported runtime URL scheme %q", parsedURL.Scheme)
	}
	if len(manifest.SHA256) != sha256.Size*2 {
		return fmt.Errorf("runtime sha256 must contain %d hex characters", sha256.Size*2)
	}
	if _, err := hex.DecodeString(manifest.SHA256); err != nil {
		return fmt.Errorf("invalid runtime sha256: %w", err)
	}
	if manifest.SizeBytes < 0 {
		return errors.New("runtime size cannot be negative")
	}
	if filepath.Base(manifest.ExecutableName) != manifest.ExecutableName || manifest.ExecutableName == "." {
		return fmt.Errorf("invalid runtime executable name %q", manifest.ExecutableName)
	}
	if manifest.Platform == "" || manifest.Architecture == "" {
		return errors.New("runtime platform and architecture are required")
	}
	return nil
}

func (s *RuntimeStore) Install(
	ctx context.Context,
	manifest RuntimeManifest,
	progress RuntimeProgressFunc,
) (InstalledRuntime, error) {
	if err := ValidateRuntimeManifest(manifest); err != nil {
		return InstalledRuntime{}, err
	}
	if err := ctx.Err(); err != nil {
		return InstalledRuntime{}, err
	}
	if err := os.MkdirAll(s.root, 0o755); err != nil {
		return InstalledRuntime{}, fmt.Errorf("create runtime root: %w", err)
	}

	staging, err := os.MkdirTemp(s.root, "."+manifest.Version+"-install-*")
	if err != nil {
		return InstalledRuntime{}, fmt.Errorf("create runtime staging: %w", err)
	}
	defer os.RemoveAll(staging)

	archivePath := filepath.Join(staging, "runtime.zip")
	if err := s.downloadArchive(ctx, manifest, archivePath, progress); err != nil {
		return InstalledRuntime{}, err
	}
	downloadedTotal := manifest.SizeBytes
	if info, statErr := os.Stat(archivePath); statErr == nil {
		downloadedTotal = info.Size()
	}
	extractRoot := filepath.Join(staging, "files")
	if err := extractZipSafely(archivePath, extractRoot); err != nil {
		return InstalledRuntime{}, fmt.Errorf("extract runtime: %w", err)
	}

	executable, err := findFileByBaseName(extractRoot, manifest.ExecutableName)
	if err != nil {
		return InstalledRuntime{}, err
	}
	executableHash, err := sha256File(executable)
	if err != nil {
		return InstalledRuntime{}, fmt.Errorf("hash runtime executable: %w", err)
	}

	relativeExecutable, err := filepath.Rel(extractRoot, executable)
	if err != nil {
		return InstalledRuntime{}, fmt.Errorf("resolve runtime executable path: %w", err)
	}
	installed := InstalledRuntime{
		ID:               manifest.ID,
		Version:          manifest.Version,
		RootDir:          "",
		ExecutablePath:   filepath.ToSlash(relativeExecutable),
		ArchiveSHA256:    strings.ToLower(manifest.SHA256),
		ExecutableSHA256: executableHash,
		Platform:         manifest.Platform,
		Architecture:     manifest.Architecture,
	}
	metadata, err := json.MarshalIndent(installed, "", "  ")
	if err != nil {
		return InstalledRuntime{}, fmt.Errorf("encode runtime metadata: %w", err)
	}
	if err := os.WriteFile(filepath.Join(extractRoot, runtimeInstallManifestName), metadata, 0o644); err != nil {
		return InstalledRuntime{}, fmt.Errorf("write runtime metadata: %w", err)
	}

	target := filepath.Join(s.root, manifest.ID, manifest.Version)
	if err := os.MkdirAll(filepath.Dir(target), 0o755); err != nil {
		return InstalledRuntime{}, fmt.Errorf("create runtime target parent: %w", err)
	}
	backup := target + ".previous"
	_ = os.RemoveAll(backup)
	hadPrevious := false
	if _, statErr := os.Stat(target); statErr == nil {
		if err := os.Rename(target, backup); err != nil {
			return InstalledRuntime{}, fmt.Errorf("stage previous runtime: %w", err)
		}
		hadPrevious = true
	} else if !os.IsNotExist(statErr) {
		return InstalledRuntime{}, fmt.Errorf("inspect existing runtime: %w", statErr)
	}

	if err := os.Rename(extractRoot, target); err != nil {
		if hadPrevious {
			_ = os.Rename(backup, target)
		}
		return InstalledRuntime{}, fmt.Errorf("commit runtime installation: %w", err)
	}

	result, err := s.Verify(manifest)
	if err != nil {
		_ = os.RemoveAll(target)
		if hadPrevious {
			if restoreErr := os.Rename(backup, target); restoreErr != nil {
				return InstalledRuntime{}, fmt.Errorf("verify installed runtime: %w (restore previous runtime: %v)", err, restoreErr)
			}
		}
		return InstalledRuntime{}, fmt.Errorf("verify installed runtime: %w", err)
	}
	if hadPrevious {
		_ = os.RemoveAll(backup)
	}
	if progress != nil {
		progress(RuntimeDownloadProgress{
			RuntimeID:       manifest.ID,
			Version:         manifest.Version,
			BytesDownloaded: downloadedTotal,
			BytesTotal:      downloadedTotal,
			Done:            true,
		})
	}
	return result, nil
}

func (s *RuntimeStore) downloadArchive(
	ctx context.Context,
	manifest RuntimeManifest,
	target string,
	progress RuntimeProgressFunc,
) error {
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, manifest.URL, nil)
	if err != nil {
		return fmt.Errorf("create runtime download request: %w", err)
	}
	response, err := s.client.Do(request)
	if err != nil {
		return fmt.Errorf("download runtime: %w", err)
	}
	defer response.Body.Close()
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return fmt.Errorf("download runtime: HTTP %s", response.Status)
	}
	totalBytes := manifest.SizeBytes
	if totalBytes <= 0 && response.ContentLength > 0 {
		totalBytes = response.ContentLength
	}

	output, err := os.Create(target)
	if err != nil {
		return fmt.Errorf("create runtime archive: %w", err)
	}
	hasher := sha256.New()
	buffer := make([]byte, 256*1024)
	var downloaded int64
	for {
		if err := ctx.Err(); err != nil {
			_ = output.Close()
			return err
		}
		n, readErr := response.Body.Read(buffer)
		if n > 0 {
			chunk := buffer[:n]
			if _, err := output.Write(chunk); err != nil {
				_ = output.Close()
				return fmt.Errorf("write runtime archive: %w", err)
			}
			if _, err := hasher.Write(chunk); err != nil {
				_ = output.Close()
				return fmt.Errorf("hash runtime archive: %w", err)
			}
			downloaded += int64(n)
			if progress != nil {
				progress(RuntimeDownloadProgress{
					RuntimeID:       manifest.ID,
					Version:         manifest.Version,
					BytesDownloaded: downloaded,
					BytesTotal:      totalBytes,
				})
			}
		}
		if readErr == io.EOF {
			break
		}
		if readErr != nil {
			_ = output.Close()
			return fmt.Errorf("read runtime archive: %w", readErr)
		}
	}
	if err := output.Close(); err != nil {
		return fmt.Errorf("close runtime archive: %w", err)
	}
	if manifest.SizeBytes > 0 && downloaded != manifest.SizeBytes {
		return fmt.Errorf("runtime size mismatch: got %d, want %d", downloaded, manifest.SizeBytes)
	}
	actual := hex.EncodeToString(hasher.Sum(nil))
	if !strings.EqualFold(actual, manifest.SHA256) {
		return fmt.Errorf("runtime sha256 mismatch: got %s, want %s", actual, manifest.SHA256)
	}
	return nil
}

func (s *RuntimeStore) Verify(manifest RuntimeManifest) (InstalledRuntime, error) {
	if err := ValidateRuntimeManifest(manifest); err != nil {
		return InstalledRuntime{}, err
	}
	root := filepath.Join(s.root, manifest.ID, manifest.Version)
	data, err := os.ReadFile(filepath.Join(root, runtimeInstallManifestName))
	if err != nil {
		if os.IsNotExist(err) {
			return InstalledRuntime{}, fmt.Errorf("runtime %s@%s is not installed", manifest.ID, manifest.Version)
		}
		return InstalledRuntime{}, fmt.Errorf("read runtime metadata: %w", err)
	}
	var installed InstalledRuntime
	if err := json.Unmarshal(data, &installed); err != nil {
		return InstalledRuntime{}, fmt.Errorf("decode runtime metadata: %w", err)
	}
	if installed.ID != manifest.ID ||
		installed.Version != manifest.Version ||
		!strings.EqualFold(installed.ArchiveSHA256, manifest.SHA256) ||
		installed.Platform != manifest.Platform ||
		installed.Architecture != manifest.Architecture {
		return InstalledRuntime{}, errors.New("installed runtime metadata does not match pinned runtime")
	}
	cleanRelative := filepath.Clean(filepath.FromSlash(installed.ExecutablePath))
	if filepath.IsAbs(cleanRelative) || cleanRelative == "." || cleanRelative == ".." ||
		strings.HasPrefix(cleanRelative, ".."+string(filepath.Separator)) {
		return InstalledRuntime{}, errors.New("installed runtime contains unsafe executable path")
	}
	executable := filepath.Join(root, cleanRelative)
	actualExecutableHash, err := sha256File(executable)
	if err != nil {
		return InstalledRuntime{}, fmt.Errorf("verify runtime executable: %w", err)
	}
	if !strings.EqualFold(actualExecutableHash, installed.ExecutableSHA256) {
		return InstalledRuntime{}, errors.New("runtime executable sha256 mismatch")
	}
	installed.RootDir = root
	installed.ExecutablePath = executable
	return installed, nil
}

func (s *RuntimeStore) Remove(manifest RuntimeManifest) error {
	target := filepath.Join(s.root, manifest.ID, manifest.Version)
	if err := os.RemoveAll(target); err != nil {
		return fmt.Errorf("remove runtime: %w", err)
	}
	parent := filepath.Dir(target)
	if entries, err := os.ReadDir(parent); err == nil && len(entries) == 0 {
		_ = os.Remove(parent)
	}
	return nil
}

func extractZipSafely(archivePath, target string) error {
	reader, err := zip.OpenReader(archivePath)
	if err != nil {
		return err
	}
	defer reader.Close()
	root, err := filepath.Abs(target)
	if err != nil {
		return err
	}
	for _, file := range reader.File {
		clean := filepath.Clean(filepath.FromSlash(file.Name))
		if filepath.IsAbs(clean) || clean == "." || clean == ".." ||
			strings.HasPrefix(clean, ".."+string(filepath.Separator)) {
			return fmt.Errorf("unsafe archive entry %q", file.Name)
		}
		destination := filepath.Join(root, clean)
		relative, err := filepath.Rel(root, destination)
		if err != nil || relative == ".." || strings.HasPrefix(relative, ".."+string(filepath.Separator)) {
			return fmt.Errorf("archive entry escapes target: %q", file.Name)
		}
		if file.FileInfo().IsDir() {
			if err := os.MkdirAll(destination, 0o755); err != nil {
				return err
			}
			continue
		}
		if file.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("runtime archive contains unsupported symlink %q", file.Name)
		}
		if err := os.MkdirAll(filepath.Dir(destination), 0o755); err != nil {
			return err
		}
		input, err := file.Open()
		if err != nil {
			return err
		}
		output, err := os.OpenFile(destination, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o755)
		if err != nil {
			input.Close()
			return err
		}
		_, copyErr := io.Copy(output, input)
		closeErr := output.Close()
		input.Close()
		if copyErr != nil {
			return copyErr
		}
		if closeErr != nil {
			return closeErr
		}
	}
	return nil
}

func findFileByBaseName(root, name string) (string, error) {
	var found string
	err := filepath.WalkDir(root, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() {
			return nil
		}
		if strings.EqualFold(entry.Name(), name) {
			if found != "" {
				return fmt.Errorf("runtime archive contains multiple %s files", name)
			}
			found = path
		}
		return nil
	})
	if err != nil {
		return "", err
	}
	if found == "" {
		return "", fmt.Errorf("%s not found in runtime archive", name)
	}
	return found, nil
}

func sha256File(path string) (string, error) {
	input, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer input.Close()
	hasher := sha256.New()
	if _, err := io.Copy(hasher, input); err != nil {
		return "", err
	}
	return hex.EncodeToString(hasher.Sum(nil)), nil
}
