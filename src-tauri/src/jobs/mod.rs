use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use crate::db::Database;
use crate::infrastructure::ThumbnailGenerator;

#[derive(Debug, Clone, serde::Serialize)]
pub struct JobStatus {
    pub id: String,
    pub job_type: String,
    pub status: String,
    pub progress: f64,
    pub total: u64,
    pub completed: u64,
    pub message: String,
}

#[derive(Debug)]
struct JobState {
    id: String,
    job_type: String,
    status: String,
    progress: f64,
    total: u64,
    completed: u64,
    message: String,
    cancel_flag: Arc<AtomicBool>,
}

pub struct JobSystem {
    jobs: Arc<Mutex<HashMap<String, JobState>>>,
    db: Arc<Mutex<Database>>,
    thumbnail_generator: Arc<ThumbnailGenerator>,
}

impl JobSystem {
    pub fn new(db: Arc<Mutex<Database>>, cache_dir: PathBuf) -> Self {
        Self {
            jobs: Arc::new(Mutex::new(HashMap::new())),
            db,
            thumbnail_generator: Arc::new(ThumbnailGenerator::new(cache_dir)),
        }
    }

    pub fn start_thumbnail_generation(&self, library_id: i64) -> String {
        let job_id = uuid::Uuid::new_v4().to_string();

        {
            let mut jobs = self.jobs.lock().unwrap();
            jobs.insert(
                job_id.clone(),
                JobState {
                    id: job_id.clone(),
                    job_type: "thumbnail_generation".to_string(),
                    status: "running".to_string(),
                    progress: 0.0,
                    total: 0,
                    completed: 0,
                    message: "Starting thumbnail generation...".to_string(),
                    cancel_flag: Arc::new(AtomicBool::new(false)),
                },
            );
        }

        let db = self.db.clone();
        let generator = self.thumbnail_generator.clone();
        let jobs = self.jobs.clone();
        let job_id_clone = job_id.clone();

        std::thread::spawn(move || {
            let cancel_flag = {
                let jobs = jobs.lock().unwrap();
                if let Some(state) = jobs.get(&job_id_clone) {
                    state.cancel_flag.clone()
                } else {
                    return;
                }
            };

            let db_guard = db.lock().unwrap();
            let conn = db_guard.connection();
            let assets: Vec<(i64, String, String, String)> = conn
                .prepare(
                    "SELECT id, file_path, COALESCE(modified_at_fs, ''), COALESCE(thumb_status, 'none')
                     FROM assets WHERE library_id = ? AND (thumb_status = 'none' OR thumb_status = 'failed')",
                )
                .unwrap()
                .query_map([library_id], |row| {
                    Ok((row.get(0)?, row.get(1)?, row.get(2)?, row.get(3)?))
                })
                .unwrap()
                .filter_map(|r| r.ok())
                .collect();

            let total = assets.len() as u64;

            {
                let mut jobs = jobs.lock().unwrap();
                if let Some(state) = jobs.get_mut(&job_id_clone) {
                    state.total = total;
                    state.message = format!("Generating thumbnails for {} assets...", total);
                }
            }

            for (idx, (asset_id, file_path, modified_at, _)) in assets.iter().enumerate() {
                if cancel_flag.load(Ordering::SeqCst) {
                    let mut jobs = jobs.lock().unwrap();
                    if let Some(state) = jobs.get_mut(&job_id_clone) {
                        state.status = "cancelled".to_string();
                        state.message = "Thumbnail generation cancelled".to_string();
                    }
                    break;
                }

                let path = std::path::Path::new(file_path);
                if !path.exists() {
                    if let Ok(conn) = db.lock() {
                        let _ = conn.connection().execute(
                            "UPDATE assets SET thumb_status = 'failed' WHERE id = ?",
                            [asset_id],
                        );
                    }
                    continue;
                }

                match generator.generate_thumbnail(*asset_id, path, modified_at) {
                    Ok(thumb_path) => {
                        if let Ok(conn) = db.lock() {
                            let _ = conn.connection().execute(
                                "UPDATE assets SET thumb_status = 'ready', thumb_path = ? WHERE id = ?",
                                [thumb_path.to_string_lossy().to_string(), asset_id.to_string()],
                            );
                        }
                    }
                    Err(_) => {
                        if let Ok(conn) = db.lock() {
                            let _ = conn.connection().execute(
                                "UPDATE assets SET thumb_status = 'failed' WHERE id = ?",
                                [asset_id],
                            );
                        }
                    }
                }

                let completed = (idx + 1) as u64;
                let progress = if total > 0 {
                    (completed as f64 / total as f64) * 100.0
                } else {
                    100.0
                };

                {
                    let mut jobs = jobs.lock().unwrap();
                    if let Some(state) = jobs.get_mut(&job_id_clone) {
                        state.completed = completed;
                        state.progress = progress;
                        state.message = format!(
                            "Generated {}/{} thumbnails",
                            completed, total
                        );
                    }
                }
            }

            {
                let mut jobs = jobs.lock().unwrap();
                if let Some(state) = jobs.get_mut(&job_id_clone) {
                    if state.status != "cancelled" {
                        state.status = "completed".to_string();
                        state.progress = 100.0;
                        state.message = "Thumbnail generation completed".to_string();
                    }
                }
            }

            // Log to job_logs table
            if let Ok(conn) = db.lock() {
                let final_status = {
                    let jobs = jobs.lock().unwrap();
                    jobs.get(&job_id_clone)
                        .map(|s| s.status.clone())
                        .unwrap_or("completed".to_string())
                };
                let _ = conn.connection().execute(
                    "INSERT INTO job_logs (job_type, status, message, finished_at) VALUES (?, ?, ?, datetime('now'))",
                    ["thumbnail_generation", &final_status, "Thumbnail generation job finished"],
                );
            }
        });

        job_id
    }

    pub fn get_job_status(&self, job_id: &str) -> Option<JobStatus> {
        let jobs = self.jobs.lock().ok()?;
        let state = jobs.get(job_id)?;
        Some(JobStatus {
            id: state.id.clone(),
            job_type: state.job_type.clone(),
            status: state.status.clone(),
            progress: state.progress,
            total: state.total,
            completed: state.completed,
            message: state.message.clone(),
        })
    }

    pub fn list_jobs(&self) -> Vec<JobStatus> {
        let jobs = self.jobs.lock().unwrap();
        jobs.values()
            .map(|state| JobStatus {
                id: state.id.clone(),
                job_type: state.job_type.clone(),
                status: state.status.clone(),
                progress: state.progress,
                total: state.total,
                completed: state.completed,
                message: state.message.clone(),
            })
            .collect()
    }

    pub fn cancel(&self, job_id: &str) {
        if let Some(state) = self.jobs.lock().unwrap().get(job_id) {
            state.cancel_flag.store(true, Ordering::SeqCst);
        }
    }

    pub fn get_thumbnail_generator(&self) -> Arc<ThumbnailGenerator> {
        self.thumbnail_generator.clone()
    }
}
