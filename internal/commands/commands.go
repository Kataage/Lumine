package commands

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"

	"github.com/kataage/lumine/internal/domain"
	"github.com/kataage/lumine/internal/infrastructure/db"
	"github.com/kataage/lumine/internal/infrastructure/scanner"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

type AppCommands struct {
	db           *db.DB
	libraryRepo  *db.LibraryRepo
	assetRepo    *db.AssetRepo
	noteRepo     *db.AssetNoteRepo
	tagRepo      *db.TagRepo
	postRepo     *db.PostRepo
	targetRepo   *db.PostTargetRepo
	accountRepo  *db.PostAccountRepo
	jobLogRepo   *db.JobLogRepo
	settingRepo  *db.AppSettingRepo
	scanSvc      *scanner.Scanner
	thumbSvc     *scanner.ThumbnailService
	ctx          context.Context
}

func New(database *db.DB, scanSvc *scanner.Scanner, thumbSvc *scanner.ThumbnailService) *AppCommands {
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
		settingRepo: db.NewAppSettingRepo(database),
		scanSvc:     scanSvc,
		thumbSvc:    thumbSvc,
	}
}

func (c *AppCommands) SetContext(ctx context.Context) {
	c.ctx = ctx
}

type LibraryDTO struct {
	ID            int64  `json:"id"`
	Name          string `json:"name"`
	RootPath      string `json:"rootPath"`
	IsEnabled     bool   `json:"isEnabled"`
	CreatedAt     string `json:"createdAt"`
	UpdatedAt     string `json:"updatedAt"`
	LastScannedAt string `json:"lastScannedAt,omitempty"`
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
	}
}

type AssetListRequest struct {
	LibraryID  int64  `json:"libraryId"`
	FolderPath string `json:"folderPath,omitempty"`
	Search     string `json:"search,omitempty"`
	Rating     int    `json:"rating,omitempty"`
	StatusLabel string `json:"statusLabel,omitempty"`
	IsFavorite *bool  `json:"isFavorite,omitempty"`
	TagIDs     []int64 `json:"tagIds,omitempty"`
	HasNote    *bool  `json:"hasNote,omitempty"`
	Extension  string `json:"extension,omitempty"`
	SortBy     string `json:"sortBy,omitempty"`
	SortDesc   bool   `json:"sortDesc,omitempty"`
	Offset     int    `json:"offset"`
	Limit      int    `json:"limit"`
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

func (c *AppCommands) BulkUpdateRating(ids []int64, rating int) error {
	return c.assetRepo.BulkUpdateRating(ids, rating)
}

func (c *AppCommands) BulkUpdateStatus(ids []int64, status string) error {
	return c.assetRepo.BulkUpdateStatus(ids, domain.StatusLabel(status))
}

func (c *AppCommands) BulkUpdateFavorite(ids []int64, favorite bool) error {
	return c.assetRepo.BulkUpdateFavorite(ids, favorite)
}

type MoveRequest struct {
	AssetIDs          []int64 `json:"assetIds"`
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

		newPath := req.DestinationFolder + "\\" + a.FileName

		switch req.ConflictPolicy {
		case "skip":
			existing, _ := c.assetRepo.GetByFilePath(newPath)
			if existing != nil {
				result.SkippedCount++
				continue
			}
		case "rename":
			newPath = findNonConflictingPath(req.DestinationFolder, a.FileName)
		default:
		}

		if err := moveFile(a.FilePath, newPath); err != nil {
			result.FailedCount++
			result.Errors = append(result.Errors, fmt.Sprintf("move %s: %v", a.FilePath, err))
			continue
		}

		if err := c.assetRepo.UpdateFilePath(a.ID, newPath, req.DestinationFolder, filepathBase(newPath)); err != nil {
			result.FailedCount++
			result.Errors = append(result.Errors, fmt.Sprintf("db update for %s: %v", newPath, err))
			continue
		}

		result.MovedCount++
	}
	return result
}

func findNonConflictingPath(dir, name string) string {
	path := dir + "\\" + name
	if _, err := statFile(path); err != nil {
		return path
	}
	ext := filepathExt(name)
	base := name[:len(name)-len(ext)]
	for i := 1; i < 1000; i++ {
		newName := fmt.Sprintf("%s (%d)%s", base, i, ext)
		newPath := dir + "\\" + newName
		if _, err := statFile(newPath); err != nil {
			return newPath
		}
	}
	return path
}

func filepathBase(path string) string {
	for i := len(path) - 1; i >= 0; i-- {
		if path[i] == '\\' || path[i] == '/' {
			return path[i+1:]
		}
	}
	return path
}

func filepathExt(name string) string {
	for i := len(name) - 1; i >= 0; i-- {
		if name[i] == '.' {
			return name[i:]
		}
	}
	return ""
}

func statFile(path string) (interface{}, error) {
	return nil, fmt.Errorf("not found")
}

func moveFile(src, dst string) error {
	return fmt.Errorf("move not implemented - requires OS-specific rename")
}

func (c *AppCommands) ScanLibrary(libraryID int64) error {
	lib, err := c.libraryRepo.GetByID(libraryID)
	if err != nil || lib == nil {
		return fmt.Errorf("library not found: %d", libraryID)
	}
	go c.scanSvc.ScanLibrary(lib, nil, func(p scanner.ScanProgress) {
		slog.Info("scan progress", "library", lib.Name, "scanned", p.ScannedCount, "added", p.AddedCount)
	})
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
	ID          int64    `json:"id"`
	Title       string   `json:"title"`
	Body        string   `json:"body"`
	Hashtags    string   `json:"hashtags"`
	Status      string   `json:"status"`
	ScheduledAt string   `json:"scheduledAt,omitempty"`
	PublishedAt string   `json:"publishedAt,omitempty"`
	AssetIDs    []int64  `json:"assetIds,omitempty"`
	CreatedAt   string   `json:"createdAt"`
	UpdatedAt   string   `json:"updatedAt"`
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
			Status:    string(p.Status),
			CreatedAt: p.CreatedAt.Format("2006-01-02T15:04:05Z"),
			UpdatedAt: p.UpdatedAt.Format("2006-01-02T15:04:05Z"),
		}
	}
	return dtos
}

type PostTargetDTO struct {
	ID   int64  `json:"id"`
	Name string `json:"name"`
	Kind string `json:"kind"`
}

func (c *AppCommands) ListPostTargets() []PostTargetDTO {
	targets, err := c.targetRepo.List()
	if err != nil {
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
		return nil
	}
	return &PostTargetDTO{ID: id, Name: name, Kind: kind}
}

func (c *AppCommands) DeletePost(id int64) error {
	return c.postRepo.Delete(id)
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

	return map[string]interface{}{
		"libraries": libDTOs,
		"settings":  settings,
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
