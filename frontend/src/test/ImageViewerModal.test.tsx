import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { AssetDTO } from "../api/client";

vi.mock("../components/MemoryImage", () => ({
  MemoryImage: ({ alt, maxDecodePixels, priority }: { alt: string; maxDecodePixels?: number; priority?: string }) => (
    <div
      data-testid="viewer-preview"
      data-max-decode-pixels={maxDecodePixels}
      data-priority={priority}
    >
      {alt}
    </div>
  ),
}));

vi.mock("../api/client", () => ({
  getLocalImageUrl: (path: string) => `/local?path=${encodeURIComponent(path)}`,
}));

import { ImageViewerModal } from "../components/ImageViewerModal";

const asset = {
  id: 1,
  libraryId: 1,
  folderPath: "C:\\images",
  fileName: "viewer-test.png",
  filePath: "C:\\images\\viewer-test.png",
  extension: ".png",
  fileSize: 1024,
  modifiedAtFs: "2026-09-04T07:00:00Z",
  width: 2000,
  height: 1200,
  thumbStatus: "none",
  rating: 0,
  statusLabel: "unsorted",
  isFavorite: false,
  iso: 0,
} as AssetDTO;

function pointerEvent(type: string, values: Record<string, number>) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperties(event, Object.fromEntries(
    Object.entries(values).map(([key, value]) => [key, { value, enumerable: true }])
  ));
  return event;
}

describe("ImageViewerModal", () => {
  it("全画面プレビューを高優先度かつ過大でないBitmapサイズで要求する", () => {
    render(<ImageViewerModal asset={asset} onClose={vi.fn()} />);

    const preview = screen.getByTestId("viewer-preview");
    expect(preview).toHaveAttribute("data-priority", "high");
    expect(preview).toHaveAttribute("data-max-decode-pixels", "4000000");
  });

  it("元画像の読み込み前でも拡大後のドラッグで表示位置を移動できる", async () => {
    render(<ImageViewerModal asset={asset} onClose={vi.fn()} />);

    const transform = screen.getByTestId("viewer-transform");
    const stage = screen.getByTestId("viewer-stage");

    fireEvent.click(screen.getByRole("button", { name: "拡大" }));
    expect(transform).toHaveStyle({ transform: "translate3d(0px, 0px, 0) scale(1.5)" });

    fireEvent(stage, pointerEvent("pointerdown", {
      pointerId: 7,
      clientX: 100,
      clientY: 100,
      button: 0,
    }));
    fireEvent(stage, pointerEvent("pointermove", {
      pointerId: 7,
      clientX: 145,
      clientY: 130,
      button: 0,
    }));

    await waitFor(() => {
      expect(transform.getAttribute("style")).toContain("translate3d(45px, 30px, 0)");
    });

    fireEvent(stage, pointerEvent("pointerup", {
      pointerId: 7,
      clientX: 145,
      clientY: 130,
      button: 0,
    }));
  });

  it("全体表示でズームと移動量をリセットする", async () => {
    render(<ImageViewerModal asset={asset} onClose={vi.fn()} />);

    const transform = screen.getByTestId("viewer-transform");
    const stage = screen.getByTestId("viewer-stage");
    fireEvent.click(screen.getByRole("button", { name: "拡大" }));
    fireEvent(stage, pointerEvent("pointerdown", { pointerId: 2, clientX: 10, clientY: 10, button: 0 }));
    fireEvent(stage, pointerEvent("pointermove", { pointerId: 2, clientX: 30, clientY: 40, button: 0 }));

    fireEvent.click(screen.getByRole("button", { name: "全体" }));

    await waitFor(() => {
      expect(transform).toHaveStyle({ transform: "translate3d(0px, 0px, 0) scale(1)" });
    });
  });
});
