using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Library;

namespace Lumine.App;

internal static class ProductNavigationViews
{
    public static Control CreateLibraries(
        IReadOnlyList<LibraryCatalogItem> libraries,
        long? activeLibraryId,
        Func<Task> addLibrary,
        Func<LibraryCatalogItem, Task> openLibrary,
        Func<LibraryCatalogItem, Task> toggleEnabled,
        Func<LibraryCatalogItem, Task> removeLibrary)
    {
        var stack = CreateListStack();

        var add =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "画像フォルダーを追加"
                });
        AttachAsync(add, addLibrary);
        stack.Children.Add(add);

        if (libraries.Count == 0)
        {
            stack.Children.Add(
                CreateHint(
                    "登録済みのライブラリはありません。"));
        }

        foreach (var library in libraries)
        {
            var rootAvailable =
                Directory.Exists(library.RootPath);
            var isActive =
                activeLibraryId == library.Id;

            var title =
                new TextBlock
                {
                    Text = library.Name,
                    Foreground = LumineDesign.Foreground,
                    FontWeight =
                        isActive
                            ? FontWeight.Bold
                            : FontWeight.SemiBold,
                    FontSize = 12,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis
                };
            var detail =
                new TextBlock
                {
                    Text =
                        $"{library.AssetCount:N0} 件 · {DescribeScanState(library.ScanState)}"
                        + (rootAvailable ? string.Empty : " · オフライン")
                        + (library.IsEnabled ? string.Empty : " · 無効"),
                    Foreground =
                        rootAvailable
                            ? LumineDesign.MutedForeground
                            : LumineDesign.Warning,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                };
            var path =
                new TextBlock
                {
                    Text = library.RootPath,
                    Foreground = LumineDesign.MutedForeground,
                    FontSize = 9.5,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis
                };

            var open =
                LumineDesign.ConfigureSecondaryButton(
                    new Button
                    {
                        Content =
                            isActive
                                ? "表示中"
                                : "開く",
                        IsEnabled =
                            library.IsEnabled
                            && rootAvailable
                            && !isActive
                    });
            AttachAsync(
                open,
                () => openLibrary(library));

            var toggle =
                new Button
                {
                    Content =
                        library.IsEnabled
                            ? "無効化"
                            : "有効化",
                    FontSize = 10,
                    Padding = new Thickness(8, 4)
                };
            LumineDesign.ConfigureSecondaryButton(toggle);
            AttachAsync(
                toggle,
                () => toggleEnabled(library));

            var remove =
                new Button
                {
                    Content = "登録解除",
                    FontSize = 10,
                    Padding = new Thickness(8, 4),
                    Foreground = LumineDesign.Danger
                };
            LumineDesign.ConfigureSecondaryButton(remove);
            remove.Foreground = LumineDesign.Danger;
            AttachAsync(
                remove,
                () => removeLibrary(library));

            var actions =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    Spacing = 6
                };
            actions.Children.Add(open);
            actions.Children.Add(toggle);
            actions.Children.Add(remove);

            var content =
                new StackPanel
                {
                    Spacing = 5
                };
            content.Children.Add(title);
            content.Children.Add(detail);
            content.Children.Add(path);

            if (!string.IsNullOrWhiteSpace(
                    library.SyncError))
            {
                content.Children.Add(
                    new TextBlock
                    {
                        Text = library.SyncError,
                        Foreground = LumineDesign.Warning,
                        FontSize = 9.5,
                        TextWrapping = TextWrapping.Wrap
                    });
            }

            content.Children.Add(actions);

            stack.Children.Add(
                CreateCard(
                    content,
                    isActive));
        }

        return CreateScroll(stack);
    }

    public static Control CreateFolders(
        IReadOnlyList<LibraryFolderInfo> folders,
        string? selectedFolder,
        Func<string?, Task> selectFolder)
    {
        var stack = CreateListStack();

        var all =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content =
                        selectedFolder is null
                            ? "✓ すべてのフォルダー"
                            : "すべてのフォルダー"
                });
        AttachAsync(
            all,
            () => selectFolder(null));
        stack.Children.Add(all);

        if (folders.Count == 0)
        {
            stack.Children.Add(
                CreateHint(
                    "サブフォルダーはありません。"));
        }

        foreach (var folder in folders)
        {
            var selected =
                string.Equals(
                    selectedFolder,
                    folder.RelativePath,
                    StringComparison.OrdinalIgnoreCase);
            var leaf =
                folder.RelativePath
                    .Split('/')
                    .LastOrDefault()
                ?? folder.RelativePath;

            var button =
                new Button
                {
                    HorizontalContentAlignment =
                        HorizontalAlignment.Stretch,
                    Padding =
                        new Thickness(
                            8 + ((folder.Depth - 1) * 12),
                            6,
                            8,
                            6),
                    Background =
                        selected
                            ? LumineDesign.AccentMuted
                            : Brushes.Transparent,
                    BorderBrush =
                        selected
                            ? LumineDesign.Border
                            : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7)
                };

            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions("*,Auto")
                };
            row.Children.Add(
                new TextBlock
                {
                    Text =
                        selected
                            ? $"✓ {leaf}"
                            : leaf,
                    Foreground =
                        selected
                            ? LumineDesign.Foreground
                            : LumineDesign.MutedForeground,
                    FontSize = 11,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis
                });
            var count =
                new TextBlock
                {
                    Text =
                        folder.DirectAssetCount.ToString("N0"),
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize = 9.5
                };
            Grid.SetColumn(count, 1);
            row.Children.Add(count);
            button.Content = row;

            AttachAsync(
                button,
                () => selectFolder(
                    folder.RelativePath));
            stack.Children.Add(button);
        }

        return CreateScroll(stack);
    }

    public static Control CreateTags(
        IReadOnlyList<LibraryTagInfo> tags,
        string? selectedTag,
        Func<string?, Task> selectTag)
    {
        var root =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,*")
            };

        var search =
            new TextBox
            {
                Watermark = "タグを検索…",
                Margin = new Thickness(0, 0, 0, 8)
            };
        root.Children.Add(search);

        var list = CreateListStack();
        var scroll = CreateScroll(list);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        void Render(string? filter)
        {
            list.Children.Clear();

            var all =
                LumineDesign.ConfigureSecondaryButton(
                    new Button
                    {
                        Content =
                            selectedTag is null
                                ? "✓ すべてのタグ"
                                : "タグ絞り込みを解除"
                    });
            AttachAsync(
                all,
                () => selectTag(null));
            list.Children.Add(all);

            var visible = tags.Where(
                tag =>
                    string.IsNullOrWhiteSpace(filter)
                    || tag.Name.Contains(
                        filter.Trim(),
                        StringComparison.OrdinalIgnoreCase));

            var count = 0;
            foreach (var tag in visible)
            {
                count++;
                var selected =
                    string.Equals(
                        selectedTag,
                        tag.Name,
                        StringComparison.OrdinalIgnoreCase);
                var button =
                    new Button
                    {
                        HorizontalContentAlignment =
                            HorizontalAlignment.Stretch,
                        Padding = new Thickness(8, 6),
                        Background =
                            selected
                                ? LumineDesign.AccentMuted
                                : Brushes.Transparent,
                        BorderBrush =
                            selected
                                ? LumineDesign.Border
                                : Brushes.Transparent,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(7)
                    };
                var row =
                    new Grid
                    {
                        ColumnDefinitions =
                            new ColumnDefinitions("*,Auto")
                    };
                row.Children.Add(
                    new TextBlock
                    {
                        Text =
                            selected
                                ? $"✓ {tag.Name}"
                                : tag.Name,
                        Foreground =
                            selected
                                ? LumineDesign.Foreground
                                : LumineDesign.MutedForeground,
                        FontSize = 11,
                        TextTrimming =
                            TextTrimming.CharacterEllipsis
                    });
                var assetCount =
                    new TextBlock
                    {
                        Text =
                            tag.AssetCount.ToString("N0"),
                        Foreground =
                            LumineDesign.MutedForeground,
                        FontSize = 9.5
                    };
                Grid.SetColumn(assetCount, 1);
                row.Children.Add(assetCount);
                button.Content = row;
                AttachAsync(
                    button,
                    () => selectTag(tag.Name));
                list.Children.Add(button);
            }

            if (count == 0)
            {
                list.Children.Add(
                    CreateHint(
                        "一致するタグはありません。"));
            }
        }

        search.TextChanged +=
            (_, _) => Render(search.Text);
        Render(null);

        return root;
    }

    public static Control CreatePublicationEntry(
        IReadOnlyList<PublicationInfo> publications)
    {
        var stack = CreateListStack();

        stack.Children.Add(
            CreateHint(
                "公開した時点のtitle / body / tags / destination / ordered imagesをsnapshotとして保持します。ローカルのタグやノートを後で変更しても、この履歴は変わりません。"));

        if (publications.Count == 0)
        {
            stack.Children.Add(
                CreateHint(
                    "公開履歴はまだありません。画像を選択し、上部のPublicationから記録できます。"));
            return CreateScroll(stack);
        }

        foreach (var publication in publications)
        {
            var content =
                new StackPanel
                {
                    Spacing = 5
                };
            content.Children.Add(
                new TextBlock
                {
                    Text =
                        publication.PublishedAtUtc
                            .ToLocalTime()
                            .ToString("yyyy-MM-dd HH:mm")
                        + $" · {publication.Destination}"
                        + (string.IsNullOrWhiteSpace(publication.Account)
                            ? string.Empty
                            : $" · {publication.Account}"),
                    Foreground = LumineDesign.MutedForeground,
                    FontSize = 9.5
                });

            if (!string.IsNullOrWhiteSpace(publication.Title))
            {
                content.Children.Add(
                    new TextBlock
                    {
                        Text = publication.Title,
                        Foreground = LumineDesign.Foreground,
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap
                    });
            }

            if (!string.IsNullOrWhiteSpace(publication.Body))
            {
                content.Children.Add(
                    new TextBlock
                    {
                        Text = publication.Body,
                        Foreground = LumineDesign.Foreground,
                        FontSize = 10,
                        MaxHeight = 72,
                        TextWrapping = TextWrapping.Wrap
                    });
            }

            if (!string.IsNullOrWhiteSpace(publication.TagsSnapshot))
            {
                content.Children.Add(
                    new TextBlock
                    {
                        Text = publication.TagsSnapshot,
                        Foreground = LumineDesign.Accent,
                        FontSize = 9.5,
                        TextWrapping = TextWrapping.Wrap
                    });
            }

            content.Children.Add(
                new TextBlock
                {
                    Text =
                        "画像: "
                        + string.Join(
                            " → ",
                            publication.Assets.Select(
                                static asset => asset.FileName)),
                    Foreground = LumineDesign.MutedForeground,
                    FontSize = 9.5,
                    TextWrapping = TextWrapping.Wrap
                });

            if (!string.IsNullOrWhiteSpace(publication.ExternalUrl))
            {
                content.Children.Add(
                    new TextBlock
                    {
                        Text = publication.ExternalUrl,
                        Foreground = LumineDesign.MutedForeground,
                        FontSize = 9,
                        TextWrapping = TextWrapping.Wrap
                    });
            }

            stack.Children.Add(
                CreateCard(
                    content,
                    selected: false));
        }

        return CreateScroll(stack);
    }

    public static Control CreateNoLibrary(
        string destination) =>
        CreatePlaceholder(
            destination,
            "先にライブラリを追加または選択してください。");

    private static StackPanel CreateListStack() =>
        new()
        {
            Spacing = 7
        };

    private static Control CreateScroll(
        Control child) =>
        new ScrollViewer
        {
            Content = child,
            HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

    private static Border CreateCard(
        Control child,
        bool selected) =>
        new()
        {
            Padding = new Thickness(9),
            Background =
                selected
                    ? LumineDesign.AccentMuted
                    : LumineDesign.SurfaceRaised,
            BorderBrush = LumineDesign.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = child
        };

    private static TextBlock CreateHint(
        string text) =>
        new()
        {
            Text = text,
            Foreground = LumineDesign.MutedForeground,
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4)
        };

    private static Control CreatePlaceholder(
        string title,
        string description)
    {
        var stack =
            new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(2, 8)
            };
        stack.Children.Add(
            new TextBlock
            {
                Text = title,
                Foreground = LumineDesign.Foreground,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12
            });
        stack.Children.Add(
            CreateHint(description));
        return stack;
    }

    private static string DescribeScanState(
        LibraryScanState state) =>
        state switch
        {
            LibraryScanState.Unknown => "未スキャン",
            LibraryScanState.InProgress => "読み込み中",
            LibraryScanState.Complete => "同期済み",
            LibraryScanState.Partial => "一部読み込み",
            _ => "状態不明"
        };

    private static void AttachAsync(
        Button button,
        Func<Task> action)
    {
        button.Click +=
            async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    await action();
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
    }
}
