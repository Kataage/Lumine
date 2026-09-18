import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../wailsjs/runtime/runtime", () => ({
  EventsOn: vi.fn(),
  EventsOff: vi.fn(),
}));

import { getLocalImageUrl, offScanProgress, onScanProgress } from "../api/client";
import { EventsOff, EventsOn } from "../../wailsjs/runtime/runtime";

describe("getLocalImageUrl", () => {
  it("Windowsパスを壊さずURLエンコードする", () => {
    const path = "C:\\Users\\test\\image.png";
    expect(getLocalImageUrl(path)).toBe(`/local?path=${encodeURIComponent(path)}`);
  });

  it("POSIXパスをURLエンコードする", () => {
    const path = "/home/user/photos/img.jpg";
    expect(getLocalImageUrl(path)).toBe(`/local?path=${encodeURIComponent(path)}`);
  });

  it("日本語やURL特殊文字を安全に扱う", () => {
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

  it("scan:progressイベントを購読する", () => {
    const callback = vi.fn();
    onScanProgress(callback);
    expect(EventsOn).toHaveBeenCalledWith("scan:progress", expect.any(Function));
  });

  it("JSON契約どおりlibraryIdを含む進捗を渡す", () => {
    const onSpy = vi.mocked(EventsOn);
    const callback = vi.fn();
    onScanProgress(callback);

    const registeredCallback = onSpy.mock.calls[0][1];
    const progressData = {
      libraryId: 1,
      scannedCount: 100,
      addedCount: 10,
      updatedCount: 5,
      skippedCount: 80,
      failedCount: 5,
      isDone: false,
    };
    registeredCallback(progressData);

    expect(callback).toHaveBeenCalledWith(progressData);
  });

  it("購読を解除する", () => {
    offScanProgress();
    expect(EventsOff).toHaveBeenCalledWith("scan:progress");
  });
});
