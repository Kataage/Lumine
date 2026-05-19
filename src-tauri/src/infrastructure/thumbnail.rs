use anyhow::{Context, Result};
use image::imageops::FilterType;
use std::fs;
use std::path::{Path, PathBuf};

const THUMBNAIL_SIZE: u32 = 256;

pub struct ThumbnailGenerator {
    cache_dir: PathBuf,
}

impl ThumbnailGenerator {
    pub fn new(cache_dir: PathBuf) -> Self {
        fs::create_dir_all(&cache_dir).ok();
        Self { cache_dir }
    }

    pub fn generate_thumbnail(
        &self,
        asset_id: i64,
        source_path: &Path,
        modified_at: &str,
    ) -> Result<PathBuf> {
        let cache_key = format!("{}_{}", asset_id, modified_at.replace([':', ' ', '-'], "_"));
        let thumb_path = self.cache_dir.join(format!("{}.webp", cache_key));

        if thumb_path.exists() {
            return Ok(thumb_path);
        }

        let img = image::open(source_path)
            .with_context(|| format!("Failed to open image: {:?}", source_path))?;

        let thumbnail = img.resize(
            THUMBNAIL_SIZE,
            THUMBNAIL_SIZE,
            FilterType::Lanczos3,
        );

        thumbnail
            .save_with_format(&thumb_path, image::ImageFormat::WebP)
            .with_context(|| format!("Failed to save thumbnail: {:?}", thumb_path))?;

        Ok(thumb_path)
    }

    pub fn get_cached_thumbnail(&self, asset_id: i64, modified_at: &str) -> Option<PathBuf> {
        let cache_key = format!("{}_{}", asset_id, modified_at.replace([':', ' ', '-'], "_"));
        let thumb_path = self.cache_dir.join(format!("{}.webp", cache_key));

        if thumb_path.exists() {
            Some(thumb_path)
        } else {
            None
        }
    }

    pub fn invalidate_thumbnail(&self, asset_id: i64) {
        let pattern = format!("{}_", asset_id);
        if let Ok(entries) = fs::read_dir(&self.cache_dir) {
            for entry in entries.flatten() {
                let path = entry.path();
                if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                    if name.starts_with(&pattern) {
                        fs::remove_file(path).ok();
                    }
                }
            }
        }
    }
}
