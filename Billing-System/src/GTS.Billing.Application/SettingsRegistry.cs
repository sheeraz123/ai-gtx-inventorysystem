using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GTS.Billing.Application;

[Flags]
public enum SettingScope { Application=1, Company=2, User=4 }
public enum SettingKind { Text, Choice, Number, Boolean, GridLayout }

public abstract record SettingDefinition(string Key,string Section,string Label,string Description,SettingScope Scopes,
    SettingKind Kind,string? EditPermission,bool AffectsPrint=false,bool RestartRequired=false)
{
    public bool IsSensitive => false;
    public bool IsEditable => Key is not ("Invoice.Label.BillOfSupply" or "Invoice.Label.Proforma" or "Invoice.Label.Quotation" or
        "Invoice.Label.DeliveryChallan" or "Invoice.Label.CreditNote" or "Invoice.Label.DebitNote");
    public bool IsDisplayText => Section==SettingsRegistry.LabelSection;
    public virtual IReadOnlyList<string> Choices => [];
    public abstract string DefaultText { get; }
    public abstract bool TryNormalize(string text,out string normalized);
}

public sealed record SettingKey<T>(string Id,string Group,string Caption,string Help,SettingScope AllowedScopes,
    SettingKind ValueKind,T Default,Func<string,(bool Valid,T Value)> Parse,Func<T,string> Format,
    string? Permission=null,bool Print=false,bool Restart=false,IReadOnlyList<string>? Options=null)
    : SettingDefinition(Id,Group,Caption,Help,AllowedScopes,ValueKind,Permission,Print,Restart)
{
    public override IReadOnlyList<string> Choices => Options ?? [];
    public override string DefaultText => Format(Default);
    public override bool TryNormalize(string text,out string normalized)
    {
        var parsed=Parse(text);
        normalized=parsed.Valid ? Format(parsed.Value) : "";
        return parsed.Valid;
    }
}

public readonly record struct GridColumnPreference(string Key,bool Hidden,int? Width);
public sealed record GridLayout(ImmutableArray<GridColumnPreference> Columns,string? SortKey,bool Descending)
{
    public static GridLayout Default { get; }=new([],null,false);
}

public sealed record ResolvedSettings(ImmutableDictionary<string,string> Values,ImmutableHashSet<string>? ExplicitKeys=null)
{
    public static ResolvedSettings Empty { get; }=new(ImmutableDictionary<string,string>.Empty);
    public T Get<T>(SettingKey<T> key) => Values.TryGetValue(key.Key,out var value) && key.Parse(value) is { Valid:true } parsed
        ? parsed.Value : key.Default;
    public string Text(string key,string fallback) => Values.GetValueOrDefault(key,fallback);
}

public static partial class SettingsRegistry
{
    public const string LabelSection="Text & Labels";
    public const string General="General";
    public const string Appearance="Appearance";
    public const string Branding="Company Branding";
    public const string Bank="Bank & Payments";
    public const string Invoice="Invoice Presentation";
    public const string Sales="Sales Defaults";
    public const string Product="Product & Inventory";
    public const string Gst="GST Presentation";
    public const string Printing="Printing";
    public const string Grid="Grid & Search";
    public const string Personal="User Preferences";
    public const string Security="Security & Diagnostics";

    private const SettingScope Global=SettingScope.Application;
    private const SettingScope Company=SettingScope.Company;
    private const SettingScope PersonalView=Global|SettingScope.User;
    private const SettingScope Presentation=Global|Company|SettingScope.User;
    private const string InvoiceEdit="Settings.InvoiceEdit";
    private const string BrandEdit="Settings.CompanyBrandingEdit";
    private const string BankEdit="Settings.BankEdit";
    private const string PrintEdit="Settings.PrintEdit";
    private const string BusinessEdit="Settings.BusinessDefaultsEdit";

