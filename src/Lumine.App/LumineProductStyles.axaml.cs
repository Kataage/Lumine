using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Lumine.App;

internal sealed partial class LumineProductStyles
    : Styles
{
    public LumineProductStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
