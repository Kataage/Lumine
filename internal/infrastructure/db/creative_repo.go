package db

import (
	"database/sql"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

type CreativeRepo struct {
	db *DB
}

func NewCreativeRepo(database *DB) *CreativeRepo {
	return &CreativeRepo{db: database}
}

func uniquePositiveIDs(ids []int64) []int64 {
	seen := make(map[int64]struct{}, len(ids))
	result := make([]int64, 0, len(ids))
	for _, id := range ids {
		if id <= 0 {
			continue
		}
		if _, ok := seen[id]; ok {
			continue
		}
		seen[id] = struct{}{}
		result = append(result, id)
	}
	return result
}

func (r *CreativeRepo) CreateWork(title, description string, assetIDs []int64) (*domain.Work, error) {
	title = strings.TrimSpace(title)
	if title == "" {
		return nil, fmt.Errorf("work title is required")
	}

	tx, err := r.db.Begin()
	if err != nil {
		return nil, err
	}
	defer tx.Rollback()

	ids := uniquePositiveIDs(assetIDs)
	var cover interface{}
	if len(ids) > 0 {
		cover = ids[0]
	}
	result, err := tx.Exec(
		"INSERT INTO works (title, description, cover_asset_id) VALUES (?, ?, ?)",
		title, strings.TrimSpace(description), cover,
	)
	if err != nil {
		return nil, fmt.Errorf("create work: %w", err)
	}
	workID, err := result.LastInsertId()
	if err != nil {
		return nil, err
	}
	for index, assetID := range ids {
		if _, err := tx.Exec(
			"INSERT INTO work_assets (work_id, asset_id, role, sort_order) VALUES (?, ?, 'member', ?)",
			workID, assetID, index,
		); err != nil {
			return nil, fmt.Errorf("attach asset %d to work: %w", assetID, err)
		}
	}
	if err := tx.Commit(); err != nil {
		return nil, err
	}
	return r.GetWorkByID(workID)
}

func (r *CreativeRepo) GetWorkByID(id int64) (*domain.Work, error) {
	var work domain.Work
	var cover sql.NullInt64
	err := r.db.QueryRow(
		"SELECT id, title, description, cover_asset_id, created_at, updated_at FROM works WHERE id = ?", id,
	).Scan(&work.ID, &work.Title, &work.Description, &cover, &work.CreatedAt, &work.UpdatedAt)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	if cover.Valid {
		value := cover.Int64
		work.CoverAssetID = &value
	}
	return &work, nil
}

func (r *CreativeRepo) ListWorks(limit int) ([]domain.Work, error) {
	if limit <= 0 || limit > 500 {
		limit = 200
	}
	rows, err := r.db.Query(
		"SELECT id, title, description, cover_asset_id, created_at, updated_at FROM works ORDER BY updated_at DESC, id DESC LIMIT ?", limit,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	works := make([]domain.Work, 0)
	for rows.Next() {
		var work domain.Work
		var cover sql.NullInt64
		if err := rows.Scan(&work.ID, &work.Title, &work.Description, &cover, &work.CreatedAt, &work.UpdatedAt); err != nil {
			return nil, err
		}
		if cover.Valid {
			value := cover.Int64
			work.CoverAssetID = &value
		}
		works = append(works, work)
	}
	return works, rows.Err()
}

func (r *CreativeRepo) GetWorksByAsset(assetID int64) ([]domain.Work, error) {
	rows, err := r.db.Query(`
		SELECT w.id, w.title, w.description, w.cover_asset_id, w.created_at, w.updated_at
		FROM works w
		INNER JOIN work_assets wa ON wa.work_id = w.id
		WHERE wa.asset_id = ?
		ORDER BY w.updated_at DESC, w.id DESC`, assetID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	works := make([]domain.Work, 0)
	for rows.Next() {
		var work domain.Work
		var cover sql.NullInt64
		if err := rows.Scan(&work.ID, &work.Title, &work.Description, &cover, &work.CreatedAt, &work.UpdatedAt); err != nil {
			return nil, err
		}
		if cover.Valid {
			value := cover.Int64
			work.CoverAssetID = &value
		}
		works = append(works, work)
	}
	return works, rows.Err()
}

func (r *CreativeRepo) AddAssetsToWork(workID int64, assetIDs []int64) error {
	ids := uniquePositiveIDs(assetIDs)
	if len(ids) == 0 {
		return nil
	}
	tx, err := r.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()
	var nextOrder int
	_ = tx.QueryRow("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM work_assets WHERE work_id = ?", workID).Scan(&nextOrder)
	for _, assetID := range ids {
		if _, err := tx.Exec(
			"INSERT OR IGNORE INTO work_assets (work_id, asset_id, role, sort_order) VALUES (?, ?, 'member', ?)",
			workID, assetID, nextOrder,
		); err != nil {
			return err
		}
		nextOrder++
	}
	_, _ = tx.Exec("UPDATE works SET cover_asset_id = COALESCE(cover_asset_id, ?), updated_at = CURRENT_TIMESTAMP WHERE id = ?", ids[0], workID)
	return tx.Commit()
}

func (r *CreativeRepo) GetWorkAssetIDs(workID int64) ([]int64, error) {
	rows, err := r.db.Query("SELECT asset_id FROM work_assets WHERE work_id = ? ORDER BY sort_order, asset_id", workID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var ids []int64
	for rows.Next() {
		var id int64
		if err := rows.Scan(&id); err != nil {
			return nil, err
		}
		ids = append(ids, id)
	}
	return ids, rows.Err()
}

func (r *CreativeRepo) CreateGenerationGroup(group *domain.GenerationGroup, assetIDs []int64) (*domain.GenerationGroup, error) {
	if group == nil || strings.TrimSpace(group.Name) == "" {
		return nil, fmt.Errorf("generation group name is required")
	}
	ids := uniquePositiveIDs(assetIDs)
	if len(ids) == 0 {
		return nil, fmt.Errorf("at least one asset is required")
	}
	tx, err := r.db.Begin()
	if err != nil {
		return nil, err
	}
	defer tx.Rollback()
	result, err := tx.Exec(`
		INSERT INTO generation_groups
		(work_id, name, prompt, negative_prompt, model_name, sampler, scheduler, steps, cfg_scale, workflow_json, notes)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		group.WorkID, strings.TrimSpace(group.Name), group.Prompt, group.NegativePrompt, group.ModelName,
		group.Sampler, group.Scheduler, group.Steps, group.CFGScale, group.WorkflowJSON, group.Notes,
	)
	if err != nil {
		return nil, fmt.Errorf("create generation group: %w", err)
	}
	groupID, err := result.LastInsertId()
	if err != nil {
		return nil, err
	}
	for index, assetID := range ids {
		primary := 0
		if index == 0 {
			primary = 1
		}
		if _, err := tx.Exec(
			"INSERT INTO generation_group_assets (generation_group_id, asset_id, sort_order, is_primary) VALUES (?, ?, ?, ?)",
			groupID, assetID, index, primary,
		); err != nil {
			return nil, fmt.Errorf("attach asset %d to generation group: %w", assetID, err)
		}
	}
	if err := tx.Commit(); err != nil {
		return nil, err
	}
	return r.GetGenerationGroupByID(groupID)
}

func (r *CreativeRepo) GetGenerationGroupByID(id int64) (*domain.GenerationGroup, error) {
	var group domain.GenerationGroup
	var workID sql.NullInt64
	err := r.db.QueryRow(`
		SELECT id, work_id, name, prompt, negative_prompt, model_name, sampler, scheduler, steps, cfg_scale, workflow_json, notes, created_at, updated_at
		FROM generation_groups WHERE id = ?`, id,
	).Scan(&group.ID, &workID, &group.Name, &group.Prompt, &group.NegativePrompt, &group.ModelName, &group.Sampler, &group.Scheduler, &group.Steps, &group.CFGScale, &group.WorkflowJSON, &group.Notes, &group.CreatedAt, &group.UpdatedAt)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	if workID.Valid {
		value := workID.Int64
		group.WorkID = &value
	}
	return &group, nil
}

func (r *CreativeRepo) ListGenerationGroups(limit int) ([]domain.GenerationGroup, error) {
	if limit <= 0 || limit > 500 {
		limit = 200
	}
	rows, err := r.db.Query(`
		SELECT id, work_id, name, prompt, negative_prompt, model_name, sampler, scheduler, steps, cfg_scale, workflow_json, notes, created_at, updated_at
		FROM generation_groups ORDER BY updated_at DESC, id DESC LIMIT ?`, limit)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	groups := make([]domain.GenerationGroup, 0)
	for rows.Next() {
		var group domain.GenerationGroup
		var workID sql.NullInt64
		if err := rows.Scan(&group.ID, &workID, &group.Name, &group.Prompt, &group.NegativePrompt, &group.ModelName, &group.Sampler, &group.Scheduler, &group.Steps, &group.CFGScale, &group.WorkflowJSON, &group.Notes, &group.CreatedAt, &group.UpdatedAt); err != nil {
			return nil, err
		}
		if workID.Valid {
			value := workID.Int64
			group.WorkID = &value
		}
		groups = append(groups, group)
	}
	return groups, rows.Err()
}

func (r *CreativeRepo) GetGenerationGroupsByAsset(assetID int64) ([]domain.GenerationGroup, error) {
	rows, err := r.db.Query(`
		SELECT g.id, g.work_id, g.name, g.prompt, g.negative_prompt, g.model_name, g.sampler, g.scheduler, g.steps, g.cfg_scale, g.workflow_json, g.notes, g.created_at, g.updated_at
		FROM generation_groups g
		INNER JOIN generation_group_assets ga ON ga.generation_group_id = g.id
		WHERE ga.asset_id = ?
		ORDER BY g.updated_at DESC, g.id DESC`, assetID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	groups := make([]domain.GenerationGroup, 0)
	for rows.Next() {
		var group domain.GenerationGroup
		var workID sql.NullInt64
		if err := rows.Scan(&group.ID, &workID, &group.Name, &group.Prompt, &group.NegativePrompt, &group.ModelName, &group.Sampler, &group.Scheduler, &group.Steps, &group.CFGScale, &group.WorkflowJSON, &group.Notes, &group.CreatedAt, &group.UpdatedAt); err != nil {
			return nil, err
		}
		if workID.Valid {
			value := workID.Int64
			group.WorkID = &value
		}
		groups = append(groups, group)
	}
	return groups, rows.Err()
}

func (r *CreativeRepo) AddAssetsToGenerationGroup(groupID int64, assetIDs []int64) error {
	ids := uniquePositiveIDs(assetIDs)
	if len(ids) == 0 {
		return nil
	}
	tx, err := r.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()
	var nextOrder int
	_ = tx.QueryRow("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM generation_group_assets WHERE generation_group_id = ?", groupID).Scan(&nextOrder)
	for _, assetID := range ids {
		if _, err := tx.Exec(
			"INSERT OR IGNORE INTO generation_group_assets (generation_group_id, asset_id, sort_order, is_primary) VALUES (?, ?, ?, 0)",
			groupID, assetID, nextOrder,
		); err != nil {
			return err
		}
		nextOrder++
	}
	_, _ = tx.Exec("UPDATE generation_groups SET updated_at = CURRENT_TIMESTAMP WHERE id = ?", groupID)
	return tx.Commit()
}

func (r *CreativeRepo) GetGenerationGroupAssetIDs(groupID int64) ([]int64, error) {
	rows, err := r.db.Query("SELECT asset_id FROM generation_group_assets WHERE generation_group_id = ? ORDER BY sort_order, asset_id", groupID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var ids []int64
	for rows.Next() {
		var id int64
		if err := rows.Scan(&id); err != nil {
			return nil, err
		}
		ids = append(ids, id)
	}
	return ids, rows.Err()
}

func (r *CreativeRepo) CreateRelation(parentAssetID, childAssetID int64, relationType, note string) (*domain.AssetRelation, error) {
	if parentAssetID <= 0 || childAssetID <= 0 || parentAssetID == childAssetID {
		return nil, fmt.Errorf("parent and child assets must be different valid assets")
	}
	relationType = strings.TrimSpace(relationType)
	if relationType == "" {
		return nil, fmt.Errorf("relation type is required")
	}
	result, err := r.db.Exec(
		"INSERT INTO asset_relations (parent_asset_id, child_asset_id, relation_type, note) VALUES (?, ?, ?, ?)",
		parentAssetID, childAssetID, relationType, strings.TrimSpace(note),
	)
	if err != nil {
		return nil, fmt.Errorf("create asset relation: %w", err)
	}
	id, err := result.LastInsertId()
	if err != nil {
		return nil, err
	}
	var relation domain.AssetRelation
	if err := r.db.QueryRow(
		"SELECT id, parent_asset_id, child_asset_id, relation_type, note, created_at FROM asset_relations WHERE id = ?", id,
	).Scan(&relation.ID, &relation.ParentAssetID, &relation.ChildAssetID, &relation.RelationType, &relation.Note, &relation.CreatedAt); err != nil {
		return nil, err
	}
	return &relation, nil
}

func (r *CreativeRepo) DeleteRelation(id int64) error {
	_, err := r.db.Exec("DELETE FROM asset_relations WHERE id = ?", id)
	return err
}

func (r *CreativeRepo) GetRelationsByAsset(assetID int64) ([]domain.AssetRelation, error) {
	rows, err := r.db.Query(`
		SELECT id, parent_asset_id, child_asset_id, relation_type, note, created_at
		FROM asset_relations
		WHERE parent_asset_id = ? OR child_asset_id = ?
		ORDER BY created_at DESC, id DESC`, assetID, assetID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	relations := make([]domain.AssetRelation, 0)
	for rows.Next() {
		var relation domain.AssetRelation
		if err := rows.Scan(&relation.ID, &relation.ParentAssetID, &relation.ChildAssetID, &relation.RelationType, &relation.Note, &relation.CreatedAt); err != nil {
			return nil, err
		}
		relations = append(relations, relation)
	}
	return relations, rows.Err()
}
