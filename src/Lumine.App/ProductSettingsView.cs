using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Image;
using Lumine.Library;

namespace Lumine.App;

internal sealed record ProductSettingsSnapshot(
    BrowsePreferences ViewerDefaults,
    ThumbnailStorageMode PersistedThumbnailStorageMode,
    ThumbnailStorageMode EffectiveThumbnailStorageMode,
    long EncodedThumbnailMemoryByteLimit,
    AppDataPaths DataPaths,
    ThumbnailCacheStats CacheStats,
    string? SettingsWarning,
    bool ThumbnailModeEnvironmentOverride);

internal static class ProductSettingsView
{
    private static readonly long[] MemoryBudgetOptions =
    [
        128L * 1024 * 1024,
        256L * 1024 * 1024,
        512L * 1024 * 1024,
        1024L * 1024 * 1024
    ];

    public static Control Create(
        ProductSettingsSnapshot snapshot,
        Func<BrowsePreferences, Task> saveViewerDefaults,
        Func<ThumbnailStorageMode, Task> saveThumbnailMode,
        Func<long, Task> saveMemoryBudget,
        Func<Task> clearCache,
        Func<Task> showDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(saveViewerDefaults);
        ArgumentNullException.ThrowIfNull(saveThumbnailMode);
        ArgumentNullException.ThrowIfNull(saveMemoryBudget);
        ArgumentNullException.ThrowIfNull(clearCache);
        ArgumentNullException.ThrowIfNull(showDiagnostics);

        var root =
            new StackPanel
            {
                Spacing = 12
            };

        if (!string.IsNullOrWhiteSpace(
                snapshot.SettingsWarning))
        {
            root.Children.Add(
                CreateNotice(
                    "設定を一部既定値へ戻しました",
                    snapshot.SettingsWarning,
                    LumineDesign.Warning));
        }

        root.Children.Add(
            CreateViewerDefaults(
                snapshot,
                saveViewerDefaults));
        root.Children.Add(
            CreateCacheSettings(
                snapshot,
                saveThumbnailMode,
                saveMemoryBudget,
                clearCache));
        root.Children.Add(
            CreateStorageSettings(
                snapshot));
        root.Children.Add(
            CreateDiagnostics(
                showDiagnostics));
        root.Children.Add(
            CreateFutureAiSection());

        return new ScrollViewer
        {
            Content = root,
            HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    private static Control CreateViewerDefaults(
        ProductSettingsSnapshot snapshot,
        Func<BrowsePreferences, Task> save)
    {
        var content = CreateCardStack();
        content.Children.Add(
            CreateSectionHeader(
                "閲覧",
                "新しくライブラリを開いたときの既定表示です。現在のライブラリにもすぐ反映します。"));

        var view =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "グリッド",
                        "リスト"
                    },
                SelectedIndex =
                    snapshot.ViewerDefaults.ViewMode
                        == BrowseViewMode.Grid
                            ? 0
                            : 1
            };
        var density =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "コンパクト",
                        "標準",
                        "ゆったり"
                    },
                SelectedIndex =
                    snapshot.ViewerDefaults.Density
            };
        var sort =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "更新日時: 新しい順",
                        "更新日時: 古い順",
                        "ファイル名: A → Z",
                        "ファイル名: Z → A"
                    },
                SelectedIndex =
                    SortIndex(
                        snapshot.ViewerDefaults.SortOrder)
            };
        var status = CreateStatusText();

        content.Children.Add(
            CreateField(
                "表示",
                view));
        content.Children.Add(
            CreateField(
                "密度",
                density));
        content.Children.Add(
            CreateField(
                "並び順",
                sort));
        content.Children.Add(status);

        var updating = false;
        async Task SaveAsync()
        {
            if (updating)
            {
                return;
            }

            updating = true;
            view.IsEnabled = false;
            density.IsEnabled = false;
            sort.IsEnabled = false;
            status.Foreground =
                LumineDesign.MutedForeground;
            status.Text = "保存しています…";

            try
            {
                await save(
                    new BrowsePreferences(
                        view.SelectedIndex == 1
                            ? BrowseViewMode.List
                            : BrowseViewMode.Grid,
                        Math.Clamp(
                            density.SelectedIndex,
                            0,
                            2),
                        SortFromIndex(
                            sort.SelectedIndex)));
                status.Text =
                    "閲覧の既定値を保存しました。";
            }
            catch (Exception exception)
            {
                status.Foreground = LumineDesign.Danger;
                status.Text =
                    $"保存できませんでした: {exception.Message}";
            }
            finally
            {
                updating = false;
                view.IsEnabled = true;
                density.IsEnabled = true;
                sort.IsEnabled = true;
            }
        }

        view.SelectionChanged +=
            async (_, _) => await SaveAsync();
        density.SelectionChanged +=
            async (_, _) => await SaveAsync();
        sort.SelectionChanged +=
            async (_, _) => await SaveAsync();

        return CreateCard(content);
    }

    private static Control CreateCacheSettings(
        ProductSettingsSnapshot snapshot,
        Func<ThumbnailStorageMode, Task> saveThumbnailMode,
        Func<long, Task> saveMemoryBudget,
        Func<Task> clearCache)
    {
        var content = CreateCardStack();
        content.Children.Add(
            CreateSectionHeader(
                "画像キャッシュ",
                "通常閲覧はMemoryOnlyが既定です。元画像はLumineへコピーせず、永続サムネイルも作りません。"));

        var persistent =
            new CheckBox
            {
                Content =
                    "表示用サムネイルをディスクへ永続保存する",
                IsChecked =
                    snapshot.PersistedThumbnailStorageMode
                    == ThumbnailStorageMode.PersistentDisk
            };
        content.Children.Add(persistent);

        content.Children.Add(
            new TextBlock
            {
                Text =
                    snapshot.ThumbnailModeEnvironmentOverride
                        ? $"環境変数による一時上書き中: 実際の動作は {DescribeMode(snapshot.EffectiveThumbnailStorageMode)}。画面の選択は上書き値を保存しません。"
                        : "変更は次回起動から有効です。MemoryOnlyへ戻すと、旧display-thumbnail cacheは安全に退役・削除されます。",
                Foreground =
                    snapshot.ThumbnailModeEnvironmentOverride
                        ? LumineDesign.Warning
                        : LumineDesign.MutedForeground,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            });

        var budgets =
            MemoryBudgetOptions
                .Concat(
                    [snapshot.EncodedThumbnailMemoryByteLimit])
                .Distinct()
                .Order()
                .ToArray();
        var memory =
            new ComboBox
            {
                ItemsSource =
                    budgets
                        .Select(
                            static value =>
                                FormatBytes(value))
                        .ToArray(),
                SelectedIndex =
                    Array.IndexOf(
                        budgets,
                        snapshot.EncodedThumbnailMemoryByteLimit)
            };
        content.Children.Add(
            CreateField(
                "メモリ内thumbnail cache上限",
                memory));
        content.Children.Add(
            new TextBlock
            {
                Text =
                    "この上限も次回起動から有効です。大きくすると再decodeを減らせますが、その分RAMを使います。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            });

        var diskStats =
            new TextBlock
            {
                Text =
                    $"現在のディスクcache: {snapshot.CacheStats.FileCount:N0}ファイル / {FormatBytes(snapshot.CacheStats.TotalBytes)}",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9.5
            };
        content.Children.Add(diskStats);

        var clear =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "表示用cacheを削除"
                });
        content.Children.Add(clear);

        var status = CreateStatusText();
        content.Children.Add(status);

        persistent.Click +=
            async (_, _) =>
            {
                var requested =
                    persistent.IsChecked == true
                        ? ThumbnailStorageMode.PersistentDisk
                        : ThumbnailStorageMode.MemoryOnly;
                persistent.IsEnabled = false;
                status.Foreground =
                    LumineDesign.MutedForeground;
                status.Text = "保存しています…";
                try
                {
                    await saveThumbnailMode(requested);
                    status.Text =
                        "cache方式を保存しました。次回起動から有効です。";
                }
                catch (Exception exception)
                {
                    persistent.IsChecked =
                        snapshot.PersistedThumbnailStorageMode
                        == ThumbnailStorageMode.PersistentDisk;
                    status.Foreground = LumineDesign.Danger;
                    status.Text =
                        $"保存できませんでした: {exception.Message}";
                }
                finally
                {
                    persistent.IsEnabled = true;
                }
            };

        memory.SelectionChanged +=
            async (_, _) =>
            {
                if (memory.SelectedIndex < 0
                    || memory.SelectedIndex >= budgets.Length)
                {
                    return;
                }

                memory.IsEnabled = false;
                status.Foreground =
                    LumineDesign.MutedForeground;
                status.Text = "保存しています…";
                try
                {
                    await saveMemoryBudget(
                        budgets[memory.SelectedIndex]);
                    status.Text =
                        "メモリ上限を保存しました。次回起動から有効です。";
                }
                catch (Exception exception)
                {
                    status.Foreground = LumineDesign.Danger;
                    status.Text =
                        $"保存できませんでした: {exception.Message}";
                }
                finally
                {
                    memory.IsEnabled = true;
                }
            };

        clear.Click +=
            async (_, _) =>
            {
                clear.IsEnabled = false;
                status.Foreground =
                    LumineDesign.MutedForeground;
                status.Text = "cacheを削除しています…";
                try
                {
                    await clearCache();
                    status.Text =
                        "表示用cacheを削除しました。元画像・タグ・評価・Publication等のユーザーデータは変更していません。";
                }
                catch (Exception exception)
                {
                    status.Foreground = LumineDesign.Danger;
                    status.Text =
                        $"cacheを削除できませんでした: {exception.Message}";
                }
                finally
                {
                    clear.IsEnabled = true;
                }
            };

        return CreateCard(content);
    }

    private static Control CreateStorageSettings(
        ProductSettingsSnapshot snapshot)
    {
        var content = CreateCardStack();
        content.Children.Add(
            CreateSectionHeader(
                "保存場所",
                "Lumineが所有するデータの場所です。元画像はライブラリとして参照するだけで、ここへコピーしません。"));

        content.Children.Add(
            CreateKeyValue(
                "モード",
                snapshot.DataPaths.DescribeLocation()));
        content.Children.Add(
            CreateKeyValue(
                "Lumineデータ",
                snapshot.DataPaths.RootPath));
        content.Children.Add(
            CreateKeyValue(
                "ユーザーメタデータDB",
                snapshot.DataPaths.DatabasePath));
        content.Children.Add(
            CreateKeyValue(
                "設定",
                snapshot.DataPaths.SettingsPath));
        content.Children.Add(
            CreateKeyValue(
                "破棄可能cache",
                snapshot.DataPaths.ThumbnailCachePath));
        content.Children.Add(
            CreateKeyValue(
                "ログ",
                snapshot.DataPaths.LogsPath));

        content.Children.Add(
            new TextBlock
            {
                Text =
                    snapshot.DataPaths.IsPortable
                        ? "ポータブルモードでは、これらはすべてLumine.exe配下の data フォルダー内に収まります。フォルダーごと移動できます。"
                        : "ポータブル起動は --portable、LUMINE_PORTABLE=1、またはLumine.exeと同じ場所の portable.flag で有効にできます。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            });

        return CreateCard(content);
    }

    private static Control CreateDiagnostics(
        Func<Task> showDiagnostics)
    {
        var content = CreateCardStack();
        content.Children.Add(
            CreateSectionHeader(
                "診断",
                "通常操作に診断情報は必要ありません。不具合調査時だけ利用します。"));

        var button =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "診断情報を開く"
                });
        button.Click +=
            async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await showDiagnostics();
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
        content.Children.Add(button);
        return CreateCard(content);
    }

    private static Control CreateFutureAiSection()
    {
        var content = CreateCardStack();
        content.Children.Add(
            CreateSectionHeader(
                "ローカルAI",
                "AI機能はProduct Acceptance完了後のフェーズで追加します。現在はモデルを読み込まず、画像管理機能だけで完結します。"));

        content.Children.Add(
            new TextBlock
            {
                Text = "現在は無効",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold
            });
        return CreateCard(content);
    }

    private static StackPanel CreateCardStack() =>
        new()
        {
            Spacing = 8
        };

    private static Border CreateCard(
        Control content) =>
        new()
        {
            Background = LumineDesign.SurfaceRaised,
            BorderBrush = LumineDesign.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Child = content
        };

    private static Control CreateSectionHeader(
        string title,
        string description)
    {
        var panel =
            new StackPanel
            {
                Spacing = 3
            };
        panel.Children.Add(
            new TextBlock
            {
                Text = title,
                Foreground = LumineDesign.Foreground,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11.5
            });
        panel.Children.Add(
            new TextBlock
            {
                Text = description,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            });
        return panel;
    }

    private static Control CreateField(
        string label,
        Control control)
    {
        var panel =
            new StackPanel
            {
                Spacing = 4
            };
        panel.Children.Add(
            new TextBlock
            {
                Text = label,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9.5
            });
        panel.Children.Add(control);
        return panel;
    }

    private static Control CreateKeyValue(
        string label,
        string value)
    {
        var panel =
            new StackPanel
            {
                Spacing = 2
            };
        panel.Children.Add(
            new TextBlock
            {
                Text = label,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9
            });
        panel.Children.Add(
            new TextBlock
            {
                Text = value,
                Foreground = LumineDesign.Foreground,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            });
        return panel;
    }

    private static Control CreateNotice(
        string title,
        string description,
        IBrush brush)
    {
        var content = CreateCardStack();
        content.Children.Add(
            new TextBlock
            {
                Text = title,
                Foreground = brush,
                FontWeight = FontWeight.SemiBold,
                FontSize = 10.5
            });
        content.Children.Add(
            new TextBlock
            {
                Text = description,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap
            });
        return CreateCard(content);
    }

    private static TextBlock CreateStatusText() =>
        new()
        {
            Foreground =
                LumineDesign.MutedForeground,
            FontSize = 9.5,
            TextWrapping = TextWrapping.Wrap
        };

    private static int SortIndex(
        AssetSortOrder sortOrder) =>
        sortOrder switch
        {
            AssetSortOrder.ModifiedNewest => 0,
            AssetSortOrder.ModifiedOldest => 1,
            AssetSortOrder.FileNameAscending => 2,
            AssetSortOrder.FileNameDescending => 3,
            _ => 0
        };

    private static AssetSortOrder SortFromIndex(
        int index) =>
        index switch
        {
            1 => AssetSortOrder.ModifiedOldest,
            2 => AssetSortOrder.FileNameAscending,
            3 => AssetSortOrder.FileNameDescending,
            _ => AssetSortOrder.ModifiedNewest
        };

    private static string DescribeMode(
        ThumbnailStorageMode mode) =>
        mode == ThumbnailStorageMode.PersistentDisk
            ? "PersistentDisk"
            : "MemoryOnly";

    private static string FormatBytes(
        long bytes)
    {
        const double mib = 1024 * 1024;
        const double gib = mib * 1024;

        return bytes >= (long)gib
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{bytes / gib:0.##} GiB")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{bytes / mib:0} MiB");
    }
}
