use crate::jobs::JobSystem;
use std::sync::Arc;
use tauri::State;

#[tauri::command]
pub fn start_thumbnail_generation(
    job_system: State<'_, Arc<JobSystem>>,
    library_id: i64,
) -> Result<String, String> {
    Ok(job_system.start_thumbnail_generation(library_id))
}

#[tauri::command]
pub fn get_job_status(
    job_system: State<'_, Arc<JobSystem>>,
    job_id: String,
) -> Result<crate::jobs::JobStatus, String> {
    job_system
        .get_job_status(&job_id)
        .ok_or_else(|| format!("Job not found: {}", job_id))
}

#[tauri::command]
pub fn list_jobs(
    job_system: State<'_, Arc<JobSystem>>,
) -> Result<Vec<crate::jobs::JobStatus>, String> {
    Ok(job_system.list_jobs())
}

#[tauri::command]
pub fn cancel_job(
    job_system: State<'_, Arc<JobSystem>>,
    job_id: String,
) -> Result<(), String> {
    job_system.cancel(&job_id);
    Ok(())
}
