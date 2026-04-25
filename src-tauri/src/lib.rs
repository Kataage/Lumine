mod application;
mod commands;
mod db;
mod domain;
mod infrastructure;
mod jobs;

use anyhow::Context;
use db::{Database, migrations};
use jobs::JobSystem;
use std::sync::{Arc, Mutex};
use tauri::Manager;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .setup(|app: &mut tauri::App| -> anyhow::Result<()> {
            let app_data_dir = app
                .path()
                .app_data_dir()
                .context("Failed to get app data dir")?;
            std::fs::create_dir_all(&app_data_dir)
                .context("Failed to create app data dir")?;

            let db_path = app_data_dir.join("lumine.db");
            let db = Database::new(&db_path).context("Failed to initialize database")?;
            migrations::run_migrations(&db.connection()).context("Failed to run migrations")?;

            let db = Arc::new(Mutex::new(db));
            app.manage(db.clone());
            app.manage(JobSystem::new(db, app.handle().clone()));

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::get_app_bootstrap,
            commands::list_libraries,
            commands::add_library,
            commands::remove_library,
            commands::scan_library,
            commands::list_assets,
            commands::get_asset_detail,
            commands::update_asset_note,
            commands::set_asset_rating,
            commands::set_asset_status,
            commands::set_asset_favorite,
            commands::list_tags,
            commands::create_tag,
            commands::set_asset_tags,
            commands::list_post_targets,
            commands::create_post_target,
            commands::list_post_accounts,
            commands::create_post_account,
            commands::list_posts,
            commands::create_post_draft,
            commands::update_post,
            commands::attach_assets_to_post,
            commands::get_post_assets,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}