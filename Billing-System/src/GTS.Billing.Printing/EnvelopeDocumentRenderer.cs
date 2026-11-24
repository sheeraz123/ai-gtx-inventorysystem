using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GTS.Billing.Application;

namespace GTS.Billing.Printing;

public sealed record EnvelopePage(FixedDocument Document,Rect TextBounds,bool Fits);

public static class EnvelopeDocumentRenderer
{
    public static double Dip(double mm)=>mm*96/25.4;

    public static EnvelopePage Render(EnvelopeMedia media,IEnumerable<(EnvelopeField Field,string Text)> lines)
    {
        var calibrated=EnvelopeConfiguration.Calibrated(media);
        var size=calibrated?new Size(Dip(media.Width),Dip(media.Height)):new Size(550,285);
        var bounds=calibrated?new Rect(Dip(media.X),Dip(media.Y),Dip(media.TextWidth),Dip(media.TextHeight)):new Rect(40,34,470,217);
        var block=new StackPanel {Width=bounds.Width};
        foreach(var (field,value) in lines)
        {
            if(string.IsNullOrWhiteSpace(value)) continue;
            block.Children.Add(new TextBlock {Text=field.Prefix+value,Foreground=Brushes.Black,
                FontFamily=new FontFamily(media.Font),FontSize=media.Size*96/72,
                FontWeight=field.Bold?FontWeights.Bold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,
                TextAlignment=Enum.Parse<TextAlignment>(media.Alignment),Margin=new Thickness(0,0,0,Dip(media.LineGap))});
        }
        block.Measure(new Size(bounds.Width,double.PositiveInfinity));
        var fits=block.DesiredSize.Height<=bounds.Height;
        var page=new FixedPage {Width=size.Width,Height=size.Height,Background=Brushes.White,ClipToBounds=true};
        var region=new Border {Width=bounds.Width,Height=bounds.Height,ClipToBounds=true,Child=block};
        FixedPage.SetLeft(region,bounds.X);
        FixedPage.SetTop(region,bounds.Y);
        page.Children.Add(region);
        var document=new FixedDocument();
        document.DocumentPaginator.PageSize=size;
        var content=new PageContent {Child=page};
        document.Pages.Add(content);
        return new(document,bounds,fits);
    }
}
