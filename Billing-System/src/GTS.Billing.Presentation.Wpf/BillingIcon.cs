using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace GTS.Billing.Presentation.Wpf;

internal static class BillingIcon
{
    private static readonly FontFamily Symbols=new("Segoe MDL2 Assets");

    internal static void Set(Button button,string label,string glyph)
    {
        AutomationProperties.SetName(button,label.Replace("_","",StringComparison.Ordinal));
        button.Content=new StackPanel {Orientation=Orientation.Horizontal,Children=
        {
            new TextBlock {Text=glyph,FontFamily=Symbols,Margin=new Thickness(0,0,7,0),
                VerticalAlignment=VerticalAlignment.Center},
            new AccessText {Text=label,VerticalAlignment=VerticalAlignment.Center}
        }};
    }
}
