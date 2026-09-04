import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { AssetDTO } from "../api/client";

vi.mock("../components/MemoryImage", () => ({
  MemoryImage: ({ alt }: { alt: string }) => <div data-testid="memory-image">{alt}</div>,
}));

vi.mock("../api/client", () => ({
  getAssetDetail: vi.fn(() => new Promise<null>(() => undefined)),
  getPostRecordsByAsset: vi.fn(async () => []),
  listTags: vi.fn(async () => []),
  setAssetTags: vi.fn(async () => undefined),
  toggleAssetFavorite: vi.fn(async () => undefined),
  updateAssetColorLabel: vi.fn(async () => undefined),
  updateAssetNote: vi.fn(async () => undefined),
  updateAssetRating: vi.fn(async () => undefined),
  updateAssetStatus: vi.fn(async () => undefined),
}));

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

describe("AssetDetailPanel", () => {
  it("詳細APIの応答待ちでも一覧データから基本情報をすぐ表示する", () => {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    render(
      <QueryClientProvider client={client}>
        <AssetDetailPanel asset={freshlyListedAsset} onClose={vi.fn()} />
      </QueryClientProvider>
    );

    expect(screen.getByText("freshly-scanned.png")).toBeInTheDocument();
    expect(screen.getByText("C:\\images\\new\\freshly-scanned.png")).toBeInTheDocument();
    expect(screen.getByTestId("memory-image")).toHaveTextContent("freshly-scanned.png");
    expect(screen.getByTitle("詳細情報を読み込み中")).toBeInTheDocument();
  });
});
