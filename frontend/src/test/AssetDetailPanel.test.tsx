import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { AssetDTO } from "../api/client";

vi.mock("../components/MemoryImage", () => ({
  MemoryImage: ({ alt, priority }: { alt: string; priority?: string }) => (
    <div data-testid="memory-image" data-priority={priority}>{alt}</div>
  ),
}));

vi.mock("../components/ImageViewerModal", () => ({
  ImageViewerModal: () => <div data-testid="image-viewer-modal" />,
}));

vi.mock("../components/PostRecordModal", () => ({
  PostRecordModal: () => <div data-testid="post-record-modal" />,
}));

vi.mock("../api/client", () => ({
  getAssetDetail: vi.fn(),
  getPostRecordsByAsset: vi.fn(async () => []),
  listTags: vi.fn(async () => []),
  setAssetTags: vi.fn(async () => undefined),
  toggleAssetFavorite: vi.fn(async () => undefined),
  updateAssetColorLabel: vi.fn(async () => undefined),
  updateAssetNote: vi.fn(async () => undefined),
  updateAssetRating: vi.fn(async () => undefined),
  updateAssetStatus: vi.fn(async () => undefined),
}));

import { getAssetDetail } from "../api/client";
import { AssetDetailPanel } from "../components/AssetDetailPanel";

const freshlyListedAsset = {
  id: 101,
  libraryId: 1,
  folderPath: "C:\\images\\new",
  fileName: "freshly-scanned.png",
  filePath: "C:\\images\\new\\freshly-scanned.png",
  extension: ".png",
  fileSize: 2048,
  modifiedAtFs: "2026-09-04T07:00:00Z",
  width: 1920,
  height: 1080,
  thumbStatus: "none",
  rating: 0,
  statusLabel: "unsorted",
  isFavorite: false,
  iso: 0,
} as AssetDTO;

function renderPanel(asset: AssetDTO = freshlyListedAsset) {
  const client = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        retryDelay: 0,
      },
    },
  });

  return render(
    <QueryClientProvider client={client}>
      <AssetDetailPanel asset={asset} onClose={vi.fn()} />
    </QueryClientProvider>
  );
}

describe("AssetDetailPanel", () => {
  beforeEach(() => {
    vi.mocked(getAssetDetail).mockReset();
  });

  it("詳細APIが待機中でも一覧データから基本情報を即表示する", () => {
    vi.mocked(getAssetDetail).mockImplementation(() => new Promise(() => undefined));

    renderPanel();

    expect(screen.getByRole("heading", { name: "freshly-scanned.png" })).toBeInTheDocument();
    expect(screen.getByText("C:\\images\\new\\freshly-scanned.png")).toBeInTheDocument();
    expect(screen.getByTestId("memory-image")).toHaveTextContent("freshly-scanned.png");
    expect(screen.getByTestId("memory-image")).toHaveAttribute("data-priority", "high");
    expect(screen.getByTitle("詳細情報を読み込み中")).toBeInTheDocument();
  });

  it("詳細APIが失敗しても基本情報を消さずに残す", async () => {
    vi.mocked(getAssetDetail).mockRejectedValue(new Error("detail failed"));

    renderPanel();

    expect(screen.getByRole("heading", { name: "freshly-scanned.png" })).toBeInTheDocument();
    await screen.findByText(/基本情報は表示できています/);
    expect(screen.getByRole("heading", { name: "freshly-scanned.png" })).toBeInTheDocument();
    expect(screen.getByText("C:\\images\\new\\freshly-scanned.png")).toBeInTheDocument();
  });

  it("軽量DTOに欠損値があっても詳細パネル全体が落ちない", () => {
    vi.mocked(getAssetDetail).mockImplementation(() => new Promise(() => undefined));
    const partial = {
      ...freshlyListedAsset,
      extension: undefined,
      fileSize: undefined,
      width: undefined,
      height: undefined,
      statusLabel: undefined,
    } as unknown as AssetDTO;

    renderPanel(partial);

    expect(screen.getByRole("heading", { name: "freshly-scanned.png" })).toBeInTheDocument();
    expect(screen.getByText("取得中 / 不明")).toBeInTheDocument();
    expect(screen.getAllByText("不明").length).toBeGreaterThan(0);
  });

  it("詳細取得後はEXIFなどの追加情報を同じパネルへ反映する", async () => {
    vi.mocked(getAssetDetail).mockResolvedValue({
      ...freshlyListedAsset,
      cameraModel: "Test Camera",
      lensModel: "Test Lens",
    } as AssetDTO);

    renderPanel();

    await waitFor(() => expect(screen.getByText("Test Camera")).toBeInTheDocument());
    expect(screen.getByText("Test Lens")).toBeInTheDocument();
  });
});
