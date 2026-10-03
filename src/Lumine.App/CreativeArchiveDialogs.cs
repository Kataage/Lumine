using System.Globalization;
using Avalonia;
using Avalonia.Controls;
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
    string PlatformMetadataJson);

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
        return dialog.ShowDialog<CreativeWorkDialogResult?>(owner);
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
        return dialog.ShowDialog<CreativeGroupDialogResult?>(owner);
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
        return dialog.ShowDialog<CreativeRelationDialogResult?>(owner);
    }

    public static Task<CreativePublicationDialogResult?> ShowPublicationAsync(
        Window owner,
        CreativeSelectionPreview selection,
        IReadOnlyList<WorkInfo> works)
    {
        var destination =
            new ComboBox
            {
                ItemsSource =
                    new[]
                    {
                        "Pixiv",
                        "X",
                        "Custom"
                    },
                SelectedIndex = 0
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
        var account = new TextBox
        {
            PlaceholderText = "アカウント名 / ID"
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

        var pixivR18 =
            LumineDesign.ConfigureCheckBox(
                new CheckBox
                {
                    Content = "Pixiv: R-18"
                });
        var pixivAi =
            LumineDesign.ConfigureCheckBox(
                new CheckBox
                {
                    Content = "Pixiv: AI生成"
                });
        var customMetadata = CreateMultiline(
            "Custom platform metadata JSON");
        customMetadata.Text = "{}";
        customMetadata.IsVisible = false;

        var destinationHint =
            new TextBlock
            {
                Foreground =
                    LumineDesign.MutedForeground,
                FontSize = LumineDesign.CaptionFontSize,
                TextWrapping =
                    Avalonia.Media.TextWrapping.Wrap
            };

        void RenderDestination()
        {
            var value =
                destination.SelectedItem as string
                ?? "Custom";
            var pixiv =
                string.Equals(
                    value,
                    "Pixiv",
                    StringComparison.Ordinal);
            var custom =
                string.Equals(
                    value,
                    "Custom",
                    StringComparison.Ordinal);
            pixivR18.IsVisible = pixiv;
            pixivAi.IsVisible = pixiv;
            customMetadata.IsVisible = custom;
            destinationHint.Text =
                value switch
                {
                    "Pixiv" =>
                        "Pixiv向け: title / caption / tags / age restriction / AI生成フラグをsnapshot化します。",
                    "X" =>
                        "X向け: body / hashtags / ordered imagesを中心にsnapshot化します。",
                    _ =>
                        "Custom向け: 共通項目に加え、任意のplatform metadata JSONを保存できます。"
                };
        }

        destination.SelectionChanged +=
            (_, _) => RenderDestination();
        RenderDestination();

        var dialog = CreateDialog(
            "Publicationを記録",
            620,
            760);
        var stack = CreateFormStack(
            $"選択した{selection.Count:N0}枚の「実際に公開した内容」を、現在のローカルmetadataとは独立したsnapshotとして保存します。");
        AddField(stack, "公開先", destination);
        stack.Children.Add(destinationHint);
        AddField(stack, "Work", work);
        AddField(stack, "アカウント", account);
        AddField(stack, "タイトル", title);
        AddField(stack, "本文", body);
        AddField(stack, "タグ", tags);
        AddField(stack, "公開日時", published);
        AddField(stack, "External ID", externalId);
        AddField(stack, "External URL", externalUrl);
        stack.Children.Add(pixivR18);
        stack.Children.Add(pixivAi);
        AddField(stack, "Platform metadata", customMetadata);
        stack.Children.Add(CreateAssetSummary(selection));

        var status = CreateStatus();
        stack.Children.Add(status);
        stack.Children.Add(
            CreateButtons(
                dialog,
                () =>
                {
                    var destinationValue =
                        destination.SelectedItem as string;
                    if (string.IsNullOrWhiteSpace(destinationValue))
                    {
                        status.Text = "公開先を選択してください。";
                        return null;
                    }

                    if (!DateTimeOffset.TryParse(
                            published.Text,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AllowWhiteSpaces,
                            out var publishedAt))
                    {
                        status.Text = "公開日時を読み取れません。";
                        return null;
                    }

                    long? workId =
                        work.SelectedIndex > 0
                        && work.SelectedIndex <= works.Count
                            ? works[work.SelectedIndex - 1].Id
                            : null;
                    var platformMetadata =
                        destinationValue switch
                        {
                            "Pixiv" =>
                                pixivR18.IsChecked == true
                                    ? $"{{\"ageRestriction\":\"r18\",\"aiGenerated\":{(pixivAi.IsChecked == true ? "true" : "false")}}}"
                                    : $"{{\"ageRestriction\":\"all\",\"aiGenerated\":{(pixivAi.IsChecked == true ? "true" : "false")}}}",
                            "X" => "{}",
                            _ =>
                                string.IsNullOrWhiteSpace(
                                    customMetadata.Text)
                                    ? "{}"
                                    : customMetadata.Text
                        };

                    return new CreativePublicationDialogResult(
                        workId,
                        destinationValue,
                        account.Text ?? string.Empty,
                        title.Text ?? string.Empty,
                        body.Text ?? string.Empty,
                        tags.Text ?? string.Empty,
                        publishedAt.ToUniversalTime(),
                        externalId.Text ?? string.Empty,
                        externalUrl.Text ?? string.Empty,
                        platformMetadata);
                }));
        dialog.Content = CreateScroll(stack);
        return dialog.ShowDialog<CreativePublicationDialogResult?>(owner);
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
