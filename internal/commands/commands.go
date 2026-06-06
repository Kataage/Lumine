package commands

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"os"
	"path/filepath"
	"strings"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/kataage/lumine/internal/infrastructure/scanner"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

type AppCommands struct {
	db          *db.DB
	libraryRepo *db.LibraryRepo
	assetRepo   *db.AssetRepo
	noteRepo    *db.AssetNoteRepo
	tagRepo     *db.TagRepo
	postRepo    *db.PostRepo
	targetRepo  *db.PostTargetRepo
	accountRepo *db.PostAccountRepo
	jobLogRepo  *db.JobLogRepo
	settingRepo *db.AppSettingRepo
	folderRepo  *db.FolderRepo
	scanSvc     *scanner.Scanner
	ctx         context.Context
}

func New(database *db.DB, scanSvc *scanner.Scanner) *AppCommands {
	settingRepo := db.NewAppSettingRepo(database)
	folderRepo := db.NewFolderRepo(database)
	scanSvc.SetSettingRepo(settingRepo)
	scanSvc.SetFolderRepo(folderRepo)
	return &AppCommands{
		db:          database,
		libraryRepo: db.NewLibraryRepo(database),
		assetRepo:   db.NewAssetRepo(database),
		noteRepo:    db.NewAssetNoteRepo(database),
		tagRepo:     db.NewTagRepo(database),
		postRepo:    db.NewPostRepo(database),
		targetRepo:  db.NewPostTargetRepo(database),
		accountRepo: db.NewPostAccountRepo(database),
		jobLogRepo:  db.NewJobLogRepo(database),
		settingRepo: settingRepo,
		folderRepo:  folderRepo,
		scanSvc:     scanSvc,
	}
}

func (c *AppCommands) SetContext(ctx context.Context) {
	c.ctx = ctx
}

type LibraryDTO struct {
	ID           int64  `json:"id"`
	Name         string `json:"name"`
	RootPath     string `json:"rootPath"`
	IsEnabled    bool   `json:"isEnabled"`
	CreatedAt    string `json:"createdAt"`
	UpdatedAt    string `json:"updatedAt"`
	LastScannedAt string `json:"lastScannedAt,omitempty"`
	AssetCount   int    `json:"assetCount"`
}

func toLibraryDTO(lib *domain.Library) LibraryDTO {
	dto := LibraryDTO{
		ID:        lib.ID,
		Name:      lib.Name,
		RootPath:  lib.RootPath,
		IsEnabled: lib.IsEnabled,
		CreatedAt: lib.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt: lib.UpdatedAt.Format("2006-01-02T15:04:05Z"),
	}
	if lib.LastScannedAt != nil {
		dto.LastScannedAt = lib.LastScannedAt.Format("2006-01-02T15:04:05Z")
	}
	return dto
}

func (c *AppCommands) ListLibraries() []LibraryDTO {
	libs, err := c.libraryRepo.List()
	if err != nil {
		slog.Error("ListLibraries", "error", err)
		return nil
	}
	dtos := make([]LibraryDTO, len(libs))
	for i, lib := range libs {
		dtos[i] = toLibraryDTO(&lib)
	}
	return dtos
}

func (c *AppCommands) AddLibrary(name, rootPath string) *LibraryDTO {
	lib, err := c.libraryRepo.Create(name, rootPath)
	if err != nil {
		slog.Error("AddLibrary", "error", err)
		return nil
	}
	dto := toLibraryDTO(lib)
	return &dto
}

func (c *AppCommands) UpdateLibrary(id int64, name, rootPath string) *LibraryDTO {
	lib, err := c.libraryRepo.GetByID(id)
	if err != nil || lib == nil {
		slog.Error("UpdateLibrary: not found", "id", id, "error", err)
		return nil
	}
	lib.Name = name
	lib.RootPath = rootPath
	if err := c.libraryRepo.Update(lib); err != nil {
		slog.Error("UpdateLibrary", "error", err)
		return nil
	}
	updated, err := c.libraryRepo.GetByID(id)
	if err != nil || updated == nil {
		slog.Error("UpdateLibrary: re-fetch failed", "id", id, "error", err)
		return nil
	}
	dto := toLibraryDTO(updated)
	return &dto
}

