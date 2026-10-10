using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.Json;
using Lumine.Core;
using Lumine.Library;
using Lumine.Viewer;

namespace Lumine.App;

internal sealed class ContextualAssetDetailPanel : UserControl
{
    private static readonly string?[] StatusValues =
    [
        null,
        "unsorted",
        "reviewed",
        "candidate",
        "published"
    ];

    private static readonly string[] StatusLabels =
    [
        "未設定",
        "未整理",
        "確認済み",
        "候補",
        "公開済み"
    ];

    private static readonly string?[] ColorValues =
    [
        null,
        "red",
        "orange",
        "yellow",
        "green",
        "blue",
        "purple",
        "gray"
    ];

    private static readonly string[] ColorLabels =
    [
        "未設定",
        "Red",
        "Orange",
        "Yellow",
        "Green",
        "Blue",
        "Purple",
        "Gray"
    ];

    private readonly CoreViewerRuntime _runtime;
    private readonly Func<Task> _closeRequested;
    private readonly Func<Task> _focusedViewRequested;
    private readonly Func<Task>? _metadataChanged;
    private readonly TextBlock _title;
    private readonly TextBlock _summary;
    private readonly TextBlock _path;
    private readonly Button _copyPath;
    private readonly TextBlock _technical;
    private readonly TextBlock _exif;
    private readonly Button _exifRetry;
    private readonly TextBlock _works;
    private readonly TextBlock _groups;
    private readonly TextBlock _relations;
    private readonly StackPanel _relationCards;
    private readonly Func<AssetRelationInfo, Task>? _deleteRelationRequested;
    private readonly TextBlock _publications;
    private readonly TextBlock _publicationCount;
    private readonly StackPanel _publicationCards;
    private readonly ComboBox _ratingEditor;
    private readonly CheckBox _favoriteEditor;
    private readonly ComboBox _statusEditor;
    private readonly ComboBox _colorEditor;
    private readonly ManagedTagPicker _tagPicker;
    private readonly TextBox _notesEditor;
    private readonly TextBlock _saveStatus;
    private readonly Border _saveActionSurface;
    private readonly Button _save;
    private readonly Button _reset;
    private readonly Button _retry;
    private readonly Button _focused;
    private readonly Button _pin;
    private Button[] _ratingButtons = [];
    private Button[] _colorButtons = [];
    private readonly Avalonia.Controls.Image _preview;
    private readonly TextBlock _previewStatus;
    private readonly Border _previewSurface;
    private readonly Grid _summaryLayout;
    private readonly StackPanel _summaryDetails;
    private readonly ContentControl _tabContent;
    private readonly Button[] _tabButtons;
    private readonly Control[] _tabPages;
    private readonly object _previewReleaseGate = new();
    private readonly HashSet<Task> _pendingPreviewReleases = [];
    private readonly List<DecodedBitmapLease> _unfencedPreviewLeases = [];
    private DecodedBitmapLease? _previewLease;
    private Compositor? _compositor;
    private Exception? _previewReleaseFailure;
    private CancellationTokenSource? _loadCancellation;
    private long _assetId;
    private ViewerAsset? _currentAsset;
    private AssetUserMetadata? _loadedMetadata;
    private bool _loadingEditor;
    private bool _dirty;
    private bool _saving;
    private int _selectedTabIndex;
    private long _exifLoadedAssetId;
    private long _exifLoadingAssetId;
    private string _fullPath = string.Empty;

    public ContextualAssetDetailPanel(
        CoreViewerRuntime runtime,
        Func<Task> closeRequested,
        Func<Task> focusedViewRequested,
        Func<Task>? metadataChanged = null,
        Func<Task>? createWorkRequested = null,
        Func<Task>? createGroupRequested = null,
        Func<Task>? createPublicationRequested = null,
        Func<Task>? addToWorkRequested = null,
        Func<Task>? addToGroupRequested = null,
        Func<AssetRelationInfo, Task>? deleteRelationRequested = null)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _closeRequested = closeRequested
            ?? throw new ArgumentNullException(nameof(closeRequested));
        _focusedViewRequested = focusedViewRequested
            ?? throw new ArgumentNullException(nameof(focusedViewRequested));
        _metadataChanged = metadataChanged;
        _deleteRelationRequested =
            deleteRelationRequested;

        Focusable = true;