    public static readonly SettingKey<string> AppTitle=Text("App.Title",General,"Application display title","GTS Billing System",Global);
    public static readonly SettingKey<string> Subtitle=Text("App.Subtitle",General,"Application subtitle","Development only",Global);
    public static readonly SettingKey<string> WindowTitle=Text("App.WindowTitle",General,"Main window title","GTS Billing System — Development Foundation",Global);
    public static readonly SettingKey<string> HomeHeading=Text("App.HomeHeading",General,"Dashboard heading","Dashboard",Global);
    public static readonly SettingKey<string> Welcome=Text("App.Welcome",General,"Welcome message","Welcome to GTS Billing.",Global);
    public static readonly SettingKey<string> CompanyDisplay=Choice("App.CompanyDisplay",General,"Company display format","Name",Presentation,["Name","Code and name","Name and code"],BrandEdit);
    public static readonly SettingKey<string> LandingPage=Choice("App.LandingPage",General,"Landing page","Dashboard",PersonalView,
        ["Dashboard","Company Master","Customer Master","Product Category Master","Product Master","Settings"]);
    public static readonly SettingKey<string> DateFormat=Choice("App.DateFormat",General,"Date display format","dd MMM yyyy",PersonalView,["dd MMM yyyy","dd/MM/yyyy","yyyy-MM-dd"]);
    public static readonly SettingKey<string> TimeFormat=Choice("App.TimeFormat",General,"Time display format","HH:mm",PersonalView,["HH:mm","hh:mm tt"]);
    public static readonly SettingKey<string> NumberFormat=Choice("App.NumberFormat",General,"Number display format","Grouped",PersonalView,["Grouped","Plain"]);
    public static readonly SettingKey<string> CurrencyFormat=Choice("App.CurrencyFormat",General,"Currency display format","Amount only",PersonalView,["Amount only","Currency code"]);
    public static readonly SettingKey<string> Language=Choice("App.Language",General,"Interface language","English",PersonalView,["English"]);
    public static readonly SettingKey<string> Theme=Choice("Appearance.Theme",Appearance,"Theme","Light",PersonalView,["Light","Dark"]);
    public static readonly SettingKey<string> ShellTheme=Choice("Appearance.CompanyTheme",Appearance,"Company theme","Existing Theme",
        Company,["Existing Theme","Modern Blue & White","Professional Dark"],BusinessEdit);
    public static readonly SettingKey<string> NavigationPosition=Choice("Appearance.NavigationPosition",Appearance,"Navigation position",
        "TOP_NAVIGATION",Company,["TOP_NAVIGATION","LEFT_NAVIGATION","RIGHT_NAVIGATION"],BusinessEdit);
    public static readonly SettingKey<string> InvoiceListLayout=Choice("SalesList.Layout",SalesListConfiguration.Filters,"Invoice list layout",
        SalesListConfiguration.Modern,Company,[SalesListConfiguration.Classic,SalesListConfiguration.Modern,SalesListConfiguration.Split],InvoiceEdit);
    public static readonly SettingKey<string> InvoiceFilterArrangement=Choice("SalesList.FilterArrangement",SalesListConfiguration.Filters,
        "Filter arrangement","Wrapped",Company,["Wrapped","Stacked"],InvoiceEdit);
    public static readonly SettingKey<string> InvoiceActionArrangement=Choice("SalesList.ActionArrangement",SalesListConfiguration.Actions,
        "Action arrangement","Wrapped",Company,["Wrapped","Stacked"],InvoiceEdit);
    public static readonly SettingKey<string> Density=Choice("Appearance.Density",Appearance,"Interface density","Comfortable",PersonalView,["Comfortable","Compact"]);
    public static readonly SettingKey<int> FontScale=Number("Appearance.FontScale",Appearance,"Font scale (%)",100,90,150,PersonalView);
    public static readonly SettingKey<int> SearchDebounce=Number("Grid.SearchDebounce",Grid,"Search debounce (ms)",300,200,1000,PersonalView,
        help:"The three-character trigger remains fixed.");
    public static readonly SettingKey<int> PageSize=Number("Grid.PageSize",Grid,"Rows per page",50,10,100,PersonalView);
    public static readonly SettingKey<bool> AlternateRows=Flag("Grid.AlternateRows",Grid,"Alternate row shading",false,PersonalView);
    public static readonly SettingKey<string> RowDensity=Choice("Grid.RowDensity",Grid,"Grid row density","Comfortable",PersonalView,["Comfortable","Compact"]);
    public static readonly SettingKey<string> InvoiceTagline=Text("Invoice.Tagline",Branding,"Invoice tagline","",Company,BrandEdit,200,true);
    public static readonly SettingKey<string> PrintedCompanyName=Choice("Invoice.CompanyName",Branding,"Company name preference","Legal name",Company,["Legal name","Display name"],BrandEdit,true);
    public static readonly SettingKey<bool> ShowBank=Flag("Invoice.ShowBankDetails",Bank,"Show the selected default bank account",true,Company,BankEdit,true);
    public static readonly SettingKey<bool> ShowIfsc=Flag("Invoice.ShowIFSC",Bank,"Show IFSC",true,Company,BankEdit,true);
    public static readonly SettingKey<bool> ShowBranch=Flag("Invoice.ShowBranch",Bank,"Show bank branch",true,Company,BankEdit,true);
    public static readonly SettingKey<bool> ShowAccountHolder=Flag("Invoice.ShowAccountHolder",Bank,"Show account holder",true,Company,BankEdit,true);
    public static readonly SettingKey<bool> ShowAccountNumber=Flag("Invoice.ShowAccountNumber",Bank,"Show account number",true,Company,BankEdit,true);
    public static readonly SettingKey<bool> ShowPaymentInstructions=Flag("Invoice.ShowPaymentInstructions",Bank,"Show payment instructions",true,Company,BankEdit,true);
    public static readonly SettingKey<string> InvoiceDateFormat=Choice("Invoice.DateFormat",Invoice,"Invoice date format","dd MMM yyyy",Company,["dd MMM yyyy","dd/MM/yyyy","yyyy-MM-dd"],InvoiceEdit,true);
    public static readonly SettingKey<int> InvoiceQuantity=Number("Invoice.QuantityPlaces",Invoice,"Quantity display decimals",2,0,4,Company,InvoiceEdit,true);
    public static readonly SettingKey<int> InvoiceRate=Number("Invoice.RatePlaces",Invoice,"Rate display decimals",2,0,4,Company,InvoiceEdit,true);
    public static readonly SettingKey<int> InvoiceAmount=Number("Invoice.AmountPlaces",Invoice,"Amount display decimals",2,2,4,Company,InvoiceEdit,true);
    public static readonly SettingKey<string> InvoiceCurrency=Choice("Invoice.CurrencySymbol",Invoice,"Currency display","₹",Company,["₹","INR","No symbol"],InvoiceEdit,true);
    public static readonly SettingKey<bool> Thousands=Flag("Invoice.GroupThousands",Invoice,"Group thousands",true,Company,InvoiceEdit,true);
    public static readonly SettingKey<string> CustomerSelection=Choice("Sales.CustomerSelection",Sales,"Customer selection preference","Search active customers",Company,["Search active customers","Recent eligible customers"],BusinessEdit);
    public static readonly SettingKey<string> BillingAddress=Choice("Sales.BillingAddress",Sales,"Billing address selection","Prefer customer default",Company,["Prefer customer default","Require explicit selection"],BusinessEdit);
    public static readonly SettingKey<string> ShippingAddress=Choice("Sales.ShippingAddress",Sales,"Shipping address selection","Prefer customer default",Company,["Prefer customer default","Require explicit selection"],BusinessEdit);
    public static readonly SettingKey<string> PaymentTerms=Text("Sales.PaymentTerms",Sales,"Default payment terms","",Company,BusinessEdit,500,true);
    public static readonly SettingKey<int> CreditDays=Number("Sales.CreditDays",Sales,"Default customer credit days",0,0,36500,Company,BusinessEdit);
    public static readonly SettingKey<bool> CreditWarning=Flag("Sales.CreditHoldWarning",Sales,"Show credit hold guidance",true,Company,BusinessEdit);
    public static readonly SettingKey<bool> InactiveWarning=Flag("Sales.InactiveWarning",Sales,"Show inactive customer guidance",true,Company,BusinessEdit);
    public static readonly SettingKey<bool> RequirePo=Flag("Sales.RequirePo",Sales,"Default customer PO requirement",false,Company,BusinessEdit);
    public static readonly SettingKey<string> InvoiceRemarks=Text("Sales.InvoiceRemarks",Sales,"Default invoice remarks","",Company,BusinessEdit,500,true);
    public static readonly SettingKey<string> FooterNotes=Text("Sales.FooterNotes",Sales,"Default invoice footer notes","",Company,BusinessEdit,500,true);
    public static readonly SettingKey<string> ProductType=Choice("Product.DefaultType",Product,"Default product type","Goods",Company,["Goods","Services"],BusinessEdit);
    public static readonly SettingKey<int> QuantityPlaces=Number("Product.QuantityPlaces",Product,"Default product quantity precision",0,0,4,Company,BusinessEdit);
    public static readonly SettingKey<int> ProductRatePlaces=Number("Product.RatePlaces",Product,"Product rate display precision",2,0,4,Company,BusinessEdit);
    public static readonly SettingKey<string> ProductSearch=Choice("Product.SearchFields",Product,"Product text search fields","All listed fields",Company,
        ["All listed fields","Code and names","Code, SKU and barcode"],BusinessEdit);
    public static readonly SettingKey<bool> ProductSku=Flag("Product.ShowSku",Product,"Show SKU in supported views",false,Company,BusinessEdit);
    public static readonly SettingKey<bool> ProductBarcode=Flag("Product.ShowBarcode",Product,"Show barcode in supported views",false,Company,BusinessEdit);
    public static readonly SettingKey<bool> ProductHsn=Flag("Product.ShowHsn",Product,"Show HSN/SAC in supported views",true,Company,BusinessEdit);
    public static readonly SettingKey<bool> ProductDescription=Flag("Product.ShowDescription",Product,"Show product description",true,Company,BusinessEdit);
    public static readonly SettingKey<bool> ProductImage=Flag("Product.ShowImage",Product,"Show product image preview",true,Company,BusinessEdit);
    public static readonly SettingKey<string> ProductUnit=Choice("Product.UnitDisplay",Product,"Product list unit","Base unit",Company,["Base unit","Sales unit"],BusinessEdit);
    public static readonly SettingKey<bool> ReorderWarning=Flag("Stock.ReorderWarning",Product,"Reorder guidance preference",true,Company,BusinessEdit,help:"Reserved for a future stock source.");
    public static readonly SettingKey<bool> LowStockWarning=Flag("Stock.LowStockWarning",Product,"Low stock guidance preference",true,Company,BusinessEdit,help:"Reserved for a future stock source.");
    public static readonly SettingKey<int> TaxPlaces=Number("Invoice.TaxPlaces",Gst,"Tax display decimals",2,2,4,Company,InvoiceEdit,true);
    public static readonly SettingKey<string> TaxSummary=Choice("Invoice.TaxSummaryStyle",Gst,"Tax summary layout","Summary",Company,["Summary","Summary and optional columns"],InvoiceEdit,true);
    public static readonly SettingKey<string> Printer=Text("Print.Printer",Printing,"Preferred printer","",Company,PrintEdit,200,true,help:"Blank asks for printer selection at print time.");
    public static readonly SettingKey<string> Paper=Choice("Print.Paper",Printing,"Paper size","A4",Company,["A4"],PrintEdit,true);
    public static readonly SettingKey<string> InvoiceTemplate=Choice("Print.SalesInvoiceTemplate",Printing,
        "Default sales invoice template",InvoiceTemplates.Modern,Company,
        InvoiceTemplates.Selectable.Select(template=>template.Code).ToArray(),"InvoiceTemplate.Manage",true,
        InvoiceTemplates.ForNewInvoice);
    public static readonly SettingKey<string> Orientation=Choice("Print.Orientation",Printing,"Orientation","Portrait",Company,["Portrait","Landscape"],PrintEdit,true);
    public static readonly SettingKey<int> Copies=Number("Print.Copies",Printing,"Copies",1,1,5,Company,PrintEdit,true);
    public static readonly SettingKey<bool> Preview=Flag("Print.ShowPreview",Printing,"Show print preview",true,Company,PrintEdit,true);
    public static readonly SettingKey<bool> PageNumbers=Flag("Print.PageNumbers",Printing,"Show page numbers",true,Company,PrintEdit,true);
    public static readonly SettingKey<bool> PrintTime=Flag("Print.PrintTime",Printing,"Show print date and time",false,Company,PrintEdit,true);
    public static readonly SettingKey<string> CopyLabel=Text("Print.CopyLabel",Printing,"Print copy label","",Company,PrintEdit,80,true);
    public static readonly ImmutableArray<SettingKey<int>> PrintMargins=new[]{"Top","Right","Bottom","Left"}.Select(side=>
        Number("Print.Margin"+side,Printing,side+" margin (mm)",12,5,35,Company,PrintEdit,true)).ToImmutableArray();
    public static readonly SettingKey<int> HeaderSpacing=Number("Print.HeaderSpacing",Printing,"Header spacing (mm)",6,0,20,Company,PrintEdit,true);
    public static readonly SettingKey<int> FooterSpacing=Number("Print.FooterSpacing",Printing,"Footer spacing (mm)",6,0,20,Company,PrintEdit,true);

