use std::path::Path;
use walkdir::WalkDir;

const DEFAULT_IMAGE_EXTENSIONS: &[&str] = &[
    "jpg", "jpeg", "png", "gif", "bmp", "webp", "tiff", "tif", "ico", "svg", "avif", "apng",
];

#[derive(serde::Serialize)]
pub struct QuickAsset {
    pub id: i64,
    pub file_path: String,
    pub file_name: String,
    pub folder_path: String,
    pub extension: String,
    pub file_size: i64,
    pub modified_at: String,
}

pub fn scan_folder_quick(
    root_path: &str,
    excluded_folders: &[String],
    supported_extensions: &[String],
) -> Vec<QuickAsset> {
    let root = Path::new(root_path);
    if !root.exists() || !root.is_dir() {
        return Vec::new();
    }

    let mut assets = Vec::new();
    let mut id: i64 = 1;

    for entry in WalkDir::new(root)
        .follow_links(false)
        .into_iter()
        .filter_map(|e| e.ok())
    {
        let path = entry.path();
        if !path.is_file() {
            continue;
        }

        let extension = match path.extension().and_then(|s| s.to_str()) {
            Some(ext) => ext.to_lowercase(),
            None => continue,
        };

        let extensions_to_check = if supported_extensions.is_empty() {
            DEFAULT_IMAGE_EXTENSIONS
        } else {
            supported_extensions.iter().map(|s| s.as_str()).collect::<Vec<_>>().leak()
        };

        if !extensions_to_check.contains(&extension.as_str()) {
            continue;
        }

        let folder_path = path
            .parent()
            .and_then(|p| p.to_str())
            .unwrap_or(root_path)
            .to_string();

        let relative = folder_path.strip_prefix(root_path).unwrap_or(&folder_path);
        let is_excluded = excluded_folders.iter().any(|excluded| {
            relative.starts_with(excluded)
                || relative.starts_with(&format!("/{}", excluded))
                || relative.starts_with(&format!("\\{}", excluded))
        });

        if is_excluded {
            continue;
        }

        let file_name = match path.file_name().and_then(|s| s.to_str()) {
            Some(name) => name.to_string(),
            None => continue,
        };

        let file_path = match path.to_str() {
            Some(p) => p.to_string(),
            None => continue,
        };

        let file_size = path.metadata().map(|m| m.len() as i64).unwrap_or(0);
        let modified_at = path
            .metadata()
            .ok()
            .and_then(|m| m.modified().ok())
            .map(|t| {
                let datetime: chrono::DateTime<chrono::Utc> = t.into();
                datetime.format("%Y-%m-%d %H:%M:%S").to_string()
            })
            .unwrap_or_default();

        assets.push(QuickAsset {
            id,
            file_path,
            file_name,
            folder_path,
            extension,
            file_size,
            modified_at,
        });
        id += 1;
    }

    assets
}
