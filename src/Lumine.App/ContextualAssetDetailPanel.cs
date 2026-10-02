using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly ComboBox _ratingEditor;
    private readonly CheckBox _favoriteEditor;
    private readonly ComboBox _statusEditor;
    private readonly ComboBox _colorEditor;
    private readonly TextBox _tagsEditor;
    private readonly TextBox _notesEditor;
    private readonly TextBlock _saveStatus;
    private readonly Button _save;
    private readonly Button _reset;
    private readonly Button _focused;
    private CancellationTokenSource? _loadCancellation;
    private long _assetId;
    private AssetUserMetadata? _loadedMetadata;
    private bool _loadingEditor;
    private bool _dirty;
    private bool _saving;

    public ContextualAssetDetailPanel(
        CoreViewerRuntime runtime,
        Func<Task> closeRequested,
        Func<Task> focusedViewRequested,
        Func<Task>? metadataChanged = null)
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
        _path = CreateValue(wrap: true);
        _technical = CreateValue(wrap: true);
        _works = CreateValue(wrap: true);
        _groups = CreateValue(wrap: true);
        _relations = CreateValue(wrap: true);
        _publications = CreateValue(wrap: true);

        _ratingEditor = new ComboBox
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
        };
        _favoriteEditor = new CheckBox
        {
            Content = "お気に入り"
        };
        _statusEditor = new ComboBox
        {
            ItemsSource = StatusLabels,
            MinWidth = 118
        };
        _colorEditor = new ComboBox
        {
            ItemsSource = ColorLabels,
            MinWidth = 118
        };
        _tagsEditor = new TextBox
        {
            PlaceholderText = "タグをカンマ区切りで入力",
            TextWrapping = TextWrapping.Wrap
        };
        _notesEditor = new TextBox
        {
            PlaceholderText = "ノート",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 92
        };

        _saveStatus = new TextBlock
        {
            Foreground = LumineDesign.MutedForeground,
            FontSize = 9.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        _save =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "保存  Ctrl+S",
                    MinHeight = 30,
                    Padding = new Thickness(12, 5),
                    IsEnabled = false
                });
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

        var close =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "×",
                    Width = 34,
                    MinHeight = 30,
                    Padding = new Thickness(0)
                });
        ToolTip.SetTip(
            close,
            "詳細を閉じる");
        close.Click +=
            async (_, _) =>
                await _closeRequested();

        _focused =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "集中表示",
                    MinHeight = 30,
                    Padding = new Thickness(12, 5),
                    IsEnabled = false
                });
        _focused.Click +=
            async (_, _) =>
                await _focusedViewRequested();

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(14, 12, 10, 8)
            };
        header.Children.Add(
            new TextBlock
            {
                Text = "詳細",
                Foreground = LumineDesign.Foreground,
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                VerticalAlignment =
                    VerticalAlignment.Center
            });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var body =
            new StackPanel
            {
                Spacing = 11,
                Margin = new Thickness(14, 4, 14, 18)
            };

        body.Children.Add(_title);
        body.Children.Add(_summary);
        body.Children.Add(_focused);

        AddSection(body, "場所", _path);
        AddSection(body, "技術情報", _technical);

        var creative =
            new StackPanel
            {
                Spacing = 8
            };
        AddSection(creative, "Work", _works);
        AddSection(creative, "Generation Group", _groups);
        AddSection(creative, "Lineage", _relations);
        AddSection(creative, "Publication", _publications);
        AddSection(body, "制作コンテキスト", creative);

        var editor =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("74,*"),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,Auto,Auto,Auto,Auto"),
                RowSpacing = 6
            };
        AddEditorRow(
            editor,
            0,
            "評価",
            _ratingEditor);
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
            _colorEditor);
        AddEditorRow(
            editor,
            4,
            "タグ",
            _tagsEditor);
        AddEditorRow(
            editor,
            5,
            "ノート",
            _notesEditor);
        AddSection(
            body,
            "整理情報",
            editor);

        var saveRow =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto,Auto")
            };
        saveRow.Children.Add(_saveStatus);
        Grid.SetColumn(_reset, 1);
        _reset.Margin =
            new Thickness(4, 0);
        saveRow.Children.Add(_reset);
        Grid.SetColumn(_save, 2);
        _save.Margin =
            new Thickness(4, 0);
        saveRow.Children.Add(_save);
        body.Children.Add(saveRow);

        body.Children.Add(
            new TextBlock
            {
                Text =
                    "状態: 未整理 / 確認済み / 候補 / 公開済み。変更は保存またはCtrl+Sで確定します。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9.5,
                TextWrapping =
                    TextWrapping.Wrap
            });

        var scroll =
            new ScrollViewer
            {
                Content = body,
                HorizontalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility =
                    Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };

        var layout =
            new Grid
            {
                RowDefinitions =
                    new RowDefinitions("Auto,*")
            };
        layout.Children.Add(header);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        Background = LumineDesign.Surface;
        Content = layout;

        _ratingEditor.SelectionChanged +=
            (_, _) => MarkDirty();
        _favoriteEditor.Click +=
            (_, _) => MarkDirty();
        _statusEditor.SelectionChanged +=
            (_, _) => MarkDirty();
        _colorEditor.SelectionChanged +=
            (_, _) => MarkDirty();
        _tagsEditor.TextChanged +=
            (_, _) => MarkDirty();
        _notesEditor.TextChanged +=
            (_, _) => MarkDirty();
        KeyDown += OnKeyDown;

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

    internal string TagsText =>
        _tagsEditor.Text ?? string.Empty;

    internal string NotesText =>
        _notesEditor.Text ?? string.Empty;

    internal bool IsDirty => _dirty;

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
            SetEditorEnabled(true);
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
            _saveStatus.Text =
                $"整理情報を取得できませんでした: {exception.Message}";
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
            _tagsEditor.Text = tags;
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
                    ParseTags(
                        _tagsEditor.Text));

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
        _loadedMetadata = null;
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
        _saveStatus.Text = "—";
        _focused.IsEnabled = false;

        _loadingEditor = true;
        try
        {
            _ratingEditor.SelectedIndex = 0;
            _favoriteEditor.IsChecked = false;
            _statusEditor.SelectedIndex = 0;
            _colorEditor.SelectedIndex = 0;
            _tagsEditor.Text = string.Empty;
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
        KeyDown -= OnKeyDown;
    }

    private async Task LoadCreativeContextAsync(
        long assetId,
        CancellationToken cancellationToken)
    {
        _works.Text = "読み込み中…";
        _groups.Text = "読み込み中…";
        _relations.Text = "読み込み中…";
        _publications.Text = "読み込み中…";

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
        }
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
            _tagsEditor.Text =
                string.Join(
                    ", ",
                    metadata.Tags);
            _notesEditor.Text =
                metadata.Notes;
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
        _favoriteEditor.IsEnabled = enabled;
        _statusEditor.IsEnabled = enabled;
        _colorEditor.IsEnabled = enabled;
        _tagsEditor.IsEnabled = enabled;
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
                FontSize = 9.5,
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
                FontSize = 10,
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
