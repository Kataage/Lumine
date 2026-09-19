package db

import (
	"database/sql"
	"encoding/json"
	"fmt"
	"strings"

	"github.com/kataage/lumine/internal/domain"
)

type PromptProjectRepo struct {
	db *DB
}

func NewPromptProjectRepo(database *DB) *PromptProjectRepo {
	return &PromptProjectRepo{db: database}
}

func encodePromptJSON(value any, fallback string) string {
	data, err := json.Marshal(value)
	if err != nil {
		return fallback
	}
	return string(data)
}

func decodePromptProject(row interface{ Scan(...any) error }) (*domain.PromptProject, error) {
	var project domain.PromptProject
	var charactersJSON, lorasJSON string
	var deleted sql.NullTime
	if err := row.Scan(
		&project.SchemaVersion, &project.ID, &project.Title, &project.Idea, &project.Notes, &project.TargetProfileID,
		&charactersJSON, &lorasJSON, &deleted, &project.CreatedAt, &project.UpdatedAt,
	); err != nil {
		return nil, err
	}
	if err := json.Unmarshal([]byte(charactersJSON), &project.Characters); err != nil {
		return nil, fmt.Errorf("decode prompt project characters: %w", err)
	}
	if project.Characters == nil {
		project.Characters = []string{}
	}
	if err := json.Unmarshal([]byte(lorasJSON), &project.LoRAs); err != nil {
		return nil, fmt.Errorf("decode prompt project LoRAs: %w", err)
	}
	if project.LoRAs == nil {
		project.LoRAs = []domain.PromptLoRA{}
	}
	if deleted.Valid {
		value := deleted.Time
		project.DeletedAt = &value
	}
	return &project, nil
}

const promptProjectColumns = `
	schema_version, id, title, idea, notes, target_profile_id,
	characters_json, loras_json, deleted_at, created_at, updated_at
`

func (r *PromptProjectRepo) Create(project *domain.PromptProject) (*domain.PromptProject, error) {
	if project == nil || strings.TrimSpace(project.Title) == "" {
		return nil, fmt.Errorf("prompt project title is required")
	}
	result, err := r.db.Exec(`
		INSERT INTO prompt_projects
			(schema_version, title, idea, notes, target_profile_id, characters_json, loras_json)
		VALUES (1, ?, ?, ?, ?, ?, ?)
	`,
		strings.TrimSpace(project.Title), strings.TrimSpace(project.Idea),
		strings.TrimSpace(project.Notes), strings.TrimSpace(project.TargetProfileID),
		encodePromptJSON(project.Characters, "[]"), encodePromptJSON(project.LoRAs, "[]"),
	)
	if err != nil {
		return nil, fmt.Errorf("create prompt project: %w", err)
	}
	id, err := result.LastInsertId()
	if err != nil {
		return nil, err
	}
	return r.GetProject(id, true)
}

func (r *PromptProjectRepo) Update(project *domain.PromptProject) (*domain.PromptProject, error) {
	if project == nil || project.ID <= 0 || strings.TrimSpace(project.Title) == "" {
		return nil, fmt.Errorf("valid prompt project is required")
	}
	result, err := r.db.Exec(`
		UPDATE prompt_projects SET
			title = ?, idea = ?, notes = ?, target_profile_id = ?,
			characters_json = ?, loras_json = ?, updated_at = CURRENT_TIMESTAMP
		WHERE id = ?
	`,
		strings.TrimSpace(project.Title), strings.TrimSpace(project.Idea),
		strings.TrimSpace(project.Notes), strings.TrimSpace(project.TargetProfileID),
		encodePromptJSON(project.Characters, "[]"), encodePromptJSON(project.LoRAs, "[]"),
		project.ID,
	)
	if err != nil {
		return nil, fmt.Errorf("update prompt project: %w", err)
	}
	affected, _ := result.RowsAffected()
	if affected == 0 {
		return nil, fmt.Errorf("prompt project not found: %d", project.ID)
	}
	return r.GetProject(project.ID, true)
}

func (r *PromptProjectRepo) GetProject(id int64, includeDeleted bool) (*domain.PromptProject, error) {
	query := "SELECT " + promptProjectColumns + " FROM prompt_projects WHERE id = ?"
	if !includeDeleted {
		query += " AND deleted_at IS NULL"
	}
	project, err := decodePromptProject(r.db.QueryRow(query, id))
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get prompt project %d: %w", id, err)
	}
	return project, nil
}

func (r *PromptProjectRepo) ListProjects(includeDeleted bool, limit int) ([]domain.PromptProject, error) {
	if limit <= 0 || limit > 1000 {
		limit = 200
	}
	query := "SELECT " + promptProjectColumns + " FROM prompt_projects"
	if !includeDeleted {
		query += " WHERE deleted_at IS NULL"
	}
	query += " ORDER BY updated_at DESC, id DESC LIMIT ?"
	rows, err := r.db.Query(query, limit)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := make([]domain.PromptProject, 0)
	for rows.Next() {
		project, err := decodePromptProject(rows)
		if err != nil {
			return nil, err
		}
		result = append(result, *project)
	}
	return result, rows.Err()
}

