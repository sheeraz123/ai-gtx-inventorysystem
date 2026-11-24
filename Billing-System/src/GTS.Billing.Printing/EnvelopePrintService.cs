using System.Printing;
using System.Windows.Controls;
using GTS.Billing.Application;

namespace GTS.Billing.Printing;

public sealed class EnvelopePrintService
{
    public async Task<string?> PrintAsync(EnvelopeMedia media,EnvelopePage page,Func<Task<CompanyOutcome>> authorize)
    {
        if(!EnvelopeConfiguration.Calibrated(media) || !page.Fits)
            return "Set up the envelope and fit the recipient text inside the printable block first.";
        using var server=new LocalPrintServer();
        var orientation=media.Orientation=="Landscape"?PageOrientation.Landscape:PageOrientation.Portrait;
        var portrait=orientation==PageOrientation.Portrait;
        var width=EnvelopeDocumentRenderer.Dip(portrait?media.Width:media.Height);
        var height=EnvelopeDocumentRenderer.Dip(portrait?media.Height:media.Width);
        var ticket=new PrintTicket {PageMediaSize=new PageMediaSize(width,height),PageOrientation=orientation,
            CopyCount=media.Copies,PageScalingFactor=100};
        var dialog=new PrintDialog {PrintTicket=ticket};
        if(media.Printer.Length>0)
        {
            var preferred=server.GetPrintQueues().FirstOrDefault(queue=>
                string.Equals(queue.FullName,media.Printer,StringComparison.OrdinalIgnoreCase) ||
                string.Equals(queue.Name,media.Printer,StringComparison.OrdinalIgnoreCase));
            if(preferred is not null) dialog.PrintQueue=preferred;
        }
        if(dialog.ShowDialog()!=true) return null;
        var queue=dialog.PrintQueue;
        if(queue is null) return "Choose an available Windows printer.";
        const double tolerance=1;
        var form=queue.GetPrintCapabilities().PageMediaSizeCapability.FirstOrDefault(size=>
            size.Width is double supportedWidth && size.Height is double supportedHeight &&
            Math.Abs(supportedWidth-width)<=tolerance && Math.Abs(supportedHeight-height)<=tolerance);
        if(form is not null) ticket.PageMediaSize=form;
        var validated=queue.MergeAndValidatePrintTicket(dialog.PrintTicket??queue.DefaultPrintTicket,ticket).ValidatedPrintTicket;
        var area=queue.GetPrintCapabilities(validated).PageImageableArea;
        if(validated.PageMediaSize is not {Width:double actualWidth,Height:double actualHeight} ||
            Math.Abs(actualWidth-width)>tolerance || Math.Abs(actualHeight-height)>tolerance ||
            validated.PageOrientation!=orientation || validated.CopyCount!=media.Copies ||
            validated.PageScalingFactor is not (null or 100) || area is null ||
            page.TextBounds.Left<area.OriginWidth-tolerance || page.TextBounds.Top<area.OriginHeight-tolerance ||
            page.TextBounds.Right>area.OriginWidth+area.ExtentWidth+tolerance ||
            page.TextBounds.Bottom>area.OriginHeight+area.ExtentHeight+tolerance)
            return "This printer cannot preserve the envelope size, scale or text position. Check the paper and printer setup.";
        var access=await authorize();
        if(access!=CompanyOutcome.Success)
            return access==CompanyOutcome.SessionRequired?"Your session expired. Sign in again.":"Envelope printing could not be authorized for this company.";
        dialog.PrintTicket=validated;
        dialog.PrintDocument(page.Document.DocumentPaginator,"GTS Envelope");
        return "The envelope was submitted to the Windows print queue.";
    }
}
