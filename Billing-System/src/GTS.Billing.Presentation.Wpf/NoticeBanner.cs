using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace GTS.Billing.Presentation.Wpf;

public enum NoticeKind { Automatic, Information, Warning, Error, Success }

public sealed class NoticeBanner : Border
{
    public static readonly DependencyProperty TextProperty=DependencyProperty.Register(nameof(Text),typeof(string),
        typeof(NoticeBanner),new PropertyMetadata("",Update));
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(NoticeKind),
        typeof(NoticeBanner),new PropertyMetadata(NoticeKind.Automatic,Update));

    private readonly TextBlock content=new() {TextWrapping=TextWrapping.Wrap};

    public string Text {get=>(string)GetValue(TextProperty); set=>SetValue(TextProperty,value);}
    public NoticeKind Kind {get=>(NoticeKind)GetValue(KindProperty); set=>SetValue(KindProperty,value);}

    public NoticeBanner()
    {
        CornerRadius=new CornerRadius(4);
        Padding=new Thickness(9,5,9,5);
        Child=content;
        AutomationProperties.SetLiveSetting(content,AutomationLiveSetting.Polite);
        Refresh();
    }

    private static void Update(DependencyObject source,DependencyPropertyChangedEventArgs args)=>((NoticeBanner)source).Refresh();

    private void Refresh()
    {
        var value=Text??"";
        Visibility=string.IsNullOrWhiteSpace(value)?Visibility.Collapsed:Visibility.Visible;
        var kind=Kind==NoticeKind.Automatic ? FromExistingMessage(value) : Kind;
        var (label,icon,background,foreground)=kind switch
        {
            NoticeKind.Error=>("Error","×",0xFEE2E2,0x821D1D),
            NoticeKind.Warning=>("Warning","!",0xFEF3C7,0x783B0C),
            NoticeKind.Success=>("Success","✓",0xDCFCE7,0x14532D),
            _=>("Information","ⓘ",0xDBEAFE,0x173E73)
        };
        Background=Brush(background);
        content.Foreground=Brush(foreground);
        content.Text=$"{icon}  {label} — {value}";
        AutomationProperties.SetName(content,$"{label}. {value}");
    }

    private static Brush Brush(int rgb)
    {
        var brush=new SolidColorBrush(Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb));
        brush.Freeze();
        return brush;
    }

    // Existing modules expose display strings. New result handlers can select a kind directly.
    private static NoticeKind FromExistingMessage(string message)
    {
        var words=message.ToLowerInvariant();
        if(new[]{"failed","could not","unavailable","not authorized","not allowed","does not allow","invalid","denied","expired",
            "incorrect","error","correct the","does not match","locked"}.Any(words.Contains)) return NoticeKind.Error;
        if(new[]{"saved","created","updated","completed","finalized","submitted","activated","deactivated","changed","queued",
            "successfully"}.Any(token=>words.StartsWith(token+" ",StringComparison.Ordinal) ||
                words.TrimEnd('.','!').EndsWith(" "+token,StringComparison.Ordinal) ||
                words.TrimEnd('.','!')==token)) return NoticeKind.Success;
        if(new[]{"choose","select","enter","review","confirm","provide","add at least","wait","no company","no matching",
            "check","cancel","too many"}.Any(prefix=>words.StartsWith(prefix,StringComparison.Ordinal))) return NoticeKind.Warning;
        return NoticeKind.Information;
    }
}