func (r *PromptProjectRepo) SetDeleted(id int64, deleted bool) error {
	var result sql.Result
	var err error
	if deleted {
		result, err = r.db.Exec(
			"UPDATE prompt_projects SET deleted_at = CURRENT_TIMESTAMP, updated_at = CURRENT_TIMESTAMP WHERE id = ? AND deleted_at IS NULL",
			id,
		)
	} else {
		result, err = r.db.Exec(
			"UPDATE prompt_projects SET deleted_at = NULL, updated_at = CURRENT_TIMESTAMP WHERE id = ? AND deleted_at IS NOT NULL",
			id,
		)
	}
	if err != nil {
		return err
	}
	affected, _ := result.RowsAffected()
	if affected == 0 {
		project, getErr := r.GetProject(id, true)
		if getErr != nil {
			return getErr
		}
		if project == nil {
			return fmt.Errorf("prompt project not found: %d", id)
		}
	}
	return nil
}

func (r *PromptProjectRepo) SetProjectAssets(projectID int64, role string, assetIDs []int64) error {
	if role != "reference" && role != "generated" {
		return fmt.Errorf("invalid prompt project asset role %q", role)
	}
	tx, err := r.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()
	if _, err := tx.Exec("DELETE FROM prompt_project_assets WHERE project_id = ? AND role = ?", projectID, role); err != nil {
		return err
	}
	seen := make(map[int64]struct{}, len(assetIDs))
	order := 0
	for _, assetID := range assetIDs {
		if assetID <= 0 {
			continue
		}
		if _, exists := seen[assetID]; exists {
			continue
		}
		seen[assetID] = struct{}{}
		if _, err := tx.Exec(
			"INSERT INTO prompt_project_assets (project_id, asset_id, role, sort_order) VALUES (?, ?, ?, ?)",
			projectID, assetID, role, order,
		); err != nil {
			return fmt.Errorf("attach %s asset %d: %w", role, assetID, err)
		}
		order++
	}
	if _, err := tx.Exec("UPDATE prompt_projects SET updated_at = CURRENT_TIMESTAMP WHERE id = ?", projectID); err != nil {
		return err
	}
	return tx.Commit()
}

