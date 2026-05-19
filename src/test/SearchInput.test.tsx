import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { SearchInput } from "@/app/layout/SearchInput";

const setSearchQueryMock = vi.fn();

vi.mock("@/shared/hooks/useAppStore", () => ({
  useAppStore: vi.fn((selector) => {
    const state = {
      searchQuery: "",
      setSearchQuery: setSearchQueryMock,
    };
    return selector(state);
  }),
}));

describe("SearchInput", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("should render search input", () => {
    render(<SearchInput />);
    const input = screen.getByPlaceholderText(/search assets/i);
    expect(input).toBeInTheDocument();
  });

  it("should call setSearchQuery on change", () => {
    render(<SearchInput />);
    const input = screen.getByPlaceholderText(/search assets/i);
    fireEvent.change(input, { target: { value: "test query" } });

    expect(setSearchQueryMock).toHaveBeenCalledWith("test query");
  });
});
