using System.Collections.Immutable;

namespace GTS.Billing.Application;

public sealed record InvoiceTemplate(string Code,string Name,string Paper,string Orientation,double WidthMm,double HeightMm,double FontSize,bool Detailed);

public static class InvoiceTemplates
{
    public const string Standard="A4-PORTRAIT-v1";
    public const string Landscape="A4-LANDSCAPE-v1";
    public const string Compact="A5-COMPACT-v1";
    public const string GraftexClassic="GRAFTEX_CLASSIC_A4";
    public const string Modern="MODERN_GST_A4";
    public const string Wholesale="WHOLESALE_DETAILED_A4_LANDSCAPE";
    public const string Retail="COMPACT_RETAIL_A5";
    // Existing versions are immutable. Add a new code for layout changes that should apply only to new invoices.
    public static ImmutableArray<InvoiceTemplate> All {get;}=
    [
        new(Standard,"Standard A4 Portrait","A4","Portrait",210,297,12,false),
        new(Landscape,"Detailed A4 Landscape","A4","Landscape",297,210,12,true),
        new(Compact,"Compact A5","A5","Portrait",148,210,10,false)
    ];

    public static ImmutableArray<InvoiceTemplate> Selectable {get;}=
    [
        new(GraftexClassic,"Classic Full Page — Dual Logo","A4","Portrait",210,297,10,true),
        new(Modern,"Modern Professional GST","A4","Portrait",210,297,12,false),
        new(Wholesale,"Wholesale Detailed","A4","Landscape",297,210,11,true),
        new(Retail,"Compact Retail","A5","Portrait",148,210,10,false)
    ];

    public static string ForNewInvoice(string code)=>code switch
    {
        Standard=>Modern,
        Landscape=>Wholesale,
        Compact=>Retail,
        "GRAFTEX_STYLE_A4" or "GRAFTEX_CLASSIC_A4_V2"=>GraftexClassic,
        _=>code
    };

    public static InvoiceTemplate Resolve(InvoicePresentationOptions? options)
    {
        var code=options?.TemplateCode;
        if(code is "GRAFTEX_STYLE_A4" or "GRAFTEX_CLASSIC_A4_V2") code=GraftexClassic;
        if(string.IsNullOrEmpty(code))
            code=options?.Printing.Orientation=="Landscape"?Landscape:Standard;
        return Selectable.Concat(All).SingleOrDefault(x=>x.Code==code) ??
            throw new InvalidOperationException("The historical invoice template is unavailable.");
    }
}

public sealed record InvoicePrintModel(string Number,SalesDraft Header,SalesSeller Seller,CustomerInvoiceSnapshot Customer,
    ImmutableArray<FinalSalesLine> Lines,SalesAmounts Totals,InvoiceTemplate Template,
    string? FinancialYearCode,string? DocumentSeriesCode)
{
    public static InvoicePrintModel FromFinalized(SalesInvoiceSnapshot snapshot,string number)
    {
        if(string.IsNullOrWhiteSpace(number) || snapshot.Number!=number || snapshot.Lines.Count==0)
            throw new InvalidOperationException("A complete finalized invoice snapshot is required.");
        return new(number,snapshot.Draft,snapshot.Seller,snapshot.Customer,snapshot.Lines.ToImmutableArray(),
            snapshot.Amounts,InvoiceTemplates.Resolve(snapshot.Seller.Presentation.Options),
            snapshot.FinancialYearCode,snapshot.DocumentSeriesCode);
    }
}
