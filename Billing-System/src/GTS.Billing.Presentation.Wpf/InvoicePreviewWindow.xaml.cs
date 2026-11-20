using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using GTS.Billing.Application;
using GTS.Billing.Printing;

namespace GTS.Billing.Presentation.Wpf;

public partial class InvoicePreviewWindow : Window
{
    private readonly InvoicePrintModel model;
    private DocumentPaginator paginator = null!;
    private InvoicePrintModel renderedModel = null!;
    private readonly Func<bool,string?,Task<SalesResult>> authorize;
    private readonly bool canPrint;
    private readonly bool canExportPdf;
    private bool busy;
    private int index;
    private double zoom=1;
    private FitMode fit=FitMode.Page;
    private enum FitMode { None, Width, Page }
    private sealed record CopyOption(string Name, string Label);

    internal InvoicePreviewWindow(InvoicePrintModel model,bool canPrint,bool canExportPdf,bool canChooseTemplate,
        Func<bool,string?,Task<SalesResult>> authorize)
    {
        this.model=model;
        this.canPrint=canPrint;
        this.canExportPdf=canPrint && canExportPdf;
        this.authorize=authorize;
        InitializeComponent();
        Owner=System.Windows.Application.Current.MainWindow;
        Title="Invoice print preview — "+model.Number;
        TemplateChoice.ItemsSource=canChooseTemplate?InvoiceTemplates.Selectable.ToArray():new[]{model.Template};
        TemplateChoice.IsEnabled=canChooseTemplate;
        var capturedCopy=model.Seller.Presentation.Options?.Printing.CopyLabel;
        CopyChoice.ItemsSource=new[] { capturedCopy ?? "", "", "Original for Recipient", "Duplicate for Transporter",
            "Triplicate for Supplier" }.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(label=>new CopyOption(string.IsNullOrWhiteSpace(label)?"Unspecified":label,label)).ToArray();
        CopyChoice.SelectedIndex=0;
        TemplateChoice.SelectedItem=model.Template;
        Render(model.Template);
        PrintButton.IsEnabled=canPrint;
        Notice.Text="Direct PDF export is unavailable. Microsoft Print to PDF requires the installed Windows driver and PDF export permission.";
        Loaded+=(_,_)=>ApplyFit();
        Closing+=(_,e)=>e.Cancel=busy;
    }