    public static readonly ImmutableArray<SettingKey<bool>> OptionalSections=new[]{
        ("CompanyContact","Company contact details"),("CustomerContact","Customer contact details"),("OptionalShipping","Optional shipping section"),
        ("ProductDescription","Product description"),("SkuBarcode","SKU / barcode"),("OptionalDiscount","Optional discount column"),
        ("FooterNotes","Footer notes"),("Signatory","Optional signatory block"),("Transport","Transport and order details"),("OptionalTaxColumns","Optional tax columns"),
        ("OptionalHsn","HSN/SAC when optional"),("OptionalGstin","GSTIN when optional"),
        ("Iec","IEC when captured"),("CompanyAddress","company address"),("CompanyPhone","company phone"),
        ("CompanyEmail","company email"),("Terms","terms and conditions")
    }.Select(item=>Flag("Invoice.Optional."+item.Item1,Invoice,"Show "+item.Item2,true,Company,InvoiceEdit,true,
        help:"A document renderer must still include mandatory fields.")).ToImmutableArray();

    public static readonly IReadOnlyList<(string Id,string Caption)> GridIds=Array.AsReadOnly<(string,string)>(
    [
        ("Companies","Companies"),("Customers","Customers"),("Categories","Product categories"),("Products","Products"),
        ("Users","Users"),("Roles","Roles"),("Logos","Company logos"),("Banks","Company bank accounts"),("Gst","GST registrations")
    ]);
    public static readonly ImmutableArray<SettingKey<GridLayout>> GridLayouts=GridIds.Select(g=>new SettingKey<GridLayout>(
        "Grid.Layout."+g.Id,Grid,g.Caption+" columns",
        "Comma-separated column numbers in desired order. Use 3:140 for width, -3 to hide, and ;sort=3:asc or ;sort=3:desc. Numbering starts at 1. Protected columns remain visible.",
        SettingScope.User,SettingKind.GridLayout,GridLayout.Default,ParseGrid,FormatGrid)).ToImmutableArray();