func (c *AppCommands) EnableLibrary(id int64) error {
	lib, err := c.libraryRepo.GetByID(id)
	if err != nil || lib == nil {
		return fmt.Errorf("library not found: %d", id)
	}
	lib.IsEnabled = true
	return c.libraryRepo.Update(lib)
}

func (c *AppCommands) DisableLibrary(id int64) error {
	lib, err := c.libraryRepo.GetByID(id)
	if err != nil || lib == nil {
		return fmt.Errorf("library not found: %d", id)
	}
	lib.IsEnabled = false
	return c.libraryRepo.Update(lib)
}

func (c *AppCommands) RemoveLibrary(id int64) error {
	return c.libraryRepo.Delete(id)
}

func (c *AppCommands) SelectFolder() string {
	if c.ctx == nil {
		return ""
	}
	path, err := runtime.OpenDirectoryDialog(c.ctx, runtime.OpenDialogOptions{
		Title: "Select Image Folder",
	})
	if err != nil {
		slog.Error("SelectFolder", "error", err)
		return ""
	}
	return path
}

func (c *AppCommands) GetExcludedDirs(libraryID int64) []string {
	setting, err := c.settingRepo.Get(fmt.Sprintf("excludedDirs:%d", libraryID))
	if err != nil || setting == nil || setting.ValueJSON == "" {
		return nil
	}
	var dirs []string
	if err := json.Unmarshal([]byte(setting.ValueJSON), &dirs); err != nil {
		return nil
	}
	return dirs
}

func (c *AppCommands) SetExcludedDirs(libraryID int64, dirs []string) error {
	data, err := json.Marshal(dirs)
	if err != nil {
		return err
	}
	return c.settingRepo.Set(fmt.Sprintf("excludedDirs:%d", libraryID), string(data))
}

func (c *AppCommands) GetSupportedExtensions() []string {
	exts := make([]string, 0, len(scanner.DefaultImageExtensions))
	for ext := range scanner.DefaultImageExtensions {
		exts = append(exts, ext)
	}
	return exts
}

func (c *AppCommands) SetSupportedExtensions(exts []string) error {
	data, err := json.Marshal(exts)
	if err != nil {
		return err
	}
	return c.settingRepo.Set("scanExtensions", string(data))
}

type AssetDTO struct {
	ID           int64  `json:"id"`
	LibraryID    int64  `json:"libraryId"`
	FolderPath   string `json:"folderPath"`
	FileName     string `json:"fileName"`
	FilePath     string `json:"filePath"`
	Extension    string `json:"extension"`
	FileSize     int64  `json:"fileSize"`
	CreatedAtFS  string `json:"createdAtFs,omitempty"`
	ModifiedAtFS string `json:"modifiedAtFs,omitempty"`
	Width        int    `json:"width"`
	Height       int    `json:"height"`
	MimeType     string `json:"mimeType,omitempty"`
	ThumbStatus  string `json:"thumbStatus"`
	Rating       int    `json:"rating"`
	StatusLabel  string `json:"statusLabel"`
	IsFavorite   bool   `json:"isFavorite"`
	ColorLabel   string `json:"colorLabel,omitempty"`
	NoteContent  string `json:"noteContent,omitempty"`
	Tags         []TagDTO `json:"tags,omitempty"`
	CameraModel  string `json:"cameraModel,omitempty"`
	LensModel    string `json:"lensModel,omitempty"`
	FocalLength  string `json:"focalLength,omitempty"`
	Aperture     string `json:"aperture,omitempty"`
	ShutterSpeed string `json:"shutterSpeed,omitempty"`
	ISO          int    `json:"iso"`
	ExifDate     string `json:"exifDate,omitempty"`
	GPSLatitude  string `json:"gpsLatitude,omitempty"`
	GPSLongitude string `json:"gpsLongitude,omitempty"`
	HashBlake3   string `json:"hashBlake3,omitempty"`
}

