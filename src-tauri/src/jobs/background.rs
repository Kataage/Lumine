use crate::db::Database;
use anyhow::Result;
use std::sync::mpsc::{channel, Receiver, Sender};
use std::sync::Mutex;
use std::thread;
use tauri::{AppHandle, Emitter};

pub enum JobCommand {
    ScanLibrary { library_id: i64 },
    GenerateThumbnail { asset_id: i64 },
    Cancel { job_id: String },
}

pub struct JobSystem {
    sender: Sender<JobCommand>,
}

impl JobSystem {
    pub fn new(db: std::sync::Arc<Database>, app_handle: AppHandle) -> Self {
        let (sender, receiver) = channel::<JobCommand>();
        let db_clone = db.clone();
        thread::spawn(move || {
            Self::worker(db_clone, app_handle, receiver);
        });
        Self { sender }
    }

    fn worker(db: std::sync::Arc<Database>, app_handle: AppHandle, receiver: Receiver<JobCommand>) {
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