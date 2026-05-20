use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use notify::{
    recommended_watcher, Event, EventKind, RecursiveMode, Watcher,
};

use crate::db::Database;
use crate::infrastructure::file_scanner::FileScanner;

pub struct FolderWatcher {
    watchers: Arc<Mutex<HashMap<i64, Arc<AtomicBool>>>>,
    db: Arc<Mutex<Database>>,
}

impl FolderWatcher {
    pub fn new(db: Arc<Mutex<Database>>) -> Self {
        Self {
            watchers: Arc::new(Mutex::new(HashMap::new())),
            db,
        }
    }

    pub fn start_watching(&self, library_id: i64, root_path: &str) -> Result<(), String> {
        let path = PathBuf::from(root_path);
        if !path.exists() || !path.is_dir() {
            return Err(format!("Path does not exist or is not a directory: {}", root_path));
        }

        {
            let mut watchers = self.watchers.lock().unwrap();
            if watchers.contains_key(&library_id) {
                return Ok(());
            }
            let cancel_flag = Arc::new(AtomicBool::new(false));
            watchers.insert(library_id, cancel_flag.clone());
        }

        let db = self.db.clone();
        let library_id_clone = library_id;
        let cancel_flag = {
            let watchers = self.watchers.lock().unwrap();
            watchers.get(&library_id).unwrap().clone()
        };

        std::thread::spawn(move || {
            let (tx, rx) = std::sync::mpsc::channel();

            let mut watcher = match recommended_watcher(tx) {
                Ok(w) => w,
                Err(e) => {
                    tracing::error!("Failed to create watcher for library {}: {}", library_id_clone, e);
                    return;
                }
            };

            if let Err(e) = watcher.watch(&path, RecursiveMode::Recursive) {
                tracing::error!("Failed to watch path for library {}: {}", library_id_clone, e);
                return;
            }

            tracing::info!("Started watching library {} at {}", library_id_clone, path.display());

            loop {
                if cancel_flag.load(Ordering::SeqCst) {
                    tracing::info!("Stopped watching library {}", library_id_clone);
                    break;
                }

                match rx.recv_timeout(std::time::Duration::from_secs(2)) {
                    Ok(Ok(event)) => {
                        if should_rescan(&event) {
                            tracing::info!(
                                "File change detected in library {}, triggering rescan",
                                library_id_clone
                            );

                            if let Ok(db_lock) = db.lock() {
                                let scanner = FileScanner::new(&db_lock);
                                if let Ok(root_path_str) = path.clone().into_os_string().into_string() {
                                    match scanner.scan_library(library_id_clone, &root_path_str) {
                                        Ok(result) => {
                                            tracing::info!(
                                                "Auto-rescan completed for library {}: +{} ={} errors={}",
                                                library_id_clone,
                                                result.added,
                                                result.unchanged,
                                                result.errors
                                            );
                                        }
                                        Err(e) => {
                                            tracing::error!("Auto-rescan failed for library {}: {}", library_id_clone, e);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    Ok(Err(e)) => {
                        tracing::error!("Watch error for library {}: {}", library_id_clone, e);
                    }
                    Err(_) => {
                        continue;
                    }
                }
            }
        });

        Ok(())
    }

    pub fn stop_watching(&self, library_id: i64) {
        if let Some(cancel_flag) = self.watchers.lock().unwrap().get(&library_id) {
            cancel_flag.store(true, Ordering::SeqCst);
        }
        self.watchers.lock().unwrap().remove(&library_id);
    }

    pub fn is_watching(&self, library_id: i64) -> bool {
        self.watchers
            .lock()
            .unwrap()
            .get(&library_id)
            .map(|f| !f.load(Ordering::SeqCst))
            .unwrap_or(false)
    }
}

fn should_rescan(event: &Event) -> bool {
    match event.kind {
        EventKind::Create(_) => true,
        EventKind::Modify(_) => true,
        EventKind::Remove(_) => true,
        _ => false,
    }
}