    private static readonly SettingDefinition[] Core=
    [
        AppTitle,Subtitle,WindowTitle,HomeHeading,Welcome,CompanyDisplay,LandingPage,DateFormat,TimeFormat,NumberFormat,CurrencyFormat,Language,
        Theme,Density,FontScale,SearchDebounce,PageSize,AlternateRows,RowDensity,InvoiceTagline,PrintedCompanyName,ShowBank,ShowIfsc,
        ShowBranch,ShowAccountHolder,ShowAccountNumber,ShowPaymentInstructions,InvoiceDateFormat,InvoiceQuantity,InvoiceRate,InvoiceAmount,InvoiceCurrency,Thousands,
        CustomerSelection,BillingAddress,ShippingAddress,PaymentTerms,CreditDays,CreditWarning,InactiveWarning,RequirePo,InvoiceRemarks,FooterNotes,
        ProductType,QuantityPlaces,ProductRatePlaces,ProductSearch,ProductSku,ProductBarcode,ProductHsn,ProductDescription,ProductImage,ProductUnit,
        ReorderWarning,LowStockWarning,TaxPlaces,TaxSummary,Printer,Paper,Orientation,Copies,Preview,PageNumbers,PrintTime,CopyLabel,HeaderSpacing,FooterSpacing,InvoiceTemplate
    ];

