package commands

import (
	"fmt"
	"strings"
	"time"

	"github.com/kataage/lumine/internal/infrastructure/db"
)

type PostRecordRequest struct {
	AssetIDs             []int64 `json:"assetIds"`
	TargetID             int64   `json:"targetId"`
	AccountID            int64   `json:"accountId"`
	Title                string  `json:"title"`
	Body                 string  `json:"body"`
	Hashtags             string  `json:"hashtags"`
	PlatformMetadataJSON string  `json:"platformMetadataJson"`
	ExternalPostID       string  `json:"externalPostId"`
	ExternalURL          string  `json:"externalUrl"`
	PublishedAt          string  `json:"publishedAt"`
}

type PostRecordAssetDTO struct {
	ID       int64  `json:"id"`
	FileName string `json:"fileName"`
	FilePath string `json:"filePath"`
}

type PostRecordDTO struct {
	ID                   int64                `json:"id"`
	Title                string               `json:"title"`
	Body                 string               `json:"body"`
	Hashtags             string               `json:"hashtags"`
	PlatformMetadataJSON string               `json:"platformMetadataJson"`
	Status               string               `json:"status"`
	PublishedAt          string               `json:"publishedAt,omitempty"`
	CreatedAt            string               `json:"createdAt"`
	UpdatedAt            string               `json:"updatedAt"`
	AssetIDs             []int64              `json:"assetIds"`
	Assets               []PostRecordAssetDTO `json:"assets"`
	TargetID             int64                `json:"targetId"`
	TargetName           string               `json:"targetName"`
	TargetKind           string               `json:"targetKind"`
	AccountID            int64                `json:"accountId"`
	AccountDisplay       string               `json:"accountDisplay"`
	AccountIdentifier    string               `json:"accountIdentifier"`
	ExternalPostID       string               `json:"externalPostId,omitempty"`
	ExternalURL          string               `json:"externalUrl,omitempty"`
}

func (c *AppCommands) toPostRecordDTO(record db.PostRecordView) (PostRecordDTO, error) {
	dto := PostRecordDTO{
		ID:                   record.ID,
		Title:                record.Title,
		Body:                 record.Body,
		Hashtags:             record.Hashtags,
		PlatformMetadataJSON: record.PlatformMetadataJSON,
		Status:               record.Status,
		CreatedAt:            record.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt:            record.UpdatedAt.Format("2006-01-02T15:04:05Z"),
		AssetIDs:             record.AssetIDs,
		Assets:               make([]PostRecordAssetDTO, 0, len(record.AssetIDs)),
		TargetID:             record.TargetID,
		TargetName:           record.TargetName,
		TargetKind:           record.TargetKind,
		AccountID:            record.AccountID,
		AccountDisplay:       record.AccountDisplay,
		AccountIdentifier:    record.AccountIdentifier,
		ExternalPostID:       record.ExternalPostID,
		ExternalURL:          record.ExternalURL,
	}
	if record.PublishedAt != nil {
		dto.PublishedAt = record.PublishedAt.Format("2006-01-02T15:04:05Z")
	}
	for _, assetID := range record.AssetIDs {
		asset, err := c.assetRepo.GetByID(assetID)
		if err != nil {
			return PostRecordDTO{}, fmt.Errorf("load post record %d asset %d: %w", record.ID, assetID, err)
		}
		if asset == nil {
			return PostRecordDTO{}, fmt.Errorf("post record %d references missing asset %d", record.ID, assetID)
		}
		dto.Assets = append(dto.Assets, PostRecordAssetDTO{
			ID:       asset.ID,
			FileName: asset.FileName,
			FilePath: asset.FilePath,
		})
	}
	return dto, nil
}

func (c *AppCommands) CreatePostRecord(req PostRecordRequest) (*PostRecordDTO, error) {
	var publishedAt *time.Time
	if value := strings.TrimSpace(req.PublishedAt); value != "" {
		parsed, err := time.Parse(time.RFC3339, value)
		if err != nil {
			return nil, fmt.Errorf("invalid publishedAt %q: %w", value, err)
		}
		publishedAt = &parsed
	}

	postID, err := c.postRepo.CreateTrackingRecord(db.PostRecordCreate{
		Title:                req.Title,
		Body:                 req.Body,
		Hashtags:             req.Hashtags,
		PlatformMetadataJSON: req.PlatformMetadataJSON,
		AssetIDs:             req.AssetIDs,
		TargetID:             req.TargetID,
		AccountID:            req.AccountID,
		ExternalPostID:       req.ExternalPostID,
		ExternalURL:          req.ExternalURL,
		PublishedAt:          publishedAt,
	})
	if err != nil {
		return nil, fmt.Errorf("create post record: %w", err)
	}

	records, err := c.postRepo.ListTrackingRecords(0, 100)
	if err != nil {
		return nil, fmt.Errorf("reload post record %d: %w", postID, err)
	}
	for _, record := range records {
		if record.ID == postID {
			dto, err := c.toPostRecordDTO(record)
			if err != nil {
				return nil, err
			}
			return &dto, nil
		}
	}
	return nil, fmt.Errorf("created post record not found after reload: %d", postID)
}

func (c *AppCommands) ListPostRecords(offset, limit int) ([]PostRecordDTO, error) {
	records, err := c.postRepo.ListTrackingRecords(offset, limit)
	if err != nil {
		return nil, fmt.Errorf("list post records: %w", err)
	}
	result := make([]PostRecordDTO, 0, len(records))
	for _, record := range records {
		dto, err := c.toPostRecordDTO(record)
		if err != nil {
			return nil, err
		}
		result = append(result, dto)
	}
	return result, nil
}

func (c *AppCommands) GetPostRecordsByAsset(assetID int64) ([]PostRecordDTO, error) {
	records, err := c.postRepo.GetTrackingRecordsByAsset(assetID)
	if err != nil {
		return nil, fmt.Errorf("get post records for asset %d: %w", assetID, err)
	}
	result := make([]PostRecordDTO, 0, len(records))
	for _, record := range records {
		dto, err := c.toPostRecordDTO(record)
		if err != nil {
			return nil, err
		}
		result = append(result, dto)
	}
	return result, nil
}
