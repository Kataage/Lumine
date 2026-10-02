using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Library;
using Lumine.Viewer;

namespace Lumine.App;

internal sealed class ContextualAssetDetailPanel : UserControl
{
    private readonly CoreViewerRuntime _runtime;
    private readonly Func<Task> _closeRequested;
    private readonly Func<Task> _focusedViewRequested;
    private readonly TextBlock _title;
    private readonly TextBlock _summary;
    private readonly TextBlock _path;
    private readonly TextBlock _technical;
    private readonly TextBlock _rating;
    private readonly TextBlock _favorite;
    private readonly TextBlock _status;
    private readonly TextBlock _color;
    private readonly TextBlock _tags;
    private readonly TextBlock _notes;
    private readonly Button _focused;
    private CancellationTokenSource? _loadCancellation;
    private long _assetId;

    public ContextualAssetDetailPanel(
        CoreViewerRuntime runtime,
        Func<Task> closeRequested,
        Func<Task> focusedViewRequested)
    {
        _runtime = runtime
            ?? throw new ArgumentNullException(nameof(runtime));
        _closeRequested = closeRequested
            ?? throw new ArgumentNullException(nameof(closeRequested));
        _focusedViewRequested = focusedViewRequested
            ?? throw new ArgumentNullException(nameof(focusedViewRequested));

        _title = CreateValue(
            fontSize: 14,
            weight: FontWeight.Bold);
        _summary = CreateValue();
        _path = CreateValue(
            wrap: true);
        _technical = CreateValue(
            wrap: true);
        _rating = CreateValue();
        _favorite = CreateValue();
        _status = CreateValue();
        _color = CreateValue();
        _tags = CreateValue(
            wrap: true);
        _notes = CreateValue(
            wrap: true);

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
                    Padding =
                        new Thickness(12, 5)
                });
        _focused.Click +=
            async (_, _) =>
                await _focusedViewRequested();
        _focused.IsEnabled = false;

        var header =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(14, 12, 10, 8)
            };
        var headerText =
            new TextBlock
            {
                Text = "詳細",
                Foreground = LumineDesign.Foreground,
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        header.Children.Add(headerText);
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

        var organization =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("Auto,*"),
                RowDefinitions =
                    new RowDefinitions(
                        "Auto,Auto,Auto,Auto")
            };
        AddGridValue(
            organization,
            0,
            "評価",
            _rating);
        AddGridValue(
            organization,
            1,
            "お気に入り",
            _favorite);
        AddGridValue(
            organization,
            2,
            "状態",
            _status);
        AddGridValue(
            organization,
            3,
            "カラー",
            _color);
        AddSection(body, "整理情報", organization);
        AddSection(body, "タグ", _tags);
        AddSection(body, "ノート", _notes);

        var editHint =
            new TextBlock
            {
                Text =
                    "編集操作は次のmetadata editing工程でこのパネルへ統合されます。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 9.5,
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(0, 6, 0, 0)
            };
        body.Children.Add(editHint);

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

        Background =
            LumineDesign.Surface;
        Content = layout;

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
        _rating.Text ?? string.Empty;

    internal string TagsText =>
        _tags.Text ?? string.Empty;

    internal string NotesText =>
        _notes.Text ?? string.Empty;

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
        _rating.Text = "読み込み中…";
        _favorite.Text = "読み込み中…";
        _status.Text = "読み込み中…";
        _color.Text = "読み込み中…";
        _tags.Text = "読み込み中…";
        _notes.Text = "読み込み中…";
        _focused.IsEnabled = true;

        try
        {
            var metadata =
                await _runtime.LibraryService
                    .GetUserMetadataAsync(
                        _runtime.Library.Id,
                        asset.Id,
                        token);

            token.ThrowIfCancellationRequested();
            if (_assetId != asset.Id)
            {
                return;
            }

            if (metadata is null)
            {
                _rating.Text = "未設定";
                _favorite.Text = "いいえ";
                _status.Text = "未設定";
                _color.Text = "未設定";
                _tags.Text = "なし";
                _notes.Text = "なし";
                return;
            }

            _rating.Text =
                metadata.Rating is { } rating
                    ? $"★{rating}"
                    : "未設定";
            _favorite.Text =
                metadata.Favorite
                    ? "はい"
                    : "いいえ";
            _status.Text =
                metadata.StatusLabel
                ?? "未設定";
            _color.Text =
                metadata.ColorLabel
                ?? "未設定";
            _tags.Text =
                metadata.Tags.Count == 0
                    ? "なし"
                    : string.Join(
                        " · ",
                        metadata.Tags);
            _notes.Text =
                string.IsNullOrWhiteSpace(
                    metadata.Notes)
                    ? "なし"
                    : metadata.Notes;
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

            _rating.Text = "取得失敗";
            _favorite.Text = "取得失敗";
            _status.Text = "取得失敗";
            _color.Text = "取得失敗";
            _tags.Text = "取得失敗";
            _notes.Text =
                $"metadataを取得できませんでした: {exception.Message}";
        }
    }

    public void ShowNoSelection()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _assetId = 0;
        _title.Text =
            "画像を選択してください";
        _summary.Text =
            "選択した画像の情報をここに表示します。";
        _path.Text = "—";
        _technical.Text = "—";
        _rating.Text = "—";
        _favorite.Text = "—";
        _status.Text = "—";
        _color.Text = "—";
        _tags.Text = "—";
        _notes.Text = "—";
        _focused.IsEnabled = false;
    }

    public void PrepareForDetach()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

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

    private static void AddGridValue(
        Grid grid,
        int row,
        string label,
        TextBlock value)
    {
        var labelBlock =
            new TextBlock
            {
                Text = label,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = 10,
                Margin =
                    new Thickness(0, 2, 10, 2)
            };
        Grid.SetRow(labelBlock, row);
        grid.Children.Add(labelBlock);

        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        value.Margin =
            new Thickness(0, 2);
        grid.Children.Add(value);
    }
}
