import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BulkActionsBar } from "../App";

describe("BulkActionsBar", () => {
  it("shows selected count", () => {
    render(
      <BulkActionsBar
        count={5}
        onRate={vi.fn()}
        onStatus={vi.fn()}
        onFavorite={vi.fn()}
  onColorLabel={vi.fn()}
          onDelete={vi.fn()}
          onClear={vi.fn()}
      />
    );
    expect(screen.getByText("5 selected")).toBeInTheDocument();
  });

  it("renders all five star rating buttons", () => {
    render(
      <BulkActionsBar
        count={3}
        onRate={vi.fn()}
        onStatus={vi.fn()}
        onFavorite={vi.fn()}
  onColorLabel={vi.fn()}
          onDelete={vi.fn()}
          onClear={vi.fn()}
      />
    );
    expect(screen.getByText("Rate:")).toBeInTheDocument();
  });

  it("renders status buttons", () => {
    render(
      <BulkActionsBar
        count={2}
        onRate={vi.fn()}
        onStatus={vi.fn()}
        onFavorite={vi.fn()}
  onColorLabel={vi.fn()}
          onDelete={vi.fn()}
          onClear={vi.fn()}
      />
    );
    expect(screen.getByText("Status:")).toBeInTheDocument();
    expect(screen.getByText("unsorted")).toBeInTheDocument();
    expect(screen.getByText("reviewed")).toBeInTheDocument();
    expect(screen.getByText("candidate")).toBeInTheDocument();
    expect(screen.getByText("published")).toBeInTheDocument();
  });

  it("renders favorite button", () => {
    render(
      <BulkActionsBar
        count={1}
        onRate={vi.fn()}
        onStatus={vi.fn()}
        onFavorite={vi.fn()}
  onColorLabel={vi.fn()}
          onDelete={vi.fn()}
          onClear={vi.fn()}
      />
    );
    expect(screen.getByText("Favorite")).toBeInTheDocument();
  });

  it("renders clear selection button", () => {
    render(
      <BulkActionsBar
        count={1}
        onRate={vi.fn()}
        onStatus={vi.fn()}
        onFavorite={vi.fn()}
  onColorLabel={vi.fn()}
          onDelete={vi.fn()}
          onClear={vi.fn()}
      />
    );
    expect(screen.getByText("Clear selection")).toBeInTheDocument();
  });

  it("calls onStatus when status button clicked", async () => {
    const onStatus = vi.fn();
    render(
      <BulkActionsBar
        count={2}
        onRate={vi.fn()}
        onStatus={onStatus}
        onFavorite={vi.fn()}
  onColorLabel={vi.fn()}
          onDelete={vi.fn()}
          onClear={vi.fn()}
      />
    );
    const user = userEvent.setup();
    await user.click(screen.getByText("reviewed"));
    expect(onStatus).toHaveBeenCalledWith("reviewed");
  });

  it("calls onClear when clear button clicked", async () => {
    const onClear = vi.fn();
    render(
      <BulkActionsBar
        count={3}
        onRate={vi.fn()}
        onStatus={vi.fn()}
        onFavorite={vi.fn()}
        onColorLabel={vi.fn()}
        onDelete={vi.fn()}
        onClear={onClear}
      />
    );
    const user = userEvent.setup();
    await user.click(screen.getByText("Clear selection"));
    expect(onClear).toHaveBeenCalledOnce();
  });

  it("renders delete button", () => {
    render(
      <BulkActionsBar
        count={2}
        onRate={vi.fn()}
        onStatus={vi.fn()}
        onFavorite={vi.fn()}
        onColorLabel={vi.fn()}
        onDelete={vi.fn()}
        onClear={vi.fn()}
      />
    );
    expect(screen.getByText("削除")).toBeInTheDocument();
  });

  it("calls onDelete when delete button clicked", async () => {
    const onDelete = vi.fn();
    render(
      <BulkActionsBar
        count={2}
        onRate={vi.fn()}
        onStatus={vi.fn()}
        onFavorite={vi.fn()}
        onColorLabel={vi.fn()}
        onDelete={onDelete}
        onClear={vi.fn()}
      />
    );
    const user = userEvent.setup();
    await user.click(screen.getByText("削除"));
    expect(onDelete).toHaveBeenCalledOnce();
  });
});
