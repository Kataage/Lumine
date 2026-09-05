import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

const api = vi.hoisted(() => ({
  targets: [] as Array<{ id: number; name: string; kind: string }>,
  accounts: [] as Array<{ id: number; postTargetId: number; displayName: string; accountIdentifier: string; isActive: boolean }>,
  createPostTarget: vi.fn(),
  createPostAccount: vi.fn(),
  createPostRecord: vi.fn(),
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
  };
});

import { PostRecordModal } from "../components/PostRecordModal";

function Wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe("PostRecordModal", () => {
  beforeEach(() => {
    api.targets.splice(0);
    api.accounts.splice(0);
    api.createPostTarget.mockReset();
    api.createPostAccount.mockReset();
    api.createPostRecord.mockReset();
  });

  it("初回利用時に投稿先とアカウントの意味を説明する", async () => {
    render(<PostRecordModal assetIds={[1]} onClose={vi.fn()} />, { wrapper: Wrapper });
    expect(await screen.findByText("初回だけ、投稿先とアカウントを登録します")).toBeInTheDocument();
    expect(screen.getByText(/投稿先 = Pixiv \/ X/)).toBeInTheDocument();
  });

  it("空の投稿先追加を無反応にせずエラー表示する", async () => {
    render(<PostRecordModal assetIds={[1]} onClose={vi.fn()} />, { wrapper: Wrapper });
    const button = await screen.findByRole("button", { name: "投稿先を追加" });
    fireEvent.click(button);
    expect(await screen.findByRole("alert")).toHaveTextContent("投稿先名を入力してください");
  });
});
