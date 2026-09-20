import { describe, expect, it } from "vitest";
import { viewerImagePriority } from "./viewerPreload";

describe("viewerImagePriority", () => {
  it("gives visible rows the highest decode priority", () => {
    expect(viewerImagePriority(4, 4, 8)).toBe("high");
    expect(viewerImagePriority(8, 4, 8)).toBe("high");
    expect(viewerImagePriority(3, 4, 8)).toBe("prefetch");
    expect(viewerImagePriority(9, 4, 8)).toBe("prefetch");
  });
});
