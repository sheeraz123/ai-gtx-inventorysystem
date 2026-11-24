using System.ComponentModel;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using GTS.Billing.Application;
using GTS.Billing.Printing;

namespace GTS.Billing.Presentation.Wpf;

public partial class EnvelopePrintingView : UserControl
{
    private EnvelopePrintingViewModel? viewModel;
    private ApplicationSettingsService? settings;
    private EnvelopePrintService? printer;
    private Action? close;
    private bool busy,fit=true;
    private double zoom=1;
    private EnvelopePage? preview;

    public EnvelopePrintingView()=>InitializeComponent();

    internal void Configure(EnvelopePrintingViewModel model,ApplicationSettingsService? settingsService,
        EnvelopePrintService? printService,Action navigateBack)
    {
        DataContext=viewModel=model;
        settings=settingsService;
        printer=printService;
        close=navigateBack;
        model.PropertyChanged+=ModelChanged;
        model.PreviewChanged+=RefreshPreview;
        SettingsDisplay.Instance.Changed+=(_,_)=>{if(IsVisible && model.CanPrint) model.LoadConfiguration();};
    }

    private void ModelChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(viewModel is not { } model) return;
        if(args.PropertyName==nameof(model.Fields))
        {
            RecipientFields.Children.Clear();
            foreach(var input in model.Fields.Where(x=>x.Field.Visible))
            {
                var multiline=input.Key=="Street";
                var text=new TextBox {IsReadOnly=!input.Field.Editable,MaxLength=multiline?600:200,
                    MinHeight=30,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,
                    Margin=new Thickness(0,0,0,5)};
                text.SetBinding(TextBox.TextProperty,new Binding(nameof(EnvelopeInput.Value))
                    {Source=input,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
                RecipientFields.Children.Add(new Label {Content=input.Field.Label+(input.Field.Required?" *":""),
                    Target=text,Padding=new Thickness(0,3,0,4)});
                RecipientFields.Children.Add(text);
            }
        }
        UpdatePrintButton();
    }

    private void RefreshPreview()
    {
        if(viewModel is not { } model) return;
        if(!IsVisible || model.Fields.Count==0){preview=null;PreviewPage.Content=null;UpdatePrintButton();return;}
        preview=EnvelopeDocumentRenderer.Render(model.Media,model.Lines());
        var size=preview.Document.DocumentPaginator.PageSize;
        var page=preview.Document.Pages[0].Child;
        page.Measure(size);
        page.Arrange(new Rect(size));
        PreviewPage.Content=new Rectangle {Width=size.Width,Height=size.Height,Fill=new VisualBrush(page)};
        PreviewStatus.Text=!EnvelopeConfiguration.Calibrated(model.Media)?"Illustrative preview. Set the physical size and position to print.":
            !preview.Fits?"The recipient text exceeds the configured block. Shorten it or adjust the layout.":"Recipient text only";
        ApplyZoom();
        UpdatePrintButton();
    }

    private void UpdatePrintButton()=>PrintButton.IsEnabled=!busy && printer is not null &&
        viewModel is {CanPrint:true,Selected:not null} model &&
        EnvelopeConfiguration.Calibrated(model.Media) && preview?.Fits==true;

    private void ZoomOut(object sender,RoutedEventArgs args){fit=false;zoom=Math.Max(.25,zoom/1.2);ApplyZoom();}
    private void ZoomIn(object sender,RoutedEventArgs args){fit=false;zoom=Math.Min(4,zoom*1.2);ApplyZoom();}
    private void FitPreview(object sender,RoutedEventArgs args){fit=true;ApplyZoom();}
    private void PreviewSizeChanged(object sender,SizeChangedEventArgs args){if(fit) ApplyZoom();}
    private void ApplyZoom()
    {
        if(preview is null) return;
        var size=preview.Document.DocumentPaginator.PageSize;
        var scale=fit?Math.Max(.1,Math.Min((PreviewScroll.ActualWidth-25)/size.Width,(PreviewScroll.ActualHeight-25)/size.Height)):zoom;
        PreviewPage.LayoutTransform=new ScaleTransform(scale,scale);
    }

    private void CloseClicked(object sender,RoutedEventArgs args)=>close?.Invoke();

    private void CustomerSelected(object sender,SelectionChangedEventArgs args)
    {
        if(viewModel is { } model && CustomerChoice.SelectedItem is EnvelopeCustomer customer)
            model.Selected=customer;
    }

    private async void PrintClicked(object sender,RoutedEventArgs args)
    {
        if(viewModel is not {Company:Guid company,Selected:{ } customer} model || busy) return;
        if(model.ValidateRecipient() is { } error){model.Message=error;return;}
        await PrintAsync(model.Media,model.Lines(),company,customer.Id);
    }

    private async Task<string?> PrintAsync(EnvelopeMedia media,IEnumerable<(EnvelopeField Field,string Text)> lines,
        Guid company,Guid customer)
    {
        if(viewModel is not { } model || printer is null || busy) return null;
        busy=true;Actions.IsEnabled=false;
        try
        {
            var page=EnvelopeDocumentRenderer.Render(media,lines);
            var result=await printer.PrintAsync(media,page,()=>model.AuthorizeAsync(company,customer,media));
            if(result is not null) model.Message=result;
            return result;
        }
        catch(Exception error) when(error is PrintSystemException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return model.Message="Windows could not submit the envelope. Check the printer and paper settings.";
        }
        finally {busy=false;Actions.IsEnabled=true;UpdatePrintButton();}
    }

    private async void SettingsClicked(object sender,RoutedEventArgs args)
    {
        if(viewModel is not {CanConfigure:true,Company:Guid company} model || settings is null) return;
        var settingsService=settings;
        var listed=await Task.Run(()=>settingsService.ExecuteAsync(new(SettingsAction.List,SettingScope.Company,company,
            EnvelopeConfiguration.Section)));
        if(model.Company!=company || !model.CanConfigure) return;
        if(listed.Outcome!=CompanyOutcome.Success || listed.Entries is null)
        {model.Message=listed.Message??"Envelope settings could not be loaded.";return;}
        EnvelopeSettingsEditor.Show(Window.GetWindow(this),listed.Entries,async changes=>
        {
            var result=await Task.Run(()=>settingsService.ExecuteAsync(new(SettingsAction.Save,SettingScope.Company,company,
                EnvelopeConfiguration.Section,changes)));
            if(result.Outcome!=CompanyOutcome.Success) return result.Message??"Envelope settings could not be saved.";
            var runtime=await Task.Run(()=>settingsService.ExecuteAsync(new(SettingsAction.Runtime)));
            if(runtime.Runtime is { } resolved && runtime.CurrentCompanyId==company && model.Company==company)
                SettingsDisplay.Instance.Apply(resolved);
            model.LoadConfiguration();
            return null;
        },media=>PrintAsync(media,[(new EnvelopeField("Calibration",0,Bold:true),"+ Recipient block origin"),
            (new EnvelopeField("Width",1),"Envelope alignment guide")],company,Guid.Empty));
    }
}
