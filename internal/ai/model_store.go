package ai

import (
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
	"sort"
	"strings"
)

const manifestFileName = "manifest.json"

var safeComponentPattern = regexp.MustCompile(`^[A-Za-z0-9._-]+$`)

type ModelStore struct {
	root   string
	client *http.Client
}

func NewModelStore(root string) *ModelStore {
	return &ModelStore{
		root:   root,
		client: &http.Client{},
	}
}

func (s *ModelStore) Root() string {
	return s.root
}

func (s *ModelStore) SetHTTPClient(client *http.Client) {
	if client != nil {
		s.client = client
	}
}

func ValidateManifest(manifest ModelManifest) error {
	if !safeComponentPattern.MatchString(manifest.ID) {
		return fmt.Errorf("invalid model id %q", manifest.ID)
	}
	if !safeComponentPattern.MatchString(manifest.Version) {
		return fmt.Errorf("invalid model version %q", manifest.Version)
	}
	if !safeComponentPattern.MatchString(manifest.Engine) {
		return fmt.Errorf("invalid engine id %q", manifest.Engine)
	}
	if len(manifest.Files) == 0 {
		return errors.New("model manifest contains no files")
	}

	var declaredTotal int64
	roles := make(map[string]struct{})
	for _, file := range manifest.Files {
		if err := validateModelFile(file); err != nil {
			return fmt.Errorf("invalid model file %q: %w", file.Path, err)
		}
		if file.Role != "" {
			if !safeComponentPattern.MatchString(file.Role) {
				return fmt.Errorf("invalid model file role %q", file.Role)
			}
			if _, exists := roles[file.Role]; exists {
				return fmt.Errorf("duplicate model file role %q", file.Role)
			}
			roles[file.Role] = struct{}{}
		}
		if file.SizeBytes > 0 {
			declaredTotal += file.SizeBytes
		}
	}
	for key := range manifest.Parameters {
		if !safeComponentPattern.MatchString(key) {
			return fmt.Errorf("invalid model parameter key %q", key)
		}
	}
	if manifest.SizeBytes > 0 && declaredTotal > 0 && manifest.SizeBytes != declaredTotal {
		return fmt.Errorf("manifest size mismatch: declared %d, files total %d", manifest.SizeBytes, declaredTotal)
	}
	return nil
}

func validateModelFile(file ModelFile) error {
	if file.Path == "" {
		return errors.New("path is required")
	}
	clean := filepath.Clean(filepath.FromSlash(file.Path))
	if filepath.IsAbs(clean) || clean == "." || clean == ".." || strings.HasPrefix(clean, ".."+string(filepath.Separator)) {
		return fmt.Errorf("unsafe relative path %q", file.Path)
	}

	parsed, err := url.Parse(file.URL)
	if err != nil {
		return fmt.Errorf("invalid URL: %w", err)
	}
	if parsed.Scheme != "http" && parsed.Scheme != "https" {
		return fmt.Errorf("unsupported URL scheme %q", parsed.Scheme)
	}

	if len(file.SHA256) != sha256.Size*2 {
		return fmt.Errorf("sha256 must be %d hex characters", sha256.Size*2)
	}
	if _, err := hex.DecodeString(file.SHA256); err != nil {
		return fmt.Errorf("invalid sha256: %w", err)
	}
	if file.SizeBytes < 0 {
		return errors.New("sizeBytes cannot be negative")
	}
	return nil
}

