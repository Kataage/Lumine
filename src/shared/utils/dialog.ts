import { open } from "@tauri-apps/plugin-dialog";

export async function selectFolder(title: string): Promise<string | null> {
  const selected = await open({
    directory: true,
    multiple: false,
    title,
  });
  if (selected && typeof selected === "string") {
    return selected;
  }
  return null;
}
