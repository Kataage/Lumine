import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { AppDialogProvider } from "../components/AppDialogProvider";

const api = vi.hoisted(() => ({
  records: [] as Array<{
    id: number;
    title: string;
    body: string;
    hashtags: string;
    platformMetadataJson: string;
    status: string;
    publishedAt?: string;
    createdAt: string;
    updatedAt: string;
    assetIds: number[];
    assets: Array<{ id: number; fileName: string; filePath: string }>;
    targetId: number;
    targetName: string;
    targetKind: string;
    accountId: number;
    accountDisplay: string;
    accountIdentifier: string;
    externalPostId?: string;
    externalUrl?: string;
  }>,
  targets: [] as Array<{ id: number; name: string; kind: string }>,
  accounts: [] as Array<{ id: number; postTargetId: number; displayName: string; accountIdentifier: string; isActive: boolean }>,
  createPostTarget: vi.fn(),
  createPostAccount: vi.fn(),
  deletePost: vi.fn(),
  deletePostTarget: vi.fn(),
  deletePostAccount: vi.fn(),
}));

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return {
    ...actual,
    listPostRecords: vi.fn(async () => [...api.records]),
    listPostTargets: vi.fn(async () => [...api.targets]),
    listPostAccounts: vi.fn(async () => [...api.accounts]),
    createPostTarget: api.createPostTarget,
    createPostAccount: api.createPostAccount,
    deletePost: api.deletePost,
    deletePostTarget: api.deletePostTarget,
    deletePostAccount: api.deletePostAccount,
  };
});

import { PostRecordsPanel } from "../components/PostRecordsPanel";

function Wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}><AppDialogProvider>{children}</AppDialogProvider></QueryClientProvider>;
}

function addRichRecord() {
  api.records.push({
    id: 1,
    title: "夏祭りフブキ",
    body: "浴衣で夏祭りに行った作品です。",
    hashtags: "白上フブキ\n浴衣\nホロライブ",
    platformMetadataJson: JSON.stringify({ aiGenerated: true, ageRestriction: "R-18" }),
    status: "published",
    publishedAt: "2026-09-17T10:00:00Z",
    createdAt: "2026-09-17T10:00:00Z",
    updatedAt: "2026-09-17T10:00:00Z",
    assetIds: [101, 102],
    assets: [
      { id: 101, fileName: "final-01.png", filePath: "C:\\images\\final-01.png" },
      { id: 102, fileName: "final-02.png", filePath: "C:\\images\\final-02.png" },
    ],
    targetId: 11,
    targetName: "Pixiv",
    targetKind: "pixiv",
    accountId: 22,
    accountDisplay: "メイン",
    accountIdentifier: "@example",
    externalPostId: "123456",
    externalUrl: "https://www.pixiv.net/artworks/123456",
  });
}

describe("PostRecordsPanel", () => {
  beforeEach(() => {
    api.records.splice(0);
    api.targets.splice(0);
    api.accounts.splice(0);
    api.createPostTarget.mockReset();
    api.createPostAccount.mockReset();
    api.deletePost.mockReset().mockResolvedValue(undefined);
    api.deletePostTarget.mockReset().mockResolvedValue(undefined);
    api.deletePostAccount.mockReset().mockResolvedValue(undefined);
    api.createPostTarget.mockImplementation(async (name: string, kind: string) => {
      const target = { id: 11, name, kind };
      api.targets.push(target);
      return target;
    });
    api.createPostAccount.mockImplementation(async (targetId: number, displayName: string, accountIdentifier: string) => {
      const account = { id: 22, postTargetId: targetId, displayName, accountIdentifier, isActive: true };
      api.accounts.push(account);
      return account;
    });
  });

  it("公開時のタイトル・本文・タグ・画像順・Pixiv設定を一覧から確認できる", async () => {
    api.targets.push({ id: 11, name: "Pixiv", kind: "pixiv" });
    api.accounts.push({ id: 22, postTargetId: 11, displayName: "メイン", accountIdentifier: "@example", isActive: true });
    addRichRecord();

    render(<PostRecordsPanel />, { wrapper: Wrapper });

    expect(await screen.findByText("夏祭りフブキ")).toBeInTheDocument();
    expect(screen.getByText("浴衣で夏祭りに行った作品です。")).toBeInTheDocument();
    expect(screen.getByText("#白上フブキ")).toBeInTheDocument();
    expect(screen.getByText("AI生成")).toBeInTheDocument();
    expect(screen.getByText("R-18")).toBeInTheDocument();
    expect(screen.getByText(/final-01\.png/)).toBeInTheDocument();
    expect(screen.getByText(/final-02\.png/)).toBeInTheDocument();
    expect(screen.getByText("https://www.pixiv.net/artworks/123456")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "投稿先設定" })).toHaveAttribute("aria-expanded", "false");
  });

  it("公開記録の削除にブラウザconfirmではなくLumineの確認ダイアログを使う", async () => {
    api.targets.push({ id: 11, name: "Pixiv", kind: "pixiv" });
    api.accounts.push({ id: 22, postTargetId: 11, displayName: "メイン", accountIdentifier: "@example", isActive: true });
    addRichRecord();

    render(<PostRecordsPanel />, { wrapper: Wrapper });
    await screen.findByText("夏祭りフブキ");
    fireEvent.click(screen.getByRole("button", { name: "削除" }));

    expect(await screen.findByRole("dialog", { name: "公開記録を削除しますか？" })).toBeInTheDocument();
    expect(api.deletePost).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "記録を削除" }));
    await waitFor(() => expect(api.deletePost).toHaveBeenCalledWith(1));
  });

  it("投稿先とアカウントは必要な時だけ設定欄を開いて登録できる", async () => {
    render(<PostRecordsPanel />, { wrapper: Wrapper });

    const targetInput = await screen.findByLabelText("投稿先名");
    fireEvent.change(targetInput, { target: { value: "Pixiv" } });
    fireEvent.click(screen.getAllByRole("button", { name: "追加" })[0]);
    await waitFor(() => expect(api.createPostTarget).toHaveBeenCalledWith("Pixiv", "pixiv"));

    await waitFor(() => expect(screen.getByLabelText("アカウント表示名")).not.toBeDisabled());
    fireEvent.change(screen.getByLabelText("アカウント表示名"), { target: { value: "メイン" } });
    fireEvent.change(screen.getByLabelText("アカウントID"), { target: { value: "@example" } });
    fireEvent.click(screen.getAllByRole("button", { name: "追加" })[1]);
    await waitFor(() => expect(api.createPostAccount).toHaveBeenCalledWith(11, "メイン", "@example"));
  });
});