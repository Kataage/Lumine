using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Lumine.Library;
using Lumine.Viewer;

namespace Lumine.App;

internal sealed record CreativeWorkDialogResult(
    string Title,
    string Description);

internal sealed record CreativeGroupDialogResult(
    string Name,
    long? WorkId,
    string Prompt,
    string NegativePrompt,
    string ModelName,
    string Sampler,
    string Scheduler,
    int Steps,
    double CfgScale,
    string WorkflowJson,
    string Notes);

internal sealed record CreativeRelationDialogResult(
    bool ReverseDirection,
    string RelationType,
    string Note);

internal sealed record CreativeArchiveTargetDialogResult(
    long Id);

internal sealed record CreativePublicationDialogResult(
    long? WorkId,
    string Destination,
    string Account,
    string Title,
    string Body,
    string Tags,
    DateTimeOffset PublishedAtUtc,
    string ExternalId,
    string ExternalUrl,
    string PlatformMetadataJson,
    IReadOnlyList<long> OrderedAssetIds);

internal sealed class CreativePublicationPixivMetadataEditor
    : StackPanel
{
    private static readonly string[] AgeRestrictionLabels =
    [
        "全年齢",
        "R-18",
        "R-18G"
    ];

    private readonly ComboBox _ageRestriction;
    private readonly CheckBox _aiGenerated;

    public CreativePublicationPixivMetadataEditor()
    {
        Spacing =
            LumineDesign.Space6;

        Children.Add(
            new TextBlock
            {
                Text =
                    "Pixiv投稿情報",
                Foreground =
                    LumineDesign.Foreground,
                FontWeight =
                    Avalonia.Media.FontWeight.SemiBold,
                FontSize =
                    LumineDesign.CaptionFontSize
            });

        Children.Add(
            new TextBlock
            {
                Text =
                    "年齢制限",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize
            });

        _ageRestriction =
            LumineDesign.ConfigureComboBox(
                new ComboBox
                {
                    ItemsSource =
                        AgeRestrictionLabels,
                    SelectedIndex = 0,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch
                });
        AutomationProperties.SetName(
            _ageRestriction,
            "Pixiv年齢制限");
        Children.Add(
            _ageRestriction);

        _aiGenerated =
            LumineDesign.ConfigureCheckBox(
                new CheckBox
                {
                    Content =
                        "Pixiv: AI生成",
                    IsChecked = true
                });
        AutomationProperties.SetName(
            _aiGenerated,
            "Pixiv AI生成作品");
        Children.Add(
            _aiGenerated);
    }

    public string PlatformMetadataJson
    {
        get
        {
            var ageRestriction =
                _ageRestriction.SelectedIndex
                    switch
                    {
                        1 => "r18",
                        2 => "r18g",
                        _ => "all"
                    };
            return
                $"{{\"ageRestriction\":\"{ageRestriction}\",\"aiGenerated\":{(_aiGenerated.IsChecked == true ? "true" : "false")}}}";
        }
    }

    internal IReadOnlyList<string>
        AgeRestrictionOptionsForSmoke =>
        AgeRestrictionLabels;

    internal string SelectedAgeRestrictionForSmoke =>
        _ageRestriction.SelectedItem as string
        ?? string.Empty;

    internal bool AgeRestrictionAccessibleForSmoke =>
        string.Equals(
            AutomationProperties.GetName(
                _ageRestriction),
            "Pixiv年齢制限",
            StringComparison.Ordinal);

    internal bool AiGeneratedForSmoke
    {
        get =>
            _aiGenerated.IsChecked == true;
        set =>
            _aiGenerated.IsChecked = value;
    }

    internal void SelectAgeRestrictionForSmoke(
        string value)
    {
        var index =
            Array.IndexOf(
                AgeRestrictionLabels,
                value);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unknown Pixiv age restriction.");
        }

        _ageRestriction.SelectedIndex =
            index;
    }
}

internal sealed record CreativePublicationAssetOption(
    long AssetId,
    string DisplayName)
{
    public override string ToString() =>
        DisplayName;
}

