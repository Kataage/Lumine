import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppDialogProvider } from "../components/AppDialogProvider";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return {
    ...actual,
    getAssetCreativeContext: vi.fn(async () => ({
      works: [{ id: 1, title: "夏祭りフブキ", description: "夏祭りシリーズ", assetIds: [10, 11], assets: [], createdAt: "", updatedAt: "" }],
      groups: [{ id: 2, name: "seed variations", prompt: "1girl, yukata", negativePrompt: "", modelName: "ILXL", sampler: "euler", scheduler: "normal", steps: 28, cfgScale: 5, workflowJson: "", notes: "", assetIds: [10, 11], assets: [], createdAt: "", updatedAt: "" }],
      relations: [{ id: 3, parentAssetId: 9, parentFileName: "base.png", parentFilePath: "", childAssetId: 10, childFileName: "final.png", childFilePath: "", relationType: "inpaint", note: "face fix", createdAt: "" }],
    })),
    listWorks: vi.fn(async () => []),
    listGenerationGroups: vi.fn(async () => []),
    addAssetsToGenerationGroup: vi.fn(),
    addAssetsToWork: vi.fn(),
    createGenerationGroup: vi.fn(),
    createWork: vi.fn(),
    deleteAssetRelation: vi.fn(),
  };
});

import { CreativeContextPanel } from "../components/CreativeContextPanel";

describe("CreativeContextPanel", () => {
  it("作品・生成グループ・派生元を同じ詳細コンテキストで確認できる", async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><AppDialogProvider><CreativeContextPanel assetId={10} /></AppDialogProvider></QueryClientProvider>);

    expect(await screen.findByText("夏祭りフブキ")).toBeInTheDocument();
    expect(screen.getByText("seed variations")).toBeInTheDocument();
    expect(screen.getByText(/base\.png/)).toBeInTheDocument();
    expect(screen.getByText(/Inpaint/)).toBeInTheDocument();
  });
});