func (s *ModelStore) Install(ctx context.Context, manifest ModelManifest, progress ProgressFunc) (InstalledModel, error) {
	if err := ValidateManifest(manifest); err != nil {
		return InstalledModel{}, err
	}
	if err := ctx.Err(); err != nil {
		return InstalledModel{}, err
	}

	modelRoot := filepath.Join(s.root, manifest.ID)
	if err := os.MkdirAll(modelRoot, 0o755); err != nil {
		return InstalledModel{}, fmt.Errorf("create model cache directory: %w", err)
	}

	staging, err := os.MkdirTemp(modelRoot, "."+manifest.Version+"-install-*")
	if err != nil {
		return InstalledModel{}, fmt.Errorf("create model staging directory: %w", err)
	}
	committed := false
	defer func() {
		if !committed {
			_ = os.RemoveAll(staging)
		}
	}()

	for index, file := range manifest.Files {
		if err := s.downloadFile(ctx, manifest, file, index, len(manifest.Files), staging, progress); err != nil {
			return InstalledModel{}, err
		}
	}

	manifestBytes, err := json.MarshalIndent(manifest, "", "  ")
	if err != nil {
		return InstalledModel{}, fmt.Errorf("encode installed manifest: %w", err)
	}
	if err := os.WriteFile(filepath.Join(staging, manifestFileName), manifestBytes, 0o644); err != nil {
		return InstalledModel{}, fmt.Errorf("write installed manifest: %w", err)
	}

	target := filepath.Join(modelRoot, manifest.Version)
	backup := filepath.Join(modelRoot, "."+manifest.Version+"-previous")
	_ = os.RemoveAll(backup)

	hadPrevious := false
	if _, statErr := os.Stat(target); statErr == nil {
		if err := os.Rename(target, backup); err != nil {
			return InstalledModel{}, fmt.Errorf("stage previous model version: %w", err)
		}
		hadPrevious = true
	} else if !os.IsNotExist(statErr) {
		return InstalledModel{}, fmt.Errorf("inspect previous model version: %w", statErr)
	}

	if err := os.Rename(staging, target); err != nil {
		if hadPrevious {
			if restoreErr := os.Rename(backup, target); restoreErr != nil {
				return InstalledModel{}, fmt.Errorf("commit model installation: %w (restore previous version: %v)", err, restoreErr)
			}
		}
		return InstalledModel{}, fmt.Errorf("commit model installation: %w", err)
	}
	committed = true
	if hadPrevious {
		_ = os.RemoveAll(backup)
	}

	installed := InstalledModel{Manifest: manifest, RootDir: target}
	if _, err := s.Verify(manifest.ID, manifest.Version); err != nil {
		_ = os.RemoveAll(target)
		return InstalledModel{}, fmt.Errorf("verify installed model: %w", err)
	}
	if progress != nil {
		progress(DownloadProgress{
			ModelID:         manifest.ID,
			Version:         manifest.Version,
			FileCount:       len(manifest.Files),
			FileIndex:       len(manifest.Files),
			BytesDownloaded: manifest.SizeBytes,
			BytesTotal:      manifest.SizeBytes,
			Done:            true,
		})
	}
	return installed, nil
}

func (s *ModelStore) Update(ctx context.Context, manifest ModelManifest, progress ProgressFunc) (InstalledModel, error) {
	return s.Install(ctx, manifest, progress)
}

func (s *ModelStore) downloadFile(
	ctx context.Context,
	manifest ModelManifest,
	file ModelFile,
	index int,
	fileCount int,
	staging string,
	progress ProgressFunc,
) error {
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, file.URL, nil)
	if err != nil {
		return fmt.Errorf("create download request for %s: %w", file.Path, err)
	}
	response, err := s.client.Do(request)
	if err != nil {
		return fmt.Errorf("download %s: %w", file.Path, err)
	}
	defer response.Body.Close()
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return fmt.Errorf("download %s: unexpected HTTP status %s", file.Path, response.Status)
	}

	targetPath := filepath.Join(staging, filepath.FromSlash(file.Path))
	if err := os.MkdirAll(filepath.Dir(targetPath), 0o755); err != nil {
		return fmt.Errorf("create directory for %s: %w", file.Path, err)
	}

	output, err := os.Create(targetPath)
	if err != nil {
		return fmt.Errorf("create model file %s: %w", file.Path, err)
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
				return fmt.Errorf("write model file %s: %w", file.Path, err)
			}
			if _, err := hasher.Write(chunk); err != nil {
				_ = output.Close()
				return fmt.Errorf("hash model file %s: %w", file.Path, err)
			}
			downloaded += int64(n)
			if progress != nil {
				total := file.SizeBytes
				if total <= 0 {
					total = response.ContentLength
				}
				progress(DownloadProgress{
					ModelID:         manifest.ID,
					Version:         manifest.Version,
					FilePath:        file.Path,
					FileIndex:       index + 1,
					FileCount:       fileCount,
					BytesDownloaded: downloaded,
					BytesTotal:      total,
				})
			}
		}
		if readErr == io.EOF {
			break
		}
		if readErr != nil {
			_ = output.Close()
			return fmt.Errorf("read model download %s: %w", file.Path, readErr)
		}
	}
	if err := output.Close(); err != nil {
		return fmt.Errorf("close model file %s: %w", file.Path, err)
	}

	if file.SizeBytes > 0 && downloaded != file.SizeBytes {
		return fmt.Errorf("size mismatch for %s: got %d, want %d", file.Path, downloaded, file.SizeBytes)
	}
	actualHash := hex.EncodeToString(hasher.Sum(nil))
	if !strings.EqualFold(actualHash, file.SHA256) {
		return fmt.Errorf("sha256 mismatch for %s: got %s, want %s", file.Path, actualHash, file.SHA256)
	}
	return nil
}

