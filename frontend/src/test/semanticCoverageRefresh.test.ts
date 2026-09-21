import { afterEach, describe, expect, it, vi } from "vitest";

import {
  createSemanticCoverageRefreshController,
  type SemanticCoverageSnapshot,
} from "../utils/semanticCoverageRefresh";

const COVERAGE: SemanticCoverageSnapshot = {
  ready: 10,
  total: 100,
  queued: 80,
  running: 5,
  failed: 2,
  stale: 3,
  unsupported: 0,
};

describe("semantic coverage refresh controller", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("coalesces many durable progress events into one refresh", async () => {
    vi.useFakeTimers();
    const refresh = vi.fn(async () => COVERAGE);
    const apply = vi.fn();
    const controller = createSemanticCoverageRefreshController({
      refresh,
      apply,
      delayMs: 250,
    });
    controller.setSession("session-1");

    for (let i = 0; i < 20; i += 1) controller.notify();
    expect(refresh).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(249);
    expect(refresh).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(refresh).toHaveBeenCalledWith("session-1");
    expect(apply).toHaveBeenCalledWith(COVERAGE);

    controller.dispose();
  });

  it("drops a stale in-flight response after the search session changes", async () => {
    vi.useFakeTimers();
    let resolveFirst: ((value: SemanticCoverageSnapshot) => void) | undefined;
    const refresh = vi.fn((sessionId: string) => {
      if (sessionId === "session-1") {
        return new Promise<SemanticCoverageSnapshot>((resolve) => {
          resolveFirst = resolve;
        });
      }
      return Promise.resolve({ ...COVERAGE, ready: 20 });
    });
    const apply = vi.fn();
    const controller = createSemanticCoverageRefreshController({
      refresh,
      apply,
      delayMs: 10,
    });

    controller.setSession("session-1");
    controller.notify();
    await vi.advanceTimersByTimeAsync(10);
    expect(refresh).toHaveBeenCalledWith("session-1");

    controller.setSession("session-2");
    resolveFirst?.({ ...COVERAGE, ready: 99 });
    await Promise.resolve();
    expect(apply).not.toHaveBeenCalled();

    controller.notify();
    await vi.advanceTimersByTimeAsync(10);
    await Promise.resolve();
    expect(refresh).toHaveBeenCalledWith("session-2");
    expect(apply).toHaveBeenLastCalledWith({ ...COVERAGE, ready: 20 });

    controller.dispose();
  });

  it("queues one trailing refresh when progress arrives during an in-flight refresh", async () => {
    vi.useFakeTimers();
    let resolveFirst: ((value: SemanticCoverageSnapshot) => void) | undefined;
    let calls = 0;
    const refresh = vi.fn(() => {
      calls += 1;
      if (calls === 1) {
        return new Promise<SemanticCoverageSnapshot>((resolve) => {
          resolveFirst = resolve;
        });
      }
      return Promise.resolve({ ...COVERAGE, ready: 11 });
    });
    const apply = vi.fn();
    const controller = createSemanticCoverageRefreshController({
      refresh,
      apply,
      delayMs: 50,
    });
    controller.setSession("session-1");

    controller.notify();
    await vi.advanceTimersByTimeAsync(50);
    controller.notify();
    controller.notify();

    resolveFirst?.(COVERAGE);
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(50);
    await Promise.resolve();

    expect(refresh).toHaveBeenCalledTimes(2);
    expect(apply).toHaveBeenCalledTimes(2);

    controller.dispose();
  });
});
