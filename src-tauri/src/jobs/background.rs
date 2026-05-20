use crate::application::LibraryService;
use crate::db::Database;
use crate::infrastructure::{FileScanner, ThumbnailGenerator};
use std::path::PathBuf;
use std::sync::{mpsc::{channel, Receiver, Sender}, Arc, Mutex};
use std::thread;
use tauri::{AppHandle, Emitter};

pub enum JobCommand {
    ScanLibrary { library_id: i64 },
    GenerateThumbnail { asset_id: i64 },
    Cancel { job_id: String },
}

pub struct JobSystem {
    sender: Sender<JobCommand>,
    db: Arc<Mutex<Database>>,
    app_handle: AppHandle,
    thumbnail_generator: Arc<ThumbnailGenerator>,
}

impl JobSystem {
    pub fn new(db: Arc<Mutex<Database>>, app_handle: AppHandle, cache_dir: PathBuf) -> Self {
        let (sender, receiver) = channel::<JobCommand>();
        let db_clone = db.clone();
        let app_handle_clone = app_handle.clone();
        let thumbnail_generator = Arc::new(ThumbnailGenerator::new(cache_dir));
        let thumbnail_gen_clone = thumbnail_generator.clone();
        thread::spawn(move || {
            Self::worker(db_clone, app_handle_clone, receiver, thumbnail_gen_clone);
        });
        Self { sender, db, app_handle, thumbnail_generator }
    }

    fn worker(db: Arc<Mutex<Database>>, app_handle: AppHandle, receiver: Receiver<JobCommand>, thumbnail_generator: Arc<ThumbnailGenerator>) {
        while let Ok(command) = receiver.recv() {
            match command {
                JobCommand::ScanLibrary { library_id } => {
                    let job_id = format!("scan_{}_{}", library_id, chrono::Utc::now().timestamp());
                    let _ = app_handle.emit("job_started", &job_id);

                    let db_guard = db.lock().unwrap();
                    let library_service = LibraryService::new(&db_guard);
                    match library_service.get_library(library_id) {
                        Ok(library) => {
                            let scanner = FileScanner::new(&db_guard);
                            match scanner.scan_library(library_id, &library.root_path) {
                                Ok(result) => {
                                    let _ = app_handle.emit("scan_progress", &result);
                                    let _ = app_handle.emit("job_completed", &job_id);
                                }
                                Err(e) => {
                                    let _ = app_handle.emit("job_failed", format!("{}: {}", job_id, e));
                                }
                            }
                        }
                        Err(e) => {
                            let _ = app_handle.emit("job_failed", format!("{}: {}", job_id, e));
                        }
                    }
                    drop(db_guard);
                }
                JobCommand::GenerateThumbnail { asset_id } => {
                    let job_id = format!("thumb_{}", asset_id);
                    let _ = app_handle.emit("job_started", &job_id);

                    let db_guard = db.lock().unwrap();
                    match db_guard.get_asset_by_id(asset_id) {
                        Ok(asset) => {
                            let source_path = std::path::Path::new(&asset.file_path);
                            let modified_at = asset.modified_at_fs.clone().unwrap_or_default();
                            match thumbnail_generator.generate_thumbnail(asset_id, source_path, &modified_at) {
                                Ok(thumb_path) => {
                                    let _ = db_guard.update_asset_thumbnail_status(asset_id, &thumb_path.to_string_lossy());
                                    let _ = app_handle.emit("job_completed", &job_id);
                                }
                                Err(e) => {
                                    let _ = app_handle.emit("job_failed", format!("{}: {}", job_id, e));
                                }
                            }
                        }
                        Err(e) => {
                            let _ = app_handle.emit("job_failed", format!("{}: {}", job_id, e));
                        }
                    }
                    drop(db_guard);
                }
                JobCommand::Cancel { job_id } => {
                    let _ = app_handle.emit("job_cancelled", &job_id);
                }
            }
        }
    }

    pub fn app_handle(&self) -> AppHandle {
        self.app_handle.clone()
    }

    pub fn queue_scan(&self, library_id: i64) {
        let _ = self.sender.send(JobCommand::ScanLibrary { library_id });
    }

    pub fn queue_thumbnail(&self, asset_id: i64) {
        let _ = self.sender.send(JobCommand::GenerateThumbnail { asset_id });
    }

    pub fn cancel(&self, job_id: &str) {
        let _ = self.sender.send(JobCommand::Cancel {
            job_id: job_id.to_string(),
        });
    }
}
