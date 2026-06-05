import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { WelcomeScreen } from "../components/Sidebar";

describe("WelcomeScreen", () => {
  it("renders the app name", () => {
    render(<WelcomeScreen onSelectFolder={vi.fn()} />);
    expect(screen.getByText("Lumine")).toBeInTheDocument();
  });

  it("renders the Open Folder button", () => {
    render(<WelcomeScreen onSelectFolder={vi.fn()} />);
    expect(screen.getByText("Open Folder")).toBeInTheDocument();
  });

  it("calls onSelectFolder when button clicked", async () => {
    const handler = vi.fn();
    render(<WelcomeScreen onSelectFolder={handler} />);
    const button = screen.getByText("Open Folder");
    button.click();
    expect(handler).toHaveBeenCalledOnce();
  });
});
