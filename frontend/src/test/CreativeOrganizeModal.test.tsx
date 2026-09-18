import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppDialogProvider } from "../components/AppDialogProvider";

const api = vi.hoisted(() => ({
  createAssetRelation: vi.fn(),
  createGenerationGroup: vi.fn(),
  createWork: vi.fn(),
  getAssetDetail: vi.fn(),
}));

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return {
    ...actual,
    createAssetRelation: api.createAssetRelation,
    createGenerationGroup: api.createGenerationGroup,
    createWork: api.createWork,
    getAssetDetail: api.getAssetDetail,
  };
});

import { CreativeOrganizeModal } from "../components/CreativeOrganizeModal";

describe("CreativeOrganizeModal", () => {
  beforeEach(() => {
    api.createAssetRelation.mockReset();
    api.createGenerationGroup.mockReset();
    api.createWork.mockReset();
    api.getAssetDetail.mockReset().mockImplementation(async (id: number) => ({ id, fileName: id === 1 ? "base.png" : "derived.png", filePath: `C:\\images\\${id}.png` }));
  });

  it("2枚選択では派生元と派生先の方向を確認して関係性を保存できる", async () => {
    api.createAssetRelation.mockResolvedValue({ id: 9 });
    const onClose = vi.fn();
    render(<AppDialogProvider><CreativeOrganizeModal assetIds={[1, 2]} onClose={onClose} /></AppDialogProvider>);

    expect((await screen.findAllByText("base.png")).length).toBeGreaterThan(0);
    expect(screen.getAllByText("derived.png").length).toBeGreaterThan(0);
    fireEvent.change(screen.getByLabelText("関係"), { target: { value: "inpaint" } });
    fireEvent.click(screen.getByRole("button", { name: "保存" }));

    await waitFor(() => expect(api.createAssetRelation).toHaveBeenCalledWith(1, 2, "inpaint", ""));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it("生成グループでは共通プロンプトと生成条件を保存する", async () => {
    api.createGenerationGroup.mockResolvedValue({ id: 10 });
    render(<AppDialogProvider><CreativeOrganizeModal assetIds={[1, 2, 3]} onClose={vi.fn()} /></AppDialogProvider>);

    fireEvent.change(screen.getByLabelText("グループ名"), { target: { value: "seed variations" } });
    fireEvent.change(screen.getByLabelText("共通プロンプト"), { target: { value: "1girl, yukata" } });
    fireEvent.change(screen.getByLabelText("モデル"), { target: { value: "illustrious-xl" } });
    fireEvent.change(screen.getByLabelText("Steps"), { target: { value: "28" } });
    fireEvent.click(screen.getByRole("button", { name: "保存" }));

    await waitFor(() => expect(api.createGenerationGroup).toHaveBeenCalledWith(expect.objectContaining({
      assetIds: [1, 2, 3],
      name: "seed variations",
      prompt: "1girl, yukata",
      modelName: "illustrious-xl",
      steps: 28,
    })));
  });
});