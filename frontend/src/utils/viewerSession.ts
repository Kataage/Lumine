import type { AssetDTO } from "../api/client";

export type ViewerOpenHandler = (asset: AssetDTO) => void;

let openHandler: ViewerOpenHandler | null = null;

export function registerViewerOpenHandler(handler: ViewerOpenHandler): () => void {
  openHandler = handler;
  return () => {
    if (openHandler === handler) openHandler = null;
  };
}

export function openAssetInViewer(asset: AssetDTO): boolean {
  if (!openHandler) return false;
  openHandler(asset);
  return true;
}
