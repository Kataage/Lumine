import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../../wailsjs/runtime/runtime", () => ({
  EventsOn: vi.fn(),
  EventsOff: vi.fn(),
}));

vi.mock("../../wailsjs/go/commands/AppCommands", async (importOriginal) => {
  const actual = await importOriginal<
    typeof import("../../wailsjs/go/commands/AppCommands")
  >();
  return {
    ...actual,
    GetAISettings: vi.fn(),
    GetAIStorageInfo: vi.fn(),
    RequestLegacyStorageMigration: vi.fn(),
    CancelLegacyStorageMigration: vi.fn(),
    SemanticSearchAssets: vi.fn(),
    SemanticSearchAssetsWithID: vi.fn(),
    GetDefaultSemanticModelInfo: vi.fn(),
  };
});

import * as Go from "../../wailsjs/go/commands/AppCommands";
import { commands, domain } from "../../wailsjs/go/models";
import {
  cancelLegacyStorageMigration,
  getAISettings,
  getAIStorageInfo,
  getDefaultSemanticModelInfo,
  getTaggerReview,
  requestLegacyStorageMigration,
  reviewTaggerSuggestions,
  semanticSearchAssets,
} from "../api/client";

const SETTINGS = {
  enabled: true,
  semanticSearch: true,
  tagger: false,
  lightweightVision: false,
  advancedVision: false,
  promptEngine: false,
  autoAnalyze: false,
  gpuAcceleration: false,
};

