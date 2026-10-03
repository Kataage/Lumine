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
    long ThumbnailCacheByteLimit,
    long EncodedThumbnailMemoryByteLimit,
    AppDataPaths DataPaths,
    ThumbnailCacheStats CacheStats,
    string? SettingsWarning,
    bool ThumbnailModeEnvironmentOverride);

internal static class ProductSettingsView
{
    private static readonly long[] DiskBudgetOptions =
    [
        1L * 1024 * 1024 * 1024,
        2L * 1024 * 1024 * 1024,
        4L * 1024 * 1024 * 1024,
        8L * 1024 * 1024 * 1024,
        16L * 1024 * 1024 * 1024,
        32L * 1024 * 1024 * 1024,
        64L * 1024 * 1024 * 1024,
        128L * 1024 * 1024 * 1024,
        256L * 1024 * 1024 * 1024
    ];

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
        Func<long, Task> saveDiskBudget,
        Func<long, Task> saveMemoryBudget,
        Func<Task> clearCache,
        Func<Task> showDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(saveViewerDefaults);
        ArgumentNullException.ThrowIfNull(saveThumbnailMode);
        ArgumentNullException.ThrowIfNull(saveDiskBudget);
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
                saveDiskBudget,
                saveMemoryBudget,
                clearCache));
        root.Children.Add(
            CreateStorageSettings(
                snapshot));
        root.Children.Add(
            CreateDiagnostics(
                showDiagnostics));

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
            LumineDesign.ConfigureComboBox(
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
                });
        var density =
            LumineDesign.ConfigureComboBox(
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
                });
        var sort =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    ItemsSource =
                        new[]
                        {
                            "更新日時: 新しい順",
                            "更新日時: 古い順",
                            "作成日時: 新しい順",
                            "作成日時: 古い順",
                            "ファイル名: A → Z",
                            "ファイル名: Z → A",
                            "サイズ: 大きい順",
                            "サイズ: 小さい順",
                            "評価: 高い順",
                            "評価: 低い順",
                            "状態: A → Z",
                            "状態: Z → A"
                        },
                    SelectedIndex =
                        SortIndex(
                            snapshot.ViewerDefaults.SortOrder)
                });
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
        Func<long, Task> saveDiskBudget,
        Func<long, Task> saveMemoryBudget,
        Func<Task> clearCache)
    {
        var content = CreateCardStack();
        content.Children.Add(
            CreateSectionHeader(
                "画像キャッシュ",
                "通常閲覧はメモリのみが既定です。元画像はLumineへコピーしません。"));

        var persistent =
            LumineDesign.ConfigureCheckBox(
                new CheckBox
                {
                    Content =
                        "表示用サムネイルを次回起動後も再利用する",
                    IsChecked =
                        snapshot.PersistedThumbnailStorageMode
                        == ThumbnailStorageMode.PersistentDisk
                });
        content.Children.Add(persistent);

        content.Children.Add(
            new TextBlock
            {
                Text =
                    snapshot.ThumbnailModeEnvironmentOverride
                        ? $"環境変数による一時上書き中: 実際の動作は {DescribeMode(snapshot.EffectiveThumbnailStorageMode)}。画面の選択は上書き値を保存しません。"
                        : "変更は次回起動から有効です。メモリのみに戻すと、不要になった表示用キャッシュは安全に削除されます。",
                Foreground =
                    snapshot.ThumbnailModeEnvironmentOverride
                        ? LumineDesign.Warning
                        : LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                TextWrapping = TextWrapping.Wrap
            });

        var diskBudgets =
            DiskBudgetOptions
                .Concat(
                    [snapshot.ThumbnailCacheByteLimit])
                .Distinct()
                .Order()
                .ToArray();
        var disk =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    ItemsSource =
                        diskBudgets
                            .Select(
                                static value =>
                                    FormatBytes(value))
                            .ToArray(),
                    SelectedIndex =
                        Array.IndexOf(
                            diskBudgets,
                            snapshot.ThumbnailCacheByteLimit)
                });
        content.Children.Add(
            CreateField(
                "ディスク保持上限",
                disk));
        content.Children.Add(
            new TextBlock
            {
                Text =
                    "永続サムネイルを有効にしたときの最大保持量です。上限を超えた古い表示用cacheは自動整理されます。変更は次回起動から有効です。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
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
            LumineDesign.ConfigureComboBox(
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
                });
        content.Children.Add(
            CreateField(
                "高速再表示用メモリ上限",
                memory));
        content.Children.Add(
            new TextBlock
            {
                Text =
                    "ディスク保持量とは別の、一時的なメモリcache上限です。大きくすると再読み込みを減らせますが、その分RAMを使います。変更は次回起動から有効です。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                TextWrapping = TextWrapping.Wrap
            });

        var diskStats =
            new TextBlock
            {
                Text =
                    $"現在のディスクcache: {snapshot.CacheStats.FileCount:N0}ファイル / {FormatBytes(snapshot.CacheStats.TotalBytes)}（上限 {FormatBytes(snapshot.ThumbnailCacheByteLimit)}）",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize
            };
        content.Children.Add(diskStats);

        var clear =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "表示用キャッシュを削除"
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
                        "キャッシュ方式を保存しました。次回起動から有効です。";
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

        disk.SelectionChanged +=
            async (_, _) =>
            {
                if (disk.SelectedIndex < 0
                    || disk.SelectedIndex >= diskBudgets.Length)
                {
                    return;
                }

                disk.IsEnabled = false;
                status.Foreground =
                    LumineDesign.MutedForeground;
                status.Text = "保存しています…";
                try
                {
                    await saveDiskBudget(
                        diskBudgets[disk.SelectedIndex]);
                    status.Text =
                        "ディスク保持上限を保存しました。次回起動から有効です。";
                }
                catch (Exception exception)
                {
                    status.Foreground = LumineDesign.Danger;
                    status.Text =
                        $"保存できませんでした: {exception.Message}";
                }
                finally
                {
                    disk.IsEnabled = true;
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
                status.Text = "キャッシュを削除しています…";
                try
                {
                    await clearCache();
                    status.Text =
                        "表示用キャッシュを削除しました。元画像・タグ・評価・公開履歴などのユーザーデータは変更していません。";
                }
                catch (Exception exception)
                {
                    status.Foreground = LumineDesign.Danger;
                    status.Text =
                        $"キャッシュを削除できませんでした: {exception.Message}";
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
                "元画像は移動・コピーせず、そのままライブラリとして参照します。"));

        content.Children.Add(
            CreateKeyValue(
                "Lumineのデータ",
                snapshot.DataPaths.IsPortable
                    ? "アプリと一緒に持ち運べる場所へ保存"
                    : "このPCのユーザー領域へ保存"));

        content.Children.Add(
            new TextBlock
            {
                Text =
                    snapshot.DataPaths.IsPortable
                        ? "Lumine本体のフォルダーごと移動できます。"
                        : "通常利用では保存先の詳細を意識する必要はありません。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                TextWrapping = TextWrapping.Wrap
            });

        var details =
            CreateCardStack();
        details.Children.Add(
            CreateKeyValue(
                "データ",
                snapshot.DataPaths.RootPath));
        details.Children.Add(
            CreateKeyValue(
                "メタデータ",
                snapshot.DataPaths.DatabasePath));
        details.Children.Add(
            CreateKeyValue(
                "設定",
                snapshot.DataPaths.SettingsPath));
        details.Children.Add(
            CreateKeyValue(
                "一時キャッシュ",
                snapshot.DataPaths.ThumbnailCachePath));
        details.Children.Add(
            CreateKeyValue(
                "ログ",
                snapshot.DataPaths.LogsPath));

        if (!snapshot.DataPaths.IsPortable)
        {
            details.Children.Add(
                new TextBlock
                {
                    Text =
                        "ポータブル利用が必要な場合は、起動オプションまたは portable.flag で切り替えられます。",
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize = LumineDesign.CaptionFontSize,
                    TextWrapping = TextWrapping.Wrap
                });
        }

        content.Children.Add(
            new Expander
            {
                Header = "詳細な保存場所",
                IsExpanded = false,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                Content = details
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
                FontSize = LumineDesign.BodyFontSize
            });
        panel.Children.Add(
            new TextBlock
            {
                Text = description,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
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
                FontSize = LumineDesign.CaptionFontSize
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
                FontSize = LumineDesign.CaptionFontSize
            });
        panel.Children.Add(
            new TextBlock
            {
                Text = value,
                Foreground = LumineDesign.Foreground,
                FontSize = LumineDesign.CaptionFontSize,
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
                FontSize = LumineDesign.CaptionFontSize
            });
        content.Children.Add(
            new TextBlock
            {
                Text = description,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                TextWrapping = TextWrapping.Wrap
            });
        return CreateCard(content);
    }

    private static TextBlock CreateStatusText() =>
        new()
        {
            Foreground =
                LumineDesign.MutedForeground,
            FontSize = LumineDesign.CaptionFontSize,
            TextWrapping = TextWrapping.Wrap
        };

    private static int SortIndex(
        AssetSortOrder sortOrder) =>
        sortOrder switch
        {
            AssetSortOrder.ModifiedNewest => 0,
            AssetSortOrder.ModifiedOldest => 1,
            AssetSortOrder.CreatedNewest => 2,
            AssetSortOrder.CreatedOldest => 3,
            AssetSortOrder.FileNameAscending => 4,
            AssetSortOrder.FileNameDescending => 5,
            AssetSortOrder.FileSizeLargest => 6,
            AssetSortOrder.FileSizeSmallest => 7,
            AssetSortOrder.RatingHighest => 8,
            AssetSortOrder.RatingLowest => 9,
            AssetSortOrder.StatusAscending => 10,
            AssetSortOrder.StatusDescending => 11,
            _ => 0
        };

    private static AssetSortOrder SortFromIndex(
        int index) =>
        index switch
        {
            1 => AssetSortOrder.ModifiedOldest,
            2 => AssetSortOrder.CreatedNewest,
            3 => AssetSortOrder.CreatedOldest,
            4 => AssetSortOrder.FileNameAscending,
            5 => AssetSortOrder.FileNameDescending,
            6 => AssetSortOrder.FileSizeLargest,
            7 => AssetSortOrder.FileSizeSmallest,
            8 => AssetSortOrder.RatingHighest,
            9 => AssetSortOrder.RatingLowest,
            10 => AssetSortOrder.StatusAscending,
            11 => AssetSortOrder.StatusDescending,
            _ => AssetSortOrder.ModifiedNewest
        };

    private static string DescribeMode(
        ThumbnailStorageMode mode) =>
        mode == ThumbnailStorageMode.PersistentDisk
            ? "再起動後も高速化データを保持"
            : "メモリのみ";

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