internal sealed class CreativePublicationOrderEditor
    : StackPanel
{
    private readonly ObservableCollection<
        CreativePublicationAssetOption> _items;
    private readonly ListBox _list;
    private readonly Button _moveUp;
    private readonly Button _moveDown;
    private readonly TextBlock _status;

    public CreativePublicationOrderEditor(
        IReadOnlyList<CreativePublicationAssetOption> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Count == 0)
        {
            throw new ArgumentException(
                "Publication order requires at least one asset.",
                nameof(assets));
        }

        if (assets.Any(
                static asset =>
                    asset.AssetId <= 0
                    || string.IsNullOrWhiteSpace(
                        asset.DisplayName))
            || assets.Select(
                    static asset =>
                        asset.AssetId)
                .Distinct()
                .Count()
                != assets.Count)
        {
            throw new ArgumentException(
                "Publication order assets must have unique positive IDs and display names.",
                nameof(assets));
        }

        Spacing =
            LumineDesign.Space6;

        _items =
            new ObservableCollection<
                CreativePublicationAssetOption>(
                assets);
        Children.Add(
            new TextBlock
            {
                Text =
                    "上から順に公開された画像です。画像を選び、上へ / 下へで実際の投稿順に並べ替えます。",
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    Avalonia.Media.TextWrapping.Wrap
            });

        _list =
            new ListBox
            {
                ItemsSource =
                    _items,
                MinHeight = 150,
                MaxHeight = 220,
                SelectedIndex = 0
            };
        AutomationProperties.SetName(
            _list,
            "Publication画像順");
        _list.SelectionChanged +=
            (_, _) => UpdateState();
        Children.Add(
            _list);

        var actions =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Spacing =
                    LumineDesign.Space6
            };
        _moveUp =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "↑ 上へ"
                });
        AutomationProperties.SetName(
            _moveUp,
            "選択画像を上へ移動");
        _moveUp.Click +=
            (_, _) => MoveSelected(-1);

        _moveDown =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "↓ 下へ"
                });
        AutomationProperties.SetName(
            _moveDown,
            "選択画像を下へ移動");
        _moveDown.Click +=
            (_, _) => MoveSelected(1);

        actions.Children.Add(
            _moveUp);
        actions.Children.Add(
            _moveDown);
        Children.Add(
            actions);

        _status =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    Avalonia.Media.TextWrapping.Wrap
            };
        Children.Add(
            _status);
        UpdateState();
    }

    public IReadOnlyList<long> OrderedAssetIds =>
        _items
            .Select(
                static item =>
                    item.AssetId)
            .ToArray();

    internal IReadOnlyList<long>
        OrderedAssetIdsForSmoke =>
        OrderedAssetIds;

    internal void SelectForSmoke(
        int index)
    {
        _list.SelectedIndex =
            index;
        UpdateState();
    }

    internal bool MoveSelectedUpForSmoke() =>
        MoveSelected(-1);

    internal bool MoveSelectedDownForSmoke() =>
        MoveSelected(1);

    internal bool MoveUpEnabledForSmoke =>
        _moveUp.IsEnabled;

    internal bool MoveDownEnabledForSmoke =>
        _moveDown.IsEnabled;

    private bool MoveSelected(
        int delta)
    {
        var index =
            _list.SelectedIndex;
        var next =
            index + delta;
        if (index < 0
            || next < 0
            || next >= _items.Count)
        {
            UpdateState();
            return false;
        }

        _items.Move(
            index,
            next);
        _list.SelectedIndex =
            next;
        UpdateState();
        return true;
    }

    private void UpdateState()
    {
        var index =
            _list.SelectedIndex;
        _moveUp.IsEnabled =
            index > 0;
        _moveDown.IsEnabled =
            index >= 0
            && index < _items.Count - 1;

        _status.Text =
            index >= 0
            && index < _items.Count
                ? $"公開順 {index + 1:N0} / {_items.Count:N0} · {_items[index].DisplayName}"
                : $"公開順 {_items.Count:N0}枚";
    }
}

