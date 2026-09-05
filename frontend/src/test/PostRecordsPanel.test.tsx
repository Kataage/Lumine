import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

const api = vi.hoisted(() => ({
  targets: [] as Array<{ id: number; name: string; kind: string }>,
  accounts: [] as Array<{ id: number; postTargetId: number; displayName: string; accountIdentifier: string; isActive: boolean }>,
  createPostTarget: vi.fn(),
  createPostAccount: vi.fn(),
}));

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return {
    ...actual,
    listPostRecords: vi.fn(async () => []),
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

  it("投稿先の入力不足を無反応にせず理由を表示する", async () => {
    render(<PostRecordsPanel />, { wrapper: Wrapper });
    await screen.findByText("投稿先はまだありません。上の欄へ名前を入力して追加してください。");
    fireEvent.click(screen.getByRole("button", { name: "投稿先を追加" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("投稿先名を入力してください");
  });

  it("投稿先からアカウントまで画面上で連続して登録できる", async () => {
    render(<PostRecordsPanel />, { wrapper: Wrapper });
    await screen.findByText("投稿先はまだありません。上の欄へ名前を入力して追加してください。");

    fireEvent.change(screen.getByLabelText("投稿先名"), { target: { value: "Pixiv" } });
    fireEvent.click(screen.getByRole("button", { name: "投稿先を追加" }));
    expect(await screen.findByRole("status")).toHaveTextContent("次に、その投稿先で使うアカウントを登録してください");
    expect(api.createPostTarget).toHaveBeenCalledWith("Pixiv", "pixiv");

    await waitFor(() => expect(screen.getByLabelText("アカウント表示名")).not.toBeDisabled());
    fireEvent.change(screen.getByLabelText("アカウント表示名"), { target: { value: "メイン" } });
    fireEvent.change(screen.getByLabelText("アカウントID"), { target: { value: "@example" } });
    fireEvent.click(screen.getByRole("button", { name: "アカウントを追加" }));

    expect(await screen.findByRole("status")).toHaveTextContent("画像を選択して「＋ 投稿記録」を押せば記録できます");
    expect(api.createPostAccount).toHaveBeenCalledWith(11, "メイン", "@example");
  });
});
