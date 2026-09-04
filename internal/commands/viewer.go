package commands

import (
	"fmt"
	"log/slog"

	"github.com/kataage/lumine/internal/infrastructure/scanner"
	"github.com/wailsapp/wails/v2/pkg/runtime"
)

// GetViewerAssetDetail is the viewer-oriented detail path. Expensive EXIF
// parsing is performed at most once per file version, when the user actually
// opens the detail panel, rather than while indexing an entire library.
func (c *AppCommands) GetViewerAssetDetail(id int64) *AssetDTO {
	asset, err := c.assetRepo.GetByID(id)
	if err != nil || asset == nil {
		if err != nil {
			slog.Error("GetViewerAssetDetail", "id", id, "error", err)
		}
		return nil
	}

	if !asset.MetadataLoaded {
		width, height, mimeType, exifData := scanner.LoadMetadata(asset.FilePath)
		if width > 0 {
			asset.Width = width
		}
		if height > 0 {
			asset.Height = height
		}
		if mimeType != "" {
			asset.MimeType = mimeType
		}
		if exifData != nil {
			asset.CameraModel = exifData.CameraModel
			asset.LensModel = exifData.LensModel
			asset.FocalLength = exifData.FocalLength
			asset.Aperture = exifData.Aperture
			asset.ShutterSpeed = exifData.ShutterSpeed
			asset.ISO = exifData.ISO
			asset.ExifDate = exifData.ExifDate
			asset.GPSLatitude = exifData.GPSLatitude
			asset.GPSLongitude = exifData.GPSLongitude
		}

		asset.MetadataLoaded = true
		if err := c.assetRepo.UpdateMetadata(asset); err != nil {
			slog.Warn("persist viewer metadata", "id", id, "error", err)
		}
	}

	dto := toAssetDTO(asset)
	note, _ := c.noteRepo.GetByAssetID(id)
	if note != nil {
		dto.NoteContent = note.Content
	}

	tags, _ := c.tagRepo.GetByAssetID(id)
	if tags != nil {
		dto.Tags = make([]TagDTO, len(tags))
		for i, tag := range tags {
			dto.Tags[i] = TagDTO{ID: tag.ID, Name: tag.Name, Color: tag.Color}
		}
	}

	return &dto
}

// ScanLibraryViewer keeps the expensive walk off the browser thread but gives
// JavaScript a reliable completion boundary. The older ScanLibrary command
// returned immediately after spawning another goroutine, which made a freshly
// added library race its first asset query and frequently appear empty.
func (c *AppCommands) ScanLibraryViewer(libraryID int64) error {
	library, err := c.libraryRepo.GetByID(libraryID)
	if err != nil || library == nil {
		return fmt.Errorf("library not found: %d", libraryID)
	}
	if !library.IsEnabled {
		return fmt.Errorf("library is disabled: %d", libraryID)
	}

	excludedDirs := c.GetExcludedDirs(libraryID)
	return c.scanSvc.ScanLibrary(library, excludedDirs, func(progress scanner.ScanProgress) {
		if c.ctx != nil {
			runtime.EventsEmit(c.ctx, "scan:progress", progress)
		}
	})
}
