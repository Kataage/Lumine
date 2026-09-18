import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ToolbarSelect } from "../components/ToolbarSelect";

describe("ToolbarSelect", () => {
  it("候補をtrigger直下へPortal表示し、親のoverflowにクリップされない", () => {
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 800 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });

    const { container } = render(
      <div style={{ overflow: "hidden", height: 40 }}>
        <ToolbarSelect
          ariaLabel="並び順"
          value="modified"
          options={[
            { value: "modified", label: "更新日時" },
            { value: "name", label: "ファイル名" },
          ]}
          onChange={vi.fn()}
        />
      </div>
    );

    const trigger = screen.getByRole("button", { name: "並び順" });
    vi.spyOn(trigger, "getBoundingClientRect").mockReturnValue({
      x: 20,
      y: 68,
      width: 120,
      height: 32,
      top: 68,
      right: 140,
      bottom: 100,
      left: 20,
      toJSON: () => ({}),
    } as DOMRect);

    fireEvent.click(trigger);

    const menu = screen.getByRole("listbox", { name: "並び順" });
    expect(container.contains(menu)).toBe(false);
    expect(menu).toHaveStyle({ top: "105px", left: "20px", width: "132px", maxHeight: "385px" });
  });

  it("候補選択後に閉じ、値を通知する", () => {
    const onChange = vi.fn();

    render(
      <ToolbarSelect
        ariaLabel="状態で絞り込み"
        value=""
        options={[
          { value: "", label: "状態: すべて" },
          { value: "reviewed", label: "確認済み" },
        ]}
        onChange={onChange}
      />
    );

    const trigger = screen.getByRole("button", { name: "状態で絞り込み" });
    vi.spyOn(trigger, "getBoundingClientRect").mockReturnValue({
      x: 10,
      y: 40,
      width: 140,
      height: 32,
      top: 40,
      right: 150,
      bottom: 72,
      left: 10,
      toJSON: () => ({}),
    } as DOMRect);

    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("option", { name: "確認済み" }));

    expect(onChange).toHaveBeenCalledWith("reviewed");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("矢印キーとEnterでも選択できる", () => {
    const onChange = vi.fn();

    render(
      <ToolbarSelect
        ariaLabel="評価で絞り込み"
        value={0}
        options={[
          { value: 0, label: "評価: すべて" },
          { value: 1, label: "★" },
          { value: 2, label: "★★" },
        ]}
        onChange={onChange}
      />
    );

    const trigger = screen.getByRole("button", { name: "評価で絞り込み" });
    vi.spyOn(trigger, "getBoundingClientRect").mockReturnValue({
      x: 10,
      y: 40,
      width: 140,
      height: 32,
      top: 40,
      right: 150,
      bottom: 72,
      left: 10,
      toJSON: () => ({}),
    } as DOMRect);

    fireEvent.keyDown(trigger, { key: "ArrowDown" });
    fireEvent.keyDown(trigger, { key: "ArrowDown" });
    fireEvent.keyDown(trigger, { key: "Enter" });

    expect(onChange).toHaveBeenCalledWith(1);
  });
});