    public static readonly SettingKey<string> ReviewedSalesGstPlace=Choice("Sales.GstReviewedPlace",Sales,
        "Automatic GST confirmation — reviewed place of supply","",Company,
        ["",..GTS.Billing.Domain.CompanyRules.States.Keys.Order()],InvoiceEdit);
    public static readonly SettingKey<string> ReviewedSalesGstJurisdiction=Choice("Sales.GstReviewedJurisdiction",Sales,
        "Automatic GST confirmation — reviewed jurisdiction","Unresolved",Company,
        ["Unresolved","IntraState","IntraUnionTerritory","Interstate"],InvoiceEdit);
    public static readonly SettingKey<string> SalesGstReviewMode=Choice("Sales.GstReviewMode",Sales,
        "GST review mode","Company workflow",Company,
        ["Company workflow","Reviewed defaults only","Manual review"],InvoiceEdit);

    public static readonly ImmutableArray<SettingDefinition> Definitions=Core.Concat(PrintMargins).Concat(OptionalSections).Concat(GridLayouts)
        .Concat([ShellTheme,NavigationPosition,InvoiceListLayout,InvoiceFilterArrangement,InvoiceActionArrangement])
        .Concat(SalesListConfiguration.Items)
        .Concat([SalesGstReviewMode,ReviewedSalesGstPlace,ReviewedSalesGstJurisdiction])
        .Concat(LoginPage.Keys.Values)
        .Concat(SalesFormConfiguration.Fields.Select(x=>x.Setting))
        .Concat([SalesFormConfiguration.EntryLayout,SalesFormConfiguration.EntryProfile])
        .Concat(ConfigurationModule.Definitions)
        .Concat(CreateLabels()).ToImmutableArray();
    public static readonly ImmutableDictionary<string,SettingDefinition> ByKey=Definitions.ToImmutableDictionary(x=>x.Key,StringComparer.Ordinal);