internal sealed record CreativeSelectionPreview(
    int Count,
    IReadOnlyList<string> SampleDisplayNames)
{
    public bool IsTruncated =>
        SampleDisplayNames.Count < Count;
}

internal static class CreativeArchiveDialogs
{
    public static Task<CreativeWorkDialogResult?> ShowWorkAsync(
        Window owner,
        CreativeSelectionPreview selection)
    {
        var title = new TextBox
        {
            PlaceholderText = "作品名"
        };
        var description = new TextBox
        {
            PlaceholderText = "説明",
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 80
        };
        var dialog = CreateDialog(
            "Workを作成",
            500,
            390);
        var stack = CreateFormStack(
            $"選択した{selection.Count:N0}枚を、1つの人間向け制作単位としてまとめます。");
        AddField(stack, "作品名", title);
        AddField(stack, "説明", description);
        stack.Children.Add(
            CreateAssetSummary(selection));

        var status = CreateStatus();
        stack.Children.Add(status);
        stack.Children.Add(
            CreateButtons(
                dialog,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(title.Text))
                    {
                        status.Text = "作品名を入力してください。";
                        return null;
                    }

                    return new CreativeWorkDialogResult(
                        title.Text.Trim(),
                        description.Text ?? string.Empty);
                }));
        dialog.Content = CreateScroll(stack);
        return ShowWithFocusReturnAsync<CreativeWorkDialogResult>(owner, dialog);
    }

    public static Task<CreativeGroupDialogResult?> ShowGenerationGroupAsync(
        Window owner,
        CreativeSelectionPreview selection,
        IReadOnlyList<WorkInfo> works)
    {
        var name = new TextBox
        {
            PlaceholderText = "Generation Group名"
        };
        var work =
            new ComboBox
            {
                ItemsSource =
                    new[] { "Workに紐付けない" }
                        .Concat(works.Select(
                            static item => item.Title))
                        .ToArray(),
                SelectedIndex = 0
            };
        var prompt = CreateMultiline("Prompt");
        var negative = CreateMultiline("Negative prompt");
        var model = new TextBox { PlaceholderText = "Model" };
        var sampler = new TextBox { PlaceholderText = "Sampler" };
        var scheduler = new TextBox { PlaceholderText = "Scheduler" };
        var steps = new TextBox { Text = "0" };
        var cfg = new TextBox { Text = "0" };
        var workflow = CreateMultiline("Workflow JSON / payload");
        var notes = CreateMultiline("Notes");

        var dialog = CreateDialog(
            "Generation Groupを作成",
            620,
            760);
        var stack = CreateFormStack(
            $"選択した{selection.Count:N0}枚を、同じ生成意図・run familyとして順序付きで保存します。");
        AddField(stack, "名前", name);
        AddField(stack, "Work", work);
        AddField(stack, "Prompt", prompt);
        AddField(stack, "Negative", negative);
        AddField(stack, "Model", model);
        AddField(stack, "Sampler", sampler);
        AddField(stack, "Scheduler", scheduler);

        var numeric =
            new Grid
            {
                ColumnDefinitions =
                    new ColumnDefinitions("*,*"),
                ColumnSpacing = 8
            };
        numeric.Children.Add(
            CreateLabeledControl(
                "Steps",
                steps));
        var cfgField =
            CreateLabeledControl(
                "CFG",
                cfg);
        Grid.SetColumn(cfgField, 1);
        numeric.Children.Add(cfgField);
        stack.Children.Add(numeric);

        AddField(stack, "Workflow", workflow);
        AddField(stack, "Notes", notes);
        stack.Children.Add(CreateAssetSummary(selection));

        var status = CreateStatus();
        stack.Children.Add(status);
        stack.Children.Add(
            CreateButtons(
                dialog,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(name.Text))
                    {
                        status.Text = "Generation Group名を入力してください。";
                        return null;
                    }

                    if (!int.TryParse(
                            steps.Text,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var stepsValue)
                        || stepsValue < 0)
                    {
                        status.Text = "Stepsは0以上の整数で入力してください。";
                        return null;
                    }

                    if (!double.TryParse(
                            cfg.Text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var cfgValue)
                        || cfgValue < 0
                        || double.IsNaN(cfgValue)
                        || double.IsInfinity(cfgValue))
                    {
                        status.Text = "CFGは0以上の数値で入力してください。";
                        return null;
                    }

                    long? workId =
                        work.SelectedIndex > 0
                        && work.SelectedIndex <= works.Count
                            ? works[work.SelectedIndex - 1].Id
                            : null;

                    return new CreativeGroupDialogResult(
                        name.Text.Trim(),
                        workId,
                        prompt.Text ?? string.Empty,
                        negative.Text ?? string.Empty,
                        model.Text ?? string.Empty,
                        sampler.Text ?? string.Empty,
                        scheduler.Text ?? string.Empty,
                        stepsValue,
                        cfgValue,
                        workflow.Text ?? string.Empty,
                        notes.Text ?? string.Empty);
                }));
        dialog.Content = CreateScroll(stack);
        return ShowWithFocusReturnAsync<CreativeGroupDialogResult>(owner, dialog);
    }

    public static Task<CreativeArchiveTargetDialogResult?>
        ShowExistingWorkAsync(
            Window owner,
            CreativeSelectionPreview selection,
            IReadOnlyList<WorkInfo> works) =>
        ShowExistingTargetAsync(
            owner,
            "既存Workへ追加",
            "追加先のWork",
            selection,
            works.Select(
                    static work =>
                        (
                            work.Id,
                            $"{work.Title} · {work.Assets.Count:N0}枚"))
                .ToArray());

    public static Task<CreativeArchiveTargetDialogResult?>
        ShowExistingGenerationGroupAsync(
            Window owner,
            CreativeSelectionPreview selection,
            IReadOnlyList<GenerationGroupInfo> groups) =>
        ShowExistingTargetAsync(
            owner,
            "既存Generation Groupへ追加",
            "追加先のGeneration Group",
            selection,
            groups.Select(
                    static group =>
                        (
                            group.Id,
                            $"{group.Name} · {group.Assets.Count:N0}枚"))
                .ToArray());

    private static Task<CreativeArchiveTargetDialogResult?>
        ShowExistingTargetAsync(
            Window owner,
            string title,
            string fieldLabel,
            CreativeSelectionPreview selection,
            IReadOnlyList<(long Id, string Label)> targets)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(targets);

        var target =
            new ComboBox
            {
                ItemsSource =
                    targets.Select(
                            static item =>
                                item.Label)
                        .ToArray(),
                SelectedIndex =
                    targets.Count > 0
                        ? 0
                        : -1
            };
        var status =
            CreateStatus();
        var dialog =
            CreateDialog(
                title,
                520,
                380);
        var stack =
            CreateFormStack(
                "既存の制作コンテキストへ選択画像を末尾追加します。既に含まれている画像は重複追加しません。");
        stack.Children.Add(
            CreateAssetSummary(
                selection));
        AddField(
            stack,
            fieldLabel,
            target);
        stack.Children.Add(status);
        stack.Children.Add(
            CreateButtons(
                dialog,
                () =>
                {
                    if (target.SelectedIndex < 0
                        || target.SelectedIndex
                            >= targets.Count)
                    {
                        status.Text =
                            "追加先を選択してください。";
                        return null;
                    }

                    return new CreativeArchiveTargetDialogResult(
                        targets[target.SelectedIndex].Id);
                }));
        dialog.Content =
            CreateScroll(
                stack);
        return ShowWithFocusReturnAsync<CreativeArchiveTargetDialogResult>(owner, dialog);
    }

    public static Task<CreativeRelationDialogResult?> ShowRelationAsync(
        Window owner,
        IReadOnlyList<ViewerAsset> assets)
    {
        if (assets.Count != 2)
        {
            throw new ArgumentException(
                "Lineage creation requires exactly two assets.",
                nameof(assets));
        }

        var direction =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        $"{assets[0].DisplayName} → {assets[1].DisplayName}",
                        $"{assets[1].DisplayName} → {assets[0].DisplayName}"
                    },
                SelectedIndex = 0
            };
        var type =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "variation",
                        "img2img",
                        "inpaint",
                        "outpaint",
                        "upscale",
                        "crop",
                        "edit",
                        "animation",
                        "reference"
                    },
                SelectedIndex = 0
            };
        var note = new TextBox
        {
            PlaceholderText = "この派生関係についてのメモ"
        };

        var dialog = CreateDialog(
            "Lineageを作成",
            560,
            390);
        var stack = CreateFormStack(
            "Group membershipとは別に、「どちらからどちらが派生したか」を明示します。");
        AddField(stack, "方向", direction);
        AddField(stack, "種類", type);
        AddField(stack, "メモ", note);

        var status = CreateStatus();
        stack.Children.Add(status);
        stack.Children.Add(
            CreateButtons(
                dialog,
                () =>
                {
                    if (type.SelectedItem is not string relationType
                        || string.IsNullOrWhiteSpace(relationType))
                    {
                        status.Text = "Relation typeを選択してください。";
                        return null;
                    }

                    return new CreativeRelationDialogResult(
                        direction.SelectedIndex == 1,
                        relationType,
                        note.Text ?? string.Empty);
                }));
        dialog.Content = CreateScroll(stack);
        return ShowWithFocusReturnAsync<CreativeRelationDialogResult>(owner, dialog);
    }

    public static Task<CreativePublicationDialogResult?> ShowPublicationAsync(
        Window owner,
        CreativeSelectionPreview selection,
        IReadOnlyList<CreativePublicationAssetOption> publicationAssets,
        IReadOnlyList<WorkInfo> works,
        IReadOnlyList<PublicationDestinationInfo>? destinations = null,
        IReadOnlyList<PublicationAccountInfo>? accounts = null)
    {
        ArgumentNullException.ThrowIfNull(publicationAssets);
        if (publicationAssets.Count != selection.Count)
        {
            throw new ArgumentException(
                "Publication asset order must match the selected asset count.",
                nameof(publicationAssets));
        }

        destinations ??=
            Array.Empty<PublicationDestinationInfo>();
        accounts ??=
            Array.Empty<PublicationAccountInfo>();

        var destinationProfiles =
            destinations.ToArray();
        var destination =
            new ComboBox
            {
                ItemsSource =
                    destinationProfiles
                        .Select(
                            static item =>
                                item.Name)
                        .Append(
                            "一時入力（保存しない）")
                        .ToArray(),
                SelectedIndex = 0
            };
        if (destinationProfiles.Length == 0)
        {
            destination.SelectedIndex = 0;
        }

        var customDestination =
            new TextBox
            {
                PlaceholderText =
                    "公開先名（例: 個人サイト）",
                IsVisible =
                    destinationProfiles.Length == 0
            };
        var work =
            new ComboBox
            {
                ItemsSource =
                    new[] { "Workに紐付けない" }
                        .Concat(works.Select(
                            static item => item.Title))
                        .ToArray(),
                SelectedIndex = 0
            };
        var account =
            new ComboBox();
        var customAccount =
            new TextBox
            {
                PlaceholderText =
                    "アカウント名 / ID"
            };
        var title = new TextBox
        {
            PlaceholderText = "タイトル"
        };
        var body = CreateMultiline("本文 / caption");
        var tags = new TextBox
        {
            PlaceholderText = "タグ / hashtags"
        };
        var published = new TextBox
        {
            Text =
                DateTimeOffset.Now
                    .ToString(
                        "yyyy-MM-dd HH:mm:ss zzz",
                        CultureInfo.InvariantCulture)
        };
        var externalId = new TextBox
        {
            PlaceholderText = "External ID"
        };
        var externalUrl = new TextBox
        {
            PlaceholderText = "https://..."
        };

        var pixivMetadata =
            new CreativePublicationPixivMetadataEditor
            {
                IsVisible = false
            };
        var customMetadata = CreateMultiline(
            "Custom platform metadata JSON");
        customMetadata.Text = "{}";
        customMetadata.IsVisible = false;

        var destinationHint =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize =
                    LumineDesign.CaptionFontSize,
                TextWrapping =
                    Avalonia.Media.TextWrapping.Wrap
            };

        PublicationDestinationInfo? SelectedDestinationProfile() =>
            destination.SelectedIndex >= 0
            && destination.SelectedIndex
                < destinationProfiles.Length
                ? destinationProfiles[
                    destination.SelectedIndex]
                : null;

        PublicationAccountInfo[] selectedAccounts = [];

        void RenderAccountChoices()
        {
            var profile =
                SelectedDestinationProfile();
            selectedAccounts =
                profile is null
                    ? []
                    : accounts
                        .Where(
                            item =>
                                item.DestinationId
                                == profile.Id)
                        .OrderBy(
                            static item =>
                                item.DisplayName,
                            StringComparer.Ordinal)
                        .ThenBy(
                            static item =>
                                item.AccountIdentifier,
                            StringComparer.Ordinal)
                        .ToArray();

            account.ItemsSource =
                new[] { "アカウントなし" }
                    .Concat(
                        selectedAccounts.Select(
                            static item =>
                                string.IsNullOrWhiteSpace(
                                    item.AccountIdentifier)
                                    ? item.DisplayName
                                    : $"{item.DisplayName} · {item.AccountIdentifier}"))
                    .Append(
                        "一時入力（保存しない）")
                    .ToArray();
            account.SelectedIndex =
                selectedAccounts.Length > 0
                    ? 1
                    : selectedAccounts.Length + 1;
            account.IsVisible =
                profile is not null;
            customAccount.IsVisible =
                profile is null
                || account.SelectedIndex
                    == selectedAccounts.Length + 1;
        }

        void RenderDestination()
        {
            var profile =
                SelectedDestinationProfile();
            var isCustom =
                profile is null;
            customDestination.IsVisible =
                isCustom;

            var kind =
                profile?.Kind
                ?? "other";
            var pixiv =
                string.Equals(
                    kind,
                    "pixiv",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    profile?.Name,
                    "Pixiv",
                    StringComparison.Ordinal);
            var twitter =
                string.Equals(
                    kind,
                    "twitter",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    profile?.Name,
                    "X",
                    StringComparison.Ordinal);
            var custom =
                isCustom
                || string.Equals(
                    kind,
                    "other",
                    StringComparison.OrdinalIgnoreCase);

            pixivMetadata.IsVisible = pixiv;
            customMetadata.IsVisible = custom;
            destinationHint.Text =
                pixiv
                    ? "Pixiv向け: title / caption / tags / age restriction / AI生成フラグをsnapshot化します。"
                    : twitter
                        ? "X向け: body / hashtags / ordered imagesを中心にsnapshot化します。"
                        : isCustom
                            ? "この記録だけの公開先を入力します。プロフィールには保存されません。"
                            : $"{profile!.Name}向けのPublication snapshotを保存します。";

            RenderAccountChoices();
        }

        destination.SelectionChanged +=
            (_, _) => RenderDestination();
        account.SelectionChanged +=
            (_, _) =>
            {
                customAccount.IsVisible =
                    SelectedDestinationProfile()
                        is null
                    || account.SelectedIndex
                        == selectedAccounts.Length + 1;
            };
        RenderDestination();

        var dialog = CreateDialog(
            "Publicationを記録",
            620,
            800);
        var stack = CreateFormStack(
            $"選択した{selection.Count:N0}枚の「実際に公開した内容」を、現在のローカルmetadataとは独立したsnapshotとして保存します。");
        AddField(stack, "公開先", destination);
        AddField(
            stack,
            "一時公開先",
            customDestination);
        stack.Children.Add(destinationHint);
        AddField(stack, "Work", work);
        AddField(stack, "アカウント", account);
        AddField(
            stack,
            "一時アカウント",
            customAccount);
        AddField(stack, "タイトル", title);
        AddField(stack, "本文", body);
        AddField(stack, "タグ", tags);
        AddField(stack, "公開日時", published);
        AddField(stack, "External ID", externalId);
        AddField(stack, "External URL", externalUrl);
        stack.Children.Add(
            pixivMetadata);
        AddField(stack, "Platform metadata", customMetadata);
        var orderEditor =
            new CreativePublicationOrderEditor(
                publicationAssets);
        AddField(
            stack,
            "画像の公開順",
            orderEditor);

        var status = CreateStatus();
        stack.Children.Add(status);
        stack.Children.Add(
            CreateButtons(
                dialog,
                () =>
                {
                    var destinationProfile =
                        SelectedDestinationProfile();
                    var destinationValue =
                        destinationProfile?.Name
                        ?? customDestination.Text
                            ?.Trim();
                    if (string.IsNullOrWhiteSpace(
                            destinationValue))
                    {
                        status.Text =
                            "公開先を入力してください。";
                        return null;
                    }

                    if (!DateTimeOffset.TryParse(
                            published.Text,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AllowWhiteSpaces,
                            out var publishedAt))
                    {
                        status.Text =
                            "公開日時を読み取れません。";
                        return null;
                    }

                    long? workId =
                        work.SelectedIndex > 0
                        && work.SelectedIndex
                            <= works.Count
                            ? works[
                                work.SelectedIndex - 1]
                                .Id
                            : null;

                    string accountValue;
                    if (destinationProfile is null)
                    {
                        accountValue =
                            customAccount.Text
                                ?.Trim()
                            ?? string.Empty;
                    }
                    else if (account.SelectedIndex
                             > 0
                             && account.SelectedIndex
                                <= selectedAccounts.Length)
                    {
                        var selected =
                            selectedAccounts[
                                account.SelectedIndex - 1];
                        accountValue =
                            string.IsNullOrWhiteSpace(
                                selected.AccountIdentifier)
                                ? selected.DisplayName
                                : $"{selected.DisplayName} · {selected.AccountIdentifier}";
                    }
                    else if (account.SelectedIndex
                             == selectedAccounts.Length + 1)
                    {
                        accountValue =
                            customAccount.Text
                                ?.Trim()
                            ?? string.Empty;
                    }
                    else
                    {
                        accountValue =
                            string.Empty;
                    }

                    var kind =
                        destinationProfile?.Kind
                        ?? "other";
                    var isPixiv =
                        string.Equals(
                            kind,
                            "pixiv",
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            destinationValue,
                            "Pixiv",
                            StringComparison.Ordinal);
                    var isTwitter =
                        string.Equals(
                            kind,
                            "twitter",
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            destinationValue,
                            "X",
                            StringComparison.Ordinal);
                    var platformMetadata =
                        isPixiv
                            ? pixivMetadata.PlatformMetadataJson
                            : isTwitter
                                ? "{}"
                                : string.IsNullOrWhiteSpace(
                                    customMetadata.Text)
                                    ? "{}"
                                    : customMetadata.Text;

                    return new CreativePublicationDialogResult(
                        workId,
                        destinationValue,
                        accountValue,
                        title.Text ?? string.Empty,
                        body.Text ?? string.Empty,
                        tags.Text ?? string.Empty,
                        publishedAt.ToUniversalTime(),
                        externalId.Text ?? string.Empty,
                        externalUrl.Text ?? string.Empty,
                        platformMetadata,
                        orderEditor.OrderedAssetIds);
                }));
        dialog.Content =
            CreateScroll(
                stack);
        return ShowWithFocusReturnAsync<CreativePublicationDialogResult>(owner, dialog);
    }

    // All creative-archive modal entry points use the same focus-return
    // contract as the shared product dialogs. A selected thumbnail or
    // invoking command may disappear while a dialog is open, so never
    // attempt to focus a detached, hidden, or disabled control.
    internal static async Task<TResult?> ShowWithFocusReturnAsync<TResult>(
        Window owner,
        Window dialog)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dialog);

        var focusReturn =
            owner.FocusManager?.GetFocusedElement()
                as Control;
        try
        {
            return await dialog.ShowDialog<TResult?>(owner);
        }
        finally
        {
            if (focusReturn is
                { IsEnabled: true, IsEffectivelyVisible: true }
                && ReferenceEquals(
                    TopLevel.GetTopLevel(focusReturn),
                    owner))
            {
                focusReturn.Focus(
                    NavigationMethod.Unspecified,
                    KeyModifiers.None);
            }
        }
    }

    private static Window CreateDialog(
        string title,
        double width,
        double height) =>
        new()
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 420),
            MinHeight = Math.Min(height, 320),
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Background = LumineDesign.Background,
            Foreground = LumineDesign.Foreground,
            FontFamily = LumineDesign.UiFont
        };

    private static StackPanel CreateFormStack(
        string description)
    {
        var stack =
            new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(18)
            };
        stack.Children.Add(
            new TextBlock
            {
                Text = description,
                Foreground =
                    LumineDesign.MutedForeground,
                TextWrapping =
                    Avalonia.Media.TextWrapping.Wrap,
                FontSize = LumineDesign.CaptionFontSize
            });
        return stack;
    }

    private static TextBox CreateMultiline(
        string placeholder) =>
        LumineDesign.ConfigureTextBox(
            new TextBox
            {
                PlaceholderText = placeholder,
                AcceptsReturn = true,
                TextWrapping =
                    Avalonia.Media.TextWrapping.Wrap,
                MinHeight = 70
            });

    private static TextBlock CreateStatus() =>
        new()
        {
            Foreground = LumineDesign.Warning,
            FontSize = LumineDesign.CaptionFontSize,
            TextWrapping =
                Avalonia.Media.TextWrapping.Wrap
        };

    private static TextBlock CreateAssetSummary(
        CreativeSelectionPreview selection) =>
        new TextBlock
        {
            Text =
                selection.SampleDisplayNames.Count == 0
                    ? $"選択画像: {selection.Count:N0}枚"
                    : "画像順: "
                      + string.Join(
                          " → ",
                          selection.SampleDisplayNames)
                      + (selection.IsTruncated
                          ? $" → …（残り{selection.Count - selection.SampleDisplayNames.Count:N0}枚）"
                          : string.Empty),
            Foreground =
                LumineDesign.MutedForeground,
            FontSize = LumineDesign.CaptionFontSize,
            TextWrapping =
                Avalonia.Media.TextWrapping.Wrap
        };

    private static StackPanel CreateLabeledControl(
        string label,
        Control control)
    {
        control =
            control switch
            {
                TextBox textBox =>
                    LumineDesign.ConfigureTextBox(
                        textBox),
                ComboBox comboBox =>
                    LumineDesign.ConfigureComboBox(
                        comboBox),
                CheckBox checkBox =>
                    LumineDesign.ConfigureCheckBox(
                        checkBox),
                _ => control
            };

        var stack =
            new StackPanel
            {
                Spacing = 4
            };
        stack.Children.Add(
            new TextBlock
            {
                Text = label,
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize
            });
        stack.Children.Add(control);
        return stack;
    }

    private static void AddField(
        Panel stack,
        string label,
        Control control) =>
        stack.Children.Add(
            CreateLabeledControl(
                label,
                control));

    private static StackPanel CreateButtons<T>(
        Window dialog,
        Func<T?> create)
        where T : class
    {
        var cancel =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "キャンセル"
                });
        cancel.Click +=
            (_, _) => dialog.Close(null);

        var save =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = "保存"
                });
        save.Click +=
            (_, _) =>
            {
                var result = create();
                if (result is not null)
                {
                    dialog.Close(result);
                }
            };

        var row =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Spacing = 8
            };
        row.Children.Add(cancel);
        row.Children.Add(save);
        return row;
    }

    private static ScrollViewer CreateScroll(
        Control child) =>
        new ScrollViewer
        {
            Content = child,
            HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
}
