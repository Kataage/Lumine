import { describe, it, expect, vi, beforeEach } from "vitest";

vi.mock("../../wailsjs/runtime/runtime", () => ({
  EventsOn: vi.fn(),
  EventsOff: vi.fn(),
}));

import { getLocalImageUrl, onScanProgress, offScanProgress } from "../api/client";
import { EventsOn, EventsOff } from "../../wailsjs/runtime/runtime";

describe("getLocalImageUrl", () => {
  it("encodes a Windows path without losing drive letters or separators", () => {
    const path = "C:\\Users\\test\\image.png";
    expect(getLocalImageUrl(path)).toBe(`/local?path=${encodeURIComponent(path)}`);
  });

  it("encodes POSIX paths", () => {
    const path = "/home/user/photos/img.jpg";
    expect(getLocalImageUrl(path)).toBe(`/local?path=${encodeURIComponent(path)}`);
  });

  it("protects URL-significant and Unicode filename characters", () => {
    const path = "C:\\画像\\a #1?.png";
    const url = getLocalImageUrl(path);
    expect(url).toBe(`/local?path=${encodeURIComponent(path)}`);
    expect(url).not.toContain("#1?");
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