type TagDTO struct {
	ID    int64  `json:"id"`
	Name  string `json:"name"`
	Color string `json:"color"`
}

func toAssetDTO(a *domain.Asset) AssetDTO {
	return AssetDTO{
		ID:           a.ID,
		LibraryID:    a.LibraryID,
		FolderPath:   a.FolderPath,
		FileName:     a.FileName,
		FilePath:     a.FilePath,
		Extension:    a.Extension,
		FileSize:     a.FileSize,
		CreatedAtFS:  a.CreatedAtFS.Format("2006-01-02T15:04:05Z"),
		ModifiedAtFS: a.ModifiedAtFS.Format("2006-01-02T15:04:05Z"),
		Width:        a.Width,
		Height:       a.Height,
		MimeType:     a.MimeType,
		ThumbStatus:  string(a.ThumbStatus),
		Rating:       a.Rating,
		StatusLabel:  string(a.StatusLabel),
		IsFavorite:   a.IsFavorite,
		ColorLabel:   a.ColorLabel,
		CameraModel:  a.CameraModel,
		LensModel:    a.LensModel,
		FocalLength:  a.FocalLength,
		Aperture:     a.Aperture,
		ShutterSpeed: a.ShutterSpeed,
		ISO:          a.ISO,
		ExifDate:     a.ExifDate,
		GPSLatitude:  a.GPSLatitude,
		GPSLongitude: a.GPSLongitude,
		HashBlake3: a.HashBlake3,
	}
}

type AssetListRequest struct {
	LibraryID   int64  `json:"libraryId"`
	FolderPath  string `json:"folderPath,omitempty"`
	Search      string `json:"search,omitempty"`
	Rating      int    `json:"rating,omitempty"`
	StatusLabel string `json:"statusLabel,omitempty"`
	IsFavorite  *bool  `json:"isFavorite,omitempty"`
	TagIDs      []int64 `json:"tagIds,omitempty"`
	HasNote     *bool  `json:"hasNote,omitempty"`
	Extension   string `json:"extension,omitempty"`
	ColorLabel  string `json:"colorLabel,omitempty"`
	SortBy      string `json:"sortBy,omitempty"`
	SortDesc    bool   `json:"sortDesc,omitempty"`
	Offset      int    `json:"offset"`
	Limit       int    `json:"limit"`
}

type AssetListResponse struct {
	Assets     []AssetDTO `json:"assets"`
	TotalCount int        `json:"totalCount"`
}

func (c *AppCommands) ListAssets(req AssetListRequest) *AssetListResponse {
	result, err := c.assetRepo.List(db.AssetQuery{
		LibraryID:   req.LibraryID,
		FolderPath:  req.FolderPath,
		Search:      req.Search,
		Rating:      req.Rating,
		StatusLabel: req.StatusLabel,
		IsFavorite:  req.IsFavorite,
		TagIDs:      req.TagIDs,
		HasNote:     req.HasNote,
		Extension:   req.Extension,
		ColorLabel:  req.ColorLabel,
		SortBy:      req.SortBy,
		SortDesc:    req.SortDesc,
		Offset:      req.Offset,
		Limit:       req.Limit,
	})
	if err != nil {
		slog.Error("ListAssets", "error", err)
		return &AssetListResponse{}
	}

	dtos := make([]AssetDTO, len(result.Assets))
	for i, a := range result.Assets {
		dtos[i] = toAssetDTO(&a)
	}

	return &AssetListResponse{
		Assets:     dtos,
		TotalCount: result.TotalCount,
	}
}

func (c *AppCommands) GetAssetDetail(id int64) *AssetDTO {
	a, err := c.assetRepo.GetByID(id)
	if err != nil || a == nil {
		return nil
	}
	dto := toAssetDTO(a)

	note, _ := c.noteRepo.GetByAssetID(id)
	if note != nil {
		dto.NoteContent = note.Content
	}

	tags, _ := c.tagRepo.GetByAssetID(id)
	if tags != nil {
		dto.Tags = make([]TagDTO, len(tags))
		for i, t := range tags {
			dto.Tags[i] = TagDTO{ID: t.ID, Name: t.Name, Color: t.Color}
		}
	}

	return &dto
}

