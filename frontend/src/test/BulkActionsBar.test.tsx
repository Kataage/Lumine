import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BulkActionsBar } from "../App";

function renderBar(overrides: Partial<React.ComponentProps<typeof BulkActionsBar>> = {}) {
  const props: React.ComponentProps<typeof BulkActionsBar> = {
    count: 3,
    onRate: vi.fn(),
    onStatus: vi.fn(),
    onFavorite: vi.fn(),
    onColorLabel: vi.fn(),
    onDelete: vi.fn(),
    onClear: vi.fn(),
    ...overrides,
  };
  render(<BulkActionsBar {...props} />);
  return props;
}

describe("BulkActionsBar", () => {
  it("選択件数を表示する", () => {
    renderBar({ count: 5 });
    expect(screen.getByText("5件を選択中")).toBeInTheDocument();
  });

  it("評価と状態の操作を表示する", () => {
    renderBar();
    expect(screen.getByText("評価")).toBeInTheDocument();
    expect(screen.getByText("状態")).toBeInTheDocument();
    expect(screen.getByText("未整理")).toBeInTheDocument();
    expect(screen.getByText("確認済み")).toBeInTheDocument();
    expect(screen.getByText("候補")).toBeInTheDocument();
    expect(screen.getByText("公開済み")).toBeInTheDocument();
  });

  it("お気に入り・削除・選択解除を表示する", () => {
    renderBar();
    expect(screen.getByText("★ お気に入り")).toBeInTheDocument();
    expect(screen.getByText("一覧から削除")).toBeInTheDocument();
    expect(screen.getByText("選択を解除")).toBeInTheDocument();
  });

  it("状態ボタンで内部ステータス値を渡す", async () => {
    const onStatus = vi.fn();
    renderBar({ onStatus });
    const user = userEvent.setup();
    await user.click(screen.getByText("確認済み"));
    expect(onStatus).toHaveBeenCalledWith("reviewed");
  });

  it("選択解除を実行できる", async () => {
    const onClear = vi.fn();
    renderBar({ onClear });
    const user = userEvent.setup();
    await user.click(screen.getByText("選択を解除"));
    expect(onClear).toHaveBeenCalledOnce();
  });

  it("一覧から削除を実行できる", async () => {
    const onDelete = vi.fn();
    renderBar({ onDelete });
    const user = userEvent.setup();
    await user.click(screen.getByText("一覧から削除"));
    expect(onDelete).toHaveBeenCalledOnce();
  });
});
