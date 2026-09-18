package db

import (
	"database/sql"
	"fmt"

	"github.com/kataage/lumine/internal/domain"
)

type AppSettingRepo struct {
	db *DB
}

func NewAppSettingRepo(db *DB) *AppSettingRepo {
	return &AppSettingRepo{db: db}
}

func (r *AppSettingRepo) Get(key string) (*domain.AppSetting, error) {
	var s domain.AppSetting
	err := r.db.QueryRow(
		"SELECT key, value_json, updated_at FROM app_settings WHERE key = ?", key,
	).Scan(&s.Key, &s.ValueJSON, &s.UpdatedAt)
	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, fmt.Errorf("get setting %s: %w", key, err)
	}
	return &s, nil
}

func (r *AppSettingRepo) Set(key, valueJSON string) error {
	_, err := r.db.Exec(
		"INSERT INTO app_settings (key, value_json) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value_json = ?, updated_at = CURRENT_TIMESTAMP",
		key, valueJSON, valueJSON,
	)
	return err
}
