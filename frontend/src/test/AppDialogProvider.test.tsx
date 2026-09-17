import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AppDialogProvider, useAppDialog } from "../components/AppDialogProvider";

function Trigger() {
  const dialog = useAppDialog();
  return <button type="button" onClick={async () => {
    const result = await dialog.confirm({ title: "画像を削除しますか？", description: "この操作は元に戻せません。", confirmLabel: "削除", tone: "danger" });
    document.body.dataset.confirmed = String(result);
  }}>開く</button>;
}

describe("AppDialogProvider", () => {
  it("ブラウザconfirmではなくアプリ内ダイアログで危険操作を確認する", async () => {
    delete document.body.dataset.confirmed;
    render(<AppDialogProvider><Trigger /></AppDialogProvider>);
    fireEvent.click(screen.getByRole("button", { name: "開く" }));

    expect(await screen.findByRole("dialog", { name: "画像を削除しますか？" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "削除" }));
    await waitFor(() => expect(document.body.dataset.confirmed).toBe("true"));
  });
});