func (c *AppCommands) UpdateAssetNote(assetID int64, content string) error {
	return c.noteRepo.Upsert(assetID, content)
}

func (c *AppCommands) SetAssetTags(assetID int64, tagIDs []int64) error {
	return c.tagRepo.SetAssetTags(assetID, tagIDs)
}

func (c *AppCommands) UpdateAssetRating(assetID int64, rating int) error {
	a, err := c.assetRepo.GetByID(assetID)
	if err != nil || a == nil {
		return fmt.Errorf("asset not found: %d", assetID)
	}
	a.Rating = rating
	return c.assetRepo.Update(a)
}

func (c *AppCommands) UpdateAssetStatus(assetID int64, status string) error {
	a, err := c.assetRepo.GetByID(assetID)
	if err != nil || a == nil {
		return fmt.Errorf("asset not found: %d", assetID)
	}
	a.StatusLabel = domain.StatusLabel(status)
	return c.assetRepo.Update(a)
}

func (c *AppCommands) ToggleAssetFavorite(assetID int64, favorite bool) error {
	a, err := c.assetRepo.GetByID(assetID)
	if err != nil || a == nil {
		return fmt.Errorf("asset not found: %d", assetID)
	}
	a.IsFavorite = favorite
	return c.assetRepo.Update(a)
}

func (c *AppCommands) UpdateAssetColorLabel(assetID int64, label string) error {
	a, err := c.assetRepo.GetByID(assetID)
	if err != nil || a == nil {
		return fmt.Errorf("asset not found: %d", assetID)
	}
	a.ColorLabel = label
	return c.assetRepo.Update(a)
}

func (c *AppCommands) BulkUpdateRating(ids []int64, rating int) error {
	return c.assetRepo.BulkUpdateRating(ids, rating)
}

func (c *AppCommands) BulkUpdateStatus(ids []int64, status string) error {
	return c.assetRepo.BulkUpdateStatus(ids, domain.StatusLabel(status))
}

func (c *AppCommands) BulkUpdateFavorite(ids []int64, favorite bool) error {
	return c.assetRepo.BulkUpdateFavorite(ids, favorite)
}

func (c *AppCommands) BulkUpdateColorLabel(ids []int64, label string) error {
	return c.assetRepo.BulkUpdateColorLabel(ids, label)
}

type MoveRequest struct {
	AssetIDs         []int64 `json:"assetIds"`
	DestinationFolder string  `json:"destinationFolder"`
	ConflictPolicy    string  `json:"conflictPolicy"`
}

type MoveResult struct {
	MovedCount  int      `json:"movedCount"`
	SkippedCount int     `json:"skippedCount"`
	FailedCount int      `json:"failedCount"`
	Errors      []string `json:"errors,omitempty"`
}

func (c *AppCommands) MoveAssets(req MoveRequest) *MoveResult {
	result := &MoveResult{}
	for _, id := range req.AssetIDs {
		a, err := c.assetRepo.GetByID(id)
		if err != nil || a == nil {
			result.FailedCount++
			result.Errors = append(result.Errors, fmt.Sprintf("asset %d not found", id))
			continue
		}

		destPath := filepath.Join(req.DestinationFolder, a.FileName)

		switch req.ConflictPolicy {
		case "skip":
			if _, err := os.Stat(destPath); err == nil {
				result.SkippedCount++
				continue
			}
		case "rename":
			destPath = findNonConflictingPath(req.DestinationFolder, a.FileName)
		default:
			if _, err := os.Stat(destPath); err == nil {
				result.FailedCount++
				result.Errors = append(result.Errors, fmt.Sprintf("file already exists: %s", destPath))
				continue
			}
		}

		if err := os.MkdirAll(req.DestinationFolder, 0755); err != nil {
			result.FailedCount++
			result.Errors = append(result.Errors, fmt.Sprintf("create dir %s: %v", req.DestinationFolder, err))
			continue
		}

		if err := moveFile(a.FilePath, destPath); err != nil {
			result.FailedCount++
			result.Errors = append(result.Errors, fmt.Sprintf("move %s: %v", a.FilePath, err))
			continue
		}

		if err := c.assetRepo.UpdateFilePath(a.ID, destPath, req.DestinationFolder, filepath.Base(destPath)); err != nil {
			result.FailedCount++
			result.Errors = append(result.Errors, fmt.Sprintf("db update for %s: %v", destPath, err))
			continue
		}

		result.MovedCount++
	}
	return result
}

