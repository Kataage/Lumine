import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../wailsjs/runtime/runtime", () => ({
  EventsEmit: vi.fn(),
}));

import { EventsEmit } from "../../wailsjs/runtime/runtime";
import {
  beginViewerForegroundWork,
  isViewerForegroundActive,
  markViewerInteraction,
} from "../utils/viewerPerformance";

describe("viewer foreground activity", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.mocked(EventsEmit).mockClear();
  });

  afterEach(() => {
    vi.runAllTimers();
    vi.useRealTimers();
  });

  it("scroll activity returns idle after the intended grace window", () => {
    markViewerInteraction();

    expect(isViewerForegroundActive()).toBe(true);
    expect(EventsEmit).toHaveBeenLastCalledWith("viewer:activity", true);

    vi.advanceTimersByTime(349);
    expect(isViewerForegroundActive()).toBe(true);

    vi.advanceTimersByTime(1);
    expect(isViewerForegroundActive()).toBe(false);
    expect(EventsEmit).toHaveBeenLastCalledWith("viewer:activity", false);
  });

  it("only currently useful foreground work extends the active period", () => {
    const release = beginViewerForegroundWork();
    markViewerInteraction();

    vi.advanceTimersByTime(500);
    expect(isViewerForegroundActive()).toBe(true);

    release();
    vi.advanceTimersByTime(349);
    expect(isViewerForegroundActive()).toBe(true);

    vi.advanceTimersByTime(1);
    expect(isViewerForegroundActive()).toBe(false);
    expect(EventsEmit).toHaveBeenLastCalledWith("viewer:activity", false);
  });

  it("releasing stale foreground work starts the idle transition immediately", () => {
    const releaseStale = beginViewerForegroundWork();
    expect(isViewerForegroundActive()).toBe(true);

    releaseStale();
    vi.advanceTimersByTime(350);

    expect(isViewerForegroundActive()).toBe(false);
    expect(vi.mocked(EventsEmit).mock.calls).toEqual([
      ["viewer:activity", true],
      ["viewer:activity", false],
    ]);
  });
});