describe("typed AI Wails bridge", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    const dynamicGetAISettings = vi.fn(async () => ({
      ...SETTINGS,
      enabled: false,
    }));
    const dynamicSemanticSearch = vi.fn(async () => ({
      assets: [],
      totalCount: 999,
    }));
    const dynamicGetTaggerReviewJSON = vi.fn(async () => JSON.stringify({
      assetId: 7,
      state: "ready",
      engine: "tagger-engine",
      modelId: "tagger-model",
      modelVersion: "1",
      suggestions: [
        {
          id: 10,
          assetId: 7,
          kind: "general",
          name: "1girl",
          confidence: 0.97,
          state: "pending",
          threshold: 0.35,
          engine: "tagger-engine",
          modelId: "tagger-model",
          modelVersion: "1",
          createdAt: "2026-09-20T00:00:00Z",
          updatedAt: "2026-09-20T00:00:00Z",
        },
      ],
    }));
    const dynamicReviewTaggerSuggestions = vi.fn(async () => undefined);

    (window as unknown as {
      go?: {
        commands?: {
          AppCommands?: {
            GetAISettings?: typeof dynamicGetAISettings;
            SemanticSearchAssetsWithID?: typeof dynamicSemanticSearch;
            GetTaggerReviewJSON?: typeof dynamicGetTaggerReviewJSON;
            ReviewTaggerSuggestions?: typeof dynamicReviewTaggerSuggestions;
          };
        };
      };
    }).go = {
      commands: {
        AppCommands: {
          GetAISettings: dynamicGetAISettings,
          SemanticSearchAssetsWithID: dynamicSemanticSearch,
          GetTaggerReviewJSON: dynamicGetTaggerReviewJSON,
          ReviewTaggerSuggestions: dynamicReviewTaggerSuggestions,
        },
      },
    };
  });

  it("AI設定はwindow.go fallbackではなくgenerated bindingを使う", async () => {
    vi.mocked(Go.GetAISettings).mockResolvedValue(new domain.AISettings(SETTINGS));

    const settings = await getAISettings();

    expect(Go.GetAISettings).toHaveBeenCalledTimes(1);
    expect(settings.enabled).toBe(true);
    const dynamic = (window as unknown as {
      go: { commands: { AppCommands: { GetAISettings: ReturnType<typeof vi.fn> } } };
    }).go.commands.AppCommands.GetAISettings;
    expect(dynamic).not.toHaveBeenCalled();
  });

  it("storage診断と移行操作はgenerated bindingを使う", async () => {
    const base = new commands.AIStorageInfo({
      mode: "installed",
      rootPath: "C:\\old\\lumine",
      dataPath: "C:\\old\\lumine",
      databasePath: "C:\\old\\lumine\\lumine.db",
      logsPath: "C:\\old\\lumine\\logs",
      modelsPath: "C:\\old\\lumine\\models",
      runtimesPath: "C:\\old\\lumine\\runtimes\\llama.cpp",
      semanticIndexPath: "C:\\old\\lumine\\semantic-index",
      webviewDataPath: "C:\\old\\lumine\\webview2",
      preferredRootPath: "C:\\Users\\user\\AppData\\Local\\Lumine",
      legacyPath: "C:\\old\\lumine",
      legacyDetected: true,
      usingLegacy: true,
      migrationAvailable: true,
      migrationPending: false,
      migrationSourcePath: "C:\\old\\lumine",
      migrationTargetPath: "C:\\Users\\user\\AppData\\Local\\Lumine",
      migrationRequiresRestart: true,
    });
    vi.mocked(Go.GetAIStorageInfo).mockResolvedValue(base);
    vi.mocked(Go.RequestLegacyStorageMigration).mockResolvedValue(
      new commands.AIStorageInfo({ ...base, migrationPending: true }),
    );
    vi.mocked(Go.CancelLegacyStorageMigration).mockResolvedValue(
      new commands.AIStorageInfo({ ...base, migrationPending: false }),
    );

    const info = await getAIStorageInfo();
    expect(info.usingLegacy).toBe(true);
    expect(info.migrationAvailable).toBe(true);

    const pending = await requestLegacyStorageMigration();
    expect(Go.RequestLegacyStorageMigration).toHaveBeenCalledTimes(1);
    expect(pending.migrationPending).toBe(true);

    const cancelled = await cancelLegacyStorageMigration();
    expect(Go.CancelLegacyStorageMigration).toHaveBeenCalledTimes(1);
    expect(cancelled.migrationPending).toBe(false);
  });

  it("request ID付き意味検索はgenerated bindingへ直接渡す", async () => {
    vi.mocked(Go.SemanticSearchAssetsWithID).mockResolvedValue(
      new commands.AssetListResponse({
        assets: [],
        totalCount: 7,
        semanticSearchSessionId: "session-1",
      }),
    );

    const result = await semanticSearchAssets(
      {
        libraryId: 1,
        folderPath: "",
        recurse: true,
        search: "blue sky",
        sortBy: "",
        sortDesc: false,
        offset: 0,
        limit: 50,
      },
      "request-1",
    );

    expect(Go.SemanticSearchAssetsWithID).toHaveBeenCalledWith(
      expect.objectContaining({ search: "blue sky" }),
      "request-1",
    );
    expect(result.totalCount).toBe(7);
    const dynamic = (window as unknown as {
      go: { commands: { AppCommands: { SemanticSearchAssetsWithID: ReturnType<typeof vi.fn> } } };
    }).go.commands.AppCommands.SemanticSearchAssetsWithID;
    expect(dynamic).not.toHaveBeenCalled();
  });

  it("SigLIP2 execution providerとfallback warningを保持する", async () => {
    vi.mocked(Go.GetDefaultSemanticModelInfo).mockResolvedValue(
      new commands.SemanticModelInfo({
        id: "siglip2",
        version: "1",
        engine: "siglip2",
        displayName: "SigLIP2",
        license: "Apache-2.0",
        sizeBytes: 123,
        installed: true,
        runtime: {
          capability: "semantic_search",
          state: "ready",
          modelId: "siglip2",
          version: "1",
          engine: "siglip2",
          executionProvider: "cpu",
          warning: "DirectML unavailable; using CPU fallback",
        },
      }),
    );

    const info = await getDefaultSemanticModelInfo();
    expect(info.runtime.executionProvider).toBe("cpu");
    expect(info.runtime.warning).toContain("DirectML unavailable");
  });

  it("not_loaded runtime stateを保持する", async () => {
    vi.mocked(Go.GetDefaultSemanticModelInfo).mockResolvedValue(
      new commands.SemanticModelInfo({
        id: "siglip2",
        version: "1",
        engine: "siglip2",
        displayName: "SigLIP2",
        license: "Apache-2.0",
        sizeBytes: 123,
        installed: true,
        runtime: {
          capability: "semantic_search",
          state: "not_loaded",
          modelId: "siglip2",
          version: "1",
          engine: "siglip2",
        },
      }),
    );

    const info = await getDefaultSemanticModelInfo();
    expect(info.installed).toBe(true);
    expect(info.runtime.state).toBe("not_loaded");
  });

  it("Tagger review JSONをparseしreview actionを渡す", async () => {
    const review = await getTaggerReview(7);
    expect(review?.state).toBe("ready");
    expect(review?.suggestions).toHaveLength(1);
    expect(review?.suggestions[0]).toMatchObject({
      kind: "general",
      name: "1girl",
      confidence: 0.97,
    });

    await reviewTaggerSuggestions(7, 10, "accept");
    const dynamic = (window as unknown as {
      go: {
        commands: {
          AppCommands: {
            GetTaggerReviewJSON: ReturnType<typeof vi.fn>;
            ReviewTaggerSuggestions: ReturnType<typeof vi.fn>;
          };
        };
      };
    }).go.commands.AppCommands;

    expect(dynamic.GetTaggerReviewJSON).toHaveBeenCalledWith(7);
    expect(dynamic.ReviewTaggerSuggestions).toHaveBeenCalledWith(7, 10, "accept");
  });

  it("未知のruntime stateは誤表示せず拒否する", async () => {
    vi.mocked(Go.GetDefaultSemanticModelInfo).mockResolvedValue(
      new commands.SemanticModelInfo({
        id: "siglip2",
        version: "1",
        engine: "siglip2",
        displayName: "SigLIP2",
        license: "Apache-2.0",
        sizeBytes: 123,
        installed: true,
        runtime: {
          capability: "semantic_search",
          state: "future_state",
        },
      }),
    );

    await expect(getDefaultSemanticModelInfo()).rejects.toThrow(
      "未知のAI runtime state",
    );
  });
});