func findNonConflictingPath(dir, name string) string {
	path := filepath.Join(dir, name)
	if _, err := os.Stat(path); err != nil {
		return path
	}
	ext := filepath.Ext(name)
	base := name[:len(name)-len(ext)]
	for i := 1; i < 1000; i++ {
		newName := fmt.Sprintf("%s (%d)%s", base, i, ext)
		newPath := filepath.Join(dir, newName)
		if _, err := os.Stat(newPath); err != nil {
			return newPath
		}
	}
	return path
}

func moveFile(src, dst string) error {
	srcDir := filepath.VolumeName(src) + filepath.Dir(src)
	dstDir := filepath.VolumeName(dst) + filepath.Dir(dst)

	if strings.EqualFold(srcDir, dstDir) {
		return os.Rename(src, dst)
	}

	in, err := os.Open(src)
	if err != nil {
		return fmt.Errorf("open source: %w", err)
	}

	if err := os.MkdirAll(filepath.Dir(dst), 0755); err != nil {
		in.Close()
		return fmt.Errorf("create dest dir: %w", err)
	}

	out, err := os.Create(dst)
	if err != nil {
		in.Close()
		return fmt.Errorf("create dest: %w", err)
	}

	buf := make([]byte, 32*1024)
	written := int64(0)
	for {
		n, readErr := in.Read(buf)
		if n > 0 {
			if _, writeErr := out.Write(buf[:n]); writeErr != nil {
				out.Close()
				in.Close()
				os.Remove(dst)
				return fmt.Errorf("write dest: %w", writeErr)
			}
			written += int64(n)
		}
		if readErr != nil {
			break
		}
	}

	srcInfo, err := os.Stat(src)
	if err == nil && written != srcInfo.Size() {
		os.Remove(dst)
		return fmt.Errorf("copy verification failed: wrote %d, expected %d", written, srcInfo.Size())
	}

	if err := out.Sync(); err != nil {
		slog.Warn("sync dest file failed", "path", dst, "error", err)
	}

	if err := out.Close(); err != nil {
		os.Remove(dst)
		return fmt.Errorf("close dest: %w", err)
	}
	if err := in.Close(); err != nil {
		slog.Warn("close source file failed", "path", src, "error", err)
	}

	if err := os.Remove(src); err != nil {
		slog.Warn("failed to remove source after copy", "path", src, "error", err)
	}

	return nil
}

func (c *AppCommands) ScanLibrary(libraryID int64) error {
	lib, err := c.libraryRepo.GetByID(libraryID)
	if err != nil || lib == nil {
		return fmt.Errorf("library not found: %d", libraryID)
	}
	if !lib.IsEnabled {
		return fmt.Errorf("library is disabled: %d", libraryID)
	}

	excludedDirs := c.GetExcludedDirs(libraryID)

	go func() {
		if err := c.scanSvc.ScanLibrary(lib, excludedDirs, func(p scanner.ScanProgress) {
			slog.Info("scan progress",
				"library", lib.Name,
				"scanned", p.ScannedCount,
				"added", p.AddedCount,
				"updated", p.UpdatedCount,
				"skipped", p.SkippedCount,
				"failed", p.FailedCount,
				"done", p.IsDone,
			)
			if c.ctx != nil {
				runtime.EventsEmit(c.ctx, "scan:progress", p)
			}
		}); err != nil {
			slog.Error("scan failed", "library", lib.Name, "error", err)
			if c.ctx != nil {
				runtime.EventsEmit(c.ctx, "scan:progress", scanner.ScanProgress{
					LibraryID:    lib.ID,
					IsDone:       true,
					FailedCount:  1,
				})
			}
		}
	}()
	return nil
}

