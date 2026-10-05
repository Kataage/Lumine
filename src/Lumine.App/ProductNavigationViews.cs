using System.Collections.ObjectModel;
using System.Text.Json;
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
                    Content = "画像フォルダーを追加",
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    HorizontalContentAlignment =
                        HorizontalAlignment.Center
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
                    FontSize = LumineDesign.BodyFontSize,
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
                        isActive
                            ? LumineDesign.Accent
                            : LumineDesign.MutedForeground,
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
            ToolTip.SetTip(
                path,
                library.RootPath);

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
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        BorderThickness =
                            new Thickness(0),
                        Padding =
                            new Thickness(
                                LumineDesign.Space8)
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
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Transparent,
                        BorderThickness =
                            new Thickness(0),
                        CornerRadius =
                            new CornerRadius(
                                LumineDesign.ControlRadius),
                        Padding =
                            new Thickness(
                                LumineDesign.Space8),
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
                    RowDefinitions =
                        new RowDefinitions("Auto,Auto")
                };
            libraryRow.Children.Add(primary);

            var actions =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    Spacing =
                        LumineDesign.Space4,
                    HorizontalAlignment =
                        HorizontalAlignment.Right,
                    Margin =
                        new Thickness(
                            LumineDesign.Space8,
                            0,
                            LumineDesign.Space6,
                            LumineDesign.Space6)
                };

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
                actions.Children.Add(rescan);
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
            actions.Children.Add(manageButton);
            Grid.SetRow(actions, 1);
            libraryRow.Children.Add(actions);

            var libraryCard =
                new Border
                {
                    Background =
                        isActive
                            ? LumineDesign.InteractionSelected
                            : LumineDesign.SurfaceRaised,
                    BorderBrush =
                        isActive
                            ? LumineDesign.BorderStrong
                            : LumineDesign.Border,
                    BorderThickness =
                        new Thickness(1),
                    CornerRadius =
                        new CornerRadius(
                            LumineDesign.PanelRadius),
                    Padding =
                        new Thickness(
                            LumineDesign.Space2),
                    Child = libraryRow
                };
            AutomationProperties.SetAutomationId(
                libraryCard,
                $"library-card-{library.Id}");
            AutomationProperties.SetName(
                libraryCard,
                $"ライブラリ: {library.Name}");
            stack.Children.Add(libraryCard);

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
                                            ? LumineDesign.CreateStrokeIcon(
                                                expanded
                                                    ? LumineDesign.ChevronDownIconPath
                                                    : LumineDesign.ChevronRightIconPath,
                                                14)
                                            : null,
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
                                        "保存できませんでした。内容を確認してもう一度お試しください。";
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
        Action<string>? reportError = null,
        IReadOnlyList<PublicationDestinationInfo>? destinations = null,
        IReadOnlyList<PublicationAccountInfo>? accounts = null,
        Func<string, string, Task<PublicationDestinationInfo?>>? createDestination = null,
        Func<PublicationDestinationInfo, string, string, Task<PublicationDestinationInfo?>>? updateDestination = null,
        Func<PublicationDestinationInfo, Task<bool>>? deleteDestination = null,
        Func<long, string, string, Task<PublicationAccountInfo?>>? createAccount = null,
        Func<PublicationAccountInfo, long, string, string, Task<PublicationAccountInfo?>>? updateAccount = null,
        Func<PublicationAccountInfo, Task<bool>>? deleteAccount = null,
        Func<PublicationInfo, Task<PublicationInfo?>>? loadPublicationDetail = null,
        Func<PublicationInfo, Task<bool>>? deletePublication = null)
    {
        ArgumentNullException.ThrowIfNull(
            publications);
        ArgumentNullException.ThrowIfNull(
            loadMore);

        var items =
            new ObservableCollection<PublicationInfo>(
                publications);
        var destinationItems =
            new ObservableCollection<PublicationDestinationInfo>(
                destinations
                ?? Array.Empty<PublicationDestinationInfo>());
        var accountItems =
            new ObservableCollection<PublicationAccountInfo>(
                accounts
                ?? Array.Empty<PublicationAccountInfo>());
        var knownIds =
            new HashSet<long>(
                publications.Select(
                    static item =>
                        item.Id));
        var displayedTotal =
            Math.Max(
                totalCount,
                items.Count);

        var summary =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    TextWrapping.Wrap,
                VerticalAlignment =
                    VerticalAlignment.Center
            };

        void UpdateSummary()
        {
            summary.Text =
                displayedTotal > items.Count
                    ? $"{items.Count:N0} / {displayedTotal:N0}件"
                    : $"{items.Count:N0}件";
        }

        UpdateSummary();

        Control? CreateProfileSettings()
        {
            if (createDestination is null
                && updateDestination is null
                && deleteDestination is null
                && createAccount is null
                && updateAccount is null
                && deleteAccount is null)
            {
                return null;
            }

            var destinationName =
                LumineDesign.ConfigureTextBox(
                    new TextBox
                    {
                        PlaceholderText =
                            "公開先名"
                    });
            var destinationKind =
                LumineDesign.ConfigureComboBox(
                    new ComboBox
                    {
                        ItemsSource =
                            new[]
                            {
                                "Pixiv",
                                "X",
                                "Misskey",
                                "Bluesky",
                                "その他"
                            },
                        SelectedIndex = 4
                    });
            var destinationList =
                new StackPanel
                {
                    Spacing = LumineDesign.Space4
                };
            var accountDestination =
                LumineDesign.ConfigureComboBox(
                    new ComboBox());
            var accountName =
                LumineDesign.ConfigureTextBox(
                    new TextBox
                    {
                        PlaceholderText =
                            "表示名"
                    });
            var accountIdentifier =
                LumineDesign.ConfigureTextBox(
                    new TextBox
                    {
                        PlaceholderText =
                            "@ID / 識別子（任意）"
                    });
            var accountList =
                new StackPanel
                {
                    Spacing = LumineDesign.Space4
                };
            long? editingDestinationId =
                null;
            long? editingAccountId =
                null;
            Button addDestination =
                null!;
            Button addAccount =
                null!;

            var feedback =
                new TextBlock
                {
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    TextWrapping =
                        TextWrapping.Wrap
                };

            string ResolveKind() =>
                destinationKind.SelectedItem as string
                    switch
                    {
                        "Pixiv" => "pixiv",
                        "X" => "twitter",
                        "Misskey" => "misskey",
                        "Bluesky" => "bluesky",
                        _ => "other"
                    };

            static string KindLabel(
                string kind) =>
                kind.ToLowerInvariant()
                    switch
                    {
                        "pixiv" => "Pixiv",
                        "twitter" => "X",
                        "misskey" => "Misskey",
                        "bluesky" => "Bluesky",
                        _ => "その他"
                    };

            void RefreshAccountDestinations()
            {
                accountDestination.ItemsSource =
                    destinationItems
                        .Select(
                            static item =>
                                item.Name)
                        .ToArray();
                accountDestination.SelectedIndex =
                    destinationItems.Count > 0
                        ? Math.Clamp(
                            accountDestination.SelectedIndex,
                            0,
                            destinationItems.Count - 1)
                        : -1;
            }

            void RenderDestinations()
            {
                destinationList.Children.Clear();
                foreach (var destinationItem in
                         destinationItems)
                {
                    var row =
                        new Grid
                        {
                            ColumnDefinitions =
                                new ColumnDefinitions(
                                    "*,Auto,Auto")
                        };
                    row.Children.Add(
                        new TextBlock
                        {
                            Text =
                                $"{destinationItem.Name} · {destinationItem.Kind}",
                            Foreground =
                                LumineDesign.Foreground,
                            FontSize =
                                LumineDesign.CaptionFontSize,
                            TextWrapping =
                                TextWrapping.Wrap,
                            VerticalAlignment =
                                VerticalAlignment.Center
                        });
                    if (updateDestination is not null)
                    {
                        var edit =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "編集",
                                    MinWidth = 56,
                                    Margin =
                                        new Thickness(
                                            LumineDesign.Space6,
                                            0,
                                            0,
                                            0)
                                });
                        AutomationProperties.SetName(
                            edit,
                            $"公開先を編集: {destinationItem.Name}");
                        edit.Click +=
                            (_, _) =>
                            {
                                editingDestinationId =
                                    destinationItem.Id;
                                destinationName.Text =
                                    destinationItem.Name;
                                destinationKind.SelectedItem =
                                    KindLabel(
                                        destinationItem.Kind);
                                addDestination.Content =
                                    "公開先を更新";
                                feedback.Text =
                                    "公開先を編集中です。";
                            };
                        Grid.SetColumn(
                            edit,
                            1);
                        row.Children.Add(
                            edit);
                    }

                    if (deleteDestination is not null)
                    {
                        var remove =
                            LumineDesign.ConfigureDangerButton(
                                new Button
                                {
                                    Content = "削除",
                                    MinWidth = 56,
                                    Margin =
                                        new Thickness(
                                            LumineDesign.Space6,
                                            0,
                                            0,
                                            0)
                                });
                        AutomationProperties.SetName(
                            remove,
                            $"公開先を削除: {destinationItem.Name}");
                        remove.Click +=
                            async (_, _) =>
                            {
                                remove.IsEnabled = false;
                                try
                                {
                                    if (await deleteDestination(
                                            destinationItem))
                                    {
                                        destinationItems.Remove(
                                            destinationItem);
                                        foreach (var linked in
                                                 accountItems
                                                     .Where(
                                                         item =>
                                                             item.DestinationId
                                                             == destinationItem.Id)
                                                     .ToArray())
                                        {
                                            accountItems.Remove(
                                                linked);
                                        }

                                        RefreshAccountDestinations();
                                        RenderDestinations();
                                        RenderAccounts();
                                        feedback.Text =
                                            "公開先を削除しました。過去のPublication snapshotは変更していません。";
                                    }
                                }
                                catch (Exception exception)
                                {
                                    System.Diagnostics.Trace.TraceError(
                                        exception.ToString());
                                    feedback.Text =
                                        "公開先を削除できませんでした。";
                                    reportError?.Invoke(
                                        "公開先を削除できませんでした。もう一度お試しください。");
                                }
                                finally
                                {
                                    remove.IsEnabled = true;
                                }
                            };
                        Grid.SetColumn(
                            remove,
                            2);
                        row.Children.Add(
                            remove);
                    }

                    destinationList.Children.Add(
                        row);
                }
            }

            void RenderAccounts()
            {
                accountList.Children.Clear();
                foreach (var accountItem in
                         accountItems)
                {
                    var destinationLabel =
                        destinationItems
                            .FirstOrDefault(
                                item =>
                                    item.Id
                                    == accountItem.DestinationId)
                            ?.Name
                        ?? "削除済み公開先";
                    var row =
                        new Grid
                        {
                            ColumnDefinitions =
                                new ColumnDefinitions(
                                    "*,Auto,Auto")
                        };
                    row.Children.Add(
                        new TextBlock
                        {
                            Text =
                                $"{destinationLabel} · {accountItem.DisplayName}"
                                + (string.IsNullOrWhiteSpace(
                                        accountItem.AccountIdentifier)
                                    ? string.Empty
                                    : $" · {accountItem.AccountIdentifier}"),
                            Foreground =
                                LumineDesign.Foreground,
                            FontSize =
                                LumineDesign.CaptionFontSize,
                            TextWrapping =
                                TextWrapping.Wrap,
                            VerticalAlignment =
                                VerticalAlignment.Center
                        });
                    if (updateAccount is not null)
                    {
                        var edit =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "編集",
                                    MinWidth = 56,
                                    Margin =
                                        new Thickness(
                                            LumineDesign.Space6,
                                            0,
                                            0,
                                            0)
                                });
                        AutomationProperties.SetName(
                            edit,
                            $"公開アカウントを編集: {accountItem.DisplayName}");
                        edit.Click +=
                            (_, _) =>
                            {
                                editingAccountId =
                                    accountItem.Id;
                                var destinationIndex =
                                    destinationItems
                                        .Select(
                                            static item =>
                                                item.Id)
                                        .ToList()
                                        .IndexOf(
                                            accountItem.DestinationId);
                                accountDestination.SelectedIndex =
                                    destinationIndex;
                                accountName.Text =
                                    accountItem.DisplayName;
                                accountIdentifier.Text =
                                    accountItem.AccountIdentifier;
                                addAccount.Content =
                                    "アカウントを更新";
                                feedback.Text =
                                    "公開アカウントを編集中です。";
                            };
                        Grid.SetColumn(
                            edit,
                            1);
                        row.Children.Add(
                            edit);
                    }

                    if (deleteAccount is not null)
                    {
                        var remove =
                            LumineDesign.ConfigureDangerButton(
                                new Button
                                {
                                    Content = "削除",
                                    MinWidth = 56,
                                    Margin =
                                        new Thickness(
                                            LumineDesign.Space6,
                                            0,
                                            0,
                                            0)
                                });
                        AutomationProperties.SetName(
                            remove,
                            $"公開アカウントを削除: {accountItem.DisplayName}");
                        remove.Click +=
                            async (_, _) =>
                            {
                                remove.IsEnabled = false;
                                try
                                {
                                    if (await deleteAccount(
                                            accountItem))
                                    {
                                        accountItems.Remove(
                                            accountItem);
                                        RenderAccounts();
                                        feedback.Text =
                                            "アカウントを削除しました。過去のPublication snapshotは変更していません。";
                                    }
                                }
                                catch (Exception exception)
                                {
                                    System.Diagnostics.Trace.TraceError(
                                        exception.ToString());
                                    feedback.Text =
                                        "アカウントを削除できませんでした。";
                                    reportError?.Invoke(
                                        "公開アカウントを削除できませんでした。もう一度お試しください。");
                                }
                                finally
                                {
                                    remove.IsEnabled = true;
                                }
                            };
                        Grid.SetColumn(
                            remove,
                            2);
                        row.Children.Add(
                            remove);
                    }

                    accountList.Children.Add(
                        row);
                }
            }

            addDestination =
                LumineDesign.ConfigurePrimaryButton(
                    new Button
                    {
                        Content = "公開先を追加",
                        IsEnabled =
                            createDestination is not null
                    });
            addDestination.Click +=
                async (_, _) =>
                {
                    if ((editingDestinationId is null
                            && createDestination is null)
                        || (editingDestinationId is not null
                            && updateDestination is null)
                        || string.IsNullOrWhiteSpace(
                            destinationName.Text))
                    {
                        feedback.Text =
                            "公開先名を入力してください。";
                        return;
                    }

                    addDestination.IsEnabled = false;
                    try
                    {
                        PublicationDestinationInfo? saved;
                        if (editingDestinationId is long destinationId)
                        {
                            var existing =
                                destinationItems
                                    .FirstOrDefault(
                                        item =>
                                            item.Id
                                            == destinationId);
                            saved =
                                existing is null
                                || updateDestination is null
                                    ? null
                                    : await updateDestination(
                                        existing,
                                        destinationName.Text.Trim(),
                                        ResolveKind());
                            if (saved is not null
                                && existing is not null)
                            {
                                var index =
                                    destinationItems.IndexOf(
                                        existing);
                                destinationItems[index] =
                                    saved;
                            }
                        }
                        else
                        {
                            saved =
                                await createDestination(
                                    destinationName.Text.Trim(),
                                    ResolveKind());
                            if (saved is not null)
                            {
                                destinationItems.Add(
                                    saved);
                            }
                        }

                        if (saved is not null)
                        {
                            editingDestinationId =
                                null;
                            destinationName.Text =
                                string.Empty;
                            destinationKind.SelectedIndex =
                                4;
                            addDestination.Content =
                                "公開先を追加";
                            RefreshAccountDestinations();
                            RenderDestinations();
                            RenderAccounts();
                            feedback.Text =
                                "公開先を保存しました。";
                        }
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Trace.TraceError(
                            exception.ToString());
                        feedback.Text =
                            "公開先を保存できませんでした。";
                        reportError?.Invoke(
                            "公開先を保存できませんでした。名前の重複などを確認してください。");
                    }
                    finally
                    {
                        addDestination.IsEnabled =
                            editingDestinationId is null
                                ? createDestination is not null
                                : updateDestination is not null;
                    }
                };

            addAccount =
                LumineDesign.ConfigurePrimaryButton(
                    new Button
                    {
                        Content = "アカウントを追加",
                        IsEnabled =
                            createAccount is not null
                    });
            addAccount.Click +=
                async (_, _) =>
                {
                    if ((editingAccountId is null
                            && createAccount is null)
                        || (editingAccountId is not null
                            && updateAccount is null)
                        || accountDestination.SelectedIndex < 0
                        || accountDestination.SelectedIndex
                            >= destinationItems.Count)
                    {
                        feedback.Text =
                            "公開先を選択してください。";
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(
                            accountName.Text))
                    {
                        feedback.Text =
                            "アカウント表示名を入力してください。";
                        return;
                    }

                    addAccount.IsEnabled = false;
                    try
                    {
                        var destinationItem =
                            destinationItems[
                                accountDestination.SelectedIndex];
                        PublicationAccountInfo? saved;
                        if (editingAccountId is long accountId)
                        {
                            var existing =
                                accountItems
                                    .FirstOrDefault(
                                        item =>
                                            item.Id
                                            == accountId);
                            saved =
                                existing is null
                                || updateAccount is null
                                    ? null
                                    : await updateAccount(
                                        existing,
                                        destinationItem.Id,
                                        accountName.Text.Trim(),
                                        accountIdentifier.Text
                                            ?.Trim()
                                        ?? string.Empty);
                            if (saved is not null
                                && existing is not null)
                            {
                                var index =
                                    accountItems.IndexOf(
                                        existing);
                                accountItems[index] =
                                    saved;
                            }
                        }
                        else
                        {
                            saved =
                                await createAccount(
                                    destinationItem.Id,
                                    accountName.Text.Trim(),
                                    accountIdentifier.Text
                                        ?.Trim()
                                    ?? string.Empty);
                            if (saved is not null)
                            {
                                accountItems.Add(
                                    saved);
                            }
                        }

                        if (saved is not null)
                        {
                            editingAccountId =
                                null;
                            accountName.Text =
                                string.Empty;
                            accountIdentifier.Text =
                                string.Empty;
                            addAccount.Content =
                                "アカウントを追加";
                            RenderAccounts();
                            feedback.Text =
                                "アカウントを保存しました。";
                        }
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Trace.TraceError(
                            exception.ToString());
                        feedback.Text =
                            "アカウントを保存できませんでした。";
                        reportError?.Invoke(
                            "公開アカウントを保存できませんでした。重複などを確認してください。");
                    }
                    finally
                    {
                        addAccount.IsEnabled =
                            editingAccountId is null
                                ? createAccount is not null
                                : updateAccount is not null;
                    }
                };

            RefreshAccountDestinations();
            RenderDestinations();
            RenderAccounts();

            var form =
                new StackPanel
                {
                    Width = 330,
                    Spacing =
                        LumineDesign.Space8,
                    Margin =
                        new Thickness(
                            LumineDesign.Space12)
                };
            form.Children.Add(
                new TextBlock
                {
                    Text = "投稿先",
                    Foreground =
                        LumineDesign.Foreground,
                    FontWeight =
                        FontWeight.SemiBold,
                    FontSize =
                        LumineDesign.BodyFontSize
                });
            form.Children.Add(
                destinationName);
            form.Children.Add(
                destinationKind);
            form.Children.Add(
                addDestination);
            form.Children.Add(
                destinationList);
            form.Children.Add(
                new Border
                {
                    Height = 1,
                    Background =
                        LumineDesign.Border,
                    Margin =
                        new Thickness(
                            0,
                            LumineDesign.Space6)
                });
            form.Children.Add(
                new TextBlock
                {
                    Text = "アカウント",
                    Foreground =
                        LumineDesign.Foreground,
                    FontWeight =
                        FontWeight.SemiBold,
                    FontSize =
                        LumineDesign.BodyFontSize
                });
            form.Children.Add(
                accountDestination);
            form.Children.Add(
                accountName);
            form.Children.Add(
                accountIdentifier);
            form.Children.Add(
                addAccount);
            form.Children.Add(
                accountList);
            form.Children.Add(
                feedback);

            var button =
                LumineDesign.ConfigureSecondaryButton(
                    new DropDownButton
                    {
                        Content = "投稿先設定",
                        Flyout =
                            new Flyout
                            {
                                Content =
                                    new ScrollViewer
                                    {
                                        Content =
                                            form,
                                        MaxHeight = 520,
                                        HorizontalScrollBarVisibility =
                                            Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                                        VerticalScrollBarVisibility =
                                            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                                    }
                            }
                    });
            AutomationProperties.SetName(
                button,
                "公開先とアカウントを管理");
            return button;
        }

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
                            publication.ExternalId))
                    {
                        content.Children.Add(
                            new TextBlock
                            {
                                Text =
                                    $"外部ID: {publication.ExternalId}",
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextWrapping =
                                    TextWrapping.Wrap
                            });
                    }

                    if (!string.IsNullOrWhiteSpace(
                            publication.ExternalUrl))
                    {
                        content.Children.Add(
                            new TextBlock
                            {
                                Text =
                                    $"URL: {publication.ExternalUrl}",
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextWrapping =
                                    TextWrapping.Wrap
                            });
                    }

                    foreach (var flag in
                             ExtractPublicationFlags(
                                 publication.PlatformMetadataJson))
                    {
                        content.Children.Add(
                            new TextBlock
                            {
                                Text = flag,
                                Foreground =
                                    LumineDesign.MutedForeground,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextWrapping =
                                    TextWrapping.Wrap
                            });
                    }

                    if (loadPublicationDetail is not null)
                    {
                        PublicationInfo? fullDetail =
                            null;
                        var expanded =
                            false;
                        var detailHost =
                            new ContentControl
                            {
                                IsVisible = false
                            };
                        var detailStatus =
                            new TextBlock
                            {
                                Foreground =
                                    LumineDesign.Warning,
                                FontSize =
                                    LumineDesign.CaptionFontSize,
                                TextWrapping =
                                    TextWrapping.Wrap,
                                IsVisible = false
                            };
                        var expand =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content =
                                        "投稿内容をすべて表示",
                                    HorizontalAlignment =
                                        HorizontalAlignment.Left
                                });
                        AutomationProperties.SetName(
                            expand,
                            $"Publication詳細を表示: {publication.Title}");
                        expand.Click +=
                            async (_, _) =>
                            {
                                if (expanded)
                                {
                                    expanded =
                                        false;
                                    detailHost.IsVisible =
                                        false;
                                    detailStatus.IsVisible =
                                        false;
                                    expand.Content =
                                        "投稿内容をすべて表示";
                                    AutomationProperties.SetName(
                                        expand,
                                        $"Publication詳細を表示: {publication.Title}");
                                    return;
                                }

                                expand.IsEnabled =
                                    false;
                                expand.Content =
                                    "読み込み中…";
                                detailStatus.IsVisible =
                                    false;
                                try
                                {
                                    fullDetail ??=
                                        await loadPublicationDetail(
                                            publication);
                                    if (fullDetail is null)
                                    {
                                        throw new InvalidOperationException(
                                            "Publication detail was not found.");
                                    }

                                    detailHost.Content =
                                        CreateExpandedPublicationDetail(
                                            fullDetail);
                                    detailHost.IsVisible =
                                        true;
                                    expanded =
                                        true;
                                    expand.Content =
                                        "詳細を閉じる";
                                    AutomationProperties.SetName(
                                        expand,
                                        $"Publication詳細を閉じる: {publication.Title}");
                                }
                                catch (Exception exception)
                                {
                                    System.Diagnostics.Trace.TraceError(
                                        exception.ToString());
                                    detailStatus.Text =
                                        "Publicationの詳細を読み込めませんでした。もう一度お試しください。";
                                    detailStatus.IsVisible =
                                        true;
                                    expand.Content =
                                        "再試行";
                                    reportError?.Invoke(
                                        "Publicationの詳細を読み込めませんでした。");
                                }
                                finally
                                {
                                    expand.IsEnabled =
                                        true;
                                }
                            };
                        content.Children.Add(
                            detailHost);
                        content.Children.Add(
                            detailStatus);
                        content.Children.Add(
                            expand);
                    }

                    if (deletePublication is not null)
                    {
                        var remove =
                            LumineDesign.ConfigureDangerButton(
                                new Button
                                {
                                    Content = "履歴を削除…",
                                    HorizontalAlignment =
                                        HorizontalAlignment.Left
                                });
                        AutomationProperties.SetName(
                            remove,
                            $"Publication履歴を削除: {publication.Title}");
                        remove.Click +=
                            async (_, _) =>
                            {
                                remove.IsEnabled = false;
                                try
                                {
                                    if (await deletePublication(
                                            publication))
                                    {
                                        items.Remove(
                                            publication);
                                        knownIds.Remove(
                                            publication.Id);
                                        displayedTotal =
                                            Math.Max(
                                                0,
                                                displayedTotal - 1);
                                        UpdateSummary();
                                    }
                                }
                                catch (Exception exception)
                                {
                                    System.Diagnostics.Trace.TraceError(
                                        exception.ToString());
                                    reportError?.Invoke(
                                        "Publication履歴を削除できませんでした。もう一度お試しください。");
                                }
                                finally
                                {
                                    remove.IsEnabled = true;
                                }
                            };
                        content.Children.Add(
                            remove);
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
                    displayedTotal =
                        page.TotalCount;
                    UpdateSummary();
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceError(
                        exception.ToString());
                    reportError?.Invoke(
                        "公開履歴を追加で読み込めませんでした。もう一度お試しください。");
                }
                finally
                {
                    loadMoreButton.Content =
                        "さらに読み込む";
                    loadMoreButton.IsEnabled =
                        loadMoreButton.IsVisible;
                }
            };

        var settings =
            CreateProfileSettings();
        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        settings is null
                            ? "*"
                            : "*,Auto")
            };
        header.Children.Add(
            summary);
        if (settings is not null)
        {
            Grid.SetColumn(
                settings,
                1);
            header.Children.Add(
                settings);
        }

        var empty =
            new TextBlock
            {
                Text =
                    "公開履歴はまだありません。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextAlignment =
                    TextAlignment.Center,
                Margin =
                    new Thickness(
                        LumineDesign.Space12),
                IsVisible =
                    items.Count == 0
            };
        items.CollectionChanged +=
            (_, _) =>
            {
                empty.IsVisible =
                    items.Count == 0;
            };

        var listHost =
            new Grid();
        listHost.Children.Add(
            list);
        listHost.Children.Add(
            empty);

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
            header);
        Grid.SetRow(
            listHost,
            1);
        root.Children.Add(
            listHost);
        Grid.SetRow(
            loadMoreButton,
            2);
        root.Children.Add(
            loadMoreButton);
        return root;
    }

    private static Control CreateExpandedPublicationDetail(
        PublicationInfo publication)
    {
        var detail =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space6,
                Margin =
                    new Thickness(
                        0,
                        LumineDesign.Space6,
                        0,
                        0)
            };

        if (!string.IsNullOrWhiteSpace(
                publication.Body))
        {
            detail.Children.Add(
                CreatePublicationDetailText(
                    publication.Body));
        }

        if (!string.IsNullOrWhiteSpace(
                publication.TagsSnapshot))
        {
            detail.Children.Add(
                CreatePublicationDetailText(
                    $"タグ: {publication.TagsSnapshot}",
                    accent: true));
        }

        if (publication.Assets.Count > 0)
        {
            var assets =
                new StackPanel
                {
                    Spacing =
                        LumineDesign.Space4
                };
            foreach (var asset in
                     publication.Assets
                         .OrderBy(
                             static item =>
                                 item.SortOrder))
            {
                assets.Children.Add(
                    CreatePublicationDetailText(
                        $"{asset.SortOrder + 1}. {asset.FileName}"));
            }

            detail.Children.Add(
                new TextBlock
                {
                    Text =
                        $"画像 {publication.Assets.Count:N0}枚",
                    Foreground =
                        LumineDesign.Foreground,
                    FontWeight =
                        FontWeight.SemiBold,
                    FontSize =
                        LumineDesign.CaptionFontSize
                });
            detail.Children.Add(
                assets);
        }

        if (!string.IsNullOrWhiteSpace(
                publication.ExternalId))
        {
            detail.Children.Add(
                CreatePublicationDetailText(
                    $"外部ID: {publication.ExternalId}"));
        }

        if (!string.IsNullOrWhiteSpace(
                publication.ExternalUrl))
        {
            detail.Children.Add(
                CreatePublicationDetailText(
                    $"URL: {publication.ExternalUrl}"));
        }

        foreach (var flag in
                 ExtractPublicationFlags(
                     publication.PlatformMetadataJson))
        {
            detail.Children.Add(
                CreatePublicationDetailText(
                    flag));
        }

        return new Border
        {
            Background =
                LumineDesign.Background,
            BorderBrush =
                LumineDesign.Border,
            BorderThickness =
                new Thickness(1),
            CornerRadius =
                new CornerRadius(
                    LumineDesign.ControlRadius),
            Padding =
                new Thickness(
                    LumineDesign.Space8),
            Child = detail
        };
    }

    private static TextBlock CreatePublicationDetailText(
        string text,
        bool accent = false) =>
        new()
        {
            Text = text,
            Foreground =
                accent
                    ? LumineDesign.Accent
                    : LumineDesign.MutedForeground,
            FontSize =
                LumineDesign.CaptionFontSize,
            TextWrapping =
                TextWrapping.Wrap
        };

    internal static IReadOnlyList<string>
        ExtractPublicationFlagsForSmoke(
            string? platformMetadataJson) =>
        ExtractPublicationFlags(
            platformMetadataJson);

    private static IReadOnlyList<string>
        ExtractPublicationFlags(
            string? platformMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(
                platformMetadataJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document =
                JsonDocument.Parse(
                    platformMetadataJson);
            if (document.RootElement.ValueKind
                != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var flags =
                new List<string>();
            foreach (var property in
                     document.RootElement
                         .EnumerateObject())
            {
                var normalized =
                    property.Name
                        .Replace(
                            "_",
                            string.Empty,
                            StringComparison.Ordinal)
                        .Replace(
                            "-",
                            string.Empty,
                            StringComparison.Ordinal)
                        .ToLowerInvariant();

                if (normalized is not
                    ("aigenerated"
                    or "isgeneratedbyai"
                    or "generatedbyai"
                    or "agerestriction"
                    or "agegate"
                    or "adult"
                    or "isadult"
                    or "r18"
                    or "isr18"))
                {
                    continue;
                }

                var value =
                    property.Value.ValueKind
                    is JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                if (string.IsNullOrWhiteSpace(
                        value))
                {
                    continue;
                }

                if (normalized is
                    "aigenerated"
                    or "isgeneratedbyai"
                    or "generatedbyai")
                {
                    flags.Add(
                        $"AI生成: {FormatPublicationBoolean(value)}");
                    continue;
                }

                if (normalized is
                    "agerestriction"
                    or "agegate")
                {
                    flags.Add(
                        $"年齢制限: {FormatPublicationAgeRestriction(value)}");
                    continue;
                }

                flags.Add(
                    $"年齢制限: {FormatPublicationBoolean(value, true)}");
            }

            return flags;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string FormatPublicationBoolean(
        string value,
        bool adultMeaning = false)
    {
        if (bool.TryParse(
                value,
                out var parsed))
        {
            if (adultMeaning)
            {
                return parsed
                    ? "R-18"
                    : "全年齢";
            }

            return parsed
                ? "はい"
                : "いいえ";
        }

        return value;
    }

    private static string FormatPublicationAgeRestriction(
        string value)
    {
        var normalized =
            value.Trim()
                .Replace(
                    "_",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "-",
                    string.Empty,
                    StringComparison.Ordinal)
                .ToLowerInvariant();
        return normalized switch
        {
            "all"
                or "allage"
                or "allages"
                or "general"
                or "全年齢" =>
                "全年齢",
            "r18g"
                or "18g" =>
                "R-18G",
            "r18"
                or "adult"
                or "18plus"
                or "18+" =>
                "R-18",
            _ =>
                value.Trim()
        };
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
                "操作を完了できませんでした。もう一度お試しください。");
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
