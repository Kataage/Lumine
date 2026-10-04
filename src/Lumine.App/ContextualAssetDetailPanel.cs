using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.Json;
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
    private readonly TextBlock _technical;
    private readonly TextBlock _works;
    private readonly TextBlock _groups;
    private readonly TextBlock _relations;
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
    private readonly Button _save;
    private readonly Button _reset;
    private readonly Button _retry;
    private readonly Button _focused;
    private readonly Button _pin;
    private Button[] _ratingButtons = [];
    private Button[] _colorButtons = [];
    private readonly Avalonia.Controls.Image _preview;
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

    public ContextualAssetDetailPanel(
        CoreViewerRuntime runtime,
        Func<Task> closeRequested,
        Func<Task> focusedViewRequested,
        Func<Task>? metadataChanged = null,
        Func<Task>? createWorkRequested = null,
        Func<Task>? createGroupRequested = null,
        Func<Task>? createPublicationRequested = null)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _closeRequested = closeRequested
            ?? throw new ArgumentNullException(nameof(closeRequested));
        _focusedViewRequested = focusedViewRequested
            ?? throw new ArgumentNullException(nameof(focusedViewRequested));
        _metadataChanged = metadataChanged;

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
        _path = CreateValue(wrap: true);
        _technical = CreateValue(wrap: true);
        _works = CreateValue(wrap: true);
        _groups = CreateValue(wrap: true);
        _relations = CreateValue(wrap: true);
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
                    Content = "お気に入り"
                });
        _statusEditor =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    ItemsSource = StatusLabels,
                    MinWidth = 118
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
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "固定",
                    MinHeight = 28,
                    Padding =
                        new Thickness(
                            LumineDesign.Space8,
                            LumineDesign.Space2)
                });
        ToolTip.SetTip(
            _pin,
            "詳細パネルを画像一覧の横に固定");
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

        var summaryBody =
            new StackPanel
            {
                Spacing = LumineDesign.Space8,
                Margin =
                    new Thickness(
                        LumineDesign.Space12,
                        LumineDesign.Space4,
                        LumineDesign.Space12,
                        LumineDesign.Space8)
            };

        var previewSurface =
            new Border
            {
                Height = 168,
                Background = LumineDesign.Background,
                BorderBrush = LumineDesign.Border,
                BorderThickness = new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        LumineDesign.PanelRadius),
                ClipToBounds = true,
                Child = _preview
            };
        summaryBody.Children.Add(previewSurface);
        summaryBody.Children.Add(_title);
        summaryBody.Children.Add(_summary);

        var editor =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("72,*"),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,Auto,Auto,Auto,Auto"),
                RowSpacing = 7
            };
        AddEditorRow(
            editor,
            0,
            "評価",
            ratingPicker);
        AddEditorRow(
            editor,
            1,
            "お気に入り",
            _favoriteEditor);
        AddEditorRow(
            editor,
            2,
            "状態",
            _statusEditor);
        AddEditorRow(
            editor,
            3,
            "カラー",
            colorPicker);
        AddEditorRow(
            editor,
            4,
            "タグ",
            _tagPicker);
        AddEditorRow(
            editor,
            5,
            "ノート",
            _notesEditor);

        var saveRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto,Auto,Auto")
            };
        saveRow.Children.Add(_saveStatus);
        Grid.SetColumn(_retry, 1);
        _retry.Margin =
            new Thickness(4, 0);
        saveRow.Children.Add(_retry);
        Grid.SetColumn(_reset, 2);
        _reset.Margin =
            new Thickness(4, 0);
        saveRow.Children.Add(_reset);
        Grid.SetColumn(_save, 3);
        _save.Margin =
            new Thickness(4, 0);
        saveRow.Children.Add(_save);

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
        organizeBody.Children.Add(saveRow);

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
                        _saveStatus.Text =
                            $"操作を完了できませんでした: {exception.Message}";
                    }
                    finally
                    {
                        button.IsEnabled = true;
                    }
                };
            return button;
        }

        var creative =
            new StackPanel
            {
                Spacing = LumineDesign.Space8
            };

        if (createWorkRequested is not null
            || createGroupRequested is not null)
        {
            var creationPanel =
                new StackPanel
                {
                    Width = 220,
                    Spacing = 6,
                    Margin = new Thickness(4)
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

            creative.Children.Add(
                LumineDesign.ConfigureSecondaryButton(
                    new DropDownButton
                    {
                        Content = "新規作成",
                        Flyout =
                            new Flyout
                            {
                                Content =
                                    new Border
                                    {
                                        Background =
                                            LumineDesign.SurfaceRaised,
                                        Padding =
                                            new Thickness(10),
                                        Child =
                                            creationPanel
                                    }
                            }
                    }));
        }

        AddSection(creative, "Work", _works);
        AddSection(creative, "Generation Group", _groups);
        AddSection(creative, "Lineage", _relations);

        var creativeBody =
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
            creativeBody,
            "制作コンテキスト",
            creative);

        var publicationBody =
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
        if (createPublicationRequested is not null)
        {
            publicationBody.Children.Add(
                CreateContextAction(
                    "公開記録を作成",
                    createPublicationRequested));
        }
        AddSection(
            publicationBody,
            "公開履歴",
            _publicationCount);
        publicationBody.Children.Add(
            _publicationCards);

        var informationBody =
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
            informationBody,
            "場所",
            _path);
        AddSection(
            informationBody,
            "技術情報",
            _technical);

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
                        LumineDesign.Space6),
                Padding =
                    new Thickness(
                        LumineDesign.Space2),
                Background =
                    LumineDesign.ControlSurface,
                BorderBrush =
                    LumineDesign.Border,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(8),
                Child = tabStrip
            };

        var tabLayout =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,*")
            };
        tabLayout.Children.Add(tabStripHost);
        Grid.SetRow(_tabContent, 1);
        tabLayout.Children.Add(_tabContent);

        var layout =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,Auto,*")
            };
        layout.Children.Add(header);
        Grid.SetRow(summaryBody, 1);
        layout.Children.Add(summaryBody);
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
        _path.Text ?? string.Empty;

    internal string TechnicalText =>
        _technical.Text ?? string.Empty;

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

    internal event EventHandler? PinToggleRequested;

    internal void SetPinPresentation(
        bool pinned,
        bool available)
    {
        _pin.IsVisible = available;
        _pin.IsEnabled = available;
        _pin.Content =
            pinned
                ? "固定中"
                : "固定";
        _pin.Background =
            pinned
                ? LumineDesign.AccentMuted
                : LumineDesign.ControlSurface;
        _pin.BorderBrush =
            pinned
                ? LumineDesign.BorderStrong
                : LumineDesign.Border;
        ToolTip.SetTip(
            _pin,
            pinned
                ? "固定を解除して画像一覧の上に重ねる"
                : "詳細パネルを画像一覧の横に固定");
    }

    internal int SelectedTabIndex =>
        _selectedTabIndex;

    internal string SelectedTabHeader =>
        TabHeaders[_selectedTabIndex];

    internal IReadOnlyList<string> TabHeaders { get; } =
        new[] { "整理", "制作", "公開", "情報" };

    internal void SelectTabForSmoke(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= TabHeaders.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index));
        }

        SelectTab(index);
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
                var expectedHover =
                    index == _selectedTabIndex
                        ? LumineDesign.InteractionSelectedHover
                        : LumineDesign.InteractionHover;
                if (!ReferenceEquals(
                        button.Resources[
                            "ButtonBackgroundPointerOver"],
                        expectedHover))
                {
                    return false;
                }
            }

            return ReferenceEquals(
                _tabButtons[_selectedTabIndex].Background,
                LumineDesign.InteractionSelected);
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
        _path.Text =
            Path.Combine(
                _runtime.LibraryRoot,
                asset.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        _technical.Text =
            FormatTechnical(asset);
        _focused.IsEnabled = true;

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
        }
        catch (OperationCanceledException)
            when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
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
            _saveStatus.Text =
                $"保存できませんでした: {exception.Message}";
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
        _path.Text = "—";
        _technical.Text = "—";
        _works.Text = "—";
        _groups.Text = "—";
        _relations.Text = "—";
        _publications.Text = "—";
        _publicationCount.Text = "0件";
        _publicationCards.Children.Clear();
        _publicationCards.Children.Add(
            CreatePublicationMessageCard(
                "公開履歴はありません。",
                warning: false));
        _saveStatus.Text = "—";
        _focused.IsEnabled = false;
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
                        ViewerThumbnailPriority.Foreground,
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
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (_assetId == asset.Id)
            {
                ReplacePreviewLease(null);
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
                context.Publications);
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

            var message =
                $"制作コンテキストを取得できませんでした: {exception.Message}";
            _works.Text = message;
            _groups.Text = "—";
            _relations.Text = "—";
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

    private void RenderPublicationCards(
        IReadOnlyList<PublicationInfo> publications)
    {
        _publicationCards.Children.Clear();
        _publicationCount.Text =
            $"{publications.Count:N0}件";

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
                    "画像: "
                    + string.Join(
                        ", ",
                        publication.Assets.Select(
                            static asset =>
                                asset.FileName))));
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
                Orientation =
                    Orientation.Horizontal,
                Spacing =
                    LumineDesign.Space2
            };

        _ratingButtons =
            Enumerable.Range(1, 5)
                .Select(
                    rating =>
                    {
                        var button =
                            LumineDesign.ConfigureSecondaryButton(
                                new Button
                                {
                                    Content = "★",
                                    Width = 31,
                                    MinWidth = 31,
                                    Height = 30,
                                    MinHeight = 30,
                                    Padding =
                                        new Thickness(0),
                                    FontSize = 17
                                });
                        var accessibleName =
                            $"評価 {rating}";
                        AutomationProperties.SetName(
                            button,
                            accessibleName);
                        ToolTip.SetTip(
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
            new WrapPanel();

        _colorButtons =
            ColorValues
                .Select(
                    (value, index) =>
                    {
                        var swatch =
                            ResolveColorBrush(value);
                        var button =
                            new Button
                            {
                                Width = 28,
                                Height = 28,
                                MinWidth = 28,
                                MinHeight = 28,
                                Padding =
                                    new Thickness(0),
                                Margin =
                                    new Thickness(
                                        0,
                                        0,
                                        LumineDesign.Space4,
                                        LumineDesign.Space4),
                                CornerRadius =
                                    new CornerRadius(14),
                                Background = swatch,
                                BorderBrush =
                                    LumineDesign.Border,
                                BorderThickness =
                                    new Thickness(1),
                                Content =
                                    value is null
                                        ? "×"
                                        : string.Empty,
                                Foreground =
                                    LumineDesign.Foreground
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
                            value is null
                                ? LumineDesign.InteractionHover
                                : swatch;
                        button.Resources[
                            "ButtonBackgroundPressed"] =
                            value is null
                                ? LumineDesign.InteractionPressed
                                : swatch;
                        button.Resources[
                            "ButtonBorderBrushPointerOver"] =
                            LumineDesign.Focus;
                        button.Resources[
                            "ButtonBorderBrushPressed"] =
                            LumineDesign.Focus;
                        button.Click +=
                            (_, _) =>
                                SetColorFromDirectControl(
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
            button.Foreground =
                rating <= selected
                    ? LumineDesign.Warning
                    : LumineDesign.MutedForeground;
            button.Background =
                rating == selected
                    ? LumineDesign.AccentMuted
                    : LumineDesign.ControlSurface;
            button.BorderBrush =
                rating == selected
                    ? LumineDesign.BorderStrong
                    : LumineDesign.Border;
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
            button.BorderBrush =
                active
                    ? LumineDesign.Focus
                    : LumineDesign.Border;
            button.BorderThickness =
                new Thickness(
                    active
                        ? 2
                        : 1);

            if (ColorValues[index] is null)
            {
                button.Background =
                    active
                        ? LumineDesign.AccentMuted
                        : LumineDesign.ControlSurface;
            }
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
        if (e.Key == Key.S
            && e.KeyModifiers.HasFlag(
                KeyModifiers.Control)
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
            + $"source revision: {asset.SourceRevision}";
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

    private Button CreateInspectorTabButton(
        string header,
        int index)
    {
        var button =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = header,
                    MinHeight = 30,
                    Padding =
                        new Thickness(8, 4),
                    CornerRadius =
                        new CornerRadius(6),
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    HorizontalContentAlignment =
                        HorizontalAlignment.Center
                });

        button.Click +=
            (_, _) => SelectTab(index);
        button.KeyDown +=
            (_, args) =>
            {
                var next =
                    args.Key switch
                    {
                        Key.Left =>
                            Math.Max(0, index - 1),
                        Key.Right =>
                            Math.Min(
                                TabHeaders.Count - 1,
                                index + 1),
                        _ =>
                            index
                    };
                if (next == index)
                {
                    return;
                }

                SelectTab(next);
                _tabButtons[next].Focus();
                args.Handled = true;
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

        for (var itemIndex = 0;
             itemIndex < _tabButtons.Length;
             itemIndex++)
        {
            var button =
                _tabButtons[itemIndex];
            var selected =
                itemIndex == index;

            button.Background =
                selected
                    ? LumineDesign.InteractionSelected
                    : Brushes.Transparent;
            button.BorderBrush =
                selected
                    ? LumineDesign.BorderStrong
                    : Brushes.Transparent;
            button.BorderThickness =
                new Thickness(1);
            button.FontWeight =
                selected
                    ? FontWeight.SemiBold
                    : FontWeight.Normal;

            if (selected)
            {
                LumineDesign
                    .ConfigureSelectedButtonStateResources(
                        button);
            }
            else
            {
                LumineDesign
                    .ConfigureNeutralButtonStateResources(
                        button);
            }
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
        Grid grid,
        int row,
        string label,
        Control editor)
    {
        var labelBlock =
            new TextBlock
            {
                Text = label,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                Margin =
                    new Thickness(0, 5, 10, 2)
            };
        Grid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);

        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
    }
}