func (c *AppCommands) CancelScan() {
	c.scanSvc.Cancel()
}

func (c *AppCommands) ListTags() []TagDTO {
	tags, err := c.tagRepo.List()
	if err != nil {
		slog.Error("ListTags", "error", err)
		return nil
	}
	dtos := make([]TagDTO, len(tags))
	for i, t := range tags {
		dtos[i] = TagDTO{ID: t.ID, Name: t.Name, Color: t.Color}
	}
	return dtos
}

func (c *AppCommands) CreateTag(name, color string) *TagDTO {
	t, err := c.tagRepo.Create(name, color)
	if err != nil {
		slog.Error("CreateTag", "error", err)
		return nil
	}
	dto := TagDTO{ID: t.ID, Name: t.Name, Color: t.Color}
	return &dto
}

func (c *AppCommands) DeleteTag(id int64) error {
	return c.tagRepo.Delete(id)
}

type PostDTO struct {
	ID          int64   `json:"id"`
	Title       string  `json:"title"`
	Body        string  `json:"body"`
	Hashtags    string  `json:"hashtags"`
	Status      string  `json:"status"`
	ScheduledAt string  `json:"scheduledAt,omitempty"`
	PublishedAt string  `json:"publishedAt,omitempty"`
	AssetIDs    []int64 `json:"assetIds,omitempty"`
	CreatedAt   string  `json:"createdAt"`
	UpdatedAt   string  `json:"updatedAt"`
}

type PostTargetDTO struct {
	ID   int64  `json:"id"`
	Name string `json:"name"`
	Kind string `json:"kind"`
}

type PostAccountDTO struct {
	ID                int64  `json:"id"`
	PostTargetID      int64  `json:"postTargetId"`
	DisplayName       string `json:"displayName"`
	AccountIdentifier string `json:"accountIdentifier"`
	IsActive          bool   `json:"isActive"`
}

func (c *AppCommands) ListPosts(offset, limit int) []PostDTO {
	posts, err := c.postRepo.List(offset, limit)
	if err != nil {
		slog.Error("ListPosts", "error", err)
		return nil
	}
	dtos := make([]PostDTO, len(posts))
	for i, p := range posts {
		dto := PostDTO{
			ID:        p.ID,
			Title:     p.Title,
			Body:      p.Body,
			Hashtags:  p.Hashtags,
			Status:    string(p.Status),
			CreatedAt: p.CreatedAt.Format("2006-01-02T15:04:05Z"),
			UpdatedAt: p.UpdatedAt.Format("2006-01-02T15:04:05Z"),
		}
		if p.ScheduledAt != nil {
			dto.ScheduledAt = p.ScheduledAt.Format("2006-01-02T15:04:05Z")
		}
		if p.PublishedAt != nil {
			dto.PublishedAt = p.PublishedAt.Format("2006-01-02T15:04:05Z")
		}
		assetIDs, _ := c.postRepo.GetAssetsByPostID(p.ID)
		dto.AssetIDs = assetIDs
		dtos[i] = dto
	}
	return dtos
}

func (c *AppCommands) CreatePostDraft(title, body, hashtags string) *PostDTO {
	p := &domain.Post{
		Title:    title,
		Body:     body,
		Hashtags: hashtags,
		Status:   domain.PostStatusDraft,
	}
	id, err := c.postRepo.Create(p)
	if err != nil {
		slog.Error("CreatePostDraft", "error", err)
		return nil
	}
	created, _ := c.postRepo.GetByID(id)
	if created == nil {
		return nil
	}
	dto := PostDTO{
		ID:        created.ID,
		Title:     created.Title,
		Body:      created.Body,
		Hashtags:  created.Hashtags,
		Status:    string(created.Status),
		CreatedAt: created.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt: created.UpdatedAt.Format("2006-01-02T15:04:05Z"),
	}
	return &dto
}

