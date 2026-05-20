use crate::jobs::JobSystem;
use tauri::State;

#[tauri::command]
pub fn cancel_job(job_system: State<'_, JobSystem>, job_id: String) -> Result<(), String> {
    job_system.cancel(&job_id);
    Ok(())
}
