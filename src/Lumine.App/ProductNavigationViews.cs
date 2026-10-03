using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
        Func<LibraryCatalogItem, Task> removeLibrary,
        Action<string>? reportError = null)
    {
        var stack = CreateListStack();

        var add =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "画像フォルダーを追加"
                });
        AttachAsync(add, addLibrary, reportError);
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
            var canOpen =
                library.IsEnabled
                && rootAvailable;

            var title =
                new TextBlock
                {
                    Text = library.Name,
                    Foreground = LumineDesign.Foreground,
                    FontWeight =
                        isActive
                            ? FontWeight.Bold
                            : FontWeight.SemiBold,
                    FontSize = LumineDesign.CaptionFontSize,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            var stateDot =
                new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius =
                        new CornerRadius(4),
                    Background =
                        !rootAvailable
                            ? LumineDesign.Warning
                            : library.IsEnabled
                                ? LumineDesign.Focus
                                : LumineDesign.MutedForeground,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            var activeLabel =
                new TextBlock
                {
                    Text =
                        isActive
                            ? "表示中"
                            : string.Empty,
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize = LumineDesign.CaptionFontSize,
                    FontWeight =
                        FontWeight.SemiBold,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            var heading =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "Auto,*,Auto"),
                    ColumnSpacing = 7
                };
            heading.Children.Add(stateDot);
            Grid.SetColumn(title, 1);
            heading.Children.Add(title);
            Grid.SetColumn(activeLabel, 2);
            heading.Children.Add(activeLabel);

            var path =
                new TextBlock
                {
                    Text = library.RootPath,
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize = LumineDesign.CaptionFontSize,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis,
                    Margin =
                        new Thickness(15, 2, 0, 0)
                };

            var detail =
                new TextBlock
                {
                    Text =
                        $"{library.AssetCount:N0}件 · {DescribeScanState(library.ScanState)}"
                        + (rootAvailable
                            ? string.Empty
                            : " · オフライン")
                        + (library.IsEnabled
                            ? string.Empty
                            : " · 無効"),
                    Foreground =
                        rootAvailable
                            ? LumineDesign.MutedForeground
                            : LumineDesign.Warning,
                    FontSize = LumineDesign.CaptionFontSize,
                    Margin =
                        new Thickness(15, 2, 0, 0)
                };

            var primaryContent =
                new StackPanel
                {
                    Spacing = 1
                };
            primaryContent.Children.Add(heading);
            primaryContent.Children.Add(path);
            primaryContent.Children.Add(detail);

            Control primary;
            if (isActive)
            {
                primary =
                    new Border
                    {
                        Child = primaryContent,
                        Background =
                            LumineDesign.AccentMuted,
                        BorderBrush =
                            LumineDesign.BorderStrong,
                        BorderThickness =
                            new Thickness(1),
                        CornerRadius =
                            new CornerRadius(LumineDesign.PanelRadius),
                        Padding =
                            new Thickness(LumineDesign.Space8)
                    };
            }
            else
            {
                var open =
                    new Button
                    {
                        Content = primaryContent,
                        HorizontalContentAlignment =
                            HorizontalAlignment.Stretch,
                        Background =
                            LumineDesign.SurfaceRaised,
                        BorderBrush =
                            LumineDesign.Border,
                        BorderThickness =
                            new Thickness(1),
                        CornerRadius =
                            new CornerRadius(LumineDesign.PanelRadius),
                        Padding =
                            new Thickness(LumineDesign.Space8),
                        IsEnabled = canOpen
                    };

                if (canOpen)
                {
                    AttachAsync(
                        open,
                        () => openLibrary(library),
                        reportError);
                }

                primary = open;
            }

            var libraryRow =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = LumineDesign.Space6
                };
            libraryRow.Children.Add(primary);

            var manageButton =
                LumineDesign.ConfigureIconButton(
                    new Button
                    {
                        Content =
                            LumineDesign.CreateStrokeIcon(
                                LumineDesign.MoreIconPath,
                                16)
                    },
                    "ライブラリを管理");
            manageButton.VerticalAlignment =
                VerticalAlignment.Center;

            var manageMenu =
                new ContextMenu();

            var toggle =
                new MenuItem
                {
                    Header =
                        library.IsEnabled
                            ? "ライブラリを無効化"
                            : "ライブラリを有効化"
                };
            AttachAsync(
                toggle,
                () => toggleEnabled(library),
                reportError);
            manageMenu.Items.Add(toggle);

            var remove =
                new MenuItem
                {
                    Header = "登録解除…"
                };
            AttachAsync(
                remove,
                () => removeLibrary(library),
                reportError);
            manageMenu.Items.Add(remove);

            manageButton.Click +=
                (_, _) =>
                    manageMenu.Open(manageButton);
            Grid.SetColumn(manageButton, 1);
            libraryRow.Children.Add(manageButton);
            stack.Children.Add(libraryRow);

            if (!string.IsNullOrWhiteSpace(
                    library.SyncError))
            {
                stack.Children.Add(
                    new TextBlock
                    {
                        Text = library.SyncError,
                        Foreground =
                            LumineDesign.Warning,
                        FontSize = LumineDesign.CaptionFontSize,
                        TextWrapping =
                            TextWrapping.Wrap,
                        Margin =
                            new Thickness(10, -3, 8, 2)
                    });
            }
        }

        return CreateScroll(stack);
    }

    public static Control CreateFolders(
        IReadOnlyList<LibraryFolderInfo> folders,
        string? selectedFolder,
        Func<string?, Task> selectFolder,
        Action<string>? reportError = null)
    {
        var root =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,*"),
                RowSpacing = LumineDesign.Space6
            };

        var all =
            new Button
            {
                Content = "すべての画像",
                HorizontalContentAlignment =
                    HorizontalAlignment.Left,
                Padding = new Thickness(LumineDesign.Space8, LumineDesign.Space6),
                Background =
                    selectedFolder is null
                        ? LumineDesign.AccentMuted
                        : Brushes.Transparent,
                BorderBrush =
                    selectedFolder is null
                        ? LumineDesign.BorderStrong
                        : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(LumineDesign.ControlRadius),
                Foreground =
                    selectedFolder is null
                        ? LumineDesign.Foreground
                        : LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize
            };
        AttachAsync(
            all,
            () => selectFolder(null),
            reportError);
        root.Children.Add(all);

        Control body;
        if (folders.Count == 0)
        {
            body =
                CreateHint(
                    "サブフォルダーはありません。");
        }
        else
        {
            var list =
                new ListBox
                {
                    ItemsSource = folders,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0)
                };
            list.ItemTemplate =
                new FuncDataTemplate<LibraryFolderInfo>(
                    (folder, _) =>
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

                        var row =
                            new Grid
                            {
                                ColumnDefinitions =
                                    new ColumnDefinitions("*,Auto")
                            };
                        row.Children.Add(
                            new TextBlock
                            {
                                Text = leaf,
                                Foreground =
                                    selected
                                        ? LumineDesign.Foreground
                                        : LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextTrimming =
                                    TextTrimming.CharacterEllipsis
                            });
                        var count =
                            new TextBlock
                            {
                                Text =
                                    folder.DirectAssetCount
                                        .ToString("N0"),
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize
                            };
                        Grid.SetColumn(count, 1);
                        row.Children.Add(count);

                        var button =
                            new Button
                            {
                                Content = row,
                                HorizontalAlignment =
                                    HorizontalAlignment.Stretch,
                                HorizontalContentAlignment =
                                    HorizontalAlignment.Stretch,
                                Padding =
                                    new Thickness(
                                        8
                                        + (Math.Max(0, folder.Depth - 1) * 12),
                                        7,
                                        8,
                                        7),
                                Background =
                                    selected
                                        ? LumineDesign.AccentMuted
                                        : Brushes.Transparent,
                                BorderBrush =
                                    selected
                                        ? LumineDesign.BorderStrong
                                        : Brushes.Transparent,
                                BorderThickness =
                                    new Thickness(1),
                                CornerRadius =
                                    new CornerRadius(LumineDesign.ControlRadius)
                            };
                        AttachAsync(
                            button,
                            () => selectFolder(
                                folder.RelativePath),
                            reportError);
                        return button;
                    },
                    supportsRecycling: true);
            body = list;
        }

        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    public static Control CreateTags(
        IReadOnlyList<LibraryTagInfo> tags,
        IReadOnlyList<string> selectedTags,
        Func<string?, Task> selectTag,
        Func<string, string, Task> createTag,
        Func<LibraryTagInfo, Task> deleteTag,
        Action<string>? reportError = null)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(selectedTags);
        ArgumentNullException.ThrowIfNull(selectTag);
        ArgumentNullException.ThrowIfNull(createTag);
        ArgumentNullException.ThrowIfNull(deleteTag);

        var selected =
            new HashSet<string>(
                selectedTags,
                StringComparer.OrdinalIgnoreCase);
        var manageMode = false;

        var root =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,Auto,Auto,*"),
                RowSpacing =
                    LumineDesign.Space8
            };

        var countText =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var add =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "＋ 新規",
                    MinHeight =
                        LumineDesign.CompactCommandHeight,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space4)
                });

        var manage =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "管理",
                    MinHeight =
                        LumineDesign.CompactCommandHeight,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space4)
                });

        var toolbar =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing =
                    LumineDesign.Space6
            };
        toolbar.Children.Add(countText);
        Grid.SetColumn(add, 1);
        toolbar.Children.Add(add);
        Grid.SetColumn(manage, 2);
        toolbar.Children.Add(manage);
        root.Children.Add(toolbar);

        var search =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText =
                        "タグを検索",
                    MinHeight =
                        LumineDesign.CompactCommandHeight,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space4)
                });
        Grid.SetRow(search, 1);
        root.Children.Add(search);

        var createName =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText =
                        "新しいタグ名"
                });

        var selectedColor =
            "#6366f1";
        var colorPreview =
            new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius =
                    new CornerRadius(12),
                Background =
                    new SolidColorBrush(
                        Color.Parse(
                            selectedColor)),
                BorderBrush =
                    LumineDesign.BorderStrong,
                BorderThickness =
                    new Thickness(1)
            };

        var palette =
            new WrapPanel();
        var paletteColors =
            new[]
            {
                "#6366f1",
                "#ef4444",
                "#f97316",
                "#eab308",
                "#22c55e",
                "#06b6d4",
                "#3b82f6",
                "#ec4899",
                "#8b5cf6",
                "#71717a"
            };
        foreach (var color in paletteColors)
        {
            var paletteButton =
                new Button
                {
                    Width = 24,
                    Height = 24,
                    MinWidth = 24,
                    MinHeight = 24,
                    Padding =
                        new Thickness(3),
                    Background =
                        Brushes.Transparent,
                    BorderBrush =
                        string.Equals(
                            color,
                            selectedColor,
                            StringComparison.Ordinal)
                            ? LumineDesign.InteractionFocus
                            : Brushes.Transparent,
                    BorderThickness =
                        new Thickness(2),
                    CornerRadius =
                        new CornerRadius(12),
                    Margin =
                        new Thickness(
                            0,
                            0,
                            LumineDesign.Space4,
                            LumineDesign.Space4),
                    Content =
                        new Border
                        {
                            Width = 14,
                            Height = 14,
                            CornerRadius =
                                new CornerRadius(7),
                            Background =
                                new SolidColorBrush(
                                    Color.Parse(color))
                        }
                };
            ToolTip.SetTip(
                paletteButton,
                color);
            var colorValue = color;
            paletteButton.Click +=
                (_, _) =>
                {
                    selectedColor =
                        colorValue;
                    colorPreview.Background =
                        new SolidColorBrush(
                            Color.Parse(
                                selectedColor));
                    foreach (var child in
                             palette.Children
                                 .OfType<Button>())
                    {
                        child.BorderBrush =
                            string.Equals(
                                ToolTip.GetTip(child)
                                    as string,
                                selectedColor,
                                StringComparison.Ordinal)
                                ? LumineDesign.InteractionFocus
                                : Brushes.Transparent;
                    }
                };
            palette.Children.Add(
                paletteButton);
        }

        var createAction =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "作成",
                    MinWidth = 68
                });

        var cancelCreate =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "キャンセル"
                });

        var createActions =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing =
                    LumineDesign.Space6,
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };
        createActions.Children.Add(
            cancelCreate);
        createActions.Children.Add(
            createAction);

        var colorRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("Auto,*"),
                ColumnSpacing =
                    LumineDesign.Space8
            };
        colorRow.Children.Add(
            colorPreview);
        Grid.SetColumn(
            palette,
            1);
        colorRow.Children.Add(
            palette);

        var createBody =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space8
            };
        createBody.Children.Add(
            new TextBlock
            {
                Text = "タグを作成",
                Foreground =
                    LumineDesign.Foreground,
                FontWeight =
                    FontWeight.SemiBold,
                FontSize =
                    LumineDesign.BodyFontSize
            });
        createBody.Children.Add(
            createName);
        createBody.Children.Add(
            colorRow);
        createBody.Children.Add(
            createActions);

        var createSurface =
            new Border
            {
                IsVisible = false,
                Background =
                    LumineDesign.SurfaceRaised,
                BorderBrush =
                    LumineDesign.Border,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.PanelRadius),
                Padding =
                    new Thickness(
                        LumineDesign.Space12),
                Child = createBody
            };
        Grid.SetRow(
            createSurface,
            2);
        root.Children.Add(
            createSurface);

        var list =
            new ListBox
            {
                Background =
                    Brushes.Transparent,
                BorderThickness =
                    new Thickness(0),
                Padding =
                    new Thickness(0)
            };

        var empty =
            CreateHint(
                "一致するタグはありません。");
        empty.IsVisible = false;

        Grid.SetRow(
            list,
            3);
        Grid.SetRow(
            empty,
            3);
        root.Children.Add(
            list);
        root.Children.Add(
            empty);

        void UpdateToolbar()
        {
            countText.Text =
                selected.Count == 0
                    ? $"{tags.Count:N0}件"
                    : $"{selected.Count:N0}件選択 / {tags.Count:N0}件";
            manage.Content =
                manageMode
                    ? "完了"
                    : "管理";
            manage.Background =
                manageMode
                    ? LumineDesign.AccentMuted
                    : LumineDesign.ControlSurface;
        }

        void ApplyFilter(
            string? filter)
        {
            var normalized =
                filter?.Trim();
            var visible =
                tags.Where(
                        tag =>
                            string.IsNullOrWhiteSpace(
                                normalized)
                            || tag.Name.Contains(
                                normalized,
                                StringComparison
                                    .OrdinalIgnoreCase))
                    .OrderByDescending(
                        tag =>
                            selected.Contains(
                                tag.Name))
                    .ThenByDescending(
                        tag =>
                            tag.AssetCount)
                    .ThenBy(
                        tag =>
                            tag.Name,
                        StringComparer
                            .OrdinalIgnoreCase)
                    .ToArray();

            list.ItemTemplate =
                new FuncDataTemplate<LibraryTagInfo>(
                    (tag, _) =>
                    {
                        var isSelected =
                            selected.Contains(
                                tag.Name);

                        var colorDot =
                            new Border
                            {
                                Width = 10,
                                Height = 10,
                                CornerRadius =
                                    new CornerRadius(5),
                                Background =
                                    new SolidColorBrush(
                                        Color.Parse(
                                            tag.Color)),
                                VerticalAlignment =
                                    VerticalAlignment
                                        .Center
                            };

                        var name =
                            new TextBlock
                            {
                                Text = tag.Name,
                                Foreground =
                                    isSelected
                                        ? LumineDesign
                                            .Foreground
                                        : LumineDesign
                                            .MutedForeground,
                                FontSize =
                                    LumineDesign
                                        .CaptionFontSize,
                                TextTrimming =
                                    TextTrimming
                                        .CharacterEllipsis,
                                VerticalAlignment =
                                    VerticalAlignment
                                        .Center
                            };

                        var count =
                            new TextBlock
                            {
                                Text =
                                    tag.AssetCount
                                        .ToString("N0"),
                                Foreground =
                                    LumineDesign
                                        .MutedForeground,
                                FontSize =
                                    LumineDesign
                                        .CaptionFontSize,
                                VerticalAlignment =
                                    VerticalAlignment
                                        .Center
                            };

                        var content =
                            new Grid
                            {
                                ColumnDefinitions =
                                    new ColumnDefinitions(
                                        "Auto,*,Auto,Auto"),
                                ColumnSpacing =
                                    LumineDesign.Space6
                            };
                        content.Children.Add(
                            colorDot);
                        Grid.SetColumn(
                            name,
                            1);
                        content.Children.Add(
                            name);
                        Grid.SetColumn(
                            count,
                            2);
                        content.Children.Add(
                            count);

                        var button =
                            new Button
                            {
                                Content = content,
                                HorizontalAlignment =
                                    HorizontalAlignment
                                        .Stretch,
                                HorizontalContentAlignment =
                                    HorizontalAlignment
                                        .Stretch,
                                Padding =
                                    new Thickness(
                                        LumineDesign.Space8,
                                        LumineDesign.Space6),
                                Background =
                                    isSelected
                                        ? LumineDesign
                                            .AccentMuted
                                        : Brushes.Transparent,
                                BorderBrush =
                                    isSelected
                                        ? LumineDesign
                                            .BorderStrong
                                        : Brushes.Transparent,
                                BorderThickness =
                                    new Thickness(1),
                                CornerRadius =
                                    new CornerRadius(
                                        LumineDesign
                                            .ControlRadius)
                            };

                        if (manageMode)
                        {
                            var remove =
                                LumineDesign
                                    .ConfigureSecondaryButton(
                                        new Button
                                        {
                                            Content =
                                                "削除",
                                            MinHeight = 26,
                                            Padding =
                                                new Thickness(
                                                    LumineDesign.Space6,
                                                    LumineDesign.Space2),
                                            FontSize =
                                                LumineDesign
                                                    .CaptionFontSize
                                        });
                            remove.Foreground =
                                LumineDesign.Danger;
                            Grid.SetColumn(
                                remove,
                                3);
                            content.Children.Add(
                                remove);
                            AttachAsync(
                                remove,
                                () => deleteTag(tag),
                                reportError);
                        }
                        else
                        {
                            AttachAsync(
                                button,
                                async () =>
                                {
                                    if (!selected.Add(
                                            tag.Name))
                                    {
                                        selected.Remove(
                                            tag.Name);
                                    }

                                    UpdateToolbar();
                                    ApplyFilter(
                                        search.Text);
                                    await selectTag(
                                        tag.Name);
                                },
                                reportError);
                        }

                        return button;
                    },
                    supportsRecycling: true);

            list.ItemsSource =
                visible;
            list.IsVisible =
                visible.Length > 0;
            empty.IsVisible =
                visible.Length == 0;
            UpdateToolbar();
        }

        add.Click +=
            (_, _) =>
            {
                createSurface.IsVisible =
                    !createSurface.IsVisible;
                if (createSurface.IsVisible)
                {
                    createName.Focus();
                }
            };

        cancelCreate.Click +=
            (_, _) =>
            {
                createName.Text =
                    string.Empty;
                createSurface.IsVisible =
                    false;
            };

        AttachAsync(
            createAction,
            async () =>
            {
                var name =
                    createName.Text?.Trim();
                if (string.IsNullOrWhiteSpace(
                        name))
                {
                    createName.Focus();
                    return;
                }

                await createTag(
                    name,
                    selectedColor);
                createName.Text =
                    string.Empty;
                createSurface.IsVisible =
                    false;
            },
            reportError);

        manage.Click +=
            (_, _) =>
            {
                manageMode =
                    !manageMode;
                ApplyFilter(
                    search.Text);
            };

        search.TextChanged +=
            (_, _) =>
                ApplyFilter(
                    search.Text);

        var clearSelection =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "選択を解除",
                    MinHeight =
                        LumineDesign.CompactCommandHeight,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space4)
                });
        clearSelection.IsVisible =
            selected.Count > 0;
        toolbar.ColumnDefinitions =
            new ColumnDefinitions(
                "*,Auto,Auto,Auto");
        Grid.SetColumn(
            clearSelection,
            1);
        Grid.SetColumn(
            add,
            2);
        Grid.SetColumn(
            manage,
            3);
        toolbar.Children.Add(
            clearSelection);
        clearSelection.Click +=
            async (_, _) =>
            {
                selected.Clear();
                clearSelection.IsVisible =
                    false;
                UpdateToolbar();
                ApplyFilter(
                    search.Text);
                await selectTag(null);
            };

        void RefreshClearVisibility() =>
            clearSelection.IsVisible =
                selected.Count > 0;

        list.PropertyChanged +=
            (_, _) =>
                RefreshClearVisibility();

        ApplyFilter(null);
        RefreshClearVisibility();
        return root;
    }

    public static Control CreatePublicationEntry(
        IReadOnlyList<PublicationInfo> publications)
    {
        if (publications.Count == 0)
        {
            return CreatePlaceholder(
                "公開履歴",
                "公開履歴はまだありません。");
        }

        var list =
            new ListBox
            {
                ItemsSource = publications,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
        list.ItemTemplate =
            new FuncDataTemplate<PublicationInfo>(
                (publication, _) =>
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
                                + (string.IsNullOrWhiteSpace(
                                        publication.Account)
                                    ? string.Empty
                                    : $" · {publication.Account}"),
                            Foreground =
                                LumineDesign.MutedForeground,
                            FontSize =
                                LumineDesign.CaptionFontSize
                        });

                    if (!string.IsNullOrWhiteSpace(
                            publication.Title))
                    {
                        content.Children.Add(
                            new TextBlock
                            {
                                Text = publication.Title,
                                Foreground =
                                    LumineDesign.Foreground,
                                FontWeight =
                                    FontWeight.SemiBold,
                                FontSize =
                                    LumineDesign.BodyFontSize,
                                TextWrapping =
                                    TextWrapping.Wrap
                            });
                    }

                    if (!string.IsNullOrWhiteSpace(
                            publication.Body))
                    {
                        content.Children.Add(
                            new TextBlock
                            {
                                Text = publication.Body,
                                Foreground =
                                    LumineDesign.Foreground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                MaxHeight = 72,
                                TextWrapping =
                                    TextWrapping.Wrap
                            });
                    }

                    if (!string.IsNullOrWhiteSpace(
                            publication.TagsSnapshot))
                    {
                        content.Children.Add(
                            new TextBlock
                            {
                                Text =
                                    publication.TagsSnapshot,
                                Foreground =
                                    LumineDesign.Accent,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextWrapping =
                                    TextWrapping.Wrap
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
                                        static asset =>
                                            asset.FileName)),
                            Foreground =
                                LumineDesign.MutedForeground,
                            FontSize =
                                LumineDesign.CaptionFontSize,
                            TextWrapping =
                                TextWrapping.Wrap
                        });

                    if (!string.IsNullOrWhiteSpace(
                            publication.ExternalUrl))
                    {
                        content.Children.Add(
                            new TextBlock
                            {
                                Text =
                                    publication.ExternalUrl,
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextWrapping =
                                    TextWrapping.Wrap
                            });
                    }

                    return CreateCard(
                        content,
                        selected: false);
                },
                supportsRecycling: true);
        return list;
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
            CornerRadius = new CornerRadius(LumineDesign.PanelRadius),
            Child = child
        };

    private static TextBlock CreateHint(
        string text) =>
        new()
        {
            Text = text,
            Foreground = LumineDesign.MutedForeground,
            FontSize = LumineDesign.CaptionFontSize,
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
                FontSize = LumineDesign.BodyFontSize
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
        MenuItem item,
        Func<Task> action,
        Action<string>? reportError = null)
    {
        item.Click +=
            async (_, _) =>
                await ExecuteAsync(
                    item,
                    action,
                    reportError);
    }

    private static void AttachAsync(
        Button button,
        Func<Task> action,
        Action<string>? reportError = null)
    {
        button.Click +=
            async (_, _) =>
                await ExecuteAsync(
                    button,
                    action,
                    reportError);
    }

    private static async Task ExecuteAsync(
        Control command,
        Func<Task> action,
        Action<string>? reportError)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(action);

        command.IsEnabled = false;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                exception.ToString());
            reportError?.Invoke(
                "操作を完了できませんでした。"
                + (string.IsNullOrWhiteSpace(exception.Message)
                    ? string.Empty
                    : $" {exception.Message}"));
        }
        finally
        {
            command.IsEnabled = true;
        }
    }

    internal static Task ExecuteAsyncForSmoke(
        Control command,
        Func<Task> action,
        Action<string>? reportError = null) =>
        ExecuteAsync(
            command,
            action,
            reportError);
}
