using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using GTS.Billing.Application;

namespace GTS.Billing.Presentation.Wpf;

// Lives on the WPF dispatcher. Resets to built-ins on sign-out so personal choices never leak.
public sealed class SettingsDisplay
{
    public static SettingsDisplay Instance { get; }=new();
    public static ResolvedSettings Current { get; private set; }=ResolvedSettings.Empty;
    public event EventHandler<EventArgs>? Changed;
    private readonly List<WeakReference<DataGrid>> grids=[];
    private bool listening;

    public static readonly DependencyProperty LabelKeyProperty=DependencyProperty.RegisterAttached(
        "LabelKey",typeof(string),typeof(SettingsDisplay));
    public static string? GetLabelKey(DependencyObject item)=>item.GetValue(LabelKeyProperty) as string;
    public static void SetLabelKey(DependencyObject item,string key)=>item.SetValue(LabelKeyProperty,key);

    public static readonly DependencyProperty GridIdProperty=DependencyProperty.RegisterAttached(
        "GridId",typeof(string),typeof(SettingsDisplay));
    public static string? GetGridId(DependencyObject item)=>item.GetValue(GridIdProperty) as string;
    public static void SetGridId(DependencyObject item,string id)=>item.SetValue(GridIdProperty,id);

    private static readonly DependencyProperty OriginalLayoutProperty=DependencyProperty.RegisterAttached(
        "OriginalLayout",typeof(GridBaseline),typeof(SettingsDisplay));
    private sealed record GridBaseline(DataGridColumn[] Columns,DataGridLength[] Widths);
    private static readonly Lazy<IReadOnlyDictionary<string,string>> FieldKeys=new(()=>
        SettingsRegistry.Definitions.Where(x=>x.Key.StartsWith("Field.",StringComparison.Ordinal))
            .GroupBy(x=>x.DefaultText,StringComparer.Ordinal).ToDictionary(x=>x.Key,x=>x.First().Key,StringComparer.Ordinal));

    public static string Text(string key,string fallback)=>Current.Text(key,fallback);
    public static string Field(string fallback)=>FieldKeys.Value.TryGetValue(fallback,out var key)?Text(key,fallback):fallback;
    public static string Section(string name)=>Text("Section."+string.Concat(name.Where(char.IsAsciiLetterOrDigit)),name);

    public void Apply(ResolvedSettings settings)
    {
        var app=System.Windows.Application.Current;
        if(app is null) return;
        if(!listening)
        {
            EventManager.RegisterClassHandler(typeof(DataGrid),FrameworkElement.LoadedEvent,new RoutedEventHandler(GridLoaded),true);
            listening=true;
        }
        if(Current.Values.Count!=0 && settings.Values.Count==Current.Values.Count &&
            settings.Values.All(x=>Current.Values.TryGetValue(x.Key,out var current) && current==x.Value)) return;
        Current=settings;
        foreach(var definition in SettingsRegistry.Definitions.Where(x=>x.IsDisplayText || x.Key.StartsWith("App.",StringComparison.Ordinal)))
            app.Resources["Display."+definition.Key]=settings.Text(definition.Key,definition.DefaultText);
        var theme=settings.Get(SettingsRegistry.ShellTheme);
        var dark=theme=="Professional Dark" || theme=="Existing Theme" && settings.Get(SettingsRegistry.Theme)=="Dark";
        var blue=theme=="Modern Blue & White";
        Brush Tone(string light,string night)
        {
            var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(dark?night:light)!;
            brush.Freeze();
            return brush;
        }
        app.Resources["Palette.Canvas"]=Tone(blue?"#EDF4FF":"#F3F5F8","#1E293B");
        app.Resources["Palette.ShellHeader"]=Tone(blue?"#103875":"#15263C","#111B2A");
        app.Resources["Palette.Surface"]=Tone("#FFFFFF","#243247");
        app.Resources["Palette.Text"]=Tone("#172033","#F1F5F9");
        app.Resources["Palette.Muted"]=Tone("#475569","#CBD5E1");
        app.Resources["Palette.GridLine"]=Tone("#CBD5E1","#475569");
        app.Resources["Palette.Alternate"]=Tone("#EAF0F7","#2C3C54");
        app.Resources["Palette.InvoiceHeading"]=Tone("#DCEBFC","#304F75");
        app.Resources["Palette.InvoiceAccent"]=Tone("#174A82","#24578A");
        app.Resources["Display.FontSize"]=13.0*settings.Get(SettingsRegistry.FontScale)/100;
        app.Resources["Display.BodyPadding"]=settings.Get(SettingsRegistry.Density)=="Compact"?new Thickness(10,6,10,6):new Thickness(16,10,16,10);
        foreach(var reference in grids.ToArray())
        {
            if(reference.TryGetTarget(out var grid)) ApplyGrid(grid);
            else grids.Remove(reference);
        }
        Changed?.Invoke(this,EventArgs.Empty);
    }

