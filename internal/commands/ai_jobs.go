package commands

import (
	"fmt"

	"github.com/kataage/lumine/internal/ai"
	"github.com/kataage/lumine/internal/domain"
)

const aiReanalysisPageSize = 1000

func (c *AppCommands) SetAIJobQueue(queue *ai.JobQueue) {
	c.aiJobQueue = queue
}

func (c *AppCommands) GetAIAnalyses(assetID int64) ([]domain.AIAnalysis, error) {
	if c.aiJobQueue == nil {
		return []domain.AIAnalysis{}, nil
	}
	return c.aiJobQueue.GetAnalysesByAsset(assetID)
}

func (c *AppCommands) ListAIJobs(limit int) ([]domain.AIJob, error) {
	if c.aiJobQueue == nil {
		return []domain.AIJob{}, nil
	}
	return c.aiJobQueue.ListJobs(limit)
}

func (c *AppCommands) ReanalyzeAssets(assetIDs []int64, capability string, priority int) (int, error) {
	if c.aiJobQueue == nil {
		return 0, fmt.Errorf("AI job queue is not available")
	}
	if len(assetIDs) == 0 {
		return 0, nil
	}
	if len(assetIDs) > 10000 {
		return 0, fmt.Errorf("too many assets in one reanalysis request: %d", len(assetIDs))
	}
	parsed, err := parseModelBackedAICapability(capability)
	if err != nil {
		return 0, err
	}
	return c.aiJobQueue.EnqueueMany(assetIDs, parsed, priority, false)
}

func (c *AppCommands) ReanalyzeLibrary(libraryID int64, capability string, priority int) (int, error) {
	return c.reanalyzeScope(libraryID, "", true, capability, priority)
}

func (c *AppCommands) ReanalyzeFolder(
	libraryID int64,
	folderPath string,
	recurse bool,
	capability string,
	priority int,
) (int, error) {
	if folderPath == "" {
		return 0, fmt.Errorf("folder path is required")
	}
	return c.reanalyzeScope(libraryID, folderPath, recurse, capability, priority)
}

func (c *AppCommands) CancelAIJob(jobID int64) error {
	if c.aiJobQueue == nil {
		return fmt.Errorf("AI job queue is not available")
	}
	return c.aiJobQueue.Cancel(jobID)
}

func (c *AppCommands) RetryAIJob(jobID int64) error {
	if c.aiJobQueue == nil {
		return fmt.Errorf("AI job queue is not available")
	}
	return c.aiJobQueue.Retry(jobID)
}

func (c *AppCommands) reanalyzeScope(
	libraryID int64,
	folderPath string,
	recurse bool,
	capability string,
	priority int,
) (int, error) {
	if c.aiJobQueue == nil {
		return 0, fmt.Errorf("AI job queue is not available")
	}
	parsed, err := parseModelBackedAICapability(capability)
	if err != nil {
		return 0, err
	}

	totalCreated := 0
	var afterID int64
	for {
		ids, err := c.assetRepo.ListIDsByScope(libraryID, folderPath, recurse, afterID, aiReanalysisPageSize)
		if err != nil {
			return totalCreated, err
		}
		if len(ids) == 0 {
			return totalCreated, nil
		}

		created, err := c.aiJobQueue.EnqueueMany(ids, parsed, priority, false)
		if err != nil {
			return totalCreated, err
		}
		totalCreated += created
		afterID = ids[len(ids)-1]
		if len(ids) < aiReanalysisPageSize {
			return totalCreated, nil
		}
	}
}

func parseModelBackedAICapability(value string) (domain.AICapability, error) {
	capability := domain.AICapability(value)
	if !domain.IsModelBackedAICapability(capability) {
		return "", fmt.Errorf("unsupported AI capability %q", value)
	}
	return capability, nil
}
