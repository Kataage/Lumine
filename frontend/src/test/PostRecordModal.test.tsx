import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
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

  it("設定済みなら説明を挟まず投稿先とアカウントを選べる", async () => {
    api.targets.push({ id: 1, name: "Pixiv", kind: "pixiv" });
    api.accounts.push({ id: 2, postTargetId: 1, displayName: "メイン", accountIdentifier: "@example", isActive: true });

    render(<PostRecordModal assetIds={[1]} onClose={vi.fn()} />, { wrapper: Wrapper });

    await waitFor(() => expect(screen.getByLabelText("投稿先")).toHaveValue("1"));
    expect(screen.getByLabelText("アカウント")).toHaveValue("2");
    expect(screen.queryByText("初回だけ、投稿先とアカウントを登録します")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "投稿先設定" })).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("button", { name: "保存" })).toBeEnabled();
  });

  it("未設定なら設定欄を自動で開き入力不足を表示する", async () => {
    render(<PostRecordModal assetIds={[1]} onClose={vi.fn()} />, { wrapper: Wrapper });

    const targetInput = await screen.findByLabelText("新しい投稿先名");
    expect(targetInput).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole("button", { name: "追加" })[0]);
    expect(await screen.findByRole("alert")).toHaveTextContent("投稿先名を入力してください");
  });

  it("設定済みなら最小入力で保存できる", async () => {
    api.targets.push({ id: 1, name: "Pixiv", kind: "pixiv" });
    api.accounts.push({ id: 2, postTargetId: 1, displayName: "メイン", accountIdentifier: "", isActive: true });
    api.createPostRecord.mockResolvedValue({ id: 10 });
    const onClose = vi.fn();

    render(<PostRecordModal assetIds={[101]} onClose={onClose} />, { wrapper: Wrapper });
    await waitFor(() => expect(screen.getByRole("button", { name: "保存" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "保存" }));

    await waitFor(() => expect(api.createPostRecord).toHaveBeenCalledWith({
      assetIds: [101],
      targetId: 1,
      accountId: 2,
      title: "",
      externalPostId: "",
    }));
    expect(onClose).toHaveBeenCalledOnce();
  });
});