        _title = CreateValue(
            fontSize: 14,
            weight: FontWeight.Bold);
        _summary = CreateValue();
        _preview =
            new Avalonia.Controls.Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                VerticalAlignment =
                    VerticalAlignment.Center,
                MaxHeight = 168
            };
        _previewStatus =
            new TextBlock
            {
                Text = string.Empty,
                Foreground = LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12),
                IsVisible = false,
                IsHitTestVisible = false
            };
        _path = CreateValue(wrap: true);
        _copyPath =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "パスをコピー",
                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    IsEnabled = false
                });
        AutomationProperties.SetName(
            _copyPath,
            "画像のフルパスをコピー");
        _copyPath.Click +=
            async (_, _) =>
            {
                await CopyCurrentPathAsync();
            };
        _technical = CreateValue(wrap: true);
        _exif = CreateValue(wrap: true);
        _exifRetry =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "EXIFを再読み込み",
                    HorizontalAlignment =
                        HorizontalAlignment.Left,
                    IsVisible = false
                });
        AutomationProperties.SetName(
            _exifRetry,
            "EXIF情報を再読み込み");
        _exifRetry.Click +=
            async (_, _) =>
            {
                if (_currentAsset is not null)
                {
                    _exifLoadedAssetId = 0;
                    await LoadExifAsync(
                        _currentAsset,
                        _loadCancellation?.Token
                        ?? CancellationToken.None);
                }
            };
        _works = CreateValue(wrap: true);
        _groups = CreateValue(wrap: true);
        _relations = CreateValue(wrap: true);
        _relationCards =
            new StackPanel
            {
                Spacing = LumineDesign.Space6
            };
        _publications = CreateValue(wrap: true);
        _publications.IsVisible = false;
        _publicationCount =
            new TextBlock
            {
                Text = "0件",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            };
        _publicationCards =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space8
            };

        _ratingEditor =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    ItemsSource =
                        new[]
                        {
                            "未設定",
                            "★1",
                            "★2",
                            "★3",
                            "★4",
                            "★5"
                        },
                    MinWidth = 118
                });
        _favoriteEditor =
            LumineDesign.ConfigureCheckBox(
                new CheckBox
                {
                    Content = null,
                    Width = 36,
                    MinWidth = 36,
                    Height = 36,
                    MinHeight = 36,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                });
        _statusEditor =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    ItemsSource = StatusLabels,
                    MinWidth = 176,
                    MinHeight = 36,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                });
        _colorEditor =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    ItemsSource = ColorLabels,
                    MinWidth = 118
                });
        _ratingEditor.IsVisible = false;
        _colorEditor.IsVisible = false;
        var ratingPicker =
            CreateRatingPicker();
        var colorPicker =
            CreateColorPicker();

        _tagPicker =
            new ManagedTagPicker(
                _runtime,
                ApplyTagsAsync);
        _notesEditor =
            LumineDesign.ConfigureTextBox(
                new TextBox
                {
                    PlaceholderText = "ノート",
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 92
                });

        _saveStatus = new TextBlock
        {
            Foreground = LumineDesign.MutedForeground,
            FontSize = LumineDesign.CaptionFontSize,
            VerticalAlignment = VerticalAlignment.Center
        };

        _save =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "保存",
                    MinHeight = 30,
                    Padding = new Thickness(12, 5),
                    IsEnabled = false
                });
        ToolTip.SetTip(
            _save,
            "保存 (Ctrl+S)");
        _save.Click +=
            async (_, _) =>
                await SaveEditorAsync();

        _reset =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "元に戻す",
                    MinHeight = 30,
                    Padding = new Thickness(10, 5),
                    IsEnabled = false
                });
        _reset.Click +=
            (_, _) =>
                ResetEditor();

        _retry =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "再読み込み",
                    MinHeight = 30,
                    Padding = new Thickness(10, 5),
                    IsVisible = false
                });
        AutomationProperties.SetName(
            _retry,
            "詳細情報を再読み込み");
        ToolTip.SetTip(
            _retry,
            "この画像の整理情報と関連情報を再読み込み");
        _retry.Click +=
            async (_, _) =>
            {
                if (_currentAsset is not null)
                {
                    await ShowAssetAsync(
                        _currentAsset);
                }
            };

        _pin =
            LumineDesign.ConfigureIconButton(
                new Button
                {
                    Content =
                        LumineDesign.CreateStrokeIcon(
                            LumineDesign.PinIconPath,
                            16)
                },
                "詳細パネルをサイドに固定");
        _pin.Click +=
            (_, _) =>
                PinToggleRequested?.Invoke(
                    this,
                    EventArgs.Empty);

        var close =
            LumineDesign.ConfigureIconButton(
                new Button
                {
                    Content =
                        LumineDesign.CreateStrokeIcon(
                            LumineDesign.CloseIconPath,
                            16)
                },
                "詳細を閉じる");
        close.Click +=
            async (_, _) =>
                await _closeRequested();

        _focused =
            LumineDesign.ConfigureIconButton(
                new Button
                {
                    Content =
                        LumineDesign.CreateStrokeIcon(
                            LumineDesign.ViewIconPath,
                            18),
                    IsEnabled = false
                },
                "画像を表示");
        _focused.Click +=
            async (_, _) =>
                await _focusedViewRequested();

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto,Auto,Auto"),
                ColumnSpacing = LumineDesign.Space6,
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        LumineDesign.Space8,
                        LumineDesign.Space8,
                        LumineDesign.Space8)
            };
        header.Children.Add(
            new TextBlock
            {
                Text = "詳細",
                Foreground = LumineDesign.Foreground,
                FontWeight = FontWeight.Bold,
                FontSize = LumineDesign.BodyFontSize,
                VerticalAlignment =
                    VerticalAlignment.Center
            });
        Grid.SetColumn(_focused, 1);
        header.Children.Add(_focused);
        Grid.SetColumn(_pin, 2);
        header.Children.Add(_pin);
        Grid.SetColumn(close, 3);
        header.Children.Add(close);

        // Keep the same preview lease and text controls across layout
        // changes; resizing the drawer must never rebuild the image.
        _summaryLayout =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,Auto"),
                RowSpacing = LumineDesign.Space6,
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        LumineDesign.Space4,
                        LumineDesign.Space12,
                        LumineDesign.Space8)
            };

        var previewStage =
            new Grid();
        previewStage.Children.Add(_preview);
        previewStage.Children.Add(_previewStatus);

        _previewSurface =
            new Border
            {
                Height = 148,
                Background = LumineDesign.Background,
                BorderBrush = LumineDesign.Border,
                BorderThickness = new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.PanelRadius),
                ClipToBounds = true,
                Child = previewStage
            };
        _summaryDetails =
            new StackPanel
            {
                Spacing = LumineDesign.Space6,
                VerticalAlignment = VerticalAlignment.Center
            };
        _summaryDetails.Children.Add(_title);
        _summaryDetails.Children.Add(_summary);
        _summaryLayout.Children.Add(_previewSurface);
        Grid.SetRow(_summaryDetails, 1);
        _summaryLayout.Children.Add(_summaryDetails);

        var editor =
            new StackPanel
            {
                Spacing = 10
            };
        AddEditorRow(
            editor,
            "評価",
            ratingPicker);
        AddEditorRow(
            editor,
            "お気に入り",
            _favoriteEditor);
        AddEditorRow(
            editor,
            "状態",
            _statusEditor);
        AddEditorRow(
            editor,
            "タグ",
            _tagPicker,
            multiline: true);
        AddEditorRow(
            editor,
            "カラー",
            colorPicker);
        AddEditorRow(
            editor,
            "ノート",
            _notesEditor,
            multiline: true);

        var saveRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto,Auto,Auto"),
                ColumnSpacing = LumineDesign.Space6
            };
        saveRow.Children.Add(_saveStatus);
        if (LumineVisualMetrics.TextScaleFactor >= 1.5)
        {
            // At Windows 150–225% text, do not clip the three commands
            // against the narrow Inspector drawer. Give status its own
            // row and allow the existing buttons to wrap naturally.
            saveRow.ColumnDefinitions =
                new ColumnDefinitions("*");
            saveRow.RowDefinitions =
                new RowDefinitions("Auto,Auto");
            saveRow.RowSpacing = LumineDesign.Space4;
            _saveStatus.TextWrapping = TextWrapping.Wrap;
            var actions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            _retry.Margin =
                new Thickness(0, 0, LumineDesign.Space6, LumineDesign.Space4);
            _reset.Margin =
                new Thickness(0, 0, LumineDesign.Space6, LumineDesign.Space4);
            _save.Margin =
                new Thickness(0, 0, 0, LumineDesign.Space4);
            actions.Children.Add(_retry);
            actions.Children.Add(_reset);
            actions.Children.Add(_save);
            Grid.SetRow(actions, 1);
            saveRow.Children.Add(actions);
        }
        else
        {
            Grid.SetColumn(_retry, 1);
            saveRow.Children.Add(_retry);
            Grid.SetColumn(_reset, 2);
            saveRow.Children.Add(_reset);
            Grid.SetColumn(_save, 3);
            saveRow.Children.Add(_save);
        }

        var organizeBody =
            new StackPanel
            {
                Spacing = LumineDesign.Space12,
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        LumineDesign.Space12,
                        LumineDesign.Space12,
                        LumineDesign.Space16)
            };
        AddSection(
            organizeBody,
            "整理情報",
            editor);

        // Saving is the primary action of the Organize tab. Keep the
        // existing controls in the same Inspector, but outside the
        // scrollable metadata fields: otherwise a short window hides
        // Save/Reset below Notes and forces a long scroll to commit.
        _saveActionSurface =
            new Border
            {
                Background = LumineDesign.SurfaceRaised,
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.ControlRadius),
                Padding =
                    new Thickness(
                        LumineDesign.Space8,
                        LumineDesign.Space6),
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        0,
                        LumineDesign.Space12,
                        LumineDesign.Space12),
                Child = saveRow
            };

        Button CreateContextAction(
            string label,
            Func<Task> action)
        {
            var button =
                LumineDesign.ConfigureSecondaryButton(
                    new Button
                    {
                        Content = label
                    });
            button.Click +=
                async (_, _) =>
                {
                    button.IsEnabled = false;
                    try
                    {
                        await action();
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Trace.TraceError(
                            exception.ToString());
                        _saveStatus.Text =
                            "操作を完了できませんでした。もう一度お試しください。";
                    }
                    finally
                    {
                        button.IsEnabled = true;
                    }
                };
            return button;
        }

        Border CreateInspectorSectionCard(
            string label,
            Control content)
        {
            var body =
                new StackPanel
                {
                    Spacing =
                        LumineDesign.Space6
                };
            body.Children.Add(
                new TextBlock
                {
                    Text = label,
                    Foreground =
                        LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    FontWeight =
                        FontWeight.SemiBold
                });
            body.Children.Add(content);

            var card =
                new Border
                {
                    Child = body
                };
            card.Classes.Add("lumine-card");
            return card;
        }

        var creative =
            new StackPanel
            {
                Spacing = LumineDesign.Space8
            };

        var creativeActionButtons =
            new List<Control>();

        DropDownButton CreateCreativeMenu(
            string label,
            StackPanel actions)
        {
            var flyout =
                new Flyout
                {
                    Content =
                        new Border
                        {
                            Background =
                                LumineDesign.SurfaceRaised,
                            Padding =
                                new Thickness(
                                    LumineDesign.Space8),
                            Child = actions
                        }
                };
            var command =
                new DropDownButton
                {
                    Content = label,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    HorizontalContentAlignment =
                        HorizontalAlignment.Center,
                    Flyout = flyout
                };
            LumineDesign.ConfigureSecondaryButton(command);
            AttachCreativeFlyoutFocus(
                flyout,
                command,
                actions.Children.OfType<Button>().First());
            return command;
        }

        if (createWorkRequested is not null
            || createGroupRequested is not null)
        {
            var creationPanel =
                new StackPanel
                {
                    Width = 220,
                    Spacing = LumineDesign.Space6,
                    Margin =
                        new Thickness(
                            LumineDesign.Space4)
                };
            if (createWorkRequested is not null)
            {
                creationPanel.Children.Add(
                    CreateContextAction(
                        "Workを作成",
                        createWorkRequested));
            }

            if (createGroupRequested is not null)
            {
                creationPanel.Children.Add(
                    CreateContextAction(
                        "生成グループを作成",
                        createGroupRequested));
            }

            creativeActionButtons.Add(
                CreateCreativeMenu(
                    "新規作成",
                    creationPanel));
        }

        if (addToWorkRequested is not null
            || addToGroupRequested is not null)
        {
            var additionPanel =
                new StackPanel
                {
                    Width = 240,
                    Spacing = LumineDesign.Space6,
                    Margin =
                        new Thickness(
                            LumineDesign.Space4)
                };
            if (addToWorkRequested is not null)
            {
                additionPanel.Children.Add(
                    CreateContextAction(
                        "既存Workへ追加",
                        addToWorkRequested));
            }

            if (addToGroupRequested is not null)
            {
                additionPanel.Children.Add(
                    CreateContextAction(
                        "既存Generation Groupへ追加",
                        addToGroupRequested));
            }

            creativeActionButtons.Add(
                CreateCreativeMenu(
                    "既存へ追加",
                    additionPanel));
        }

        if (creativeActionButtons.Count > 0)
        {
            var actionColumns =
                string.Join(
                    ",",
                    Enumerable.Repeat(
                        "*",
                        creativeActionButtons.Count));
            var creativeActions =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            actionColumns),
                    ColumnSpacing =
                        LumineDesign.Space8
                };
            for (var index = 0;
                 index < creativeActionButtons.Count;
                 index++)
            {
                Grid.SetColumn(
                    creativeActionButtons[index],
                    index);
                creativeActions.Children.Add(
                    creativeActionButtons[index]);
            }

            creative.Children.Add(
                creativeActions);
        }

        creative.Children.Add(
            CreateInspectorSectionCard(
                "Work",
                _works));
        creative.Children.Add(
            CreateInspectorSectionCard(
                "Generation Group",
                _groups));

        var lineage =
            new StackPanel
            {
                Spacing = LumineDesign.Space6
            };
        lineage.Children.Add(_relations);
        lineage.Children.Add(_relationCards);
        creative.Children.Add(
            CreateInspectorSectionCard(
                "Lineage",
                lineage));

        var creativeBody =
            new StackPanel
            {
                Spacing = LumineDesign.Space8,
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        LumineDesign.Space12,
                        LumineDesign.Space12,
                        LumineDesign.Space16)
            };
        creativeBody.Children.Add(
            new TextBlock
            {
                Text = "制作コンテキスト",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                FontWeight =
                    FontWeight.SemiBold
            });
        creativeBody.Children.Add(
            creative);

        var publicationBody =
            new StackPanel
            {
                Spacing = LumineDesign.Space8,
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        LumineDesign.Space12,
                        LumineDesign.Space12,
                        LumineDesign.Space16)
            };
        if (createPublicationRequested is not null)
        {
            var createPublication =
                CreateContextAction(
                    "公開記録を作成",
                    createPublicationRequested);
            createPublication.HorizontalAlignment =
                HorizontalAlignment.Stretch;
            createPublication.HorizontalContentAlignment =
                HorizontalAlignment.Center;
            publicationBody.Children.Add(
                createPublication);
        }

        var publicationHeader =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto")
            };
        publicationHeader.Children.Add(
            new TextBlock
            {
                Text = "公開履歴",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                FontWeight =
                    FontWeight.SemiBold,
                VerticalAlignment =
                    VerticalAlignment.Center
            });
        Grid.SetColumn(
            _publicationCount,
            1);
        _publicationCount.VerticalAlignment =
            VerticalAlignment.Center;
        publicationHeader.Children.Add(
            _publicationCount);
        publicationBody.Children.Add(
            publicationHeader);
        publicationBody.Children.Add(
            _publicationCards);

        var informationBody =
            new StackPanel
            {
                Spacing = LumineDesign.Space8,
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        LumineDesign.Space12,
                        LumineDesign.Space12,
                        LumineDesign.Space16)
            };

        var pathBody =
            new StackPanel
            {
                Spacing = LumineDesign.Space6
            };
        pathBody.Children.Add(
            _path);
        pathBody.Children.Add(
            _copyPath);
        informationBody.Children.Add(
            CreateInspectorSectionCard(
                "場所",
                pathBody));

        informationBody.Children.Add(
            CreateInspectorSectionCard(
                "技術情報",
                _technical));

        var exifBody =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space6
            };
        exifBody.Children.Add(
            _exif);
        exifBody.Children.Add(
            _exifRetry);
        informationBody.Children.Add(
            CreateInspectorSectionCard(
                "撮影情報 (EXIF)",
                exifBody));

        _tabPages =
        [
            CreateTabScroll(organizeBody),
            CreateTabScroll(creativeBody),
            CreateTabScroll(publicationBody),
            CreateTabScroll(informationBody)
        ];

        _tabContent =
            new ContentControl
            {
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalContentAlignment =
                    VerticalAlignment.Stretch
            };

        _tabButtons =
            TabHeaders
                .Select(
                    (headerText, index) =>
                        CreateInspectorTabButton(
                            headerText,
                            index))
                .ToArray();

        var tabStrip =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,*,*,*"),
                ColumnSpacing = 2
            };
        for (var index = 0;
             index < _tabButtons.Length;
             index++)
        {
            Grid.SetColumn(
                _tabButtons[index],
                index);
            tabStrip.Children.Add(
                _tabButtons[index]);
        }

        var tabStripHost =
            new Border
            {
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        0,
                        LumineDesign.Space12,
                        LumineDesign.Space8),
                Child = tabStrip
            };
        tabStripHost.Classes.Add(
            "lumine-segmented-host");

        var tabLayout =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,*,Auto")
            };
        tabLayout.Children.Add(tabStripHost);
        Grid.SetRow(_tabContent, 1);
        tabLayout.Children.Add(_tabContent);
        Grid.SetRow(_saveActionSurface, 2);
        tabLayout.Children.Add(_saveActionSurface);

        var layout =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,Auto,*")
            };
        layout.Children.Add(header);
        Grid.SetRow(_summaryLayout, 1);
        layout.Children.Add(_summaryLayout);
        Grid.SetRow(tabLayout, 2);
        layout.Children.Add(tabLayout);

        SelectTab(0);

        Background = LumineDesign.Surface;
        Content = layout;

        _ratingEditor.SelectionChanged +=
            (_, _) =>
            {
                UpdateRatingPickerVisuals();
                MarkDirty();
            };
        _favoriteEditor.Click +=
            (_, _) => MarkDirty();
        _statusEditor.SelectionChanged +=
            (_, _) => MarkDirty();
        _colorEditor.SelectionChanged +=
            (_, _) =>
            {
                UpdateColorPickerVisuals();
                MarkDirty();
            };
        _notesEditor.TextChanged +=
            (_, _) => MarkDirty();
        KeyDown += OnKeyDown;
        AttachedToVisualTree += OnAttachedToVisualTree;

        ShowNoSelection();
    }

    internal long AssetId => _assetId;

    internal string TitleText =>
        _title.Text ?? string.Empty;

    internal string PathText =>
        _fullPath;

    internal string DisplayedPathTextForSmoke =>
        _path.Text ?? string.Empty;

    internal bool PathCopyEnabledForSmoke =>
        _copyPath.IsEnabled;

    internal string PathCopyValueForSmoke =>
        _copyPath.IsEnabled
            ? _fullPath
            : string.Empty;

    internal string TechnicalText =>
        _technical.Text ?? string.Empty;

    internal string ExifTextForSmoke =>
        _exif.Text ?? string.Empty;

    internal bool ExifRetryVisibleForSmoke =>
        _exifRetry.IsVisible;

    internal string RatingText =>
        _ratingEditor.SelectedIndex > 0
            ? $"★{_ratingEditor.SelectedIndex}"
            : "未設定";

    internal string ColorLabelForSmoke =>
        _colorEditor.SelectedIndex >= 0
        && _colorEditor.SelectedIndex < ColorValues.Length
            ? ColorValues[_colorEditor.SelectedIndex]
                ?? string.Empty
            : string.Empty;

    internal bool UsesDirectRatingControlsForSmoke =>
        _ratingButtons.Length == 5;

    internal bool UsesDirectColorControlsForSmoke =>
        _colorButtons.Length
            == ColorValues.Length;

    internal bool RetryVisibleForSmoke =>
        _retry.IsVisible;

    internal void InvokeRatingForSmoke(
        int rating) =>
        SetRatingFromDirectControl(rating);

    internal void InvokeColorForSmoke(
        int index) =>
        SetColorFromDirectControl(index);

    internal void PresentLoadFailureForSmoke() =>
        PresentLoadFailure(
            "テスト用の読み込み失敗");

    internal Task InvokeRetryForSmokeAsync() =>
        _currentAsset is null
            ? Task.CompletedTask
            : ShowAssetAsync(_currentAsset);

    internal string TagsText =>
        string.Join(
            ", ",
            _tagPicker.SelectedTags);

    internal string NotesText =>
        _notesEditor.Text ?? string.Empty;

    internal string WorksText =>
        _works.Text ?? string.Empty;

    internal string GroupsText =>
        _groups.Text ?? string.Empty;

    internal string RelationsText =>
        _relations.Text ?? string.Empty;

    internal string PublicationsText =>
        _publications.Text ?? string.Empty;

    internal bool IsDirty => _dirty;

    internal bool HasPreview =>
        _preview.Source is not null;

    internal string PreviewStatusForSmoke =>
        _previewStatus.Text ?? string.Empty;

    internal bool PreviewStatusVisibleForSmoke =>
        _previewStatus.IsVisible;


    internal event EventHandler? PinToggleRequested;

    internal bool IsCompactPresentationForSmoke =>
        _previewSurface.Height <= 120.5
        && _notesEditor.MinHeight <= 72.5;

    internal double PreviewHeightForSmoke =>
        _previewSurface.Height;

    internal bool UsesCompactHorizontalSummaryForSmoke =>
        Grid.GetColumn(_summaryDetails) == 1
        && Grid.GetRow(_summaryDetails) == 0
        && Math.Abs(_previewSurface.Width - 96) < 0.5;

    // A short desktop layout needs usable editable fields without
    // scrolling beyond the entire preview. Only the image preview,
    // not text or the four Inspector tabs, is reduced in height.
    // Large/full-height views retain the existing 120/148-DIP preview.
    internal static double ResolvePreviewHeightForSmoke(
        bool compact,
        double availableHeight) =>
        !compact
            ? 148
            : availableHeight > 0
                && availableHeight < 720
                    ? 80
                    : 120;

    internal void SetCompactPresentation(
        bool compact,
        double availableHeight = 0)
    {
        var previewHeight = ResolvePreviewHeightForSmoke(
            compact,
            availableHeight);
        // The selected image is already present in the Gallery. On short
        // screens the secondary Inspector overview places a small preview
        // beside the filename and image facts rather than stacking three
        // full-width rows above the edit controls. All metadata stays live.
        var horizontalSummary = compact
            && availableHeight > 0
            && availableHeight < 720;
        _summaryLayout.ColumnDefinitions = horizontalSummary
            ? new ColumnDefinitions("96,*")
            : new ColumnDefinitions("*");
        _summaryLayout.RowDefinitions = horizontalSummary
            ? new RowDefinitions("Auto")
            : new RowDefinitions("Auto,Auto");
        _summaryLayout.ColumnSpacing = horizontalSummary
            ? LumineDesign.Space8
            : 0;
        _summaryLayout.RowSpacing = horizontalSummary
            ? 0
            : LumineDesign.Space6;
        Grid.SetColumn(_summaryDetails, horizontalSummary ? 1 : 0);
        Grid.SetRow(_summaryDetails, horizontalSummary ? 0 : 1);
        _previewSurface.Width = horizontalSummary ? 96 : double.NaN;
        _summary.TextWrapping = horizontalSummary
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
        _previewSurface.Height = previewHeight;
        _preview.MaxHeight = previewHeight;
        _notesEditor.MinHeight =
            compact
                ? 72
                : 92;
    }

    internal void SetPinPresentation(
        bool pinned,
        bool available)
    {
        _pin.IsVisible = available;
        _pin.IsEnabled = available;
        _pin.Background =
            pinned
                ? LumineDesign.InteractionSelected
                : Brushes.Transparent;
        _pin.BorderBrush =
            pinned
                ? LumineDesign.BorderStrong
                : Brushes.Transparent;
        ToolTip.SetTip(
            _pin,
            pinned
                ? "詳細パネルの固定を解除"
                : "詳細パネルをサイドに固定");
        AutomationProperties.SetName(
            _pin,
            pinned
                ? "詳細パネルの固定を解除"
                : "詳細パネルをサイドに固定");
    }

    internal int SelectedTabIndex =>
        _selectedTabIndex;

    internal string SelectedTabHeader =>
        TabHeaders[_selectedTabIndex];

    internal IReadOnlyList<string> TabHeaders { get; } =
        new[] { "整理", "制作", "公開", "情報" };

    // The keyboard I shortcut enters the current Inspector tab. Do not
    // reset the user's tab/scroll state or create a second tab strip.
    internal bool FocusSelectedTab() =>
        _tabButtons[_selectedTabIndex].Focus();

    internal bool IsSelectedTabFocusedForSmoke =>
        _tabButtons[_selectedTabIndex].IsFocused;

    internal void SelectTabForSmoke(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            index,
            TabHeaders.Count);

        SelectTab(index);
    }

    // One stable footer for the Organize tab: it does not take part in
    // the tab's ScrollViewer content and collapses for the other jobs.
    internal bool SaveActionsDockedForSmoke =>
        _saveActionSurface.Parent is Grid parent
        && ReferenceEquals(_tabContent.Parent, parent)
        && Grid.GetRow(_tabContent) == 1
        && Grid.GetRow(_saveActionSurface) == 2
        && _saveActionSurface.IsVisible == (_selectedTabIndex == 0);

    internal bool SaveActionsVisibleForSmoke =>
        _saveActionSurface.IsVisible;

    internal bool SaveActionButtonsContainedForSmoke
    {
        get
        {
            if (!_saveActionSurface.IsVisible)
            {
                return true;
            }

            if (_saveActionSurface.Bounds.Width <= 0
                || _saveActionSurface.Bounds.Height <= 0)
            {
                return false;
            }

            foreach (var command in new[] { _retry, _reset, _save })
            {
                if (!command.IsEffectivelyVisible)
                {
                    continue;
                }

                var origin = command.TranslatePoint(
                    new Point(0, 0),
                    _saveActionSurface);
                if (origin is not { } point
                    || point.X < -0.5
                    || point.Y < -0.5
                    || point.X + command.Bounds.Width
                        > _saveActionSurface.Bounds.Width + 0.5
                    || point.Y + command.Bounds.Height
                        > _saveActionSurface.Bounds.Height + 0.5)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal bool TabPagesHaveIndependentScrollStateForSmoke =>
        _tabPages.Length == TabHeaders.Count
        && _tabPages.All(
            static page => page is ScrollViewer)
        && _tabPages
            .Distinct(
                ReferenceEqualityComparer.Instance)
            .Count() == _tabPages.Length;

    internal bool TabStripUsesLumineStatesForSmoke
    {
        get
        {
            if (_tabButtons.Length != TabHeaders.Count)
            {
                return false;
            }

            for (var index = 0;
                 index < _tabButtons.Length;
                 index++)
            {
                var button = _tabButtons[index];
                var selected =
                    index == _selectedTabIndex;

                if (!button.Classes.Contains(
                        "lumine-segment")
                    || button.Resources.Count != 0
                    || Math.Abs(
                        button.FontSize
                        - LumineDesign.CaptionFontSize) > 0.001
                    || button.Classes.Contains(
                        "selected") != selected)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public async Task ShowAssetAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);
        var token =
            _loadCancellation.Token;

        _assetId = asset.Id;
        _currentAsset = asset;
        _retry.IsVisible = false;
        _saveStatus.Foreground =
            LumineDesign.MutedForeground;
        _title.Text = asset.DisplayName;
        _summary.Text =
            FormatSummary(asset);
        _fullPath =
            Path.GetFullPath(
                Path.Combine(
                    _runtime.LibraryRoot,
                    asset.RelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
        _path.Text =
            asset.RelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar);
        _copyPath.Content =
            "パスをコピー";
        _copyPath.IsEnabled = true;
        _technical.Text =
            FormatTechnical(asset);
        _exifLoadedAssetId = 0;
        _exifLoadingAssetId = 0;
        _exif.Text =
            _selectedTabIndex == 3
                ? "EXIF情報を読み込んでいます…"
                : "「情報」タブを開くとEXIF情報を読み込みます。";
        _exifRetry.IsVisible = false;
        _focused.IsEnabled = true;
        _previewStatus.Text = "プレビューを読み込んでいます…";
        _previewStatus.IsVisible = true;

        SetEditorEnabled(false);
        _saveStatus.Text = "整理情報を読み込んでいます…";

        await LoadPreviewAsync(
            asset,
            token);

        try
        {
            var metadata =
                await _runtime.LibraryService
                    .GetUserMetadataAsync(
                        _runtime.Library.Id,
                        asset.Id,
                        token)
                ?? new AssetUserMetadata(
                    asset.Id,
                    null,
                    false,
                    string.Empty,
                    null,
                    null,
                    Array.Empty<string>());

            token.ThrowIfCancellationRequested();
            if (_assetId != asset.Id)
            {
                return;
            }

            _loadedMetadata = metadata;
            PopulateEditor(metadata);
            await _tagPicker.RefreshAsync(
                metadata.Tags,
                token);
            SetEditorEnabled(true);
            _retry.IsVisible = false;
            _saveStatus.Foreground =
                LumineDesign.MutedForeground;
            _saveStatus.Text = "保存済み";

            await LoadCreativeContextAsync(
                asset.Id,
                token);

            if (_selectedTabIndex == 3)
            {
                await LoadExifAsync(
                    asset,
                    token);
            }
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (_assetId != asset.Id)
            {
                return;
            }

            _loadedMetadata = null;
            SetEditorEnabled(false);
            PresentLoadFailure(
                "整理情報を取得できませんでした。再読み込みできます。");
        }
    }

    private async Task LoadExifAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken)
    {
        if (_exifLoadedAssetId == asset.Id
            || _exifLoadingAssetId == asset.Id)
        {
            return;
        }

        _exifLoadingAssetId = asset.Id;
        _exifRetry.IsVisible = false;
        _exif.Text =
            "EXIF情報を読み込んでいます…";

        try
        {
            var metadata =
                await _runtime.GetExifMetadataAsync(
                    asset,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (_assetId != asset.Id)
            {
                return;
            }

            _exif.Text =
                FormatExif(metadata);
            _exifLoadedAssetId = asset.Id;
            _exifRetry.IsVisible = false;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_assetId != asset.Id)
            {
                return;
            }

            System.Diagnostics.Trace.TraceWarning(
                $"EXIF metadata load failed for asset {asset.Id}: {exception}");
            _exif.Text =
                "EXIF情報を読み込めませんでした。画像の表示や整理情報には影響しません。";
            _exifRetry.IsVisible = true;
        }
        finally
        {
            if (_exifLoadingAssetId == asset.Id)
            {
                _exifLoadingAssetId = 0;
            }
        }
    }

    internal void SetEditorValuesForSmoke(
        int? rating,
        bool favorite,
        string? statusLabel,
        string? colorLabel,
        string tags,
        string notes)
    {
        _loadingEditor = true;
        try
        {
            _ratingEditor.SelectedIndex =
                rating ?? 0;
            _favoriteEditor.IsChecked =
                favorite;
            _statusEditor.SelectedIndex =
                IndexOfValue(
                    StatusValues,
                    statusLabel);
            _colorEditor.SelectedIndex =
                IndexOfValue(
                    ColorValues,
                    colorLabel);
            _tagPicker.SetSelectedTags(
                ParseTags(tags));
            _notesEditor.Text = notes;
        }
        finally
        {
            _loadingEditor = false;
        }

        MarkDirty();
    }

    internal async Task<AssetUserMetadata?>
        SaveEditorAsync(
            bool notify = true,
            CancellationToken cancellationToken = default)
    {
        if (_assetId <= 0
            || _saving)
        {
            return _loadedMetadata;
        }

        var update =
            new AssetUserMetadataUpdate(
                Rating:
                    _ratingEditor.SelectedIndex > 0
                        ? _ratingEditor.SelectedIndex
                        : null,
                Favorite:
                    _favoriteEditor.IsChecked == true,
                Notes:
                    _notesEditor.Text
                    ?? string.Empty,
                StatusLabel:
                    ValueAt(
                        StatusValues,
                        _statusEditor.SelectedIndex),
                ColorLabel:
                    ValueAt(
                        ColorValues,
                        _colorEditor.SelectedIndex),
                Tags:
                    _tagPicker.SelectedTags);

        _saving = true;
        _save.IsEnabled = false;
        _reset.IsEnabled = false;
        _saveStatus.Text = "保存しています…";

        try
        {
            var saved =
                await _runtime.LibraryService
                    .SetUserMetadataAsync(
                        _runtime.Library.Id,
                        _assetId,
                        update,
                        cancellationToken);

            _loadedMetadata = saved;
            _dirty = false;
            PopulateEditor(saved);
            _saveStatus.Text = "保存しました";

            if (notify
                && _metadataChanged is not null)
            {
                await _metadataChanged();
            }

            return saved;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                exception.ToString());
            _saveStatus.Text =
                "保存できませんでした。もう一度お試しください。";
            _save.IsEnabled = true;
            _reset.IsEnabled =
                _loadedMetadata is not null;
            throw;
        }
        finally
        {
            _saving = false;
        }
    }

    public void ShowNoSelection()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _assetId = 0;
        _currentAsset = null;
        _loadedMetadata = null;
        _retry.IsVisible = false;
        _saveStatus.Foreground =
            LumineDesign.MutedForeground;
        _dirty = false;
        _title.Text =
            "画像を選択してください";
        _summary.Text =
            "選択した画像の情報をここに表示します。";
        _fullPath = string.Empty;
        _path.Text = "—";
        _copyPath.Content =
            "パスをコピー";
        _copyPath.IsEnabled = false;
        _technical.Text = "—";
        _exif.Text = "—";
        _exifRetry.IsVisible = false;
        _exifLoadedAssetId = 0;
        _exifLoadingAssetId = 0;
        _works.Text = "—";
        _groups.Text = "—";
        _relations.Text = "—";
        _relations.IsVisible = true;
        _relationCards.Children.Clear();
        _publications.Text = "—";
        _publicationCount.Text = "0件";
        _publicationCards.Children.Clear();
        _publicationCards.Children.Add(
            CreatePublicationMessageCard(
                "公開履歴はありません。",
                warning: false));
        _saveStatus.Text = "—";
        _focused.IsEnabled = false;
        _previewStatus.Text = string.Empty;
        _previewStatus.IsVisible = false;
        SelectTab(0);
        ReplacePreviewLease(null);

        _loadingEditor = true;
        try
        {
            _ratingEditor.SelectedIndex = 0;
            _favoriteEditor.IsChecked = false;
            _statusEditor.SelectedIndex = 0;
            _colorEditor.SelectedIndex = 0;
            _tagPicker.SetSelectedTags(
                Array.Empty<string>());
            _tagPicker.SetInteractionEnabled(false);
            _notesEditor.Text = string.Empty;
        }
        finally
        {
            _loadingEditor = false;
        }

        SetEditorEnabled(false);
    }

    private async Task CopyCurrentPathAsync()
    {
        if (!_copyPath.IsEnabled)
        {
            return;
        }

        var path =
            _fullPath;
        if (string.IsNullOrWhiteSpace(path)
            || string.Equals(
                path,
                "—",
                StringComparison.Ordinal))
        {
            _copyPath.IsEnabled = false;
            return;
        }

        var clipboard =
            TopLevel.GetTopLevel(this)
                ?.Clipboard;
        if (clipboard is null)
        {
            _saveStatus.Foreground =
                LumineDesign.Warning;
            _saveStatus.Text =
                "パスをコピーできませんでした。";
            return;
        }

        _copyPath.IsEnabled = false;
        try
        {
            await clipboard.SetTextAsync(path);
            await clipboard.FlushAsync();
            _copyPath.Content =
                "コピー済み";
            _saveStatus.Foreground =
                LumineDesign.MutedForeground;
            _saveStatus.Text =
                "画像のフルパスをコピーしました。";

            await Task.Delay(
                TimeSpan.FromMilliseconds(1500));

            if (string.Equals(
                    _copyPath.Content as string,
                    "コピー済み",
                    StringComparison.Ordinal))
            {
                _copyPath.Content =
                    "パスをコピー";
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                exception.ToString());
            _copyPath.Content =
                "パスをコピー";
            _saveStatus.Foreground =
                LumineDesign.Warning;
            _saveStatus.Text =
                "パスをコピーできませんでした。もう一度お試しください。";
        }
        finally
        {
            _copyPath.IsEnabled =
                _assetId > 0
                && !string.IsNullOrWhiteSpace(
                    _fullPath);
        }
    }

    public void PrepareForDetach()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        ReplacePreviewLease(null);
        KeyDown -= OnKeyDown;
        AttachedToVisualTree -= OnAttachedToVisualTree;
    }

    public async Task DrainPreviewBitmapReleasesAsync()
    {
        while (true)
        {
            Task[] pending;

            lock (_previewReleaseGate)
            {
                if (_previewReleaseFailure is not null)
                {
                    throw new InvalidOperationException(
                        "An Inspector preview composition release failed.",
                        _previewReleaseFailure);
                }

                pending = [.. _pendingPreviewReleases];
                if (pending.Length == 0)
                {
                    return;
                }
            }

            await Task.WhenAll(pending)
                .ConfigureAwait(false);
        }
    }

    private void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        _compositor =
            ElementComposition.GetElementVisual(this)?.Compositor
            ?? _compositor;
    }

    private async Task LoadPreviewAsync(
        ViewerAsset asset,
        CancellationToken cancellationToken)
    {
        DecodedBitmapLease? lease = null;

        try
        {
            var thumbnail =
                await _runtime.ViewerSession
                    .GetThumbnailAsync(
                        asset,
                        ViewerThumbnailPriority.Interactive,
                        cancellationToken);
            lease =
                await _runtime.ViewerSession.BitmapCache
                    .AcquireAsync(
                        thumbnail,
                        cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (_assetId != asset.Id)
            {
                return;
            }

            var next = lease;
            lease = null;
            ReplacePreviewLease(next);
            _previewStatus.Text = string.Empty;
            _previewStatus.IsVisible = false;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                $"Inspector preview load failed for asset {asset.Id}: {exception}");

            if (_assetId == asset.Id)
            {
                ReplacePreviewLease(null);
                _previewStatus.Text =
                    "プレビューを表示できません。再選択すると再試行します。";
                _previewStatus.IsVisible = true;
            }
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private void ReplacePreviewLease(
        DecodedBitmapLease? next)
    {
        var previous = _previewLease;
        _previewLease = next;
        _preview.Source = next?.Bitmap;

        if (previous is not null)
        {
            ReleasePreviewLeaseAfterComposition(
                previous);
        }
    }

    private void ReleasePreviewLeaseAfterComposition(
        DecodedBitmapLease lease)
    {
        var compositor = _compositor;
        if (compositor is null)
        {
            lease.Dispose();
            return;
        }

        Task release;
        try
        {
            var batch =
                compositor.RequestCompositionBatchCommitAsync();
            release =
                DisposePreviewLeaseAfterAsync(
                    lease,
                    batch.Rendered);
        }
        catch (Exception exception)
        {
            lock (_previewReleaseGate)
            {
                _previewReleaseFailure ??= exception;
                _unfencedPreviewLeases.Add(lease);
            }

            return;
        }

        lock (_previewReleaseGate)
        {
            _pendingPreviewReleases.Add(release);
        }

        _ = ObservePreviewReleaseAsync(release);
    }

    private async Task ObservePreviewReleaseAsync(
        Task release)
    {
        try
        {
            await release.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_previewReleaseGate)
            {
                _previewReleaseFailure ??= exception;
            }
        }
        finally
        {
            lock (_previewReleaseGate)
            {
                _pendingPreviewReleases.Remove(release);
            }
        }
    }

    private static async Task DisposePreviewLeaseAfterAsync(
        DecodedBitmapLease lease,
        Task compositionRendered)
    {
        await compositionRendered.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(
            lease.Dispose);
    }

    private async Task LoadCreativeContextAsync(
        long assetId,
        CancellationToken cancellationToken)
    {
        _works.Text = "読み込み中…";
        _groups.Text = "読み込み中…";
        _relations.Text = "読み込み中…";
        _relations.IsVisible = true;
        _relationCards.Children.Clear();
        _publications.Text = "読み込み中…";
        _publicationCount.Text =
            "読み込み中…";
        _publicationCards.Children.Clear();

        try
        {
            var context =
                await _runtime.LibraryService
                    .GetAssetCreativeContextAsync(
                        _runtime.Library.Id,
                        assetId,
                        cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_assetId != assetId)
            {
                return;
            }

            _works.Text =
                context.Works.Count == 0
                    ? "なし"
                    : string.Join(
                        "\n",
                        context.Works.Select(
                            static work =>
                                $"{work.Title} · {work.Assets.Count:N0}枚"
                                + (string.IsNullOrWhiteSpace(work.Description)
                                    ? string.Empty
                                    : $"\n  {work.Description}")));

            _groups.Text =
                context.GenerationGroups.Count == 0
                    ? "なし"
                    : string.Join(
                        "\n\n",
                        context.GenerationGroups.Select(
                            static group =>
                                $"{group.Name} · {group.Assets.Count:N0}枚"
                                + (string.IsNullOrWhiteSpace(group.ModelName)
                                    ? string.Empty
                                    : $" · {group.ModelName}")
                                + (group.Steps > 0
                                    ? $" · {group.Steps} steps / CFG {group.CfgScale:0.##}"
                                    : string.Empty)
                                + (string.IsNullOrWhiteSpace(group.Prompt)
                                    ? string.Empty
                                    : $"\n  Prompt: {group.Prompt}")));

            _relations.Text =
                context.Relations.Count == 0
                    ? "なし"
                    : string.Join(
                        "\n",
                        context.Relations.Select(
                            static relation =>
                                $"{relation.Parent.FileName} → {relation.Child.FileName}"
                                + $" · {relation.RelationType}"
                                + (string.IsNullOrWhiteSpace(relation.Note)
                                    ? string.Empty
                                    : $" · {relation.Note}")));
            RenderRelationCards(
                context.Relations);

            _publications.Text =
                context.Publications.Count == 0
                    ? "なし"
                    : string.Join(
                        "\n\n",
                        context.Publications.Select(
                            static publication =>
                                $"{publication.PublishedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
                                + $" · {publication.Destination}"
                                + (string.IsNullOrWhiteSpace(publication.Account)
                                    ? string.Empty
                                    : $" · {publication.Account}")
                                + (string.IsNullOrWhiteSpace(publication.Title)
                                    ? string.Empty
                                    : $"\n  {publication.Title}")
                                + (string.IsNullOrWhiteSpace(publication.TagsSnapshot)
                                    ? string.Empty
                                    : $"\n  {publication.TagsSnapshot}")
                                + $"\n  {string.Join(", ", publication.Assets.Select(static asset => asset.FileName))}"));
            RenderPublicationCards(
                context.Publications,
                context.PublicationCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_assetId != assetId)
            {
                return;
            }

            System.Diagnostics.Trace.TraceError(
                exception.ToString());
            const string message =
                "制作コンテキストを取得できませんでした。再読み込みしてください。";
            _works.Text = message;
            _groups.Text = "—";
            _relations.Text = "—";
            _relations.IsVisible = true;
            _relationCards.Children.Clear();
            _publications.Text = "—";
            _publicationCount.Text =
                "取得できませんでした";
            _publicationCards.Children.Clear();
            _publicationCards.Children.Add(
                CreatePublicationMessageCard(
                    "公開履歴を取得できませんでした。",
                    warning: true));
            _retry.IsVisible = true;
            _saveStatus.Foreground =
                LumineDesign.Warning;
            _saveStatus.Text =
                "追加情報の取得に失敗しました。再読み込みできます。";
        }
    }

    private void RenderRelationCards(
        IReadOnlyList<AssetRelationInfo> relations)
    {
        _relationCards.Children.Clear();
        _relations.IsVisible =
            relations.Count == 0
            || _deleteRelationRequested is null;
        _relationCards.IsVisible =
            relations.Count > 0
            && _deleteRelationRequested is not null;

        if (!_relationCards.IsVisible
            || _deleteRelationRequested is null)
        {
            return;
        }

        foreach (var relation in relations)
        {
            var description =
                $"{relation.Parent.FileName} → {relation.Child.FileName}"
                + $" · {relation.RelationType}"
                + (string.IsNullOrWhiteSpace(
                        relation.Note)
                    ? string.Empty
                    : $" · {relation.Note}");
            var text =
                new TextBlock
                {
                    Text = description,
                    Foreground =
                        LumineDesign.Foreground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    TextWrapping =
                        TextWrapping.Wrap,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };
            var remove =
                LumineDesign.ConfigureDangerButton(
                    new Button
                    {
                        Content = "削除…",
                        MinWidth = 68
                    });
            AutomationProperties.SetName(
                remove,
                $"Lineageを削除: {description}");
            remove.Click +=
                async (_, _) =>
                {
                    remove.IsEnabled = false;
                    try
                    {
                        await _deleteRelationRequested(
                            relation);
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Trace.TraceError(
                            exception.ToString());
                        _saveStatus.Foreground =
                            LumineDesign.Warning;
                        _saveStatus.Text =
                            "Lineageを削除できませんでした。もう一度お試しください。";
                    }
                    finally
                    {
                        remove.IsEnabled = true;
                    }
                };

            var row =
                new Grid
                {
                    ColumnDefinitions =
                        new ColumnDefinitions(
                            "*,Auto"),
                    ColumnSpacing =
                        LumineDesign.Space8
                };
            row.Children.Add(text);
            Grid.SetColumn(
                remove,
                1);
            row.Children.Add(remove);
            _relationCards.Children.Add(row);
        }
    }

    private void RenderPublicationCards(
        IReadOnlyList<PublicationInfo> publications,
        long totalCount)
    {
        _publicationCards.Children.Clear();
        _publicationCount.Text =
            $"{totalCount:N0}件";

        if (publications.Count == 0)
        {
            _publicationCards.Children.Add(
                CreatePublicationMessageCard(
                    "この画像の公開履歴はありません。",
                    warning: false));
            return;
        }

        foreach (var publication in
                 publications
                     .OrderByDescending(
                         static item =>
                             item.PublishedAtUtc))
        {
            _publicationCards.Children.Add(
                CreatePublicationCard(
                    publication));
        }

        if (totalCount > publications.Count)
        {
            _publicationCards.Children.Add(
                CreatePublicationMessageCard(
                    $"最新{publications.Count:N0}件を表示しています。全{totalCount:N0}件は左の「公開履歴」から確認できます。",
                    warning: false));
        }
    }

    private static Control CreatePublicationCard(
        PublicationInfo publication)
    {
        var body =
            new StackPanel
            {
                Spacing =
                    LumineDesign.Space6
            };

        body.Children.Add(
            new TextBlock
            {
                Text =
                    $"{publication.PublishedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
                    + $" · {publication.Destination}"
                    + (string.IsNullOrWhiteSpace(
                            publication.Account)
                        ? string.Empty
                        : $" · {publication.Account}"),
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    TextWrapping.Wrap
            });

        if (!string.IsNullOrWhiteSpace(
                publication.Title))
        {
            body.Children.Add(
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
            var excerpt =
                publication.Body.Length <= 280
                    ? publication.Body
                    : publication.Body[..277]
                        + "…";
            body.Children.Add(
                CreatePublicationDetail(
                    excerpt));
        }

        if (!string.IsNullOrWhiteSpace(
                publication.TagsSnapshot))
        {
            body.Children.Add(
                CreatePublicationDetail(
                    $"タグ: {publication.TagsSnapshot}"));
        }

        if (publication.Assets.Count > 0)
        {
            body.Children.Add(
                CreatePublicationDetail(
                    FormatPublicationAssets(
                        publication)));
        }

        if (!string.IsNullOrWhiteSpace(
                publication.ExternalId))
        {
            body.Children.Add(
                CreatePublicationDetail(
                    $"外部ID: {publication.ExternalId}"));
        }

        if (!string.IsNullOrWhiteSpace(
                publication.ExternalUrl))
        {
            body.Children.Add(
                CreatePublicationDetail(
                    $"URL: {publication.ExternalUrl}"));
        }

        foreach (var flag in
                 ExtractPublicationFlags(
                     publication.PlatformMetadataJson))
        {
            body.Children.Add(
                CreatePublicationDetail(
                    flag));
        }

        return new Border
        {
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
            Child = body
        };
    }

    private static string FormatPublicationAssets(
        PublicationInfo publication)
    {
        var total =
            publication.AssetCount >= 0
                ? publication.AssetCount
                : publication.Assets.Count;
        var visible =
            string.Join(
                ", ",
                publication.Assets.Select(
                    static asset =>
                        asset.FileName));

        if (total <= publication.Assets.Count)
        {
            return "画像: " + visible;
        }

        return "画像: "
            + visible
            + $", … +{total - publication.Assets.Count:N0}枚";
    }

    private static TextBlock CreatePublicationDetail(
        string text) =>
        new()
        {
            Text = text,
            Foreground =
                LumineDesign.MutedForeground,
            FontSize =
                LumineDesign.CaptionFontSize,
            TextWrapping =
                TextWrapping.Wrap
        };

    private static Control CreatePublicationMessageCard(
        string message,
        bool warning) =>
        new Border
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
                    LumineDesign.Space12),
            Child =
                new TextBlock
                {
                    Text = message,
                    Foreground =
                        warning
                            ? LumineDesign.Warning
                            : LumineDesign.MutedForeground,
                    FontSize =
                        LumineDesign.CaptionFontSize,
                    TextWrapping =
                        TextWrapping.Wrap
                }
        };

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
                if (!string.IsNullOrWhiteSpace(
                        value))
                {
                    flags.Add(
                        $"{property.Name}: {value}");
                }
            }

            return flags;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    internal int PublicationCardCountForSmoke =>
        _publicationCards.Children.Count;

    internal string PublicationCountTextForSmoke =>
        _publicationCount.Text
        ?? string.Empty;

    internal IReadOnlyList<string>
        PublicationCardTextForSmoke =>
        _publicationCards.Children
            .Select(
                static child =>
                    string.Join(
                        "\n",
                        child
                            .GetVisualDescendants()
                            .OfType<TextBlock>()
                            .Select(
                                static block =>
                                    block.Text
                                    ?? string.Empty)))
            .ToArray();

    private async Task ApplyTagsAsync(
        IReadOnlyList<string> tags)
    {
        if (_assetId <= 0)
        {
            return;
        }

        var saved =
            await _runtime.LibraryService
                .SetAssetTagsAsync(
                    _runtime.Library.Id,
                    _assetId,
                    tags);

        if (_loadedMetadata is not null)
        {
            _loadedMetadata =
                _loadedMetadata with
                {
                    Tags = saved
                };
        }

        _saveStatus.Text =
            "タグを更新しました";

        if (_metadataChanged is not null)
        {
            await _metadataChanged();
        }
    }

    private Control CreateRatingPicker()
    {
        var panel =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = LumineDesign.Space4,
                MinHeight = 36,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
        AutomationProperties.SetName(
            panel,
            "評価");

        _ratingButtons =
            Enumerable.Range(1, 5)
                .Select(
                    rating =>
                    {
                        var glyph =
                            new TextBlock
                            {
                                Text = "★",
                                FontSize = 19,
                                LineHeight = 20,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                IsHitTestVisible = false
                            };

                        var button =
                            new Button
                            {
                                Content = glyph,
                                Width = 34,
                                MinWidth = 34,
                                MaxWidth = 34,
                                Height = 34,
                                MinHeight = 34,
                                MaxHeight = 34,
                                Padding = new Thickness(0),
                                CornerRadius =
                                    new CornerRadius(
                                        LumineDesign.ControlRadius),
                                Background = Brushes.Transparent,
                                BorderBrush = Brushes.Transparent,
                                BorderThickness = new Thickness(1),
                                HorizontalContentAlignment =
                                    HorizontalAlignment.Center,
                                VerticalContentAlignment =
                                    VerticalAlignment.Center,
                                HorizontalAlignment =
                                    HorizontalAlignment.Center,
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            };

                        button.Resources[
                            "ButtonBackgroundPointerOver"] =
                            LumineDesign.InteractionHover;
                        button.Resources[
                            "ButtonBackgroundPressed"] =
                            LumineDesign.InteractionPressed;
                        button.Resources[
                            "ButtonBackgroundFocused"] =
                            LumineDesign.InteractionNeutral;
                        button.Resources[
                            "ButtonBorderBrushFocused"] =
                            LumineDesign.Focus;

                        var accessibleName =
                            $"評価 {rating}";
                        AutomationProperties.SetName(
                            button,
                            accessibleName);
                        button.Click +=
                            (_, _) =>
                                SetRatingFromDirectControl(
                                    rating);
                        panel.Children.Add(button);
                        return button;
                    })
                .ToArray();

        UpdateRatingPickerVisuals();
        return panel;
    }

    private Control CreateColorPicker()
    {
        var panel =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions(
                        "*,*,*,*,*,*,*,*"),
                Width = 224,
                MinWidth = 224,
                MaxWidth = 224,
                MinHeight = 36,
                HorizontalAlignment =
                    HorizontalAlignment.Left,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        AutomationProperties.SetName(
            panel,
            "カラー");

        _colorButtons =
            ColorValues
                .Select(
                    (value, index) =>
                    {
                        var swatch =
                            ResolveColorBrush(value);

                        var swatchContent =
                            new Grid
                            {
                                Width = 20,
                                Height = 20
                            };
                        swatchContent.Children.Add(
                            new Border
                            {
                                Width = 20,
                                Height = 20,
                                CornerRadius =
                                    new CornerRadius(10),
                                Background =
                                    value is null
                                        ? LumineDesign.ControlSurface
                                        : swatch,
                                BorderBrush =
                                    value is null
                                        ? LumineDesign.BorderStrong
                                        : LumineDesign.Border,
                                BorderThickness =
                                    new Thickness(1),
                                HorizontalAlignment =
                                    HorizontalAlignment.Center,
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            });
                        if (value is null)
                        {
                            swatchContent.Children.Add(
                                new TextBlock
                                {
                                    Text = "×",
                                    FontSize = 14,
                                    LineHeight = 16,
                                    Foreground =
                                        LumineDesign.MutedForeground,
                                    TextAlignment =
                                        TextAlignment.Center,
                                    HorizontalAlignment =
                                        HorizontalAlignment.Center,
                                    VerticalAlignment =
                                        VerticalAlignment.Center,
                                    IsHitTestVisible = false
                                });
                        }

                        var button =
                            new Button
                            {
                                Content = swatchContent,
                                Width = 28,
                                MinWidth = 28,
                                MaxWidth = 28,
                                Height = 28,
                                MinHeight = 28,
                                MaxHeight = 28,
                                Padding = new Thickness(0),
                                Margin =
                                    new Thickness(
                                        0,
                                        4),
                                CornerRadius =
                                    new CornerRadius(14),
                                Background = Brushes.Transparent,
                                BorderBrush = Brushes.Transparent,
                                BorderThickness =
                                    new Thickness(2),
                                HorizontalContentAlignment =
                                    HorizontalAlignment.Center,
                                VerticalContentAlignment =
                                    VerticalAlignment.Center,
                                HorizontalAlignment =
                                    HorizontalAlignment.Center,
                                VerticalAlignment =
                                    VerticalAlignment.Center
                            };
                        var accessibleName =
                            $"カラー {ColorLabels[index]}";
                        AutomationProperties.SetName(
                            button,
                            accessibleName);
                        ToolTip.SetTip(
                            button,
                            ColorLabels[index]);

                        button.Resources[
                            "ButtonBackgroundPointerOver"] =
                            LumineDesign.InteractionHover;
                        button.Resources[
                            "ButtonBackgroundPressed"] =
                            LumineDesign.InteractionPressed;
                        button.Resources[
                            "ButtonBackgroundFocused"] =
                            LumineDesign.InteractionNeutral;
                        button.Resources[
                            "ButtonBorderBrushFocused"] =
                            LumineDesign.Focus;

                        button.Click +=
                            (_, _) =>
                                SetColorFromDirectControl(
                                    index);
                        Grid.SetColumn(
                            button,
                            index);
                        panel.Children.Add(button);
                        return button;
                    })
                .ToArray();

        UpdateColorPickerVisuals();
        return panel;
    }

    private void SetRatingFromDirectControl(
        int rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rating));
        }

        _ratingEditor.SelectedIndex =
            _ratingEditor.SelectedIndex == rating
                ? 0
                : rating;
    }

    private void SetColorFromDirectControl(
        int index)
    {
        if (index < 0
            || index >= ColorValues.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index));
        }

        _colorEditor.SelectedIndex = index;
    }

    private void UpdateRatingPickerVisuals()
    {
        var selected =
            _ratingEditor.SelectedIndex;
        for (var index = 0;
             index < _ratingButtons.Length;
             index++)
        {
            var rating = index + 1;
            var button = _ratingButtons[index];
            if (button.Content is TextBlock glyph)
            {
                glyph.Foreground =
                    rating <= selected
                        ? LumineDesign.Warning
                        : LumineDesign.MutedForeground;
            }

            // Rating is communicated by the filled-star count. Keep every
            // resting cell visually identical so the selected rating never
            // looks like one oversized boxed button.
            button.Background =
                Brushes.Transparent;
            button.BorderBrush =
                Brushes.Transparent;
        }
    }

    private void UpdateColorPickerVisuals()
    {
        var selected =
            _colorEditor.SelectedIndex;
        for (var index = 0;
             index < _colorButtons.Length;
             index++)
        {
            var button = _colorButtons[index];
            var active = index == selected;

            // Keep the hit target and inner swatch geometry fixed. Selection
            // is an outer ring, so choosing "未設定" or a color never changes
            // the apparent chip size or shifts the row.
            button.Background =
                Brushes.Transparent;
            button.BorderBrush =
                active
                    ? LumineDesign.Focus
                    : Brushes.Transparent;
            button.BorderThickness =
                new Thickness(2);
        }
    }

    private static IBrush ResolveColorBrush(
        string? value) =>
        value switch
        {
            "red" =>
                new SolidColorBrush(
                    Color.Parse("#EF4444")),
            "orange" =>
                new SolidColorBrush(
                    Color.Parse("#F97316")),
            "yellow" =>
                new SolidColorBrush(
                    Color.Parse("#EAB308")),
            "green" =>
                new SolidColorBrush(
                    Color.Parse("#22C55E")),
            "blue" =>
                new SolidColorBrush(
                    Color.Parse("#3B82F6")),
            "purple" =>
                new SolidColorBrush(
                    Color.Parse("#A855F7")),
            "gray" =>
                new SolidColorBrush(
                    Color.Parse("#6B7280")),
            _ => LumineDesign.ControlSurface
        };

    private void PresentLoadFailure(
        string message)
    {
        _retry.IsVisible =
            _currentAsset is not null;
        _saveStatus.Foreground =
            LumineDesign.Danger;
        _saveStatus.Text = message;
    }

    private void PopulateEditor(
        AssetUserMetadata metadata)
    {
        _loadingEditor = true;
        try
        {
            _ratingEditor.SelectedIndex =
                metadata.Rating ?? 0;
            _favoriteEditor.IsChecked =
                metadata.Favorite;
            _statusEditor.SelectedIndex =
                IndexOfValue(
                    StatusValues,
                    metadata.StatusLabel);
            _colorEditor.SelectedIndex =
                IndexOfValue(
                    ColorValues,
                    metadata.ColorLabel);
            _tagPicker.SetSelectedTags(
                metadata.Tags);
            _notesEditor.Text =
                metadata.Notes;
            UpdateRatingPickerVisuals();
            UpdateColorPickerVisuals();
            _dirty = false;
            _save.IsEnabled = false;
            _reset.IsEnabled = false;
        }
        finally
        {
            _loadingEditor = false;
        }
    }

    private void ResetEditor()
    {
        if (_loadedMetadata is null)
        {
            return;
        }

        PopulateEditor(_loadedMetadata);
        _saveStatus.Text = "変更を元に戻しました";
    }

    private void MarkDirty()
    {
        if (_loadingEditor
            || _assetId <= 0
            || _saving)
        {
            return;
        }

        _dirty = true;
        _save.IsEnabled = true;
        _reset.IsEnabled =
            _loadedMetadata is not null;
        _saveStatus.Text = "未保存の変更";
    }

    private void SetEditorEnabled(
        bool enabled)
    {
        _ratingEditor.IsEnabled = enabled;
        foreach (var button in _ratingButtons)
        {
            button.IsEnabled = enabled;
        }
        _favoriteEditor.IsEnabled = enabled;
        _statusEditor.IsEnabled = enabled;
        _colorEditor.IsEnabled = enabled;
        foreach (var button in _colorButtons)
        {
            button.IsEnabled = enabled;
        }
        _tagPicker.SetInteractionEnabled(
            enabled);
        _notesEditor.IsEnabled = enabled;
        _save.IsEnabled =
            enabled && _dirty;
        _reset.IsEnabled =
            enabled
            && _dirty
            && _loadedMetadata is not null;
    }

    private async void OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (!e.Handled
            && e.Key == Key.S
            && e.KeyModifiers == KeyModifiers.Control
            && _dirty)
        {
            e.Handled = true;
            await SaveEditorAsync();
        }
    }

    private static IReadOnlyList<string>
        ParseTags(
            string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text
                .Split(
                    [',', '，', '\n', '\r'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Where(
                    static tag =>
                        !string.IsNullOrWhiteSpace(
                            tag))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static int IndexOfValue(
        IReadOnlyList<string?> values,
        string? value)
    {
        for (var index = 0;
             index < values.Count;
             index++)
        {
            if (string.Equals(
                    values[index],
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private static string? ValueAt(
        IReadOnlyList<string?> values,
        int index) =>
        index >= 0
        && index < values.Count
            ? values[index]
            : null;

    private static string FormatSummary(
        ViewerAsset asset)
    {
        var dimensions =
            asset.Width is > 0
            && asset.Height is > 0
                ? $"{asset.Width}×{asset.Height}"
                : "寸法未取得";
        var format =
            string.IsNullOrWhiteSpace(
                asset.Format)
                ? "形式未取得"
                : asset.Format.ToUpperInvariant();

        return
            $"{dimensions} · {format} · {FormatBytes(asset.FileSize)}";
    }

    private static string FormatTechnical(
        ViewerAsset asset)
    {
        var modified =
            new DateTimeOffset(
                new DateTime(
                    asset.ModifiedAtUtcTicks,
                    DateTimeKind.Utc))
                .ToLocalTime();

        var raw =
            asset.RawWidth is > 0
            && asset.RawHeight is > 0
                ? $"{asset.RawWidth}×{asset.RawHeight}"
                : "未取得";
        var display =
            asset.Width is > 0
            && asset.Height is > 0
                ? $"{asset.Width}×{asset.Height}"
                : "未取得";

        return
            $"表示寸法: {display}\n"
            + $"元寸法: {raw}\n"
            + $"アルファ: {(asset.HasAlpha == true ? "あり" : asset.HasAlpha == false ? "なし" : "未取得")}\n"
            + $"更新日時: {modified:yyyy-MM-dd HH:mm:ss}\n"
            + $"ソース版: {asset.SourceRevision}";
    }

    private static string FormatExif(
        AssetExifMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (!metadata.HasValues)
        {
            return "EXIF情報はありません。";
        }

        var lines =
            new List<string>(9);

        static void Add(
            List<string> target,
            string label,
            string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(
                    $"{label}: {value}");
            }
        }

        Add(lines, "カメラ", metadata.CameraModel);
        Add(lines, "レンズ", metadata.LensModel);
        Add(lines, "焦点距離", metadata.FocalLength);
        Add(lines, "絞り", metadata.Aperture);
        Add(lines, "シャッター", metadata.ShutterSpeed);
        if (metadata.Iso.HasValue)
        {
            lines.Add(
                $"ISO: {metadata.Iso.Value}");
        }

        Add(lines, "撮影日時", metadata.CapturedAt);

        var gps =
            new[]
            {
                metadata.GpsLatitude,
                metadata.GpsLongitude
            }
            .Where(
                static value =>
                    !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (gps.Length > 0)
        {
            lines.Add(
                $"GPS: {string.Join(", ", gps)}");
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private static string FormatBytes(
        long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes:N0} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:N1} KiB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024):N1} MiB";
        }

        return
            $"{bytes / (1024d * 1024 * 1024):N2} GiB";
    }

    private static TextBlock CreateValue(
        double fontSize = 11,
        FontWeight? weight = null,
        bool wrap = false) =>
        new()
        {
            Foreground =
                LumineDesign.Foreground,
            FontSize = fontSize,
            FontWeight =
                weight ?? FontWeight.Normal,
            TextWrapping =
                wrap
                    ? TextWrapping.Wrap
                    : TextWrapping.NoWrap,
            TextTrimming =
                wrap
                    ? TextTrimming.None
                    : TextTrimming.CharacterEllipsis
        };

    // Creative action menus follow the same keyboard-focus ownership
    // as Browse flyouts, without resetting a newer owner-window choice.
    private static void AttachCreativeFlyoutFocus(
        Flyout flyout,
        DropDownButton trigger,
        Button firstAction)
    {
        Control? originalFocus = null;
        var generation = 0;
        flyout.Opened += (_, _) =>
        {
            var openedGeneration = ++generation;
            var owner = TopLevel.GetTopLevel(trigger);
            originalFocus =
                owner?.FocusManager?.GetFocusedElement()
                    as Control;
            var focusedAtOpen = originalFocus;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (openedGeneration != generation
                        || !flyout.IsOpen
                        || owner is null
                        || !firstAction.IsEnabled
                        || !firstAction.IsEffectivelyVisible
                        || !ReferenceEquals(
                            TopLevel.GetTopLevel(trigger),
                            owner))
                    {
                        return;
                    }

                    var current =
                        owner.FocusManager?.GetFocusedElement();
                    if (current is Control live
                        && live.IsEnabled
                        && live.IsEffectivelyVisible
                        && ReferenceEquals(
                            TopLevel.GetTopLevel(live),
                            owner)
                        && !ReferenceEquals(live, focusedAtOpen)
                        && !ReferenceEquals(live, trigger)
                        && !ReferenceEquals(live, firstAction))
                    {
                        return;
                    }

                    firstAction.Focus(
                        NavigationMethod.Unspecified,
                        KeyModifiers.None);
                },
                DispatcherPriority.Input);
        };
        flyout.Closed += (_, _) =>
        {
            var closedGeneration = ++generation;
            var owner = TopLevel.GetTopLevel(trigger);
            var focusedAtOpen = originalFocus;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (closedGeneration != generation
                        || flyout.IsOpen
                        || owner is null
                        || !trigger.IsEnabled
                        || !trigger.IsEffectivelyVisible
                        || !ReferenceEquals(
                            TopLevel.GetTopLevel(trigger),
                            owner))
                    {
                        return;
                    }

                    var current =
                        owner.FocusManager?.GetFocusedElement();
                    if (current is Control live
                        && live.IsEnabled
                        && live.IsEffectivelyVisible
                        && ReferenceEquals(
                            TopLevel.GetTopLevel(live),
                            owner)
                        && !ReferenceEquals(live, focusedAtOpen)
                        && !ReferenceEquals(live, trigger))
                    {
                        return;
                    }

                    trigger.Focus(
                        NavigationMethod.Unspecified,
                        KeyModifiers.None);
                },
                DispatcherPriority.Input);
        };
    }

    private Button CreateInspectorTabButton(
        string header,
        int index)
    {
        var button =
            new Button
            {
                Content = header,
                HorizontalAlignment =
                    HorizontalAlignment.Stretch,
                HorizontalContentAlignment =
                    HorizontalAlignment.Center
            };
        button.Classes.Add(
            "lumine-segment");

        button.Click +=
            (_, _) => SelectTab(index);
        button.KeyDown +=
            (_, args) =>
            {
                // Only plain arrows navigate the segmented tabs.
                // Modified arrows and handled events belong to their
                // higher-level keyboard command owner.
                if (args.Handled
                    || args.KeyModifiers != KeyModifiers.None)
                {
                    return;
                }

                var next =
                    args.Key switch
                    {
                        Key.Left =>
                            Math.Max(0, index - 1),
                        Key.Right =>
                            Math.Min(
                                TabHeaders.Count - 1,
                                index + 1),
                        Key.Home => 0,
                        Key.End => TabHeaders.Count - 1,
                        _ => -1
                    };
                if (next < 0)
                {
                    return;
                }

                // The strip owns plain navigation even at its edges.
                // Otherwise Left/Right can escape to the outer viewer.
                args.Handled = true;
                if (next == index)
                {
                    return;
                }

                SelectTab(next);
                _tabButtons[next].Focus();
            };

        return button;
    }

    private void SelectTab(int index)
    {
        if ((uint)index
            >= (uint)_tabPages.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index));
        }

        _selectedTabIndex = index;
        _tabContent.Content =
            _tabPages[index];
        _saveActionSurface.IsVisible = index == 0;

        for (var itemIndex = 0;
             itemIndex < _tabButtons.Length;
             itemIndex++)
        {
            var button =
                _tabButtons[itemIndex];
            var selected =
                itemIndex == index;

            if (selected)
            {
                if (!button.Classes.Contains(
                        "selected"))
                {
                    button.Classes.Add(
                        "selected");
                }
            }
            else
            {
                button.Classes.Remove(
                    "selected");
            }
        }

        if (index == 3
            && _currentAsset is { } asset
            && _exifLoadedAssetId != asset.Id
            && _exifLoadingAssetId != asset.Id)
        {
            _ = LoadExifAsync(
                asset,
                _loadCancellation?.Token
                ?? CancellationToken.None);
        }
    }

    private static ScrollViewer CreateTabScroll(
        Control content) =>
        new()
        {
            Content = content,
            HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

    private static void AddSection(
        Panel panel,
        string label,
        Control content)
    {
        var section =
            new StackPanel
            {
                Spacing = 4
            };
        section.Children.Add(
            new TextBlock
            {
                Text = label,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                FontWeight =
                    FontWeight.SemiBold
            });
        section.Children.Add(content);
        panel.Children.Add(section);
    }

    private static void AddEditorRow(
        Panel panel,
        string label,
        Control editor,
        bool multiline = false)
    {
        if (string.IsNullOrWhiteSpace(
                AutomationProperties.GetName(
                    editor)))
        {
            AutomationProperties.SetName(
                editor,
                label);
        }

        var labelBlock =
            new TextBlock
            {
                Text = label,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                VerticalAlignment =
                    multiline
                        ? VerticalAlignment.Top
                        : VerticalAlignment.Center,
                Margin =
                    multiline
                        ? new Thickness(
                            0,
                            LumineDesign.Space6,
                            0,
                            0)
                        : new Thickness(0)
            };

        var stacked =
            LumineVisualMetrics.TextScaleFactor
                >= 1.5;

        var row =
            new Grid
            {
                MinHeight =
                    multiline
                        ? 0
                        : 36
            };
        AutomationProperties.SetAutomationId(
            row,
            $"inspector-editor-row-{label}");

        if (stacked)
        {
            row.RowDefinitions =
                new RowDefinitions("Auto,Auto");
            row.RowSpacing =
                LumineDesign.Space4;
            labelBlock.Margin =
                new Thickness(0);
            row.Children.Add(labelBlock);
            Grid.SetRow(editor, 1);
            row.Children.Add(editor);
        }
        else
        {
            row.ColumnDefinitions =
                new ColumnDefinitions("76,*");
            row.ColumnSpacing =
                LumineDesign.Space12;
            row.Children.Add(labelBlock);
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
        }

        panel.Children.Add(row);
    }
}