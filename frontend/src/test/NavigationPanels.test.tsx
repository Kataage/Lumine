import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppDialogProvider } from "../components/AppDialogProvider";

const api = vi.hoisted(() => ({
  removeLibrary: vi.fn(),
  listLibraries: vi.fn(),
  listTags: vi.fn(),
  deleteTag: vi.fn(),
}));

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return {
    ...actual,
    removeLibrary: api.removeLibrary,
    listLibraries: api.listLibraries,
    listTags: api.listTags,
    deleteTag: api.deleteTag,
  };
});

vi.mock("../App", () => ({
  useApp: () => ({
    state: {
      libraries: [{ id: 1, name: "Images", rootPath: "C:\\images", isEnabled: true }],
      selectedLibraryId: 1,
      selectedFolderPath: "",
      selectedAssets: new Set<number>(),
      detailOpen: false,
      detailAsset: null,
      filterTagIds: [],
    },
    setState: vi.fn(),
  }),
}));

import { LibrariesPanel, TagsPanel } from "../components/NavigationPanels";

function Wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}><AppDialogProvider>{children}</AppDialogProvider></QueryClientProvider>;
}

describe("NavigationPanels destructive UX", () => {
  beforeEach(() => {
    api.removeLibrary.mockReset().mockResolvedValue(undefined);
    api.listLibraries.mockReset().mockResolvedValue([]);
    api.listTags.mockReset().mockResolvedValue([{ id: 9, name: "test-tag", color: "#fff" }]);
    api.deleteTag.mockReset().mockResolvedValue(undefined);
  });

  it("ライブラリ解除をLumineダイアログで確認する", async () => {
    render(<LibrariesPanel scanProgress={{}} />, { wrapper: Wrapper });
    expect(screen.queryByRole("button", { name: "登録を解除…" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "管理" }));
    fireEvent.click(screen.getByRole("button", { name: "登録を解除…" }));
    expect(await screen.findByRole("dialog", { name: "ライブラリの登録を解除しますか？" })).toBeInTheDocument();
    expect(api.removeLibrary).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "登録を解除" }));
    await waitFor(() => expect(api.removeLibrary).toHaveBeenCalledWith(1));
  });

  it("タグ削除をLumineダイアログで確認する", async () => {
    render(<TagsPanel />, { wrapper: Wrapper });
    await screen.findByText("test-tag");
    expect(screen.queryByRole("button", { name: "削除" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "管理" }));
    fireEvent.click(screen.getByRole("button", { name: "削除" }));
    expect(await screen.findByRole("dialog", { name: "タグを削除しますか？" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "タグを削除" }));
    await waitFor(() => expect(api.deleteTag).toHaveBeenCalledWith(9));
  });
});
