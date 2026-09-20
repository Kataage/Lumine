import { EventsEmit } from "../../wailsjs/runtime/runtime";

const VIEWER_ACTIVITY_EVENT = "viewer:activity";
const IDLE_GRACE_MS = 350;

let active = false;
let foregroundWork = 0;
let idleTimer: number | null = null;

function emitActive(next: boolean): void {
  if (active === next) return;
  active = next;
  try {
    EventsEmit(VIEWER_ACTIVITY_EVENT, next);
  } catch {
    // Unit tests and browser-only tooling do not provide the Wails runtime.
  }
}

function cancelIdleTimer(): void {
  if (idleTimer == null || typeof window === "undefined") return;
  window.clearTimeout(idleTimer);
  idleTimer = null;
}

function scheduleIdle(): void {
  cancelIdleTimer();
  if (foregroundWork > 0 || typeof window === "undefined") return;
  idleTimer = window.setTimeout(() => {
    idleTimer = null;
    if (foregroundWork === 0) emitActive(false);
  }, IDLE_GRACE_MS);
}

export function beginViewerForegroundWork(): () => void {
  cancelIdleTimer();
  foregroundWork += 1;
  emitActive(true);

  let released = false;
  return () => {
    if (released) return;
    released = true;
    foregroundWork = Math.max(0, foregroundWork - 1);
    if (foregroundWork === 0) scheduleIdle();
  };
}

export function markViewerInteraction(): void {
  cancelIdleTimer();
  emitActive(true);
  scheduleIdle();
}
