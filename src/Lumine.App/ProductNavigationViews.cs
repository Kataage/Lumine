using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Media;
using Lumine.Core;
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

        var addContent =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = LumineDesign.Space8,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        addContent.Children.Add(
            LumineDesign.CreateStrokeIcon(
                LumineDesign.PlusIconPath,
                16,
                LumineDesign.MutedForeground));
        addContent.Children.Add(
            new TextBlock
            {
                Text = "画像フォルダーを追加",
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        var add =
            LumineDesign.ConfigureTertiaryButton(
                new Button
                {
                    Content = addContent,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    HorizontalContentAlignment =
                        HorizontalAlignment.Left
                });
        AutomationProperties.SetName(
            add,
            "画像フォルダーを追加");
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
            var hasStateWarning =
                !rootAvailable
                || !library.IsEnabled;

            var title =
                new TextBlock
                {
                    Text = library.Name,
                    Foreground = LumineDesign.Foreground,
                    FontWeight =
                        isActive
                            ? FontWeight.SemiBold
                            : FontWeight.Normal,
                    FontSize = LumineDesign.BodyFontSize,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis,
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
                        hasStateWarning
                            ? new ColumnDefinitions(
                                "Auto,*,Auto")
                            : new ColumnDefinitions(
                                "*,Auto"),
                    ColumnSpacing =
                        LumineDesign.Space6
                };

            var titleColumn = 0;
            if (hasStateWarning)
            {
                var stateDot =
                    new Border
                    {
                        Width = 6,
                        Height = 6,
                        CornerRadius =
                            new CornerRadius(3),
                        Background =
                            !rootAvailable
                                ? LumineDesign.Warning
                                : LumineDesign.MutedForeground,
                        VerticalAlignment =
                            VerticalAlignment.Center
                    };
                heading.Children.Add(stateDot);
                titleColumn = 1;
            }

            Grid.SetColumn(
                title,
                titleColumn);
            heading.Children.Add(title);
            Grid.SetColumn(
                activeLabel,
                titleColumn + 1);
            heading.Children.Add(activeLabel);

            var path =
                new TextBlock
                {
                    Text = library.RootPath,
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize = LumineDesign.CaptionFontSize,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis
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
                    FontSize = LumineDesign.CaptionFontSize
                };

            var primaryContent =
                new StackPanel
                {
                    Spacing = LumineDesign.Space2
                };
            primaryContent.Children.Add(heading);
            primaryContent.Children.Add(path);
            primaryContent.Children.Add(detail);

            if (!string.IsNullOrWhiteSpace(
                    library.SyncError))
            {
                primaryContent.Children.Add(
                    new TextBlock
                    {
                        Text = library.SyncError,
                        Foreground =
                            LumineDesign.Warning,
                        FontSize =
                            LumineDesign.CaptionFontSize,
                        TextWrapping =
                            TextWrapping.Wrap
                    });
            }

            Control primary;
            if (isActive)
            {
                var staticPrimary =
                    new Border
                    {
                        Child = primaryContent
                    };
                staticPrimary.Classes.Add(
                    "lumine-library-static");
                primary = staticPrimary;
            }
            else
            {
                var open =
                    new Button
                    {
                        Content = primaryContent,
                        IsEnabled = canOpen
                    };
                open.Classes.Add(
                    "lumine-library-open");
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
                        new ColumnDefinitions(
                            "*,Auto,Auto"),
                    ColumnSpacing =
                        LumineDesign.Space2,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            libraryRow.Children.Add(primary);

            if (isActive
                && library.IsEnabled
                && rootAvailable)
            {
                var rescan =
                    LumineDesign.ConfigureIconButton(
                        new Button
                        {
                            Content =
                                LumineDesign.CreateStrokeIcon(
                                    LumineDesign.RefreshIconPath,
                                    16)
                        },
                        "現在のライブラリを再スキャン");
                rescan.VerticalAlignment =
                    VerticalAlignment.Center;
                AttachAsync(
                    rescan,
                    () => rescanLibrary(library),
                    reportError);
                Grid.SetColumn(
                    rescan,
                    1);
                libraryRow.Children.Add(
                    rescan);
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
            Grid.SetColumn(
                manageButton,
                2);
            libraryRow.Children.Add(
                manageButton);

            var librarySurface =
                new Border
                {
                    Child = libraryRow
                };
            librarySurface.Classes.Add(
                "lumine-library-row");
            if (isActive)
            {
                librarySurface.Classes.Add(
                    "selected");
            }

            AutomationProperties.SetAutomationId(
                librarySurface,
                $"library-card-{library.Id}");
            AutomationProperties.SetName(
                librarySurface,
                $"ライブラリ: {library.Name}");
            stack.Children.Add(
                librarySurface);
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

        var allSelected = selectedFolder is null;
        var all =
            new Button
            {
                // A visible checkmark keeps the current location legible
                // in monochrome and high-contrast themes.
                Content = allSelected
                    ? "✓ すべての画像"
                    : "すべての画像"
            };
        all.Classes.Add(
            "lumine-folder-row");
        if (allSelected)
        {
            all.Classes.Add(
                "selected");
        }
        AutomationProperties.SetName(
            all,
            allSelected
                ? "すべての画像（表示中）"
                : "すべての画像");
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
                new ListBox();
            list.Classes.Add(
                "lumine-flat-list");

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
                                        Text = selected
                                            ? $"✓ {leaf}"
                                            : leaf,
                                        TextTrimming =
                                            TextTrimming.CharacterEllipsis
                                    }
                            };
                        folderButton.Classes.Add(
                            "lumine-folder-row");
                        if (selected)
                        {
                            folderButton.Classes.Add(
                                "selected");
                        }
                        ToolTip.SetTip(
                            folderButton,
                            folder.RelativePath);
                        AutomationProperties.SetName(
                            folderButton,
                            selected
                                ? $"フォルダー: {folder.RelativePath}（表示中）"
                                : $"フォルダー: {folder.RelativePath}");
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
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            };
                        count.Classes.Add(
                            "lumine-muted-caption");
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

        var tagEditorWidth =
            Math.Min(
                400,
                320
                + ((Math.Clamp(
                        LumineVisualMetrics.TextScaleFactor,
                        1,
                        2.25)
                    - 1)
                    * 64));

        Border CreateTagEditorSurface(
            Control content,
            string accessibleName)
        {
            var surface =
                new Border
                {
                    Width = tagEditorWidth,
                    MaxWidth = 400,
                    Child = content
                };
            surface.Classes.Add(
                "lumine-popover");
            AutomationProperties.SetName(
                surface,
                accessibleName);
            return surface;
        }

        static void SetStateClass(
            StyledElement element,
            string className,
            bool enabled)
        {
            if (enabled)
            {
                if (!element.Classes.Contains(
                        className))
                {
                    element.Classes.Add(
                        className);
                }
            }
            else
            {
                element.Classes.Remove(
                    className);
            }
        }

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
                    IsVisible = false
                });

        var add =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "＋ 新規"
                });

        var manage =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "管理"
                });
        add.Classes.Add("lumine-tag-focus-anchor");
        manage.Classes.Add("lumine-tag-focus-anchor");

        var actionRow =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing =
                    LumineDesign.Space6,
                HorizontalAlignment =
                    HorizontalAlignment.Left
            };
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
                        "タグを検索"
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
                    Content = "作成"
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
            CreateTagEditorSurface(
                createBody,
                "タグを作成");

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
            SetStateClass(
                manage,
                "active",
                manageMode);
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
                        name.Classes.Add("lumine-tag-name");
                        if (isSelected)
                        {
                            name.Classes.Add("selected");
                        }
                        ToolTip.SetTip(name, tag.Name);

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
                                        "Auto,Auto,*,Auto"),
                                ColumnSpacing =
                                    LumineDesign.Space6
                            };
                        content.Children.Add(
                            colorDot);
                        if (isSelected)
                        {
                            // A dedicated gutter keeps ✓ visible even when
                            // a long tag name is fully ellipsized at 225%.
                            var marker = new TextBlock
                            {
                                Text = "✓",
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            };
                            marker.Classes.Add(
                                "lumine-tag-selection-marker");
                            Grid.SetColumn(marker, 1);
                            content.Children.Add(marker);
                        }
                        Grid.SetColumn(name, 2);
                        content.Children.Add(name);
                        Grid.SetColumn(count, 3);
                        content.Children.Add(count);

                        if (!manageMode)
                        {
                            var button =
                                new Button
                                {
                                    Content = content
                                };
                            button.Classes.Add(
                                "lumine-tag-row");
                            SetStateClass(
                                button,
                                "selected",
                                isSelected);
                            AutomationProperties.SetName(
                                button,
                                isSelected
                                    ? $"タグ: {tag.Name}（選択中）"
                                    : $"タグ: {tag.Name}");
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

                        var stackedManageActions =
                            LumineVisualMetrics.TextScaleFactor >= 1.75;
                        var row =
                            new Grid
                            {
                                ColumnDefinitions =
                                    new ColumnDefinitions(
                                        stackedManageActions
                                            ? "*"
                                            : "*,Auto,Auto"),
                                RowDefinitions =
                                    new RowDefinitions(
                                        stackedManageActions
                                            ? "Auto,Auto"
                                            : "Auto"),
                                ColumnSpacing =
                                    LumineDesign.Space6,
                                RowSpacing =
                                    stackedManageActions
                                        ? LumineDesign.Space4
                                        : 0,
                                HorizontalAlignment =
                                    HorizontalAlignment.Stretch
                            };

                        var tagSurface =
                            new Border
                            {
                                Child = content
                            };
                        tagSurface.Classes.Add(
                            "lumine-tag-row");
                        SetStateClass(
                            tagSurface,
                            "selected",
                            isSelected);
                        AutomationProperties.SetName(
                            tagSurface,
                            isSelected
                                ? $"タグ: {tag.Name}（選択中）"
                                : $"タグ: {tag.Name}");
                        row.Children.Add(
                            tagSurface);

                        var edit =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "編集",
                                    VerticalAlignment =
                                        VerticalAlignment.Center
                                });
                        edit.Classes.Add(
                            "lumine-compact");
                        edit.Classes.Add(
                            "lumine-tag-focus-anchor");

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
                                    Content = "保存"
                                });
                        var cancelEdit =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "キャンセル"
                                });
                        var editActions =
                            new StackPanel
                            {
                                Orientation =
                                    Orientation.Horizontal,
                                Spacing =
                                    LumineDesign.Space6,
                                HorizontalAlignment =
                                    HorizontalAlignment.Right
                            };
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
                                    CreateTagEditorSurface(
                                        editBody,
                                        $"タグを編集: {tag.Name}")
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

                        Control? editPreviousOwnerFocus = null;
                        editFlyout.Opened +=
                            (_, _) =>
                            {
                                editPreviousOwnerFocus =
                                    TopLevel.GetTopLevel(edit)
                                        ?.FocusManager
                                        ?.GetFocusedElement() as Control;
                                editName.Text =
                                    tag.Name;
                                editColor.SetColor(
                                    tag.Color);
                                editStatus.Text =
                                    string.Empty;
                                editStatus.Foreground =
                                    LumineDesign.MutedForeground;
                                FocusFlyoutEditor(
                                    edit,
                                    editName);
                                UpdateEditActionState();
                            };
                        editFlyout.Closed +=
                            (_, _) =>
                            {
                                UpdateEditActionState();
                                RestoreFocusAfterFlyoutClose(
                                    edit,
                                    editFlyout,
                                    editPreviousOwnerFocus);
                            };
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

                        var remove =
                            LumineDesign.ConfigureDangerButton(
                                new Button
                                {
                                    Content = "削除",
                                    VerticalAlignment =
                                        VerticalAlignment.Center
                                });
                        remove.Classes.Add(
                            "lumine-compact");
                        if (stackedManageActions)
                        {
                            var actions = new StackPanel
                            {
                                Orientation =
                                    Orientation.Horizontal
                            };
                            actions.Classes.Add(
                                "lumine-tag-manage-actions");
                            actions.Children.Add(edit);
                            actions.Children.Add(remove);
                            Grid.SetRow(actions, 1);
                            row.Children.Add(actions);
                        }
                        else
                        {
                            Grid.SetColumn(edit, 1);
                            row.Children.Add(edit);
                            Grid.SetColumn(remove, 2);
                            row.Children.Add(remove);
                        }
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
        Control? createPreviousOwnerFocus = null;
        createFlyout.Opened +=
            (_, _) =>
            {
                createPreviousOwnerFocus =
                    TopLevel.GetTopLevel(add)
                        ?.FocusManager
                        ?.GetFocusedElement() as Control;
                FocusFlyoutEditor(
                    add,
                    createName);
                UpdateCreateActionState();
            };
        createFlyout.Closed +=
            (_, _) =>
            {
                UpdateCreateActionState();
                RestoreFocusAfterFlyoutClose(
                    add,
                    createFlyout,
                    createPreviousOwnerFocus);
            };

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
            CreatePublicationText(
                string.Empty,
                "lumine-muted-caption");
        summary.VerticalAlignment =
            VerticalAlignment.Center;

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
                new StackPanel();
            destinationList.Classes.Add(
                "lumine-compact-stack");
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
                new StackPanel();
            accountList.Classes.Add(
                "lumine-compact-stack");
            long? editingDestinationId =
                null;
            long? editingAccountId =
                null;
            Button addDestination =
                null!;
            Button addAccount =
                null!;

            var feedback =
                CreatePublicationText(
                    string.Empty,
                    "lumine-muted-caption");

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
                    row.Classes.Add(
                        "lumine-inline-actions");
                    var destinationText =
                        CreatePublicationText(
                            $"{destinationItem.Name} · {destinationItem.Kind}",
                            "lumine-body-caption");
                    destinationText.VerticalAlignment =
                        VerticalAlignment.Center;
                    row.Children.Add(
                        destinationText);
                    if (updateDestination is not null)
                    {
                        var edit =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "編集"
                                });
                        edit.Classes.Add(
                            "lumine-compact");
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
                                    Content = "削除"
                                });
                        remove.Classes.Add(
                            "lumine-compact");
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
                    row.Classes.Add(
                        "lumine-inline-actions");
                    var accountText =
                        CreatePublicationText(
                            $"{destinationLabel} · {accountItem.DisplayName}"
                            + (string.IsNullOrWhiteSpace(
                                    accountItem.AccountIdentifier)
                                ? string.Empty
                                : $" · {accountItem.AccountIdentifier}"),
                            "lumine-body-caption");
                    accountText.VerticalAlignment =
                        VerticalAlignment.Center;
                    row.Children.Add(
                        accountText);
                    if (updateAccount is not null)
                    {
                        var edit =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "編集"
                                });
                        edit.Classes.Add(
                            "lumine-compact");
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
                                    Content = "削除"
                                });
                        remove.Classes.Add(
                            "lumine-compact");
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
                new StackPanel();
            form.Classes.Add(
                "lumine-publication-form");
            form.Children.Add(
                CreatePublicationText(
                    "投稿先",
                    "lumine-section-title"));
            form.Children.Add(
                destinationName);
            form.Children.Add(
                destinationKind);
            form.Children.Add(
                addDestination);
            form.Children.Add(
                destinationList);
            var divider =
                new Border();
            divider.Classes.Add(
                "lumine-divider");
            form.Children.Add(
                divider);
            form.Children.Add(
                CreatePublicationText(
                    "アカウント",
                    "lumine-section-title"));
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

            // Keyboard-opened Publication settings must place focus on
            // the first editable destination field, just like Tag
            // create/edit flyouts. Preserve newly chosen owner focus
            // when the popup is dismissed.
            var settingsFlyout =
                button.Flyout as Flyout
                ?? throw new InvalidOperationException(
                    "Publication settings flyout is missing.");
            Control? settingsPreviousOwnerFocus = null;
            settingsFlyout.Opened +=
                (_, _) =>
                {
                    settingsPreviousOwnerFocus =
                        TopLevel.GetTopLevel(button)
                            ?.FocusManager
                            ?.GetFocusedElement() as Control;
                    FocusFlyoutEditor(
                        button,
                        destinationName);
                };
            settingsFlyout.Closed +=
                (_, _) =>
                    RestoreFocusAfterFlyoutClose(
                        button,
                        settingsFlyout,
                        settingsPreviousOwnerFocus);
            return button;
        }

        var list =
            new ListBox
            {
                ItemsSource = items
            };
        list.Classes.Add(
            "lumine-flat-list");
        list.ItemTemplate =
            new FuncDataTemplate<PublicationInfo>(
                (publication, _) =>
                {
                    var content =
                        new StackPanel();
                    content.Classes.Add(
                        "lumine-publication-row-content");
                    content.Children.Add(
                        CreatePublicationText(
                            publication.PublishedAtUtc
                                .ToLocalTime()
                                .ToString("yyyy-MM-dd HH:mm")
                            + $" · {publication.Destination}"
                            + (string.IsNullOrWhiteSpace(
                                    publication.Account)
                                ? string.Empty
                                : $" · {publication.Account}"),
                            "lumine-muted-caption",
                            wrap: false));

                    if (!string.IsNullOrWhiteSpace(
                            publication.Title))
                    {
                        content.Children.Add(
                            CreatePublicationText(
                                publication.Title,
                                "lumine-section-title"));
                    }

                    if (!string.IsNullOrWhiteSpace(
                            publication.Body))
                    {
                        content.Children.Add(
                            CreatePublicationText(
                                publication.Body,
                                "lumine-body-caption",
                                maxHeight: 72));
                    }

                    if (!string.IsNullOrWhiteSpace(
                            publication.TagsSnapshot))
                    {
                        content.Children.Add(
                            CreatePublicationText(
                                publication.TagsSnapshot,
                                "lumine-accent-caption"));
                    }

                    content.Children.Add(
                        CreatePublicationText(
                            FormatPublicationAssets(
                                publication,
                                " → "),
                            "lumine-muted-caption"));

                    if (!string.IsNullOrWhiteSpace(
                            publication.ExternalId))
                    {
                        content.Children.Add(
                            CreatePublicationText(
                                $"外部ID: {publication.ExternalId}",
                                "lumine-muted-caption"));
                    }

                    if (!string.IsNullOrWhiteSpace(
                            publication.ExternalUrl))
                    {
                        content.Children.Add(
                            CreatePublicationText(
                                $"URL: {publication.ExternalUrl}",
                                "lumine-muted-caption"));
                    }

                    foreach (var flag in
                             ExtractPublicationFlags(
                                 publication.PlatformMetadataJson))
                    {
                        content.Children.Add(
                            CreatePublicationText(
                                flag,
                                "lumine-muted-caption"));
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
                            CreatePublicationText(
                                string.Empty,
                                "lumine-warning-caption");
                        detailStatus.IsVisible =
                            false;
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

                    return CreatePublicationRow(
                        content);
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
                IsVisible =
                    items.Count == 0
            };
        empty.Classes.Add(
            "lumine-empty-message");
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
            new StackPanel();
        detail.Classes.Add(
            "lumine-publication-detail-body");

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
                new StackPanel();
            assets.Classes.Add(
                "lumine-compact-stack");
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
                CreatePublicationText(
                    $"画像 {publication.Assets.Count:N0}枚",
                    "lumine-section-title"));
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

        var surface =
            new Border
            {
                Child = detail
            };
        surface.Classes.Add(
            "lumine-publication-detail");
        return surface;
    }

    private static TextBlock CreatePublicationText(
        string text,
        string styleClass,
        bool wrap = true,
        double? maxHeight = null)
    {
        var block =
            new TextBlock
            {
                Text = text,
                TextWrapping =
                    wrap
                        ? TextWrapping.Wrap
                        : TextWrapping.NoWrap
            };
        if (maxHeight is double resolvedMaxHeight)
        {
            block.MaxHeight =
                resolvedMaxHeight;
        }

        block.Classes.Add(
            styleClass);
        return block;
    }

    private static TextBlock CreatePublicationDetailText(
        string text,
        bool accent = false) =>
        CreatePublicationText(
            text,
            accent
                ? "lumine-accent-caption"
                : "lumine-muted-caption");

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

    private static Border CreatePublicationRow(
        Control child)
    {
        var row =
            new Border
            {
                Child = child
            };
        row.Classes.Add(
            "lumine-publication-row");
        return row;
    }

    private static TextBlock CreateHint(
        string text)
    {
        var hint =
            new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap
            };
        hint.Classes.Add(
            "lumine-nav-hint");
        return hint;
    }

    private static Control CreatePlaceholder(
        string title,
        string description)
    {
        var stack =
            new StackPanel();
        stack.Classes.Add(
            "lumine-nav-placeholder");
        var titleText =
            new TextBlock
            {
                Text = title
            };
        titleText.Classes.Add(
            "lumine-nav-placeholder-title");
        stack.Children.Add(
            titleText);
        stack.Children.Add(
            CreateHint(description));
        return stack;
    }

    private static void FocusFlyoutEditor(
        Button origin,
        Control editor)
    {
        // The flyout owns focus while it is open. The parent window may
        // otherwise retain a stale focused Tag-toolbar button, which would
        // incorrectly look like a post-dismissal user navigation.
        TopLevel.GetTopLevel(origin)
            ?.FocusManager
            .Focus(
                null!,
                NavigationMethod.Unspecified,
                KeyModifiers.None);
        editor.Focus();
    }

    private static void RestoreFocusAfterFlyoutClose(
        Button origin,
        Flyout flyout,
        Control? previousOwnerFocus)
    {
        // Popup detach and LightDismiss can clear focus after Closed fires.
        // Defer restoration until the popup is removed, but never override
        // a user's subsequent click/Tab into a live command.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (flyout.IsOpen
                    || !origin.IsEffectivelyVisible
                    || !origin.IsEnabled)
                {
                    return;
                }

                var owner = TopLevel.GetTopLevel(origin);
                if (owner is null)
                {
                    return; // Tag rows may be recycled while closing.
                }

                var focused = owner.FocusManager?.GetFocusedElement();
                if (focused is Control control
                    && control.IsEffectivelyVisible
                    && ReferenceEquals(
                        TopLevel.GetTopLevel(control),
                        owner)
                    && !ReferenceEquals(
                        control,
                        previousOwnerFocus))
                {
                    return; // Newly chosen live control owns the focus.
                }

                // Avalonia can restore the pre-popup window focus
                // automatically. That is stale, not a new user choice.

                origin.Focus(
                    NavigationMethod.Unspecified,
                    KeyModifiers.None);
            },
            DispatcherPriority.Input);
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