    private static SettingKey<string> Text(string key,string section,string name,string value,SettingScope scopes,string? permission=null,
        int max=120,bool print=false,string? help=null) =>
        new(key,section,name,help ?? "Plain display text.",scopes,SettingKind.Text,value,
            s=>(ValidText(s,max,value.Length!=0,value),s.Trim()),s=>s,permission,print);

    private static SettingKey<string> Choice(string key,string section,string name,string value,SettingScope scopes,string[] choices,
        string? permission=null,bool print=false,Func<string,string>? normalize=null) =>
        new(key,section,name,"Select a supported value.",scopes,SettingKind.Choice,value,
            s=>
            {
                var candidate=normalize?.Invoke(s)??s;
                return (choices.Contains(candidate,StringComparer.Ordinal),candidate);
            },s=>s,permission,print,Options:Array.AsReadOnly(choices));

    private static SettingKey<bool> Flag(string key,string section,string name,bool value,SettingScope scopes,string? permission=null,bool print=false,string? help=null)=>
        new(key,section,name,help ?? "Choose true or false.",scopes,SettingKind.Boolean,value,
            s=>(bool.TryParse(s,out var parsed),parsed),v=>v?"true":"false",permission,print,Options:Array.AsReadOnly(new[]{"true","false"}));

    private static SettingKey<int> Number(string key,string section,string name,int value,int min,int max,SettingScope scopes,
        string? permission=null,bool print=false,string? help=null)=>
        new(key,section,name,help ?? FormattableString.Invariant($"Whole number from {min} to {max}."),scopes,SettingKind.Number,value,
            s=>(int.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out var parsed)&&parsed>=min&&parsed<=max,parsed),
            n=>n.ToString(CultureInfo.InvariantCulture),permission,print);

    private static bool ValidText(string text,int max,bool required,string original)
    {
        if(text.Length>max || required && string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl)) return false;
        var tokens=Regex.Matches(text,@"\{[0-9]+\}").Select(x=>x.Value).Order(StringComparer.Ordinal);
        var expected=Regex.Matches(original,@"\{[0-9]+\}").Select(x=>x.Value).Order(StringComparer.Ordinal);
        return tokens.SequenceEqual(expected) && !Regex.Replace(text,@"\{[0-9]+\}","").Any(x=>x is '{' or '}');
    }

    private static (bool,GridLayout) ParseGrid(string text)
    {
        if(text.Length>300) return (false,GridLayout.Default);
        if(string.IsNullOrWhiteSpace(text)) return (true,GridLayout.Default);
        var sections=text.Split(';',StringSplitOptions.TrimEntries);
        if(sections.Length>2) return (false,GridLayout.Default);
        var cols=ImmutableArray.CreateBuilder<GridColumnPreference>();
        foreach(var token in sections[0].Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
        {
            var m=Regex.Match(token,@"\A(-?)([1-9][0-9]?)(?::([0-9]{2,3}))?\z");
            if(!m.Success || cols.Any(x=>x.Key==m.Groups[2].Value)) return (false,GridLayout.Default);
            var width=m.Groups[3].Success?int.Parse(m.Groups[3].Value,CultureInfo.InvariantCulture):(int?)null;
            if(width is <50 or >600) return (false,GridLayout.Default);
            cols.Add(new(m.Groups[2].Value,m.Groups[1].Length!=0,width));
        }
        if(sections.Length==1) return (true,new(cols.ToImmutable(),null,false));
        var sort=Regex.Match(sections[1],@"\Asort=([1-9][0-9]?):(asc|desc)\z");
        return sort.Success?(true,new(cols.ToImmutable(),sort.Groups[1].Value,sort.Groups[2].Value=="desc")):(false,GridLayout.Default);
    }

    private static string FormatGrid(GridLayout value)=>string.Join(",",value.Columns.Select(x=>
        (x.Hidden?"-":"")+x.Key+(x.Width is int width?":"+width.ToString(CultureInfo.InvariantCulture):""))) +
        (value.SortKey is { } sort?";sort="+sort+":"+(value.Descending?"desc":"asc"):"");

    private static SettingKey<string> Label(string key,string title,string text,bool print=false)
    {
        if(key is "Invoice.Label.Title" or "Invoice.Label.BillOfSupply" or "Invoice.Label.Proforma" or "Invoice.Label.Quotation"
            or "Invoice.Label.DeliveryChallan" or "Invoice.Label.CreditNote" or "Invoice.Label.DebitNote" or "Invoice.Label.Gstin"
            or "Invoice.Label.CGST" or "Invoice.Label.SGST" or "Invoice.Label.IGST" or "Invoice.Label.Cess" or "Invoice.Label.HsnSac"
            or "Invoice.Label.InvoiceNumber" or "Invoice.Label.InvoiceDate" or "Invoice.Label.PlaceOfSupply" or "Invoice.Label.TaxableValue"
            or "Invoice.Label.TotalTax" or "Invoice.Label.Total")
            return new(key,LabelSection,title,"Keep the standard legal caption. Optional additional wording can follow it after a dash.",
                Company,SettingKind.Text,text,s=>(ValidText(s,160,true,text) &&
                    (string.Equals(s.Trim(),text,StringComparison.Ordinal) || s.Trim().StartsWith(text+" — ",StringComparison.Ordinal)),s.Trim()),
                s=>s,InvoiceEdit,true);
        return Text(key,LabelSection,title,text,print?Company:Global,print?InvoiceEdit:null,160,print);
    }
}
