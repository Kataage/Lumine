package commands

import (
	"log/slog"

	"github.com/kataage/lumine/internal/infrastructure/db"
)

type PostRecordRequest struct {
	AssetIDs       []int64 `json:"assetIds"`
	TargetID       int64   `json:"targetId"`
	AccountID      int64   `json:"accountId"`
	Title          string  `json:"title"`
	ExternalPostID string  `json:"externalPostId"`
}

type PostRecordDTO struct {
	ID                int64   `json:"id"`
	Title             string  `json:"title"`
	Status            string  `json:"status"`
	PublishedAt       string  `json:"publishedAt,omitempty"`
	CreatedAt         string  `json:"createdAt"`
	UpdatedAt         string  `json:"updatedAt"`
	AssetIDs          []int64 `json:"assetIds"`
	TargetID          int64   `json:"targetId"`
	TargetName        string  `json:"targetName"`
	TargetKind        string  `json:"targetKind"`
	AccountID         int64   `json:"accountId"`
	AccountDisplay    string  `json:"accountDisplay"`
	AccountIdentifier string  `json:"accountIdentifier"`
	ExternalPostID    string  `json:"externalPostId,omitempty"`
}

func toPostRecordDTO(record db.PostRecordView) PostRecordDTO {
	dto := PostRecordDTO{
		ID:                record.ID,
		Title:             record.Title,
		Status:            record.Status,
		CreatedAt:         record.CreatedAt.Format("2006-01-02T15:04:05Z"),
		UpdatedAt:         record.UpdatedAt.Format("2006-01-02T15:04:05Z"),
		AssetIDs:          record.AssetIDs,
		TargetID:          record.TargetID,
		TargetName:        record.TargetName,
		TargetKind:        record.TargetKind,
		AccountID:         record.AccountID,
		AccountDisplay:    record.AccountDisplay,
		AccountIdentifier: record.AccountIdentifier,
		ExternalPostID:    record.ExternalPostID,
	}
	if record.PublishedAt != nil {
		dto.PublishedAt = record.PublishedAt.Format("2006-01-02T15:04:05Z")
	}
	return dto
}

func (c *AppCommands) CreatePostRecord(req PostRecordRequest) *PostRecordDTO {
	postID, err := c.postRepo.CreateTrackingRecord(
		req.Title,
		req.AssetIDs,
		req.TargetID,
		req.AccountID,
		req.ExternalPostID,
	)
	if err != nil {
		slog.Error("CreatePostRecord", "error", err)
		return nil
	}

	records, err := c.postRepo.ListTrackingRecords(0, 100)
	if err != nil {
		slog.Error("CreatePostRecord: reload", "error", err)
		return nil
	}
	for _, record := range records {
		if record.ID == postID {
			dto := toPostRecordDTO(record)
			return &dto
		}
	}
	return nil
}

func (c *AppCommands) ListPostRecords(offset, limit int) []PostRecordDTO {
	records, err := c.postRepo.ListTrackingRecords(offset, limit)
	if err != nil {
		slog.Error("ListPostRecords", "error", err)
		return nil
	}
	result := make([]PostRecordDTO, len(records))
	for i, record := range records {
		result[i] = toPostRecordDTO(record)
	}
	return result
}

func (c *AppCommands) GetPostRecordsByAsset(assetID int64) []PostRecordDTO {
	records, err := c.postRepo.GetTrackingRecordsByAsset(assetID)
	if err != nil {
		slog.Error("GetPostRecordsByAsset", "assetID", assetID, "error", err)
		return nil
	}
	result := make([]PostRecordDTO, len(records))
	for i, record := range records {
		result[i] = toPostRecordDTO(record)
	}
	return result
}
