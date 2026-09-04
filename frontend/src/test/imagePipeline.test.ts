import { describe, expect, it } from "vitest";
import { computeContainSize, computeCoverCrop } from "../utils/imagePipeline";

describe("computeCoverCrop", () => {
  it("center-crops a wide image for a square preview", () => {
    expect(computeCoverCrop(4000, 2000, 200, 200)).toEqual({
      x: 1000,
      y: 0,
      width: 2000,
      height: 2000,
    });
  });

  it("center-crops a tall image for a square preview", () => {
    expect(computeCoverCrop(2000, 4000, 200, 200)).toEqual({
      x: 0,
      y: 1000,
      width: 2000,
      height: 2000,
    });
  });
});

describe("computeContainSize", () => {
  it("fits a landscape image without upscaling it", () => {
    expect(computeContainSize(4000, 2000, 1000, 1000)).toEqual({
      width: 1000,
      height: 500,
    });
  });

  it("does not enlarge an image smaller than the target", () => {
    expect(computeContainSize(320, 240, 1920, 1080)).toEqual({
      width: 320,
      height: 240,
    });
  });

  it("guards invalid source dimensions", () => {
    expect(computeContainSize(0, 0, 100, 100)).toEqual({
      width: 1,
      height: 1,
    });
  });
});
