use crate::db::Database;
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
}

impl JobSystem {
    pub fn new(db: Arc<Mutex<Database>>, app_handle: AppHandle) -> Self {
        let (sender, receiver) = channel::<JobCommand>();
        let db_clone = db.clone();
        let app_handle_clone = app_handle.clone();
        thread::spawn(move || {
            Self::worker(db_clone, app_handle_clone, receiver);
        });
        Self { sender, db, app_handle }
    }

    fn worker(_db: Arc<Mutex<Database>>, app_handle: AppHandle, receiver: Receiver<JobCommand>) {
        while let Ok(command) = receiver.recv() {
            match command {
                JobCommand::ScanLibrary { library_id } => {
                    let job_id = format!("scan_{}_{}", library_id, chrono::Utc::now().timestamp());
                    let _ = app_handle.emit("job_started", &job_id);
                }
                JobCommand::GenerateThumbnail { asset_id } => {
                    let job_id = format!("thumb_{}", asset_id);
                    let _ = app_handle.emit("job_started", &job_id);
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