    private void GridLoaded(object sender,RoutedEventArgs args)
    {
        if(sender is not DataGrid grid || args.OriginalSource!=grid) return;
        if(!grids.Any(x=>x.TryGetTarget(out var item)&&ReferenceEquals(item,grid))) grids.Add(new(grid));
        ApplyGrid(grid);
    }

    private static void ApplyGrid(DataGrid grid)
    {
        if(grid.Columns.Count==0) return;
        var baseline=(GridBaseline?)grid.GetValue(OriginalLayoutProperty);
        if(baseline is null)
        {
            var original=grid.Columns.ToArray();
            baseline=new(original,original.Select(x=>x.Width).ToArray());
            grid.SetValue(OriginalLayoutProperty,baseline);
        }
        var columns=baseline.Columns;
        var id=GetGridId(grid);
        var key=SettingsRegistry.GridLayouts.FirstOrDefault(x=>x.Key=="Grid.Layout."+id);
        var layout=key is null?GridLayout.Default:Current.Get(key);
        var scale=Current.Get(SettingsRegistry.FontScale)/100.0;
        grid.RowHeight=(Current.Get(SettingsRegistry.RowDensity)=="Compact"?25:32)*scale;
        grid.SetResourceReference(Control.ForegroundProperty,"Palette.Text");
        grid.SetResourceReference(Control.BackgroundProperty,"Palette.Surface");
        grid.SetResourceReference(DataGrid.RowBackgroundProperty,"Palette.Surface");
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty,"Palette.GridLine");
        grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty,"Palette.GridLine");
        grid.AlternationCount=Current.Get(SettingsRegistry.AlternateRows)?2:0;
        if(grid.AlternationCount==2) grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,"Palette.Alternate");
        else grid.ClearValue(DataGrid.AlternatingRowBackgroundProperty);

        for(var index=0;index<columns.Length;index++)
        {
            var column=columns[index];
            if(GetLabelKey(column) is { } header && SettingsRegistry.ByKey.TryGetValue(header,out var definition))
                column.Header=Text(header,definition.DefaultText);
            var number=(index+1).ToString(CultureInfo.InvariantCulture);
            var preference=layout.Columns.FirstOrDefault(x=>x.Key==number);
            var property=PropertyName(column);
            var guarded=index==0 || property is "Status" or "Active" or "IsActive" or "IsLocked" or "CreditHold" or "Default"
                or "Row.Status" or "Draft.Active" or "Draft.Default";
            var product=id=="Products";
            var permitted= !product || (property switch
            {
                "Hsn"=>Current.Get(SettingsRegistry.ProductHsn),"Sku"=>Current.Get(SettingsRegistry.ProductSku),
                "Barcode"=>Current.Get(SettingsRegistry.ProductBarcode),_=>true
            });
            if(product && property is not null)
            {
                var field=property switch {"Category"=>"CategoryId","Status"=>"Active","Hsn"=>"TaxClassification",_=>property};
                permitted &= ProductFormConfiguration.EditorFieldVisible(Current,field);
                var baselineCaption=ProductFormConfiguration.IsLegacy(Current)?field switch
                {
                    "Code"=>"Item Code","Name"=>"Item Name","BaseUnit"=>"Unit","SalesRate"=>"Sale Price",
                    "TaxClassification"=>"HSN Code",_=>column.Header?.ToString()??field
                }:column.Header?.ToString()??field;
                column.Header=ProductFormConfiguration.Caption(Current,field,baselineCaption);
            }
            if(id=="Customers" && property=="TradeName")
            {
                permitted=ConfigurationModule.CustomerMode(Current,property)!="Hidden";
                column.Header=ConfigurationModule.CustomerCaption(Current,property,column.Header?.ToString()??"Trade name");
            }
            column.Visibility=(!preference.Hidden || guarded) && permitted?Visibility.Visible:Visibility.Collapsed;
            column.Width=preference.Width is int width?new DataGridLength(width):baseline.Widths[index];
            if(column is DataGridTextColumn text && !string.IsNullOrEmpty(property))
                FormatColumn(text,property,product);
        }
        var ordered=layout.Columns.Select(x=>Index(x.Key,columns.Length)).Where(x=>x>=0)
            .Concat(Enumerable.Range(0,columns.Length)).Distinct().ToArray();
        for(var i=0;i<ordered.Length;i++) columns[ordered[i]].DisplayIndex=i;
        ApplySort(grid,columns,layout);
    }

    private static string? PropertyName(DataGridColumn column)
    {
        if(column is DataGridBoundColumn {Binding:Binding binding}) return binding.Path?.Path;
        return string.IsNullOrEmpty(column.SortMemberPath)?null:column.SortMemberPath;
    }

    private static void FormatColumn(DataGridTextColumn column,string property,bool product)
    {
        var currentBinding=column.Binding as Binding;
        var targetPath=product && property is "BaseUnit" or "SalesUnit"
            ? Current.Get(SettingsRegistry.ProductUnit)=="Sales unit" ? "SalesUnit" : "BaseUnit"
            : property;
        var dates=property is "LastLogin" or "OccurredUtc" or "UpdatedUtc" or "CreatedUtc" or "UpdatedDate";
        var dateOnly=property is "Date" or "EstablishedOn" or "Start" or "End";
        var money=property is "SalesRate" or "Mrp" or "CreditLimit" or "PurchaseRate" or "Taxable" or "Tax" or "Total";
        if(!dates && !dateOnly && !money && targetPath==property) return;
        var companyFormat=Current.Values.ContainsKey(ConfigurationModule.DateFormat.Key);
        var date=companyFormat?Current.Get(ConfigurationModule.DateFormat):Current.Get(SettingsRegistry.DateFormat);
        var time=companyFormat?Current.Get(ConfigurationModule.TimeFormat):Current.Get(SettingsRegistry.TimeFormat);
        var digits=companyFormat?int.Parse(Current.Get(ConfigurationModule.Precision),CultureInfo.InvariantCulture):
            product?Current.Get(SettingsRegistry.ProductRatePlaces):2;
        var numberSpec=(companyFormat || Current.Get(SettingsRegistry.NumberFormat)=="Grouped"?"N":"F")+digits.ToString(CultureInfo.InvariantCulture);
        var format=dates?"{0:"+date+" "+time+"}":dateOnly?"{0:"+date+"}":
            money?(companyFormat?"₹":"")+"{0:"+numberSpec+"}":currentBinding?.StringFormat;
        if(money && !companyFormat && Current.Get(SettingsRegistry.CurrencyFormat)=="Currency code")
        {
            var combined=new MultiBinding {Mode=BindingMode.OneWay,StringFormat="{0:"+numberSpec+"} {1}"};
            combined.Bindings.Add(new Binding(targetPath) {TargetNullValue=""});
            combined.Bindings.Add(new Binding("Currency") {TargetNullValue=""});
            column.SortMemberPath=targetPath;
            column.Binding=combined;
            return;
        }
        var culture=money && companyFormat?CultureInfo.GetCultureInfo(
            Current.Get(ConfigurationModule.Grouping)=="Indian"?"en-IN":"en-US"):null;
        if(currentBinding?.Path?.Path==targetPath && currentBinding.StringFormat==format && Equals(currentBinding.ConverterCulture,culture)) return;
        column.Binding=new Binding(targetPath) { Mode=BindingMode.OneWay,StringFormat=format,TargetNullValue="",ConverterCulture=culture };
    }

    private static int Index(string? number,int count) =>
        int.TryParse(number,NumberStyles.None,CultureInfo.InvariantCulture,out var index)&&index>=1&&index<=count?index-1:-1;

    private static void ApplySort(DataGrid grid,DataGridColumn[] columns,GridLayout layout)
    {
        var index=Index(layout.SortKey,columns.Length);
        if(index<0 || !grid.Items.CanSort || columns[index].Visibility!=Visibility.Visible ||
            PropertyName(columns[index]) is not {Length:>0} property) return;
        var direction=layout.Descending?ListSortDirection.Descending:ListSortDirection.Ascending;
        grid.Items.SortDescriptions.Clear();
        grid.Items.SortDescriptions.Add(new SortDescription(property,direction));
        foreach(var column in columns) column.SortDirection=null;
        columns[index].SortDirection=direction;
    }
}
