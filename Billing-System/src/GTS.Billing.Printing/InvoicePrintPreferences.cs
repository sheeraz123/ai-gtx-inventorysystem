using System.Printing;
using GTS.Billing.Application;

namespace GTS.Billing.Printing;

public static class InvoicePrintPreferences
{
    // The future renderer calls this immediately before offering a print queue.
    // An unavailable queue is never replaced with the system default.
    public static bool IsAvailable(string printer)
    {
        if(string.IsNullOrWhiteSpace(printer)) return false;
        try
        {
            using var server=new LocalPrintServer();
            using var queues=server.GetPrintQueues([EnumeratedPrintQueueTypes.Local,EnumeratedPrintQueueTypes.Connections]);
            foreach(var queue in queues)
            {
                using(queue)
                {
                    if(string.Equals(queue.FullName,printer,StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(queue.Name,printer,StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch(Exception error) when(error is PrintSystemException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
        return false;
    }

    public static PrintTicket BuildTicket(InvoicePrintOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if(options.PaperSize is not ("A4" or "A5") || options.Orientation is not ("Portrait" or "Landscape") || options.Copies is <1 or >5)
            throw new ArgumentException("Unsupported invoice paper, orientation or number of copies.",nameof(options));
        return new PrintTicket {PageMediaSize=new(options.PaperSize=="A5"?PageMediaSizeName.ISOA5:PageMediaSizeName.ISOA4),
            PageOrientation=options.Orientation=="Landscape"?PageOrientation.Landscape:PageOrientation.Portrait,
            CopyCount=options.Copies};
    }
}
