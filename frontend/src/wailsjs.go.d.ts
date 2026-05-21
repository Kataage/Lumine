// Wails generated types (simulated)
declare global {
  interface Window {
    go: {
      main: {
        App: {
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
          OpenFolderDialog: () => Promise<string>;
        };
      };
    };
    runtime: {
      EventsOn: (eventName: string, callback: (...args: unknown[]) => void) => void;
      EventsOff: (eventName: string) => void;
    };
  }
}

export {};
