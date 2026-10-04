using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
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
        Func<LibraryCatalogItem, Task> rescanLibrary,
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

                LumineDesign.ConfigureNeutralButtonStateResources(
                    open);
                AutomationProperties.SetName(
                    open,
                    $"ライブラリを開く: {library.Name}");

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
                        new ColumnDefinitions("*,Auto,Auto"),
                    ColumnSpacing = LumineDesign.Space6
                };
            libraryRow.Children.Add(primary);

            if (isActive
                && library.IsEnabled
                && rootAvailable)
            {
                var rescan =
                    LumineDesign.ConfigureSecondaryButton(
                        new Button
                        {
                            Content = "再スキャン",
                            MinHeight =
                                LumineDesign.CompactCommandHeight,
                            Padding =
                                new Thickness(
                                    LumineDesign.Space8,
                                    LumineDesign.Space4),
                            VerticalAlignment =
                                VerticalAlignment.Center
                        });
                ToolTip.SetTip(
                    rescan,
                    "現在のライブラリを再スキャン");
                AttachAsync(
                    rescan,
                    () => rescanLibrary(library),
                    reportError);
                Grid.SetColumn(rescan, 1);
                libraryRow.Children.Add(rescan);
            }

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

            manageMenu.Items.Add(
                new Separator());

            var remove =
                new MenuItem
                {
                    Header = "登録解除…",
                    Foreground =
                        LumineDesign.Danger
                };
            AttachAsync(
                remove,
                () => removeLibrary(library),
                reportError);
            manageMenu.Items.Add(remove);

            manageButton.Click +=
                (_, _) =>
                    manageMenu.Open(manageButton);
            Grid.SetColumn(manageButton, 2);
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
        ISet<string> expandedFolders,
        Func<string?, Task> selectFolder,
        Action<string>? reportError = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(expandedFolders);
        ArgumentNullException.ThrowIfNull(selectFolder);

        static string? GetParentPath(
            string relativePath)
        {
            var normalized =
                relativePath.Replace(
                    '\\',
                    '/');
            var separator =
                normalized.LastIndexOf('/');
            return separator <= 0
                ? null
                : normalized[..separator];
        }

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
                Padding =
                    new Thickness(
                        LumineDesign.Space8,
                        LumineDesign.Space6),
                Background =
                    selectedFolder is null
                        ? LumineDesign.AccentMuted
                        : Brushes.Transparent,
                BorderBrush =
                    selectedFolder is null
                        ? LumineDesign.BorderStrong
                        : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.ControlRadius),
                Foreground =
                    selectedFolder is null
                        ? LumineDesign.Foreground
                        : LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            };
        if (selectedFolder is null)
        {
            LumineDesign.ConfigureSelectedButtonStateResources(
                all);
        }
        else
        {
            LumineDesign.ConfigureNeutralButtonStateResources(
                all);
        }
        AutomationProperties.SetName(
            all,
            "すべての画像");
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
            var pathSet =
                new HashSet<string>(
                    folders.Select(
                        static folder =>
                            folder.RelativePath.Replace(
                                '\\',
                                '/')),
                    StringComparer.OrdinalIgnoreCase);

            var roots =
                new List<LibraryFolderInfo>();
            var children =
                new Dictionary<
                    string,
                    List<LibraryFolderInfo>>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (var folder in folders)
            {
                var normalized =
                    folder.RelativePath.Replace(
                        '\\',
                        '/');
                var parent =
                    GetParentPath(normalized);
                if (parent is null
                    || !pathSet.Contains(parent))
                {
                    roots.Add(folder);
                    continue;
                }

                if (!children.TryGetValue(
                        parent,
                        out var bucket))
                {
                    bucket = [];
                    children[parent] = bucket;
                }

                bucket.Add(folder);
            }

            roots.Sort(
                static (left, right) =>
                    StringComparer.CurrentCultureIgnoreCase
                        .Compare(
                            left.RelativePath,
                            right.RelativePath));
            foreach (var bucket in children.Values)
            {
                bucket.Sort(
                    static (left, right) =>
                        StringComparer.CurrentCultureIgnoreCase
                            .Compare(
                                left.RelativePath,
                                right.RelativePath));
            }

            if (!string.IsNullOrWhiteSpace(
                    selectedFolder))
            {
                var parent =
                    GetParentPath(
                        selectedFolder.Replace(
                            '\\',
                            '/'));
                while (parent is not null)
                {
                    expandedFolders.Add(parent);
                    parent = GetParentPath(parent);
                }
            }

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

            IReadOnlyList<LibraryFolderInfo>
                BuildVisibleFolders()
            {
                var visible =
                    new List<LibraryFolderInfo>(
                        Math.Min(
                            folders.Count,
                            512));

                var stack =
                    new Stack<
                        (LibraryFolderInfo Folder, int State)>();

                for (var index =
                         roots.Count - 1;
                     index >= 0;
                     index--)
                {
                    stack.Push(
                        (roots[index], 0));
                }

                while (stack.Count > 0)
                {
                    var entry =
                        stack.Pop();
                    var folder =
                        entry.Folder;
                    visible.Add(folder);

                    var path =
                        folder.RelativePath.Replace(
                            '\\',
                            '/');
                    if (!expandedFolders.Contains(path)
                        || !children.TryGetValue(
                            path,
                            out var nested))
                    {
                        continue;
                    }

                    for (var index =
                             nested.Count - 1;
                         index >= 0;
                         index--)
                    {
                        stack.Push(
                            (nested[index], 0));
                    }
                }

                return visible;
            }

            void RebuildVisibleFolders()
            {
                list.ItemsSource = null;
                list.ItemsSource =
                    BuildVisibleFolders();
            }

            list.ItemTemplate =
                new FuncDataTemplate<LibraryFolderInfo>(
                    (folder, _) =>
                    {
                        var normalized =
                            folder.RelativePath.Replace(
                                '\\',
                                '/');
                        var selected =
                            string.Equals(
                                selectedFolder,
                                folder.RelativePath,
                                StringComparison.OrdinalIgnoreCase);
                        var hasChildren =
                            children.ContainsKey(
                                normalized);
                        var expanded =
                            hasChildren
                            && expandedFolders.Contains(
                                normalized);
                        var leaf =
                            normalized
                                .Split('/')
                                .LastOrDefault()
                            ?? normalized;

                        var row =
                            new Grid
                            {
                                ColumnDefinitions =
                                    new ColumnDefinitions(
                                        "Auto,*,Auto"),
                                ColumnSpacing =
                                    LumineDesign.Space4,
                                Margin =
                                    new Thickness(
                                        Math.Max(
                                            0,
                                            folder.Depth - 1)
                                        * 10,
                                        0,
                                        0,
                                        0)
                            };

                        var disclosure =
                            LumineDesign.ConfigureIconButton(
                                new Button
                                {
                                    Content =
                                        hasChildren
                                            ? expanded
                                                ? "▼"
                                                : "▶"
                                            : string.Empty,
                                    Width = 26,
                                    Height = 28,
                                    MinWidth = 26,
                                    MinHeight = 28,
                                    Padding =
                                        new Thickness(0),
                                    IsEnabled =
                                        hasChildren
                                },
                                hasChildren
                                    ? expanded
                                        ? $"{leaf} を閉じる"
                                        : $"{leaf} を開く"
                                    : $"{leaf} に子フォルダーはありません");
                        disclosure.Opacity =
                            hasChildren
                                ? 1
                                : 0;
                        disclosure.Click +=
                            (_, _) =>
                            {
                                if (!hasChildren)
                                {
                                    return;
                                }

                                if (!expandedFolders.Add(
                                        normalized))
                                {
                                    expandedFolders.Remove(
                                        normalized);
                                }

                                RebuildVisibleFolders();
                            };
                        row.Children.Add(disclosure);

                        var folderButton =
                            new Button
                            {
                                Content =
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
                                    },
                                HorizontalAlignment =
                                    HorizontalAlignment.Stretch,
                                HorizontalContentAlignment =
                                    HorizontalAlignment.Left,
                                Padding =
                                    new Thickness(
                                        LumineDesign.Space4,
                                        LumineDesign.Space6),
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
                                    new CornerRadius(
                                        LumineDesign.ControlRadius)
                            };
                        if (selected)
                        {
                            LumineDesign.ConfigureSelectedButtonStateResources(
                                folderButton);
                        }
                        else
                        {
                            LumineDesign.ConfigureNeutralButtonStateResources(
                                folderButton);
                        }
                        ToolTip.SetTip(
                            folderButton,
                            folder.RelativePath);
                        AutomationProperties.SetName(
                            folderButton,
                            $"フォルダー: {folder.RelativePath}");
                        AttachAsync(
                            folderButton,
                            () => selectFolder(
                                folder.RelativePath),
                            reportError);
                        Grid.SetColumn(
                            folderButton,
                            1);
                        row.Children.Add(
                            folderButton);

                        var count =
                            new TextBlock
                            {
                                Text =
                                    folder.DirectAssetCount
                                        .ToString("N0"),
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            };
                        Grid.SetColumn(count, 2);
                        row.Children.Add(count);

                        return row;
                    },
                    supportsRecycling: true);

            RebuildVisibleFolders();
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
        Func<LibraryTagInfo, string, string, Task> updateTag,
        Func<LibraryTagInfo, Task> deleteTag,
        Action<string>? reportError = null)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(selectedTags);
        ArgumentNullException.ThrowIfNull(selectTag);
        ArgumentNullException.ThrowIfNull(createTag);
        ArgumentNullException.ThrowIfNull(updateTag);
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
                    new RowDefinitions(
                        "Auto,Auto,*"),
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
                TextWrapping =
                    TextWrapping.Wrap
            };

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
                            LumineDesign.Space4),
                    IsVisible = false
                });

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

        var actionRow =
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Left
            };
        clearSelection.Margin =
            new Thickness(
                0,
                0,
                LumineDesign.Space6,
                LumineDesign.Space4);
        add.Margin =
            new Thickness(
                0,
                0,
                LumineDesign.Space6,
                LumineDesign.Space4);
        manage.Margin =
            new Thickness(
                0,
                0,
                0,
                LumineDesign.Space4);
        actionRow.Children.Add(
            clearSelection);
        actionRow.Children.Add(
            add);
        actionRow.Children.Add(
            manage);

        var toolbar =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space4
            };
        toolbar.Children.Add(
            countText);
        toolbar.Children.Add(
            actionRow);
        root.Children.Add(
            toolbar);

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
        Grid.SetRow(
            search,
            1);
        root.Children.Add(
            search);

        var createName =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText =
                        "新しいタグ名"
                });

        var colorEditor =
            new TagColorEditor();

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
            new WrapPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Right
            };
        cancelCreate.Margin =
            new Thickness(
                0,
                0,
                LumineDesign.Space6,
                LumineDesign.Space4);
        createAction.Margin =
            new Thickness(
                0,
                0,
                0,
                LumineDesign.Space4);
        createActions.Children.Add(
            cancelCreate);
        createActions.Children.Add(
            createAction);

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
            colorEditor);
        createBody.Children.Add(
            createActions);

        var createSurface =
            new Border
            {
                Width = 320,
                MaxWidth = 360,
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

        var createFlyout =
            new Flyout
            {
                Content = createSurface
            };
        add.Flyout =
            createFlyout;

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
            2);
        Grid.SetRow(
            empty,
            2);
        root.Children.Add(
            list);
        root.Children.Add(
            empty);

        void UpdateCreateActionState()
        {
            createAction.IsEnabled =
                createFlyout.IsOpen
                && !string.IsNullOrWhiteSpace(
                    createName.Text)
                && colorEditor.IsColorValid;
        }

        void UpdateToolbar()
        {
            countText.Text =
                selected.Count == 0
                    ? $"{tags.Count:N0}件"
                    : $"{selected.Count:N0}件選択 / {tags.Count:N0}件";
            clearSelection.IsVisible =
                selected.Count > 0;
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
                                MinWidth = 10,
                                MinHeight = 10,
                                CornerRadius =
                                    new CornerRadius(5),
                                Background =
                                    TagColor.ToBrush(
                                        tag.Color),
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            };

                        var name =
                            new TextBlock
                            {
                                Text = tag.Name,
                                Foreground =
                                    isSelected
                                        ? LumineDesign.Foreground
                                        : LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextTrimming =
                                    TextTrimming.CharacterEllipsis,
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            };

                        var count =
                            new TextBlock
                            {
                                Text =
                                    tag.AssetCount
                                        .ToString("N0"),
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            };

                        var content =
                            new Grid
                            {
                                ColumnDefinitions =
                                    new ColumnDefinitions(
                                        "Auto,*,Auto"),
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

                        if (!manageMode)
                        {
                            var button =
                                new Button
                                {
                                    Content = content,
                                    HorizontalAlignment =
                                        HorizontalAlignment.Stretch,
                                    HorizontalContentAlignment =
                                        HorizontalAlignment.Stretch,
                                    Padding =
                                        new Thickness(
                                            LumineDesign.Space8,
                                            LumineDesign.Space6),
                                    Background =
                                        isSelected
                                            ? LumineDesign.AccentMuted
                                            : Brushes.Transparent,
                                    BorderBrush =
                                        isSelected
                                            ? LumineDesign.BorderStrong
                                            : Brushes.Transparent,
                                    BorderThickness =
                                        new Thickness(1),
                                    CornerRadius =
                                        new CornerRadius(
                                            LumineDesign.ControlRadius)
                                };
                            if (isSelected)
                            {
                                LumineDesign.ConfigureSelectedButtonStateResources(
                                    button);
                            }
                            else
                            {
                                LumineDesign.ConfigureNeutralButtonStateResources(
                                    button);
                            }
                            AutomationProperties.SetName(
                                button,
                                $"タグ: {tag.Name}");
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
                            return button;
                        }

                        var row =
                            new Grid
                            {
                                ColumnDefinitions =
                                    new ColumnDefinitions(
                                        "*,Auto,Auto"),
                                ColumnSpacing =
                                    LumineDesign.Space6,
                                HorizontalAlignment =
                                    HorizontalAlignment.Stretch
                            };

                        var tagSurface =
                            new Border
                            {
                                Padding =
                                    new Thickness(
                                        LumineDesign.Space8,
                                        LumineDesign.Space6),
                                Background =
                                    isSelected
                                        ? LumineDesign.AccentMuted
                                        : Brushes.Transparent,
                                BorderBrush =
                                    isSelected
                                        ? LumineDesign.BorderStrong
                                        : LumineDesign.Border,
                                BorderThickness =
                                    new Thickness(1),
                                CornerRadius =
                                    new CornerRadius(
                                        LumineDesign.ControlRadius),
                                Child = content
                            };
                        row.Children.Add(
                            tagSurface);

                        var edit =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "編集",
                                    MinHeight = 28,
                                    Padding =
                                        new Thickness(
                                            LumineDesign.Space6,
                                            LumineDesign.Space2),
                                    FontSize =
                                        LumineDesign.CaptionFontSize,
                                    VerticalAlignment =
                                        VerticalAlignment.Center
                                });

                        var editName =
                            LumineDesign.ConfigureTextBox(
                                new TextBox
                                {
                                    PlaceholderText =
                                        "タグ名",
                                    Text = tag.Name
                                });
                        var editColor =
                            new TagColorEditor(
                                tag.Color);
                        var editStatus =
                            new TextBlock
                            {
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextWrapping =
                                    TextWrapping.Wrap
                            };
                        var saveEdit =
                            LumineDesign.ConfigurePrimaryButton(
                                new Button
                                {
                                    Content = "保存",
                                    MinWidth = 68
                                });
                        var cancelEdit =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "キャンセル"
                                });
                        var editActions =
                            new WrapPanel
                            {
                                HorizontalAlignment =
                                    HorizontalAlignment.Right
                            };
                        cancelEdit.Margin =
                            new Thickness(
                                0,
                                0,
                                LumineDesign.Space6,
                                LumineDesign.Space4);
                        saveEdit.Margin =
                            new Thickness(
                                0,
                                0,
                                0,
                                LumineDesign.Space4);
                        editActions.Children.Add(
                            cancelEdit);
                        editActions.Children.Add(
                            saveEdit);

                        var editBody =
                            new StackPanel
                            {
                                Spacing =
                                    LumineDesign.Space8
                            };
                        editBody.Children.Add(
                            new TextBlock
                            {
                                Text = "タグを編集",
                                Foreground =
                                    LumineDesign.Foreground,
                                FontWeight =
                                    FontWeight.SemiBold,
                                FontSize =
                                    LumineDesign.BodyFontSize
                            });
                        editBody.Children.Add(
                            new TextBlock
                            {
                                Text =
                                    $"{tag.AssetCount:N0}件の画像で使用",
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize
                            });
                        editBody.Children.Add(
                            editName);
                        editBody.Children.Add(
                            editColor);
                        editBody.Children.Add(
                            editStatus);
                        editBody.Children.Add(
                            editActions);

                        var editFlyout =
                            new Flyout
                            {
                                Content =
                                    new Border
                                    {
                                        Width = 320,
                                        MaxWidth = 360,
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
                                        Child = editBody
                                    }
                            };
                        edit.Flyout =
                            editFlyout;

                        var editBusy = false;
                        void UpdateEditActionState()
                        {
                            saveEdit.IsEnabled =
                                editFlyout.IsOpen
                                && !editBusy
                                && !string.IsNullOrWhiteSpace(
                                    editName.Text)
                                && editColor.IsColorValid;
                            cancelEdit.IsEnabled =
                                !editBusy;
                            editName.IsEnabled =
                                !editBusy;
                            editColor.IsEnabled =
                                !editBusy;
                        }

                        editFlyout.Opened +=
                            (_, _) =>
                            {
                                editName.Text =
                                    tag.Name;
                                editColor.SetColor(
                                    tag.Color);
                                editStatus.Text =
                                    string.Empty;
                                editStatus.Foreground =
                                    LumineDesign.MutedForeground;
                                editName.Focus();
                                UpdateEditActionState();
                            };
                        editFlyout.Closed +=
                            (_, _) =>
                                UpdateEditActionState();
                        editName.TextChanged +=
                            (_, _) =>
                                UpdateEditActionState();
                        editColor.StateChanged +=
                            (_, _) =>
                                UpdateEditActionState();
                        cancelEdit.Click +=
                            (_, _) =>
                            {
                                if (!editBusy)
                                {
                                    editFlyout.Hide();
                                }
                            };
                        saveEdit.Click +=
                            async (_, _) =>
                            {
                                var updatedName =
                                    editName.Text?.Trim();
                                var updatedColor =
                                    editColor.SelectedColor;
                                if (string.IsNullOrWhiteSpace(
                                        updatedName)
                                    || updatedColor is null
                                    || editBusy)
                                {
                                    return;
                                }

                                editBusy = true;
                                editStatus.Foreground =
                                    LumineDesign.MutedForeground;
                                editStatus.Text =
                                    "保存しています…";
                                UpdateEditActionState();
                                try
                                {
                                    await updateTag(
                                        tag,
                                        updatedName,
                                        updatedColor);
                                    editStatus.Text =
                                        string.Empty;
                                    editFlyout.Hide();
                                }
                                catch (OperationCanceledException)
                                {
                                    editStatus.Text =
                                        "保存をキャンセルしました。";
                                }
                                catch (Exception exception)
                                {
                                    System.Diagnostics.Trace.TraceError(
                                        exception.ToString());
                                    editStatus.Foreground =
                                        LumineDesign.Warning;
                                    editStatus.Text =
                                        "保存できませんでした。"
                                        + (string.IsNullOrWhiteSpace(
                                                exception.Message)
                                            ? string.Empty
                                            : $" {exception.Message}");
                                }
                                finally
                                {
                                    editBusy = false;
                                    UpdateEditActionState();
                                }
                            };

                        Grid.SetColumn(
                            edit,
                            1);
                        row.Children.Add(
                            edit);

                        var remove =
                            LumineDesign.ConfigureDangerButton(
                                new Button
                                {
                                    Content = "削除",
                                    MinHeight = 28,
                                    Padding =
                                        new Thickness(
                                            LumineDesign.Space6,
                                            LumineDesign.Space2),
                                    FontSize =
                                        LumineDesign.CaptionFontSize,
                                    VerticalAlignment =
                                        VerticalAlignment.Center
                                });
                        Grid.SetColumn(
                            remove,
                            2);
                        row.Children.Add(
                            remove);
                        AttachAsync(
                            remove,
                            () => deleteTag(tag),
                            reportError);
                        UpdateEditActionState();
                        return row;
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
                if (manageMode)
                {
                    manageMode = false;
                    ApplyFilter(
                        search.Text);
                }
            };
        createFlyout.Opened +=
            (_, _) =>
            {
                createName.Focus();
                UpdateCreateActionState();
            };
        createFlyout.Closed +=
            (_, _) =>
                UpdateCreateActionState();

        cancelCreate.Click +=
            (_, _) =>
            {
                createName.Text =
                    string.Empty;
                colorEditor.Reset();
                createFlyout.Hide();
                UpdateCreateActionState();
            };

        createName.TextChanged +=
            (_, _) =>
                UpdateCreateActionState();
        colorEditor.StateChanged +=
            (_, _) =>
                UpdateCreateActionState();

        createAction.Click +=
            async (_, _) =>
            {
                await ExecuteAsync(
                    createAction,
                    async () =>
                    {
                        var name =
                            createName.Text?.Trim();
                        var color =
                            colorEditor.SelectedColor;
                        if (string.IsNullOrWhiteSpace(
                                name)
                            || color is null)
                        {
                            return;
                        }

                        await createTag(
                            name,
                            color);
                        createName.Text =
                            string.Empty;
                        colorEditor.Reset();
                        createFlyout.Hide();
                    },
                    reportError);
                UpdateCreateActionState();
            };

        manage.Click +=
            (_, _) =>
            {
                manageMode =
                    !manageMode;
                if (manageMode
                    && createFlyout.IsOpen)
                {
                    createFlyout.Hide();
                }

                UpdateCreateActionState();
                ApplyFilter(
                    search.Text);
            };

        search.TextChanged +=
            (_, _) =>
                ApplyFilter(
                    search.Text);

        clearSelection.Click +=
            async (_, _) =>
            {
                selected.Clear();
                UpdateToolbar();
                ApplyFilter(
                    search.Text);
                await selectTag(null);
            };

        UpdateCreateActionState();
        ApplyFilter(null);
        return root;
    }

    public static Control CreatePublicationEntry(
        IReadOnlyList<PublicationInfo> publications) =>
        CreatePublicationEntry(
            publications,
            publications.Count,
            hasMore: false,
            static () =>
                Task.FromResult(
                    new PublicationPage(
                        Array.Empty<PublicationInfo>(),
                        null,
                        0)));

    public static Control CreatePublicationEntry(
        IReadOnlyList<PublicationInfo> publications,
        long totalCount,
        bool hasMore,
        Func<Task<PublicationPage>> loadMore,
        Action<string>? reportError = null)
    {
        ArgumentNullException.ThrowIfNull(
            publications);
        ArgumentNullException.ThrowIfNull(
            loadMore);

        if (publications.Count == 0
            && totalCount == 0)
        {
            return CreatePlaceholder(
                "公開履歴",
                "公開履歴はまだありません。");
        }

        var items =
            new ObservableCollection<PublicationInfo>(
                publications);
        var knownIds =
            new HashSet<long>(
                publications.Select(
                    static item =>
                        item.Id));

        var summary =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    TextWrapping.Wrap
            };

        var list =
            new ListBox
            {
                ItemsSource = items,
                Background = Brushes.Transparent,
                BorderThickness =
                    new Thickness(0),
                Padding =
                    new Thickness(0)
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
                                Text =
                                    publication.Title,
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
                                Text =
                                    publication.Body,
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
                                FormatPublicationAssets(
                                    publication,
                                    " → "),
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

        var loadMoreButton =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "さらに読み込む",
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    MinHeight =
                        LumineDesign.CompactCommandHeight,
                    IsVisible = hasMore
                });

        void UpdateSummary(
            long count)
        {
            summary.Text =
                count > items.Count
                    ? $"{items.Count:N0} / {count:N0}件"
                    : $"{items.Count:N0}件";
        }

        UpdateSummary(
            Math.Max(
                totalCount,
                items.Count));

        loadMoreButton.Click +=
            async (_, _) =>
            {
                if (!loadMoreButton.IsVisible
                    || !loadMoreButton.IsEnabled)
                {
                    return;
                }

                loadMoreButton.IsEnabled =
                    false;
                loadMoreButton.Content =
                    "読み込み中…";
                try
                {
                    var page =
                        await loadMore();
                    foreach (var publication in
                             page.Items)
                    {
                        if (knownIds.Add(
                                publication.Id))
                        {
                            items.Add(
                                publication);
                        }
                    }

                    loadMoreButton.IsVisible =
                        page.NextCursor is not null;
                    UpdateSummary(
                        page.TotalCount);
                }
                catch (Exception exception)
                {
                    reportError?.Invoke(
                        $"公開履歴を追加で読み込めませんでした: {exception.Message}");
                }
                finally
                {
                    loadMoreButton.Content =
                        "さらに読み込む";
                    loadMoreButton.IsEnabled =
                        loadMoreButton.IsVisible;
                }
            };

        var root =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,*,Auto"),
                RowSpacing =
                    LumineDesign.Space8
            };
        root.Children.Add(
            summary);
        Grid.SetRow(
            list,
            1);
        root.Children.Add(
            list);
        Grid.SetRow(
            loadMoreButton,
            2);
        root.Children.Add(
            loadMoreButton);
        return root;
    }

    private static string FormatPublicationAssets(
        PublicationInfo publication,
        string separator)
    {
        var total =
            publication.AssetCount >= 0
                ? publication.AssetCount
                : publication.Assets.Count;
        var visible =
            string.Join(
                separator,
                publication.Assets.Select(
                    static asset =>
                        asset.FileName));

        if (total <= publication.Assets.Count)
        {
            return "画像: " + visible;
        }

        return "画像: "
            + visible
            + separator
            + $"… +{total - publication.Assets.Count:N0}枚";
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
