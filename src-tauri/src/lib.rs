mod application;
mod commands;
mod db;
mod domain;
mod infrastructure;

use anyhow::anyhow;
use db::{Database, migrations};
use std::sync::{Arc, Mutex};
use tauri::Manager;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .setup(|app: &mut tauri::App| -> Result<(), Box<dyn std::error::Error>> {
            let app_data_dir = app
                .path()
                .app_data_dir()
                .map_err(|e| -> Box<dyn std::error::Error> { anyhow::anyhow!(e).into() })?;
            std::fs::create_dir_all(&app_data_dir)?;

            let db_path = app_data_dir.join("lumine.db");
            let db = Database::new(&db_path)
                .map_err(|e| -> Box<dyn std::error::Error> { anyhow::anyhow!(e).into() })?;
            migrations::run_migrations(&db.connection())
                .map_err(|e| -> Box<dyn std::error::Error> { anyhow::anyhow!(e).into() })?;

            let db = Arc::new(Mutex::new(db));
            app.manage(db.clone());

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
            commands::get_asset_note,
            commands::update_asset_note,
            commands::set_asset_rating,
            commands::set_asset_status,
            commands::set_asset_favorite,
            commands::move_assets,
            commands::set_asset_color_label,
            commands::batch_update_assets,
            commands::list_tags,
            commands::create_tag,
            commands::set_asset_tags,
            commands::get_asset_tags,
            commands::list_post_targets,
            commands::create_post_target,
            commands::list_post_accounts,
            commands::create_post_account,
            commands::list_posts,
            commands::create_post_draft,
            commands::update_post,
            commands::attach_assets_to_post,
            commands::get_post_assets,
            commands::get_library_path,
            commands::list_assets_from_folder,
            commands::get_excluded_folders,
            commands::set_excluded_folders,
            commands::get_supported_extensions,
            commands::set_supported_extensions,
            commands::start_folder_watcher,
            commands::stop_folder_watcher,
            commands::is_folder_watching,
            commands::execute_scheduled_posts,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
