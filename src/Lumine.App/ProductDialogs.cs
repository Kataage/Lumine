using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumine.Core;

namespace Lumine.App;

internal enum ProductDialogTone
{
    Default = 0,
    Danger = 1
}

internal static class ProductDialogs
{
    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string description,
        string? detail = null,
        string confirmLabel = "実行",
        string cancelLabel = "キャンセル",
        ProductDialogTone tone = ProductDialogTone.Default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var dialog =
            CreateDialog(
                title,
                height:
                    string.IsNullOrWhiteSpace(detail)
                        ? 250
                        : 340);

        var panel =
            CreateContent(
                title,
                description,
                detail,
                tone);

        var cancel =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = cancelLabel,
                    IsCancel = true
                });
        cancel.Click +=
            (_, _) => dialog.Close(false);

        var confirm =
            tone == ProductDialogTone.Danger
                ? LumineDesign.ConfigureDangerButton(
                    new Button
                    {
                        Content = confirmLabel,
                        IsDefault = true
                    })
                : LumineDesign.ConfigurePrimaryButton(
                    new Button
                    {
                        Content = confirmLabel,
                        IsDefault = true
                    });
        confirm.Click +=
            (_, _) => dialog.Close(true);

        panel.Children.Add(
            CreateButtons(
                cancel,
                confirm));
        dialog.Content = panel;

        return await ShowWithFocusReturnAsync<bool>(
            owner,
            dialog);
    }

    public static async Task NotifyAsync(
        Window owner,
        string title,
        string description,
        string? detail = null,
        string buttonLabel = "OK",
        ProductDialogTone tone = ProductDialogTone.Default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var dialog =
            CreateDialog(
                title,
                height:
                    string.IsNullOrWhiteSpace(detail)
                        ? 240
                        : 330);

        var panel =
            CreateContent(
                title,
                description,
                detail,
                tone);

        var close =
            LumineDesign.ConfigureSecondaryButton(
                new Button
                {
                    Content = buttonLabel,
                    IsDefault = true,
                    IsCancel = true
                });
        close.Click +=
            (_, _) => dialog.Close();

        panel.Children.Add(
            CreateButtons(close));
        dialog.Content = panel;

        await ShowWithFocusReturnAsync<object?>(
            owner,
            dialog);
    }

    // Reuse the creative-dialog focus return contract for both standard
    // confirmation and notification modals. Invoking controls may become
    // disabled, hidden, or detached before the modal closes.
    internal static async Task<TResult> ShowWithFocusReturnAsync<TResult>(
        Window owner,
        Window dialog)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dialog);

        var focusReturn =
            owner.FocusManager?.GetFocusedElement()
                as Control;
        try
        {
            return await dialog.ShowDialog<TResult>(owner);
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

    internal static Size ResolveDialogSizeForSmoke(
        double baseHeight)
    {
        var scale =
            Math.Clamp(
                LumineVisualMetrics.TextScaleFactor,
                1,
                2.25);
        return new Size(
            Math.Min(
                640,
                500
                + ((scale - 1) * 120)),
            Math.Min(
                520,
                baseHeight
                + ((scale - 1) * 64)));
    }

    internal static Window CreatePreviewForSmoke(
        ProductDialogTone tone,
        bool notification)
    {
        var title =
            notification
                ? "処理が完了しました"
                : "元ファイルを削除しますか？";
        var dialog =
            CreateDialog(
                title,
                notification
                    ? 240
                    : 340);
        var panel =
            CreateContent(
                title,
                notification
                    ? "変更内容を保存しました。"
                    : "3件の元画像ファイルをディスクから削除します。",
                notification
                    ? null
                    : "この操作はLumineから元に戻せません。整理情報には削除前の参照が残る場合があります。",
                tone);

        if (notification)
        {
            panel.Children.Add(
                CreateButtons(
                    LumineDesign.ConfigureSecondaryButton(
                        new Button
                        {
                            Content = "OK",
                            IsDefault = true,
                            IsCancel = true
                        })));
        }
        else
        {
            panel.Children.Add(
                CreateButtons(
                    LumineDesign.ConfigureSecondaryButton(
                        new Button
                        {
                            Content = "キャンセル",
                            IsCancel = true
                        }),
                    tone == ProductDialogTone.Danger
                        ? LumineDesign.ConfigureDangerButton(
                            new Button
                            {
                                Content = "元ファイルを削除",
                                IsDefault = true
                            })
                        : LumineDesign.ConfigurePrimaryButton(
                            new Button
                            {
                                Content = "実行",
                                IsDefault = true
                            })));
        }

        dialog.Content = panel;
        return dialog;
    }

    private static Window CreateDialog(
        string title,
        double height)
    {
        var resolved =
            ResolveDialogSizeForSmoke(height);
        var dialog =
            new Window
            {
                Title = title,
                Icon = LumineDesign.CreateWindowIcon(),
                Width = resolved.Width,
                Height = resolved.Height,
                MinWidth = 380,
                MinHeight = 220,
                MaxWidth = 680,
                MaxHeight = 540,
                CanResize = true,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner
            };
        dialog.Classes.Add(
            "lumine-dialog-window");
        return dialog;
    }

    private static StackPanel CreateContent(
        string title,
        string description,
        string? detail,
        ProductDialogTone tone)
    {
        var panel =
            new StackPanel();
        panel.Classes.Add(
            "lumine-dialog-content");

        var symbolGlyph =
            new TextBlock
            {
                Text =
                    tone == ProductDialogTone.Danger
                        ? "!"
                        : "i",
                HorizontalAlignment =
                    HorizontalAlignment.Center,
                VerticalAlignment =
                    VerticalAlignment.Center
            };
        symbolGlyph.Classes.Add(
            "lumine-dialog-symbol-glyph");
        if (tone == ProductDialogTone.Danger)
        {
            symbolGlyph.Classes.Add(
                "danger");
        }

        var symbol =
            new Border
            {
                HorizontalAlignment =
                    HorizontalAlignment.Left,
                Child = symbolGlyph
            };
        symbol.Classes.Add(
            "lumine-dialog-symbol");
        if (tone == ProductDialogTone.Danger)
        {
            symbol.Classes.Add(
                "danger");
        }
        panel.Children.Add(symbol);

        var titleText =
            new TextBlock
            {
                Text = title,
                TextWrapping = TextWrapping.Wrap
            };
        titleText.Classes.Add(
            "lumine-dialog-title");
        panel.Children.Add(
            titleText);

        var descriptionText =
            new TextBlock
            {
                Text = description,
                TextWrapping =
                    TextWrapping.Wrap
            };
        descriptionText.Classes.Add(
            "lumine-dialog-description");
        panel.Children.Add(
            descriptionText);

        if (!string.IsNullOrWhiteSpace(detail))
        {
            var detailText =
                new TextBlock
                {
                    Text = detail,
                    TextWrapping =
                        TextWrapping.Wrap
                };
            detailText.Classes.Add(
                "lumine-dialog-detail-text");

            var detailSurface =
                new Border
                {
                    MaxHeight =
                        Math.Min(
                            180,
                            120
                            * Math.Max(
                                1,
                                LumineVisualMetrics.TextScaleFactor)),
                    Child =
                        new ScrollViewer
                        {
                            Content =
                                detailText
                        }
                };
            detailSurface.Classes.Add(
                "lumine-dialog-detail");
            panel.Children.Add(
                detailSurface);
        }

        return panel;
    }

    private static StackPanel CreateButtons(
        params Button[] buttons)
    {
        var panel =
            new StackPanel();
        panel.Classes.Add(
            "lumine-dialog-actions");

        foreach (var button in buttons)
        {
            panel.Children.Add(button);
        }

        return panel;
    }

}
