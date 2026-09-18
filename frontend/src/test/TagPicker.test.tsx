import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { TagDTO } from "../api/client";

vi.mock("../api/client", () => ({
  createTag: vi.fn(),
  setAssetTags: vi.fn(async () => undefined),
}));

import { createTag, setAssetTags } from "../api/client";
import { TagPicker } from "../components/TagPicker";

function renderPicker(tags: TagDTO[], assignedTags: TagDTO[] = [], onAssignedTagsChange = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <TagPicker
        assetId={42}
        tags={tags}
        assignedTags={assignedTags}
        onAssignedTagsChange={onAssignedTagsChange}
      />
    </QueryClientProvider>
  );
  return onAssignedTagsChange;
}

function makeTag(id: number, name: string): TagDTO {
  return { id, name, color: "#6366f1" } as TagDTO;
}

describe("TagPicker", () => {
  beforeEach(() => {
    vi.mocked(createTag).mockReset();
    vi.mocked(setAssetTags).mockClear();
  });

  it("大量タグでは候補を80件に制限し、検索でそれ以外へ到達できる", async () => {
    const tags = Array.from({ length: 120 }, (_, index) => makeTag(index + 1, `tag-${String(index + 1).padStart(3, "0")}`));
    renderPicker(tags, [tags[0]]);

    expect(screen.getByText("未付与 119件")).toBeInTheDocument();
    expect(screen.getByText("上位80件を表示")).toBeInTheDocument();
    expect(screen.queryByText("tag-120")).not.toBeInTheDocument();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("タグを検索・追加"), "tag-120");

    expect(screen.getByText("候補 1件")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /tag-120/ })).toBeInTheDocument();
  });

  it("検索語から新規タグを作成して同じ画像へ即付与する", async () => {
    const assigned = makeTag(1, "existing");
    const created = makeTag(99, "new-tag");
    const onAssignedTagsChange = vi.fn();
    vi.mocked(createTag).mockResolvedValue(created);
    renderPicker([assigned], [assigned], onAssignedTagsChange);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText("タグを検索・追加"), "new-tag");
    await user.click(screen.getByRole("button", { name: "＋ 「new-tag」を新規作成して付与" }));

    await waitFor(() => expect(createTag).toHaveBeenCalledWith("new-tag", "#6366f1"));
    expect(setAssetTags).toHaveBeenCalledWith(42, [1, 99]);
    expect(onAssignedTagsChange).toHaveBeenCalledWith([assigned, created]);
  });
});