func (c *AppCommands) UpdatePost(id int64, title, body, hashtags, status string) *PostDTO {
	p, err := c.postRepo.GetByID(id)
	if err != nil || p == nil {
		slog.Error("UpdatePost: not found", "id", id)
		return nil
	}
	p.Title = title
	p.Body = body
	p.Hashtags = hashtags
	p.Status = domain.PostStatus(status)
	if err := c.postRepo.Update(p); err != nil {
		slog.Error("UpdatePost", "error", err)
		return nil
	}
	updated, err := c.postRepo.GetByID(id)
	if err != nil || updated == nil {
		slog.Error("UpdatePost: re-fetch failed", "id", id, "error", err)
		return nil
	}
	dto := PostDTO{
		ID:        updated.ID,
		Title:     updated.Title,
		Body:      updated.Body,
		Hashtags:  updated.Hashtags,
		Status:    string(updated.Status),
		CreatedAt: updated.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt: updated.UpdatedAt.Format("2006-01-02T15:04:05Z"),
	}
	return &dto
}

func (c *AppCommands) AttachAssetsToPost(postID int64, assetIDs []int64) error {
	return c.postRepo.AttachAssets(postID, assetIDs)
}

func (c *AppCommands) GetPostsByAsset(assetID int64) []PostDTO {
	posts, err := c.postRepo.GetPostsByAssetID(assetID)
	if err != nil {
		slog.Error("GetPostsByAsset", "error", err)
		return nil
	}
	dtos := make([]PostDTO, len(posts))
	for i, p := range posts {
		dtos[i] = PostDTO{
			ID:        p.ID,
			Title:     p.Title,
			Body:      p.Body,
			Hashtags:  p.Hashtags,
			Status:    string(p.Status),
			CreatedAt: p.CreatedAt.Format("2006-01-02T15:04:05Z"),
			UpdatedAt: p.UpdatedAt.Format("2006-01-02T15:04:05Z"),
		}
	}
	return dtos
}

func (c *AppCommands) DeletePost(id int64) error {
	return c.postRepo.Delete(id)
}

func (c *AppCommands) ListPostTargets() []PostTargetDTO {
	targets, err := c.targetRepo.List()
	if err != nil {
		slog.Error("ListPostTargets", "error", err)
		return nil
	}
	dtos := make([]PostTargetDTO, len(targets))
	for i, t := range targets {
		dtos[i] = PostTargetDTO{ID: t.ID, Name: t.Name, Kind: t.Kind}
	}
	return dtos
}

func (c *AppCommands) CreatePostTarget(name, kind string) *PostTargetDTO {
	id, err := c.targetRepo.Create(name, kind)
	if err != nil {
		slog.Error("CreatePostTarget", "error", err)
		return nil
	}
	return &PostTargetDTO{ID: id, Name: name, Kind: kind}
}

func (c *AppCommands) DeletePostTarget(id int64) error {
	return c.targetRepo.Delete(id)
}

func (c *AppCommands) ListPostAccounts() []PostAccountDTO {
	accounts, err := c.accountRepo.List()
	if err != nil {
		slog.Error("ListPostAccounts", "error", err)
		return nil
	}
	dtos := make([]PostAccountDTO, len(accounts))
	for i, a := range accounts {
		dtos[i] = PostAccountDTO{
			ID:                a.ID,
			PostTargetID:      a.PostTargetID,
			DisplayName:       a.DisplayName,
			AccountIdentifier: a.AccountIdentifier,
			IsActive:          a.IsActive,
		}
	}
	return dtos
}

func (c *AppCommands) CreatePostAccount(targetID int64, displayName, identifier string) *PostAccountDTO {
	id, err := c.accountRepo.Create(targetID, displayName, identifier)
	if err != nil {
		slog.Error("CreatePostAccount", "error", err)
		return nil
	}
	return &PostAccountDTO{
		ID:                id,
		PostTargetID:      targetID,
		DisplayName:       displayName,
		AccountIdentifier: identifier,
		IsActive:          true,
	}
}

func (c *AppCommands) DeletePostAccount(id int64) error {
	return c.accountRepo.Delete(id)
}

func (c *AppCommands) GetSetting(key string) string {
	s, err := c.settingRepo.Get(key)
	if err != nil || s == nil {
		return ""
	}
	return s.ValueJSON
}

