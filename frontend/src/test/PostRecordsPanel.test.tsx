import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

const api = vi.hoisted(() => ({
  records: [] as Array<{
    id: number;
    title: string;
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
  }>,
  targets: [] as Array<{ id: number; name: string; kind: string }>,
  accounts: [] as Array<{ id: number; postTargetId: number; displayName: string; accountIdentifier: string; isActive: boolean }>,
  createPostTarget: vi.fn(),
  createPostAccount: vi.fn(),
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
  };
});

import { PostRecordsPanel } from "../components/PostRecordsPanel";

function Wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe("PostRecordsPanel", () => {
  beforeEach(() => {
    api.records.splice(0);
    api.targets.splice(0);
    api.accounts.splice(0);
    api.createPostTarget.mockReset();
    api.createPostAccount.mockReset();
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

  it("設定済みなら記録一覧を主表示にして設定を畳む", async () => {
    api.targets.push({ id: 11, name: "Pixiv", kind: "pixiv" });
    api.accounts.push({ id: 22, postTargetId: 11, displayName: "メイン", accountIdentifier: "@example", isActive: true });
    api.records.push({
      id: 1,
      title: "作品投稿",
      status: "published",
      publishedAt: "2026-09-17T10:00:00Z",
      createdAt: "2026-09-17T10:00:00Z",
      updatedAt: "2026-09-17T10:00:00Z",
      assetIds: [101],
      assets: [{ id: 101, fileName: "image.png", filePath: "C:\\images\\image.png" }],
      targetId: 11,
      targetName: "Pixiv",
      targetKind: "pixiv",
      accountId: 22,
      accountDisplay: "メイン",
      accountIdentifier: "@example",
      externalPostId: "https://example.com/post/1",
    });

    render(<PostRecordsPanel />, { wrapper: Wrapper });

    expect(await screen.findByText("作品投稿")).toBeInTheDocument();
    expect(screen.getByText("https://example.com/post/1")).toBeInTheDocument();
    expect(screen.queryByText("使い方は3ステップ")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "投稿先設定" })).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByLabelText("投稿先名")).not.toBeInTheDocument();
  });

  it("未設定なら設定欄を自動で開き入力不足を表示する", async () => {
    render(<PostRecordsPanel />, { wrapper: Wrapper });

    await screen.findByLabelText("投稿先名");
    fireEvent.click(screen.getAllByRole("button", { name: "追加" })[0]);
    expect(await screen.findByRole("alert")).toHaveTextContent("投稿先名を入力してください");
  });

  it("投稿先からアカウントまで画面上で連続して登録できる", async () => {
    render(<PostRecordsPanel />, { wrapper: Wrapper });
    await screen.findByLabelText("投稿先名");

    fireEvent.change(screen.getByLabelText("投稿先名"), { target: { value: "Pixiv" } });
    fireEvent.click(screen.getAllByRole("button", { name: "追加" })[0]);
    expect(await screen.findByRole("status")).toHaveTextContent("Pixiv を追加しました");
    expect(api.createPostTarget).toHaveBeenCalledWith("Pixiv", "pixiv");

    await waitFor(() => expect(screen.getByLabelText("アカウント表示名")).not.toBeDisabled());
    fireEvent.change(screen.getByLabelText("アカウント表示名"), { target: { value: "メイン" } });
    fireEvent.change(screen.getByLabelText("アカウントID"), { target: { value: "@example" } });
    fireEvent.click(screen.getAllByRole("button", { name: "追加" })[1]);

    expect(await screen.findByRole("status")).toHaveTextContent("メイン を追加しました");
    expect(api.createPostAccount).toHaveBeenCalledWith(11, "メイン", "@example");
  });
});