    private void ChangePresentation(object sender,SelectionChangedEventArgs e)
    {
        if(!IsLoaded || busy) return;
        var template=TemplateChoice.SelectedItem as InvoiceTemplate ?? model.Template;
        try {Render(template);}
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.IO.IOException or NotSupportedException)
        {
            PrintButton.IsEnabled=PdfButton.IsEnabled=false;
            Notice.Text="This template cannot lay out the saved invoice. Select another template.";
        }
    }

    private void Render(InvoiceTemplate template)
    {
        var selection=model with {Template=template};
        var next=InvoiceDocumentRenderer.Render(selection,(CopyChoice.SelectedItem as CopyOption)?.Label,preview:true).DocumentPaginator;
        next.ComputePageCount();
        renderedModel=selection;
        paginator=next;
        Page.DocumentPaginator=paginator;
        Page.Width=paginator.PageSize.Width;
        Page.Height=paginator.PageSize.Height;
        index=0;
        PrintButton.IsEnabled=canPrint;
        PdfButton.IsEnabled=canExportPdf;
        Notice.Text="Layout: "+template.Name+". For PDF export, choose Microsoft Print to PDF in the print dialog.";
        ShowPage();
        ApplyFit();
    }

    private void ShowPage()
    {
        Page.PageNumber=index;
        PageLabel.Text=$"Page {index+1} of {paginator.PageCount}";
        PreviousButton.IsEnabled=index>0;
        NextButton.IsEnabled=index+1<paginator.PageCount;
        Viewport.ScrollToTop();
    }
    private void Previous(object sender,RoutedEventArgs e) {if(index>0){index--;ShowPage();}}
    private void Next(object sender,RoutedEventArgs e) {if(index+1<paginator.PageCount){index++;ShowPage();}}
    private void ZoomIn(object sender,RoutedEventArgs e) {fit=FitMode.None;SetZoom(zoom*1.2);}
    private void ZoomOut(object sender,RoutedEventArgs e) {fit=FitMode.None;SetZoom(zoom/1.2);}
    private void FitWidth(object sender,RoutedEventArgs e) {fit=FitMode.Width;ApplyFit();}
    private void FitPage(object sender,RoutedEventArgs e) {fit=FitMode.Page;ApplyFit();}
    private void ViewportChanged(object sender,SizeChangedEventArgs e)=>ApplyFit();
    private void ApplyFit()
    {
        if(fit==FitMode.None || Viewport is null || paginator is null || !IsLoaded) return;
        var byWidth=(Viewport.ActualWidth-38)/paginator.PageSize.Width;
        var byHeight=(Viewport.ActualHeight-28)/paginator.PageSize.Height;
        SetZoom(fit==FitMode.Width?byWidth:Math.Min(byWidth,byHeight));
    }
    private void SetZoom(double value)
    {
        zoom=Math.Clamp(value,.15,4);
        PageFrame.LayoutTransform=new ScaleTransform(zoom,zoom);
    }

    private async void Print(object sender,RoutedEventArgs e)=>await SubmitPrintAsync(false);
    private async void ExportPdf(object sender,RoutedEventArgs e)=>await SubmitPrintAsync(true);

    private async Task SubmitPrintAsync(bool pdfOnly)
    {
        if(!PrintButton.IsEnabled || pdfOnly && !PdfButton.IsEnabled || busy) return;
        busy=true;
        PrintButton.IsEnabled=PdfButton.IsEnabled=false;
        TemplateChoice.IsEnabled=CopyChoice.IsEnabled=false;
        try
        {
            var capture=model.Seller.Presentation.Options?.Printing ??
                new InvoicePrintOptions(null,"A4","Portrait",1,true,true,false,12,12,12,12,6,6,"");
            var options=capture with {PaperSize=renderedModel.Template.Paper,Orientation=renderedModel.Template.Orientation};
            var documentTicket=InvoicePrintPreferences.BuildTicket(options);
            var dialog=new PrintDialog {UserPageRangeEnabled=false,SelectedPagesEnabled=false,CurrentPageEnabled=false,
                PrintTicket=documentTicket};
            if(dialog.ShowDialog()!=true) return;
            var queue=dialog.PrintQueue;
            if(queue is null)
            {
                Notice.Text="Choose an available Windows printer.";
                return;
            }
            documentTicket.CopyCount=Math.Clamp(dialog.PrintTicket?.CopyCount??options.Copies,1,5);
            var validated=queue.MergeAndValidatePrintTicket(dialog.PrintTicket??queue.DefaultPrintTicket,documentTicket).ValidatedPrintTicket;
            var capabilities=queue.GetPrintCapabilities(validated);
            if(validated.PageMediaSize?.PageMediaSizeName!=documentTicket.PageMediaSize?.PageMediaSizeName ||
                validated.PageOrientation!=documentTicket.PageOrientation ||
                validated.CopyCount!=documentTicket.CopyCount ||
                capabilities.PageImageableArea is { } area && WouldClip(area))
            {
                Notice.Text="The selected printer cannot preserve the invoice's paper size, orientation, margins or copy count. Choose another printer.";
                return;
            }
            var isPdf=queue.Name.Contains("PDF",StringComparison.OrdinalIgnoreCase) ||
                queue.QueueDriver?.Name.Contains("PDF",StringComparison.OrdinalIgnoreCase)==true;
            if(pdfOnly && !isPdf)
            {
                Notice.Text="Select Microsoft Print to PDF or another approved PDF printer.";
                return;
            }
            var authorization=await authorize(isPdf,renderedModel.Template.Code==model.Template.Code?null:renderedModel.Template.Code);
            if(authorization.Outcome!=CompanyOutcome.Success || authorization.PrintModel?.Number!=model.Number ||
                authorization.PrintModel.Template.Code!=renderedModel.Template.Code)
            {
                Notice.Text=SalesInvoiceViewModel.Error(authorization);
                if(Notice.Text.Length==0) Notice.Text="The invoice could not be authorized for printing.";
                return;
            }
            dialog.PrintTicket=validated;
            var printDocument=InvoiceDocumentRenderer.Render(renderedModel,(CopyChoice.SelectedItem as CopyOption)?.Label);
            dialog.PrintDocument(printDocument.DocumentPaginator,"GTS Invoice "+model.Number);
            Notice.Text="The invoice was submitted to the Windows print queue.";
        }
        catch(Exception error) when(error is PrintSystemException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {Notice.Text="Windows could not submit the invoice to the selected printer. Check the queue and try again.";}
        finally
        {
            busy=false;
            TemplateChoice.IsEnabled=TemplateChoice.Items.Count>1;
            CopyChoice.IsEnabled=true;
            PrintButton.IsEnabled=canPrint;
            PdfButton.IsEnabled=canExportPdf;
        }
    }

    private bool WouldClip(PageImageableArea area)
    {
        var options=renderedModel.Seller.Presentation.Options?.Printing;
        double Dip(int? mm)=>InvoiceDocumentRenderer.MarginDip(renderedModel,mm);
        var size=paginator.PageSize;
        const double tolerance=2;
        return area.OriginWidth>Dip(options?.LeftMm)+tolerance || area.OriginHeight>Dip(options?.TopMm)+tolerance ||
            area.OriginWidth+area.ExtentWidth<size.Width-Dip(options?.RightMm)-tolerance ||
            area.OriginHeight+area.ExtentHeight<size.Height-Dip(options?.BottomMm)-tolerance;
    }
}
