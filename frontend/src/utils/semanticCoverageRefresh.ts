export interface SemanticCoverageSnapshot {
  ready: number;
  total: number;
  queued: number;
  running: number;
  failed: number;
  stale: number;
}

interface SemanticCoverageRefreshControllerOptions {
  refresh: (sessionId: string) => Promise<SemanticCoverageSnapshot>;
  apply: (coverage: SemanticCoverageSnapshot) => void;
  delayMs?: number;
}

export interface SemanticCoverageRefreshController {
  setSession(sessionId: string): void;
  notify(): void;
  dispose(): void;
}

// Coalesces per-image durable progress events into a bounded refresh rate.
// Session generations prevent a slow response for an old search from
// overwriting coverage for a newer query.
export function createSemanticCoverageRefreshController(
  options: SemanticCoverageRefreshControllerOptions,
): SemanticCoverageRefreshController {
  const delayMs = Math.max(0, options.delayMs ?? 500);
  let sessionId = "";
  let generation = 0;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let inFlight = false;
  let dirty = false;
  let disposed = false;

  const clearTimer = () => {
    if (timer != null) {
      clearTimeout(timer);
      timer = null;
    }
  };

  const schedule = () => {
    if (disposed || !sessionId || timer != null) return;
    timer = setTimeout(() => {
      timer = null;
      void run();
    }, delayMs);
  };

  const run = async () => {
    if (disposed || !sessionId) return;
    if (inFlight) {
      dirty = true;
      return;
    }

    const requestedSession = sessionId;
    const requestedGeneration = generation;
    inFlight = true;
    dirty = false;
    try {
      const coverage = await options.refresh(requestedSession);
      if (
        !disposed &&
        requestedGeneration === generation &&
        requestedSession === sessionId
      ) {
        options.apply(coverage);
      }
    } finally {
      inFlight = false;
      if (dirty && !disposed && sessionId) {
        dirty = false;
        schedule();
      }
    }
  };

  return {
    setSession(nextSessionId: string) {
      const next = nextSessionId.trim();
      if (next === sessionId) return;
      sessionId = next;
      generation += 1;
      dirty = false;
      clearTimer();
    },
    notify() {
      if (disposed || !sessionId) return;
      if (inFlight) {
        dirty = true;
        return;
      }
      schedule();
    },
    dispose() {
      disposed = true;
      generation += 1;
      dirty = false;
      clearTimer();
    },
  };
}
