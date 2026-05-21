declare global {
  interface Window {
    go: {
      main: {
        App: {
          SelectFolder: () => Promise<string>;
          ScanFolder: (folderPath: string, offset: number, limit: number) => Promise<{
            images: Array<{
              filePath: string;
              fileName: string;
              folderPath: string;
              extension: string;
              fileSize: number;
            }>;
            totalCount: number;
            hasMore: boolean;
          }>;
        };
      };
    };
  }
}

export {};
