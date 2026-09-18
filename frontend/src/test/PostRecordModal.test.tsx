import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  targets: [] as Array<{ id: number; name: string; kind: string }>,
  accounts: [] as Array<{ id: number; postTargetId: number; displayName: string; accountIdentifier: string; isActive: boolean }>,
  createPostTarget: vi.fn(),
  createPostAccount: vi.fn(),
  createPostRecord: vi.fn(),
  getAssetDetail: vi.fn(),
}));

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return {
    ...actual,
    listPostTargets: vi.fn(async () => [...api.targets]),
    listPostAccounts: vi.fn(async () => [...api.accounts]),
    createPostTarget: api.createPostTarget,
    createPostAccount: api.createPostAccount,
    createPostRecord: api.createPostRecord,
    getAssetDetail: api.getAssetDetail,
  };
});

import { PostRecordModal } from "../components/PostRecordModal";

describe("PostRecordModal", () => {
  beforeEach(() => {
    api.targets.splice(0);
    api.accounts.splice(0);
    api.createPostTarget.mockReset();
    api.createPostAccount.mockReset();
    api.createPostRecord.mockReset();
    api.getAssetDetail.mockReset();
    api.getAssetDetail.mockImplementation(async (id: number) => ({ id, fileName: `image-${id}.png`, filePath: `C:\\images\\image-${id}.png` }));
  });

  it("設定済みなら投稿内容をすぐ編集でき、Pixiv固有項目も表示する", async () => {
    api.targets.push({ id: 1, name: "Pixiv", kind: "pixiv" });
    api.accounts.push({ id: 2, postTargetId: 1, displayName: "メイン", accountIdentifier: "@example", isActive: true });

    render(<PostRecordModal assetIds={[1]} onClose={vi.fn()} />);

    await waitFor(() => expect(screen.getByLabelText("投稿先")).toHaveValue("1"));
    expect(screen.getByLabelText("アカウント")).toHaveValue("2");
    expect(screen.getByLabelText("タイトル")).toBeInTheDocument();
    expect(screen.getByLabelText("キャプション / 詳細")).toBeInTheDocument();
    expect(screen.getByLabelText("年齢制限")).toHaveValue("全年齢");
    expect(screen.getByText("AI生成作品")).toBeInTheDocument();
    expect(screen.getByText("image-1.png")).toBeInTheDocument();
  });

  it("複数画像の投稿順を変更できる", async () => {
    api.targets.push({ id: 1, name: "X", kind: "twitter" });
    api.accounts.push({ id: 2, postTargetId: 1, displayName: "メイン", accountIdentifier: "", isActive: true });

    render(<PostRecordModal assetIds={[101, 202]} onClose={vi.fn()} />);
    await screen.findByText("image-101.png");

    const downButtons = screen.getAllByRole("button", { name: "↓" });
    fireEvent.click(downButtons[0]);

    const rows = screen.getAllByText(/image-(101|202)\.png/);
    expect(rows[0]).toHaveTextContent("image-202.png");
    expect(rows[1]).toHaveTextContent("image-101.png");
  });

  it("Pixivのタイトル・本文・タグ・固有設定を投稿スナップショットとして保存する", async () => {
    api.targets.push({ id: 1, name: "Pixiv", kind: "pixiv" });
    api.accounts.push({ id: 2, postTargetId: 1, displayName: "メイン", accountIdentifier: "", isActive: true });
    api.createPostRecord.mockResolvedValue({ id: 10 });
    const onClose = vi.fn();

    render(<PostRecordModal assetIds={[101]} onClose={onClose} />);
    await waitFor(() => expect(screen.getByLabelText("投稿先")).toHaveValue("1"));

    fireEvent.change(screen.getByLabelText("タイトル"), { target: { value: "夏祭りフブキ" } });
    fireEvent.change(screen.getByLabelText("キャプション / 詳細"), { target: { value: "浴衣の作品です" } });
    const tagInput = screen.getByPlaceholderText("入力して Enter（複数可）");
    fireEvent.change(tagInput, { target: { value: "白上フブキ" } });
    fireEvent.keyDown(tagInput, { key: "Enter" });
    fireEvent.change(screen.getByLabelText("年齢制限"), { target: { value: "R-18" } });
    fireEvent.click(screen.getByRole("button", { name: "公開記録を保存" }));

    await waitFor(() => expect(api.createPostRecord).toHaveBeenCalledWith(expect.objectContaining({
      assetIds: [101],
      targetId: 1,
      accountId: 2,
      title: "夏祭りフブキ",
      body: "浴衣の作品です",
      hashtags: "白上フブキ",
      platformMetadataJson: JSON.stringify({ ageRestriction: "R-18", aiGenerated: true }),
      externalPostId: "",
      externalUrl: "",
      publishedAt: expect.any(String),
    })));
    expect(onClose).toHaveBeenCalledOnce();
  });
});