func (s *ModelStore) Verify(modelID, version string) (InstalledModel, error) {
	if !safeComponentPattern.MatchString(modelID) || !safeComponentPattern.MatchString(version) {
		return InstalledModel{}, errors.New("invalid model id or version")
	}

	root := filepath.Join(s.root, modelID, version)
	manifestBytes, err := os.ReadFile(filepath.Join(root, manifestFileName))
	if err != nil {
		if os.IsNotExist(err) {
			return InstalledModel{}, fmt.Errorf("model %s@%s is not installed", modelID, version)
		}
		return InstalledModel{}, fmt.Errorf("read installed manifest: %w", err)
	}

	var manifest ModelManifest
	if err := json.Unmarshal(manifestBytes, &manifest); err != nil {
		return InstalledModel{}, fmt.Errorf("decode installed manifest: %w", err)
	}
	if manifest.ID != modelID || manifest.Version != version {
		return InstalledModel{}, errors.New("installed manifest identity mismatch")
	}
	if err := ValidateManifest(manifest); err != nil {
		return InstalledModel{}, fmt.Errorf("invalid installed manifest: %w", err)
	}

	for _, file := range manifest.Files {
		path := filepath.Join(root, filepath.FromSlash(file.Path))
		if err := verifyFile(path, file); err != nil {
			return InstalledModel{}, err
		}
	}
	return InstalledModel{Manifest: manifest, RootDir: root}, nil
}

func verifyFile(path string, expected ModelFile) error {
	input, err := os.Open(path)
	if err != nil {
		return fmt.Errorf("open installed file %s: %w", expected.Path, err)
	}
	defer input.Close()

	info, err := input.Stat()
	if err != nil {
		return fmt.Errorf("stat installed file %s: %w", expected.Path, err)
	}
	if expected.SizeBytes > 0 && info.Size() != expected.SizeBytes {
		return fmt.Errorf("size mismatch for installed file %s: got %d, want %d", expected.Path, info.Size(), expected.SizeBytes)
	}

	hasher := sha256.New()
	if _, err := io.Copy(hasher, input); err != nil {
		return fmt.Errorf("hash installed file %s: %w", expected.Path, err)
	}
	actualHash := hex.EncodeToString(hasher.Sum(nil))
	if !strings.EqualFold(actualHash, expected.SHA256) {
		return fmt.Errorf("sha256 mismatch for installed file %s", expected.Path)
	}
	return nil
}

func (s *ModelStore) List() ([]InstalledModelInfo, error) {
	entries, err := os.ReadDir(s.root)
	if err != nil {
		if os.IsNotExist(err) {
			return []InstalledModelInfo{}, nil
		}
		return nil, fmt.Errorf("list model cache: %w", err)
	}

	var result []InstalledModelInfo
	for _, modelEntry := range entries {
		if !modelEntry.IsDir() || !safeComponentPattern.MatchString(modelEntry.Name()) {
			continue
		}
		versions, err := os.ReadDir(filepath.Join(s.root, modelEntry.Name()))
		if err != nil {
			continue
		}
		for _, versionEntry := range versions {
			if !versionEntry.IsDir() || strings.HasPrefix(versionEntry.Name(), ".") {
				continue
			}
			installed, err := s.Verify(modelEntry.Name(), versionEntry.Name())
			if err != nil {
				continue
			}
			result = append(result, InstalledModelInfo{
				ID:          installed.Manifest.ID,
				Version:     installed.Manifest.Version,
				Engine:      installed.Manifest.Engine,
				DisplayName: installed.Manifest.DisplayName,
				License:     installed.Manifest.License,
				SizeBytes:   installed.Manifest.SizeBytes,
				RootDir:     installed.RootDir,
			})
		}
	}
	sort.Slice(result, func(i, j int) bool {
		if result[i].ID == result[j].ID {
			return result[i].Version < result[j].Version
		}
		return result[i].ID < result[j].ID
	})
	return result, nil
}

func (s *ModelStore) Remove(modelID, version string) error {
	if !safeComponentPattern.MatchString(modelID) || !safeComponentPattern.MatchString(version) {
		return errors.New("invalid model id or version")
	}
	target := filepath.Join(s.root, modelID, version)
	if err := os.RemoveAll(target); err != nil {
		return fmt.Errorf("remove model %s@%s: %w", modelID, version, err)
	}

	parent := filepath.Join(s.root, modelID)
	entries, err := os.ReadDir(parent)
	if err == nil && len(entries) == 0 {
		_ = os.Remove(parent)
	}
	return nil
}