func (c *AppCommands) SetSetting(key, valueJSON string) error {
	return c.settingRepo.Set(key, valueJSON)
}

func (c *AppCommands) GetAppBootstrap() map[string]interface{} {
	libs, _ := c.libraryRepo.List()
	libDTOs := make([]LibraryDTO, len(libs))
	for i, lib := range libs {
		libDTOs[i] = toLibraryDTO(&lib)
	}

	settings := make(map[string]interface{})
	for _, key := range []string{"theme", "thumbnailSize", "scanExtensions", "conflictPolicy", "logLevel"} {
		if s, _ := c.settingRepo.Get(key); s != nil {
			var val interface{}
			if err := json.Unmarshal([]byte(s.ValueJSON), &val); err == nil {
				settings[key] = val
			}
		}
	}

	tags, _ := c.tagRepo.List()
	tagDTOs := make([]TagDTO, len(tags))
	for i, t := range tags {
		tagDTOs[i] = TagDTO{ID: t.ID, Name: t.Name, Color: t.Color}
	}

	return map[string]interface{}{
		"libraries": libDTOs,
		"settings":  settings,
		"tags":      tagDTOs,
	}
}

func (c *AppCommands) ScanFolder(folderPath string, offset, limit int) *scanner.FolderScanResult {
	result, err := scanner.ScanFolderDirect(folderPath, offset, limit)
	if err != nil {
		slog.Error("ScanFolder", "error", err)
		return nil
	}
	return result
}

type FolderDTO struct {
	ID         int64  `json:"id"`
	LibraryID  int64  `json:"libraryId"`
	Path       string `json:"path"`
	ParentPath string `json:"parentPath,omitempty"`
}

func (c *AppCommands) GetFolderTree(libraryID int64) []FolderDTO {
	folders, err := c.folderRepo.GetTreeByLibrary(libraryID)
	if err != nil {
		slog.Error("GetFolderTree", "error", err)
		return []FolderDTO{}
	}
	result := make([]FolderDTO, 0, len(folders))
	for _, f := range folders {
		result = append(result, FolderDTO{
			ID:         f.ID,
			LibraryID:  f.LibraryID,
			Path:       f.Path,
			ParentPath: f.ParentPath,
		})
	}
	return result
}

func (c *AppCommands) BulkDeleteAssets(ids []int64) error {
	return c.assetRepo.BulkDelete(ids)
}

type CopyRequest struct {
	AssetIDs     []int64 `json:"assetIds"`
	TargetFolder string  `json:"targetFolder"`
}

type CopyResult struct {
	CopiedCount int     `json:"copiedCount"`
	FailedIDs   []int64 `json:"failedIds"`
	Errors      []string `json:"errors"`
}

func (c *AppCommands) CopyAssets(req CopyRequest) *CopyResult {
	result := &CopyResult{}
	for _, id := range req.AssetIDs {
		asset, err := c.assetRepo.GetByID(id)
		if err != nil || asset == nil {
			result.FailedIDs = append(result.FailedIDs, id)
			result.Errors = append(result.Errors, fmt.Sprintf("asset %d: %v", id, err))
			continue
		}

		newPath := filepath.Join(req.TargetFolder, asset.FileName)
		src, err := os.Open(asset.FilePath)
		if err != nil {
			result.FailedIDs = append(result.FailedIDs, id)
			result.Errors = append(result.Errors, fmt.Sprintf("open %s: %v", asset.FilePath, err))
			continue
		}
		dst, err := os.Create(newPath)
		if err != nil {
			src.Close()
			result.FailedIDs = append(result.FailedIDs, id)
			result.Errors = append(result.Errors, fmt.Sprintf("create %s: %v", newPath, err))
			continue
		}
		_, err = io.Copy(dst, src)
		src.Close()
		dst.Close()
		if err != nil {
			result.FailedIDs = append(result.FailedIDs, id)
			result.Errors = append(result.Errors, fmt.Sprintf("copy data: %v", err))
			continue
		}
		result.CopiedCount++
	}
	return result
}