func (r *PromptProjectRepo) GetProjectAssetIDs(projectID int64, role string) ([]int64, error) {
	rows, err := r.db.Query(
		"SELECT asset_id FROM prompt_project_assets WHERE project_id = ? AND role = ? ORDER BY sort_order, asset_id",
		projectID, role,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := make([]int64, 0)
	for rows.Next() {
		var id int64
		if err := rows.Scan(&id); err != nil {
			return nil, err
		}
		result = append(result, id)
	}
	return result, rows.Err()
}

func (r *PromptProjectRepo) CreateVariant(projectID int64, name string) (*domain.PromptVariant, error) {
	name = strings.TrimSpace(name)
	if projectID <= 0 || name == "" {
		return nil, fmt.Errorf("prompt variant project and name are required")
	}
	result, err := r.db.Exec(
		"INSERT INTO prompt_variants (project_id, name) VALUES (?, ?)",
		projectID, name,
	)
	if err != nil {
		return nil, fmt.Errorf("create prompt variant: %w", err)
	}
	id, err := result.LastInsertId()
	if err != nil {
		return nil, err
	}
	if _, err := r.db.Exec("UPDATE prompt_projects SET updated_at = CURRENT_TIMESTAMP WHERE id = ?", projectID); err != nil {
		return nil, err
	}
	return r.GetVariant(id)
}

func (r *PromptProjectRepo) GetVariant(id int64) (*domain.PromptVariant, error) {
	var variant domain.PromptVariant
	err := r.db.QueryRow(
		"SELECT id, project_id, name, created_at FROM prompt_variants WHERE id = ?", id,
	).Scan(&variant.ID, &variant.ProjectID, &variant.Name, &variant.CreatedAt)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	return &variant, nil
}

func (r *PromptProjectRepo) ListVariants(projectID int64) ([]domain.PromptVariant, error) {
	rows, err := r.db.Query(
		"SELECT id, project_id, name, created_at FROM prompt_variants WHERE project_id = ? ORDER BY id",
		projectID,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := make([]domain.PromptVariant, 0)
	for rows.Next() {
		var variant domain.PromptVariant
		if err := rows.Scan(&variant.ID, &variant.ProjectID, &variant.Name, &variant.CreatedAt); err != nil {
			return nil, err
		}
		result = append(result, variant)
	}
	return result, rows.Err()
}

func (r *PromptProjectRepo) CreateVersion(version *domain.PromptVersion) (*domain.PromptVersion, error) {
	if version == nil || version.VariantID <= 0 {
		return nil, fmt.Errorf("prompt version variant is required")
	}
	if strings.TrimSpace(version.Source) == "" {
		version.Source = "manual"
	}
	if strings.TrimSpace(version.ProfileSnapshotJSON) == "" {
		version.ProfileSnapshotJSON = "{}"
	}
	if strings.TrimSpace(version.MetadataJSON) == "" {
		version.MetadataJSON = "{}"
	}
	if version.ParentVersionID != nil {
		parent, err := r.GetVersion(*version.ParentVersionID)
		if err != nil {
			return nil, err
		}
		if parent == nil {
			return nil, fmt.Errorf("parent prompt version not found: %d", *version.ParentVersionID)
		}
		parentVariant, err := r.GetVariant(parent.VariantID)
		if err != nil || parentVariant == nil {
			return nil, fmt.Errorf("parent prompt variant not found")
		}
		targetVariant, err := r.GetVariant(version.VariantID)
		if err != nil || targetVariant == nil {
			return nil, fmt.Errorf("target prompt variant not found")
		}
		if parentVariant.ProjectID != targetVariant.ProjectID {
			return nil, fmt.Errorf("parent version belongs to another prompt project")
		}
	}
	result, err := r.db.Exec(`
		INSERT INTO prompt_versions (
			variant_id, schema_version, parent_version_id, positive, negative, source,
			change_instruction, profile_id, profile_snapshot_json,
			ai_engine, ai_model_id, ai_model_version, metadata_json
		) VALUES (?, 1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
	`,
		version.VariantID, version.ParentVersionID, version.Positive, version.Negative,
		strings.TrimSpace(version.Source), strings.TrimSpace(version.ChangeInstruction),
		strings.TrimSpace(version.ProfileID), version.ProfileSnapshotJSON,
		strings.TrimSpace(version.AIEngine), strings.TrimSpace(version.AIModelID),
		strings.TrimSpace(version.AIModelVersion), version.MetadataJSON,
	)
	if err != nil {
		return nil, fmt.Errorf("create prompt version: %w", err)
	}
	id, err := result.LastInsertId()
	if err != nil {
		return nil, err
	}
	variant, err := r.GetVariant(version.VariantID)
	if err == nil && variant != nil {
		_, _ = r.db.Exec("UPDATE prompt_projects SET updated_at = CURRENT_TIMESTAMP WHERE id = ?", variant.ProjectID)
	}
	return r.GetVersion(id)
}

func (r *PromptProjectRepo) GetVersion(id int64) (*domain.PromptVersion, error) {
	var version domain.PromptVersion
	var parent sql.NullInt64
	err := r.db.QueryRow(`
		SELECT schema_version, id, variant_id, parent_version_id, positive, negative, source,
			change_instruction, profile_id, profile_snapshot_json,
			ai_engine, ai_model_id, ai_model_version, metadata_json, created_at
		FROM prompt_versions WHERE id = ?
	`, id).Scan(
		&version.SchemaVersion, &version.ID, &version.VariantID, &parent, &version.Positive, &version.Negative,
		&version.Source, &version.ChangeInstruction, &version.ProfileID,
		&version.ProfileSnapshotJSON, &version.AIEngine, &version.AIModelID,
		&version.AIModelVersion, &version.MetadataJSON, &version.CreatedAt,
	)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	if parent.Valid {
		value := parent.Int64
		version.ParentVersionID = &value
	}
	return &version, nil
}

func (r *PromptProjectRepo) ListVersions(variantID int64) ([]domain.PromptVersion, error) {
	rows, err := r.db.Query(`
		SELECT schema_version, id, variant_id, parent_version_id, positive, negative, source,
			change_instruction, profile_id, profile_snapshot_json,
			ai_engine, ai_model_id, ai_model_version, metadata_json, created_at
		FROM prompt_versions WHERE variant_id = ? ORDER BY id DESC
	`, variantID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := make([]domain.PromptVersion, 0)
	for rows.Next() {
		var version domain.PromptVersion
		var parent sql.NullInt64
		if err := rows.Scan(
			&version.SchemaVersion, &version.ID, &version.VariantID, &parent, &version.Positive, &version.Negative,
			&version.Source, &version.ChangeInstruction, &version.ProfileID,
			&version.ProfileSnapshotJSON, &version.AIEngine, &version.AIModelID,
			&version.AIModelVersion, &version.MetadataJSON, &version.CreatedAt,
		); err != nil {
			return nil, err
		}
		if parent.Valid {
			value := parent.Int64
			version.ParentVersionID = &value
		}
		result = append(result, version)
	}
	return result, rows.Err()
}
