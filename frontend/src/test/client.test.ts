import { describe, it, expect, vi, beforeEach } from "vitest";

vi.mock("../../wailsjs/runtime/runtime", () => ({
  EventsOn: vi.fn(),
  EventsOff: vi.fn(),
}));

import { getLocalImageUrl, onScanProgress, offScanProgress } from "../api/client";
import { EventsOn, EventsOff } from "../../wailsjs/runtime/runtime";

describe("getLocalImageUrl", () => {
  it("converts backslash paths to forward slashes", () => {
    expect(getLocalImageUrl("C:\\Users\\test\\image.png")).toBe(
      "/local/C:/Users/test/image.png"
    );
  });

  it("preserves forward slash paths", () => {
    expect(getLocalImageUrl("/home/user/photos/img.jpg")).toBe(
      "/local//home/user/photos/img.jpg"
    );
  });

  it("handles mixed slashes", () => {
    expect(getLocalImageUrl("C:/Users\\test/mixed.png")).toBe(
      "/local/C:/Users/test/mixed.png"
    );
  });

  it("handles simple filename", () => {
    expect(getLocalImageUrl("photo.png")).toBe("/local/photo.png");
  });
});

describe("onScanProgress / offScanProgress", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("calls EventsOn with scan:progress", () => {
    const cb = vi.fn();
    onScanProgress(cb);

    expect(EventsOn).toHaveBeenCalledWith("scan:progress", expect.any(Function));
  });

  it("callback receives ScanProgress data", () => {
    const onSpy = vi.mocked(EventsOn);
    const cb = vi.fn();
    onScanProgress(cb);

    const registeredCallback = onSpy.mock.calls[0][1];
    const progressData = {
      libraryID: 1,
      scannedCount: 100,
      addedCount: 10,
      updatedCount: 5,
      skippedCount: 80,
      failedCount: 5,
      isDone: false,
    };
    registeredCallback(progressData);

    expect(cb).toHaveBeenCalledWith(progressData);
  });

  it("offScanProgress calls EventsOff", () => {
    offScanProgress();

    expect(EventsOff).toHaveBeenCalledWith("scan:progress");
  });
});
