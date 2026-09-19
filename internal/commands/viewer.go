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
func (c *AppCommands) GetViewerAssetDetail(id int64) (*AssetDTO, error) {
	asset, err := c.assetRepo.GetByID(id)
	if err != nil {
		return nil, fmt.Errorf("get viewer asset %d: %w", id, err)
	}
	if asset == nil {
		return nil, fmt.Errorf("asset not found: %d", id)
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
			return nil, fmt.Errorf("persist viewer metadata for asset %d: %w", id, err)
		}
	}

	if _, metadataErr := c.loadAssetGenerationMetadata(id, false); metadataErr != nil {
		// Embedded generation metadata is optional. A malformed/unsupported
		// metadata payload must never make the normal asset detail unusable.
		slog.Debug("generation metadata parse skipped", "asset_id", id, "error", metadataErr)
	}

	dto := toAssetDTO(asset)
	note, err := c.noteRepo.GetByAssetID(id)
	if err != nil {
		return nil, fmt.Errorf("get viewer note for asset %d: %w", id, err)
	}
	if note != nil {
		dto.NoteContent = note.Content
	}

	tags, err := c.tagRepo.GetByAssetID(id)
	if err != nil {
		return nil, fmt.Errorf("get viewer tags for asset %d: %w", id, err)
	}
	if tags != nil {
		dto.Tags = make([]TagDTO, len(tags))
		for i, tag := range tags {
			dto.Tags[i] = TagDTO{ID: tag.ID, Name: tag.Name, Color: tag.Color}
		}
	}

	return &dto, nil
}

// ScanLibraryViewer keeps the expensive walk off the browser thread but gives
// JavaScript a reliable completion boundary. The older ScanLibrary command
// returned immediately after spawning another goroutine, which made a freshly
// added library race its first asset query and frequently appear empty.
func (c *AppCommands) ScanLibraryViewer(libraryID int64) error {
	library, err := c.libraryRepo.GetByID(libraryID)
	if err != nil {
		return fmt.Errorf("get library %d: %w", libraryID, err)
	}
	if library == nil {
		return fmt.Errorf("library not found: %d", libraryID)
	}
	if !library.IsEnabled {
		return fmt.Errorf("library is disabled: %d", libraryID)
	}

	excludedDirs, err := c.GetExcludedDirs(libraryID)
	if err != nil {
		return err
	}
	return c.scanSvc.ScanLibrary(library, excludedDirs, func(progress scanner.ScanProgress) {
		if c.ctx != nil {
			runtime.EventsEmit(c.ctx, "scan:progress", progress)
		}
	})
}
