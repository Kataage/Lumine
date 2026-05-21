export interface ImageInfo {
  filePath: string;
  fileName: string;
  folderPath: string;
  extension: string;
  fileSize: number;
}

export interface ScanResult {
  images: ImageInfo[];
  totalCount: number;
  hasMore: boolean;
}

export async function selectFolder(): Promise<string> {
  return window.go.main.App.SelectFolder();
}

export async function scanFolder(
  folderPath: string,
  offset: number,
  limit: number
): Promise<ScanResult> {
  return window.go.main.App.ScanFolder(folderPath, offset, limit);
}
