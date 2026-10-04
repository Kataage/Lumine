using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Lumine.App;

internal sealed partial class ColorPickerFluentStyles
    : Styles
{
    public ColorPickerFluentStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
