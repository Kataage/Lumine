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
                    "Pixivの年齢制限（任意）",
                Foreground =
                    LumineDesign.Foreground,
                FontSize =
                    LumineDesign.BodyFontSize
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
                        "AI生成作品として登録（任意）",
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
            "投稿画像の順番");
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
                ? $"選択位置 {index + 1:N0} / {_items.Count:N0}"
                : $"{_items.Count:N0}枚";
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
            owner,
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
        dialog.Content = CreateDialogForm(stack);
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
            owner,
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
        dialog.Content = CreateDialogForm(stack);
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
                owner,
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
        dialog.Content = CreateDialogForm(stack);
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
            owner,
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
        dialog.Content = CreateDialogForm(stack);
        return ShowWithFocusReturnAsync<CreativeRelationDialogResult>(owner, dialog);
    }

    public static Task<CreativePublicationDialogResult?> ShowPublicationAsync(
        Window owner,
        CreativeSelectionPreview selection,
        IReadOnlyList<CreativePublicationAssetOption> publicationAssets,
        IReadOnlyList<WorkInfo> works,
        IReadOnlyList<PublicationDestinationInfo>? destinations = null,
        IReadOnlyList<PublicationAccountInfo>? accounts = null,
        Func<string, string, Task<PublicationDestinationInfo?>>?
            registerDestination = null,
        Func<long, string, string, Task<PublicationAccountInfo?>>?
            registerAccount = null) =>
        ShowWithFocusReturnAsync<CreativePublicationDialogResult>(
            owner,
            CreatePublicationDialogForSmoke(
                owner,
                selection,
                publicationAssets,
                works,
                destinations,
                accounts,
                registerDestination,
                registerAccount));

    // Production dialog construction is also mounted by the App smoke.
    // The test invokes actual Save-account button and then reopens the same
    // production form with persisted repository records.
    internal static Window CreatePublicationDialogForSmoke(
        Window owner,
        CreativeSelectionPreview selection,
        IReadOnlyList<CreativePublicationAssetOption> publicationAssets,
        IReadOnlyList<WorkInfo> works,
        IReadOnlyList<PublicationDestinationInfo>? destinations = null,
        IReadOnlyList<PublicationAccountInfo>? accounts = null,
        Func<string, string, Task<PublicationDestinationInfo?>>?
            registerDestination = null,
        Func<long, string, string, Task<PublicationAccountInfo?>>?
            registerAccount = null)
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
            destinations.ToList();
        var registeredAccounts = accounts.ToList();
        var destination =
            new ComboBox
            {
                ItemsSource =
                    destinationProfiles
                        .Select(
                            static item =>
                                item.Name)
                        .Append(
                            "新しい公開先を入力")
                        .ToArray(),
                SelectedIndex = 0
            };
        if (destinationProfiles.Count == 0)
        {
            destination.SelectedIndex = 0;
        }

        var customDestination =
            new TextBox
            {
                PlaceholderText =
                    string.Empty,
                IsVisible =
                    destinationProfiles.Count == 0
            };
        var work =
            new ComboBox
            {
                ItemsSource =
                    new[] { "関連作品なし" }
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
                    string.Empty
            };
        AutomationProperties.SetName(
            destination,
            "公開先を選択");
        AutomationProperties.SetName(
            customDestination,
            "新しい公開先名を入力");
        AutomationProperties.SetName(
            account,
            "登録済みアカウントを選択");
        AutomationProperties.SetName(
            customAccount,
            "新しいアカウント名を入力");
        var accountRegistrationStatus = CreateStatus();
        var saveAccount =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "アカウントを登録して使用",
                    IsVisible = registerAccount is not null
                });
        AutomationProperties.SetName(
            saveAccount,
            "公開アカウントを登録して次回から再利用");
        var title = new TextBox
        {
            PlaceholderText = string.Empty
        };
        var body = CreateMultiline(string.Empty);
        var tags = new TextBox
        {
            PlaceholderText = string.Empty
        };
        var published = new TextBox
        {
            Text =
                DateTimeOffset.Now
                    .ToString(
                        "yyyy/MM/dd HH:mm",
                        CultureInfo.InvariantCulture)
        };
        var externalId = new TextBox
        {
            PlaceholderText = string.Empty
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
        var customMetadata = CreateMultiline(string.Empty);
        customMetadata.Text = "{}";

        var newDestinationField =
            CreatePublicationField(
                "新しい公開先名（必須）",
                customDestination);
        var savedAccountField =
            CreatePublicationField(
                "投稿アカウント（任意）",
                account);
        var newAccountField =
            CreatePublicationField(
                "登録するアカウント名（任意）",
                customAccount);
        var technicalMetadata =
            CreatePublicationField(
                "サイト固有情報（任意・JSON）",
                customMetadata);
        // Keep the registration action with its input, not as an
        // unrelated primary-form action far from the field.
        newAccountField.Children.Add(saveAccount);
        newAccountField.Children.Add(accountRegistrationStatus);

        PublicationDestinationInfo? SelectedDestinationProfile() =>
            destination.SelectedIndex >= 0
            && destination.SelectedIndex
                < destinationProfiles.Count
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
                    : registeredAccounts
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
                        "新しいアカウントを登録")
                    .ToArray();
            account.SelectedIndex =
                selectedAccounts.Length > 0
                    ? 1
                    : selectedAccounts.Length + 1;
            savedAccountField.IsVisible =
                profile is not null;
            UpdateNewAccountVisibility();
        }

        void UpdateNewAccountVisibility()
        {
            var needsAccountInput =
                SelectedDestinationProfile() is null
                || account.SelectedIndex
                    == selectedAccounts.Length + 1;
            newAccountField.IsVisible = needsAccountInput;
            saveAccount.IsVisible =
                needsAccountInput
                && registerAccount is not null;
        }

        void RenderDestination()
        {
            var profile =
                SelectedDestinationProfile();
            var isCustom =
                profile is null;
            newDestinationField.IsVisible =
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
            var custom =
                isCustom
                || string.Equals(
                    kind,
                    "other",
                    StringComparison.OrdinalIgnoreCase);

            pixivMetadata.IsVisible = pixiv;
            technicalMetadata.IsVisible = custom;

            RenderAccountChoices();
        }

        destination.SelectionChanged +=
            (_, _) => RenderDestination();
        account.SelectionChanged +=
            (_, _) => UpdateNewAccountVisibility();
        var accountSaving = false;
        async Task<bool> RegisterCurrentAccountAsync()
        {
            if (accountSaving || registerAccount is null)
            {
                return false;
            }

            var name = customAccount.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                accountRegistrationStatus.Text =
                    "登録するアカウント名を入力してください。";
                return false;
            }

            accountSaving = true;
            saveAccount.IsEnabled = false;
            accountRegistrationStatus.Text = "保存中…";
            try
            {
                var profile = SelectedDestinationProfile();
                if (profile is null)
                {
                    var destinationName =
                        customDestination.Text?.Trim()
                        ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(destinationName))
                    {
                        accountRegistrationStatus.Text =
                            "公開先名を入力してください。";
                        return false;
                    }

                    if (registerDestination is null)
                    {
                        accountRegistrationStatus.Text =
                            "公開先を登録できません。";
                        return false;
                    }

                    var kind =
                        destinationName.Equals(
                            "Pixiv",
                            StringComparison.OrdinalIgnoreCase)
                            ? "pixiv"
                            : destinationName.Equals(
                                "X",
                                StringComparison.OrdinalIgnoreCase)
                              || destinationName.Equals(
                                  "Twitter",
                                  StringComparison.OrdinalIgnoreCase)
                                ? "twitter"
                                : destinationName.Equals(
                                    "Misskey",
                                    StringComparison.OrdinalIgnoreCase)
                                    ? "misskey"
                                    : destinationName.Equals(
                                        "Bluesky",
                                        StringComparison.OrdinalIgnoreCase)
                                        ? "bluesky"
                                        : "other";
                    profile =
                        await registerDestination(
                            destinationName,
                            kind);
                    if (profile is null)
                    {
                        accountRegistrationStatus.Text =
                            "公開先を保存できませんでした。";
                        return false;
                    }

                    destinationProfiles.Add(profile);
                    destination.ItemsSource =
                        destinationProfiles
                            .Select(static item => item.Name)
                            .Append("新しい公開先を入力")
                            .ToArray();
                    destination.SelectedIndex =
                        destinationProfiles.Count - 1;
                    customDestination.Text = string.Empty;
                    RenderDestination();
                }

                var saved =
                    await registerAccount(
                        profile.Id,
                        name,
                        string.Empty);
                if (saved is null)
                {
                    accountRegistrationStatus.Text =
                        "アカウントを保存できませんでした。";
                    return false;
                }

                registeredAccounts.Add(saved);
                RenderAccountChoices();
                account.SelectedIndex =
                    Array.FindIndex(
                        selectedAccounts,
                        item => item.Id == saved.Id) + 1;
                customAccount.Text = string.Empty;
                UpdateNewAccountVisibility();
                accountRegistrationStatus.Text =
                    "アカウントを保存しました。";
                return true;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError(
                    exception.ToString());
                accountRegistrationStatus.Text =
                    "アカウントを保存できませんでした。";
                return false;
            }
            finally
            {
                accountSaving = false;
                saveAccount.IsEnabled = true;
            }
        }
        saveAccount.Click +=
            async (_, _) =>
                await RegisterCurrentAccountAsync();
        RenderDestination();

        var dialog = CreateDialog(
            owner,
            "公開記録を追加",
            620,
            800);
        var stack = CreateFormStack(string.Empty);

        // Essential path: where / when / which images.
        // The optional archive metadata lives behind one disclosure,
        // so the first-run form stays focused even on small screens.
        var destinationSection =
            CreatePublicationSection(
                stack,
                "1. 公開先とアカウント");
        AddPublicationField(
            destinationSection,
            "公開先サービス（必須）",
            destination);
        destinationSection.Children.Add(newDestinationField);
        destinationSection.Children.Add(savedAccountField);
        destinationSection.Children.Add(newAccountField);

        var contentSection =
            CreatePublicationSection(
                stack,
                "2. 投稿内容");
        AddPublicationField(
            contentSection,
            "公開日時（必須）",
            published);
        AddPublicationField(
            contentSection,
            "投稿タイトル（任意）",
            title);
        AddPublicationField(
            contentSection,
            "投稿本文（任意）",
            body);
        AddPublicationField(
            contentSection,
            "投稿タグ（任意）",
            tags);
        contentSection.Children.Add(pixivMetadata);

        var imageSection =
            CreatePublicationSection(
                stack,
                "3. 投稿画像");
        var orderEditor =
            new CreativePublicationOrderEditor(
                publicationAssets);
        if (publicationAssets.Count == 1)
        {
            AddPublicationField(
                imageSection,
                "選択画像（必須・1枚）",
                new TextBlock
                {
                    Text = publicationAssets[0].DisplayName,
                    Foreground = LumineDesign.Foreground,
                    FontSize = LumineDesign.BodyFontSize,
                    TextWrapping =
                        Avalonia.Media.TextWrapping.Wrap
                });
        }
        else
        {
            AddPublicationField(
                imageSection,
                $"投稿画像の順番（必須・{publicationAssets.Count:N0}枚）",
                orderEditor);
        }

        var extraFields = new StackPanel
        {
            Spacing = LumineDesign.Space12,
            Margin = new Thickness(12, 8, 12, 12)
        };
        AddPublicationField(
            extraFields,
            "関連する作品（任意）",
            work);
        AddPublicationField(
            extraFields,
            "投稿ID（任意）",
            externalId);
        AddPublicationField(
            extraFields,
            "投稿URL（任意）",
            externalUrl);
        extraFields.Children.Add(technicalMetadata);
        stack.Children.Add(
            new Expander
            {
                Header = "その他の情報（任意）",
                IsExpanded = false,
                Content = extraFields
            });

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
                            "公開先サービス（必須）を指定してください。";
                        if (destinationProfile is null)
                        {
                            customDestination.Focus();
                        }
                        else
                        {
                            destination.Focus();
                        }

                        return null;
                    }

                    if (!DateTimeOffset.TryParse(
                            published.Text,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AllowWhiteSpaces,
                            out var publishedAt))
                    {
                        status.Text =
                            "公開日時（必須）を確認してください。";
                        published.Focus();
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
                },
                "公開記録を保存",
                beforeSaveAsync: async () =>
                {
                    if (registerAccount is null
                        || !newAccountField.IsVisible
                        || string.IsNullOrWhiteSpace(
                            customAccount.Text))
                    {
                        return true;
                    }

                    return await RegisterCurrentAccountAsync();
                },
                saveStatus: status));
        dialog.Content = CreateDialogForm(stack);
        return dialog;
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

    // Window sizes are in Avalonia device-independent pixels. Clamp to
    // the *current owner's client area* AND the active screen work area;
    // the latter includes window frame/title bar headroom. A hard 800-DIP
    // Publication height could otherwise place Save/Cancel off-screen.
    internal static Size ResolveDialogSizeForSmoke(
        double preferredWidth,
        double preferredHeight,
        double ownerWidth,
        double ownerHeight,
        double screenWorkWidth,
        double screenWorkHeight)
    {
        const double minimumUsefulWidth = 240;
        const double minimumUsefulHeight = 180;

        static double BoundedDimension(
            double preferred, double owner, double workArea,
            double ownerInset, double screenInset, double fallback)
        {
            var limit = fallback;
            if (double.IsFinite(owner) && owner > 0)
            {
                limit = Math.Min(limit, Math.Max(1, owner - ownerInset));
            }

            if (double.IsFinite(workArea) && workArea > 0)
            {
                limit = Math.Min(limit, Math.Max(1, workArea - screenInset));
            }

            return Math.Min(preferred, limit);
        }

        return new Size(
            Math.Max(1, BoundedDimension(
                preferredWidth, ownerWidth, screenWorkWidth,
                24, 48, Math.Max(minimumUsefulWidth, preferredWidth))),
            Math.Max(1, BoundedDimension(
                preferredHeight, ownerHeight, screenWorkHeight,
                24, 88, Math.Max(minimumUsefulHeight, preferredHeight))));
    }

    private static Window CreateDialog(
        Window owner,
        string title,
        double width,
        double height)
    {
        var dialog = new Window
        {
            Title = title,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Background = LumineDesign.Background,
            Foreground = LumineDesign.Foreground,
            FontFamily = LumineDesign.UiFont,
            CanResize = true,
            // Never let an old fixed MinHeight force the modal off-screen.
            MinWidth = 1,
            MinHeight = 1
        };

        void FitToAvailableSpace()
        {
            var screen = owner.Screens.ScreenFromWindow(owner);
            var screenWidth = screen is null
                ? double.PositiveInfinity
                : screen.WorkingArea.Width / Math.Max(1, screen.Scaling);
            var screenHeight = screen is null
                ? double.PositiveInfinity
                : screen.WorkingArea.Height / Math.Max(1, screen.Scaling);
            var result = ResolveDialogSizeForSmoke(
                width, height,
                owner.Bounds.Width, owner.Bounds.Height,
                screenWidth, screenHeight);
            dialog.MaxWidth = result.Width;
            dialog.MaxHeight = result.Height;
            dialog.Width = result.Width;
            dialog.Height = result.Height;
        }

        FitToAvailableSpace();
        EventHandler<SizeChangedEventArgs> ownerResized =
            (_, _) => FitToAvailableSpace();
        owner.SizeChanged += ownerResized;
        dialog.Opened += (_, _) => FitToAvailableSpace();
        dialog.Closed += (_, _) => owner.SizeChanged -= ownerResized;
        return dialog;
    }

    private static StackPanel CreateFormStack(
        string description)
    {
        var stack =
            new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(18)
            };
        if (!string.IsNullOrWhiteSpace(description))
        {
            stack.Children.Add(
                new TextBlock
                {
                    Text = description,
                    Foreground = LumineDesign.MutedForeground,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = LumineDesign.CaptionFontSize
                });
        }

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

    private static StackPanel CreatePublicationField(
        string label,
        Control control)
    {
        var field = CreateLabeledControl(label, control);
        // The field's control may itself be a TextBlock (one-image
        // summary), so pick the first label, not Single().
        var title = field.Children.OfType<TextBlock>().First();
        if (string.IsNullOrWhiteSpace(
            AutomationProperties.GetName(control)))
        {
            AutomationProperties.SetName(control, label);
        }

        title.FontSize = LumineDesign.BodyFontSize;
        title.Foreground = LumineDesign.Foreground;
        return field;
    }

    private static void AddPublicationField(
        Panel stack,
        string label,
        Control control) =>
        stack.Children.Add(CreatePublicationField(label, control));

    private static StackPanel CreatePublicationSection(
        Panel form,
        string title)
    {
        var fields = new StackPanel
        {
            Spacing = LumineDesign.Space12
        };
        fields.Children.Add(
            new TextBlock
            {
                Text = title,
                FontWeight =
                    Avalonia.Media.FontWeight.SemiBold,
                FontSize = LumineDesign.DialogTitleFontSize,
                Foreground = LumineDesign.Foreground
            });
        var section = new Border
        {
            Background = LumineDesign.Surface,
            BorderBrush = LumineDesign.Border,
            BorderThickness = new Thickness(1),
            CornerRadius =
                new CornerRadius(LumineDesign.PanelRadius),
            Padding = new Thickness(16),
            Child = fields
        };
        section.Classes.Add("lumine-publication-section");
        AutomationProperties.SetName(section, title);
        form.Children.Add(section);
        return fields;
    }

    private static StackPanel CreateButtons<T>(
        Window dialog,
        Func<T?> create,
        string saveLabel = "保存",
        Func<Task<bool>>? beforeSaveAsync = null,
        TextBlock? saveStatus = null)
        where T : class
    {
        var cancel =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = "キャンセル",
                    IsCancel = true
                });
        cancel.Click +=
            (_, _) => dialog.Close(null);

        var save =
            LumineDesign.ConfigurePrimaryButton(
                new Button
                {
                    Content = saveLabel
                });
        if (beforeSaveAsync is null)
        {
            // Preserve the original synchronous behavior of unrelated
            // Work, Group and Relation dialogs.
            save.Click +=
                (_, _) =>
                {
                    var result = create();
                    if (result is not null)
                    {
                        dialog.Close(result);
                    }
                };
        }
        else
        {
            save.Click +=
                async (_, _) =>
                {
                    if (!save.IsEnabled)
                    {
                        return;
                    }

                    var result = create();
                    if (result is null)
                    {
                        return;
                    }

                    // Only Publication supplies a persistence hook. It keeps
                    // the dialog open if account registration fails instead
                    // of silently losing a reusable account identity.
                    if (beforeSaveAsync is not null)
                    {
                        save.IsEnabled = false;
                        cancel.IsEnabled = false;
                        try
                        {
                            if (!await beforeSaveAsync())
                            {
                                if (saveStatus is not null)
                                {
                                    saveStatus.Text =
                                        "アカウントを登録できませんでした。";
                                }

                                return;
                            }

                            result = create();
                            if (result is null)
                            {
                                return;
                            }
                        }
                        catch (Exception exception)
                        {
                            System.Diagnostics.Trace.TraceError(
                                exception.ToString());
                            if (saveStatus is not null)
                            {
                                saveStatus.Text =
                                    "公開記録を保存できませんでした。";
                            }

                            return;
                        }
                        finally
                        {
                            save.IsEnabled = true;
                            cancel.IsEnabled = true;
                        }
                    }

                    if (dialog.IsVisible)
                    {
                        dialog.Close(result);
                    }
                };

        }
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

    // All archive forms share one scrollable body and a fixed bottom
    // action row. Neither tall platform fields nor 225% text may push
    // Save/Cancel outside the modal viewport. Validation remains visible
    // above the actions even when the user is scrolled to the first field.
    internal static Grid CreateDialogForm(StackPanel fields)
    {
        if (fields.Children.Count < 2
            || fields.Children[fields.Children.Count - 1] is not StackPanel actions
            || fields.Children[fields.Children.Count - 2] is not TextBlock status)
        {
            throw new InvalidOperationException(
                "Archive dialog must end with validation status and Save/Cancel actions.");
        }

        fields.Children.Remove(actions);
        fields.Children.Remove(status);

        var footer = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(18, 8, 18, 12)
        };
        footer.Children.Add(status);
        footer.Children.Add(actions);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        var scroll = CreateScroll(fields);
        Grid.SetRow(scroll, 0);
        root.Children.Add(scroll);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        return root;
    }

    // Exercises the EXACT shared production dialog geometry and fixed
    // footer layout with the Publication form's unusually long body.
    // No database operation or real publication is performed.
    internal static Window CreatePublicationLayoutPreviewForSmoke(
        Window owner)
    {
        var dialog = CreateDialog(owner, "Publicationを記録", 620, 800);
        var form = CreateFormStack(
            "公開記録の登録: 小さな画面でも保存とキャンセルは常に表示されます。");
        for (var index = 0; index < 14; index++)
        {
            AddField(form, $"公開項目 {index + 1}", CreateMultiline("入力内容"));
        }

        var status = CreateStatus();
        form.Children.Add(status);
        form.Children.Add(
            CreateButtons(
                dialog,
                () => new CreativeWorkDialogResult("fixture", string.Empty)));
        dialog.Content = CreateDialogForm(form);
        return dialog;
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
