import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { WelcomeScreen } from "../components/Sidebar";

describe("WelcomeScreen", () => {
  it("アプリ名と案内を表示する", () => {
    render(<WelcomeScreen onSelectFolder={vi.fn()} />);
    expect(screen.getByText("Lumine")).toBeInTheDocument();
    expect(screen.getByText("最初に画像フォルダーを登録してください")).toBeInTheDocument();
  });

  it("画像フォルダー追加ボタンを表示する", () => {
    render(<WelcomeScreen onSelectFolder={vi.fn()} />);
    expect(screen.getByRole("button", { name: "画像フォルダーを追加" })).toBeInTheDocument();
  });

  it("ボタンを押すとフォルダー選択を開始する", async () => {
    const handler = vi.fn();
    const user = userEvent.setup();
    render(<WelcomeScreen onSelectFolder={handler} />);
    await user.click(screen.getByRole("button", { name: "画像フォルダーを追加" }));
    expect(handler).toHaveBeenCalledOnce();
  });

  it("処理中はボタンを無効化する", () => {
    render(<WelcomeScreen onSelectFolder={vi.fn()} busy />);
    expect(screen.getByRole("button", { name: "画像を読み込んでいます…" })).toBeDisabled();
  });
});
