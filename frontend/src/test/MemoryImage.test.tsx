import { render, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../utils/imagePipeline", () => ({
  computeCoverCrop: vi.fn((sourceWidth: number, sourceHeight: number) => ({
    x: 0,
    y: 0,
    width: sourceWidth,
    height: sourceHeight,
  })),
  getCachedMemoryBitmap: vi.fn(() => null),
  loadMemoryBitmap: vi.fn(),
}));

vi.mock("../api/client", () => ({
  getLocalImageUrl: (path: string) => `/local?path=${encodeURIComponent(path)}`,
}));

import { getCachedMemoryBitmap, loadMemoryBitmap } from "../utils/imagePipeline";
import { MemoryImage } from "../components/MemoryImage";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function bitmap(width: number, height: number): ImageBitmap {
  return { width, height } as ImageBitmap;
}

describe("MemoryImage", () => {
  let drawImage: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.mocked(loadMemoryBitmap).mockReset();
    vi.mocked(getCachedMemoryBitmap).mockReset();
    vi.mocked(getCachedMemoryBitmap).mockReturnValue(null);
    drawImage = vi.fn();
    Object.defineProperty(globalThis, "createImageBitmap", {
      configurable: true,
      value: vi.fn(),
    });
    Object.defineProperty(HTMLCanvasElement.prototype, "getContext", {
      configurable: true,
      value: vi.fn(() => ({
        clearRect: vi.fn(),
        drawImage,
      })),
    });
  });

  it("表示サイズを大へ変更しても新しいデコード完了まで既存canvasを消さない", async () => {
    const larger = deferred<ImageBitmap>();
    vi.mocked(loadMemoryBitmap)
      .mockResolvedValueOnce(bitmap(180, 180))
      .mockImplementationOnce(() => larger.promise);

    const { container, rerender } = render(
      <MemoryImage filePath="C:\\images\\a.png" width={180} height={180} alt="a.png" />
    );
    const canvas = container.querySelector("canvas") as HTMLCanvasElement;

    await waitFor(() => expect(canvas.width).toBe(180));
    expect(canvas.height).toBe(180);

    rerender(<MemoryImage filePath="C:\\images\\a.png" width={260} height={260} alt="a.png" />);

    expect(canvas.style.width).toBe("260px");
    expect(canvas.style.height).toBe("260px");
    expect(canvas.width).toBe(180);
    expect(canvas.height).toBe(180);

    larger.resolve(bitmap(260, 260));
    await waitFor(() => expect(canvas.width).toBe(260));
    expect(canvas.height).toBe(260);
  });

  it("列組み替えでカードが再マウントされても既存メモリBitmapを即表示する", async () => {
    const exact = deferred<ImageBitmap>();
    vi.mocked(getCachedMemoryBitmap).mockReturnValue(bitmap(180, 180));
    vi.mocked(loadMemoryBitmap).mockImplementation(() => exact.promise);

    const { container } = render(
      <MemoryImage filePath="C:\\images\\a.png" width={260} height={260} alt="a.png" />
    );
    const canvas = container.querySelector("canvas") as HTMLCanvasElement;

    await waitFor(() => expect(canvas.width).toBe(260));
    expect(canvas.height).toBe(260);
    expect(drawImage).toHaveBeenCalled();

    exact.resolve(bitmap(260, 260));
    await waitFor(() => expect(drawImage.mock.calls.length).toBeGreaterThanOrEqual(2));
  });
});
