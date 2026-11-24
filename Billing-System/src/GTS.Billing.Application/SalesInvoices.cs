using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public enum SalesAction { List, Options, View, Preview, Save, Finalize, PrintPreview, Print, PrintToPdf, CustomerAddresses, PrepareFinalization, DeleteDraft, CancelInvoice }
public sealed record SalesChoice(Guid Id,string Label,Guid? Scope=null,Guid? SecondaryScope=null,decimal? Price=null,bool Inclusive=false,
    string? Code=null,string? Sku=null,string? ProductName=null,string? Brand=null,decimal? Mrp=null,
    string? ProductKind=null,string? Hsn=null,decimal? GstRate=null,Guid? TaxClassificationId=null,string? TaxContext=null,
    bool Preferred=false,bool Active=true,DateOnly? From=null,DateOnly? To=null,string? NextNumberPreview=null,
    bool PurchaseOrderRequired=false)
{
    public override string ToString()=>Label;
}
public sealed record SalesLineInput(Guid ProductId,Guid? UnitId,decimal Quantity,decimal Price,bool Inclusive,
    decimal DiscountPercent=0,decimal DiscountAmount=0,Guid? ProductVariantId=null,
    string? ManualHsn=null,decimal? ManualGstRate=null);
public sealed record SalesTransport(string GoodsReceipt, string Vehicle, string Weight, string Carrier,
    string OrderThrough, string PrivateMark, string Document, string PreparedBy,string Distance="",string TransportId="");
public enum SalesIntent { Local, Interstate }
public sealed record SalesEntryDetails(SalesIntent? Intent=null,string PaymentMode="",string Tcs="",string Reversal="",string Ern="");
public sealed record SalesDraft(DateOnly Date,Guid? YearId=null,Guid? SeriesId=null,Guid? RegistrationId=null,
    Guid? CustomerId=null,Guid? BillingAddressId=null,Guid? ShippingAddressId=null,string PlaceOfSupply="",
    GstJurisdiction Jurisdiction=GstJurisdiction.Unresolved, bool DomesticConfirmed=false,string PurchaseOrder="",
    DateOnly? PurchaseOrderDate=null,string Reference="",string Terms="",DateOnly? DueDate=null,string Notes="",string InternalRemarks="",
    IReadOnlyList<SalesLineInput>? Lines=null,SalesTransport? Transport=null,SalesEntryDetails? EntryDetails=null)
{
    public IReadOnlyList<SalesFieldError> RequiredFields(bool requireNumbering)
    {
        var errors=new List<SalesFieldError>();
        void Require(bool valid,string field,string message)
        {
            if(!valid) errors.Add(new(field,message));
        }
        Require(Date!=default,nameof(Date),"Enter an invoice date.");
        Require(YearId.HasValue || !requireNumbering,nameof(YearId),"Select a financial year.");
        Require(RegistrationId.HasValue,nameof(RegistrationId),"Select a supplier GST registration.");
        Require(SeriesId.HasValue || !requireNumbering,nameof(SeriesId),"Select a document series.");
        Require(CustomerId.HasValue,nameof(CustomerId),"Select a customer.");
        Require(Lines is {Count:>0},nameof(Lines),"Add at least one product line.");
        return errors;
    }
}
public sealed record SalesAmounts(decimal Subtotal,decimal Discount,decimal Taxable,decimal Cgst,decimal Sgst,decimal Igst,
    decimal Utgst,decimal Cess,decimal RoundOff,decimal Total)
{
    public decimal Tax=>Cgst+Sgst+Igst+Utgst+Cess;
}
public sealed record FinalSalesLine(int Number,SalesLineInput Input,ProductInvoiceSnapshot Product,AppliedTaxSnapshot Tax,
    decimal BaseQuantity,SalesAmounts Amounts);
public sealed record SalesInvoiceSnapshot(SalesDraft Draft,CustomerInvoiceSnapshot Customer,SalesSeller Seller,
    IReadOnlyList<FinalSalesLine> Lines,SalesAmounts Amounts,string? Number=null,string? FinancialYearCode=null,string? DocumentSeriesCode=null);
public sealed record SalesSeller(string Name,string? DisplayName,string? Phone,string? Email,string Gstin,string StateCode,
    string Address,CompanyInvoicePresentation Presentation,string? Iec=null,string? BusinessDescription=null,string? AlternateMobile=null)
{
    public IEnumerable<string> InvoiceTaglines()=>new[]{BusinessDescription,Presentation.Options?.Tagline}
        .Where(value=>!string.IsNullOrWhiteSpace(value)).Select(value=>value!.Trim()).Distinct(StringComparer.Ordinal);
    public string InvoicePhones()=>string.Join(", ",new[]{Phone,AlternateMobile}
        .Where(value=>!string.IsNullOrWhiteSpace(value)).Select(value=>value!.Trim()).Distinct(StringComparer.Ordinal));
}
public sealed record SalesRow(Guid Id,long Version,DateOnly Date,string? Number,string Customer,string Year,string Series,
    decimal Taxable,decimal Tax,decimal Total,bool Finalized,SalesIntent? Intent=null,bool Cancelled=false)
{
    public string Status=>Cancelled?"Cancelled":Finalized?"Finalized":"Draft";
    public string TaxType=>Intent switch {SalesIntent.Local=>"Local Sale",SalesIntent.Interstate=>"Inter State Sale",_=>"Unreviewed"};
}
public sealed record SalesOptions(IReadOnlyList<SalesChoice> Years,IReadOnlyList<SalesChoice> Series,
    IReadOnlyList<SalesChoice> Registrations,IReadOnlyList<SalesChoice> Customers,IReadOnlyList<SalesChoice> Billing,
    IReadOnlyList<SalesChoice> Shipping,IReadOnlyList<SalesChoice> Products,IReadOnlyList<SalesChoice> Units,
    string? CompanyName=null,IReadOnlyList<SalesTaxChoice>? TaxChoices=null,bool CanFillMrp=false,bool CanLinkTax=false,
    IReadOnlyList<SalesVariantChoice>? Variants=null,SalesFormConfiguration? Form=null,string? PreparedBy=null,
    string EntryLayout="Company default",bool GraftexEntry=false)
{
    public static bool IncludesDate(SalesChoice year,DateOnly date)=>year.Active && year.From<=date && year.To>=date;
    public SalesChoice? DefaultYear(DateOnly date)=>ChooseDefault(Years.Where(year=>IncludesDate(year,date)));
    public SalesChoice? DefaultRegistration()=>ChooseDefault(Registrations.Where(x=>x.Active));
    public SalesChoice? DefaultSeries(Guid? year,Guid? registration)=>year.HasValue && registration.HasValue
        ?ChooseDefault(Series.Where(x=>x.Active && x.Scope==year && x.SecondaryScope==registration)):null;

    private static SalesChoice? ChooseDefault(IEnumerable<SalesChoice> source)
    {
        var choices=source.ToArray();
        var preferred=choices.Where(x=>x.Preferred).Take(2).ToArray();
        return preferred.Length==1?preferred[0]:choices.Length==1?choices[0]:null;
    }
}
public sealed record SalesVariantChoice(Guid Id,Guid ProductId,string Code,string? Sku,string? Barcode,string Attributes,
    decimal? Mrp,decimal? SalesRate,bool Active)
{
    public string Label=>string.Join(" · ",new[]{Code,Sku,Barcode,Attributes}.Where(x=>!string.IsNullOrWhiteSpace(x)));
}
public sealed record SalesTaxChoice(Guid AssignmentId,Guid ClassificationId,string Type,string Code,string Context,
    decimal Rate,DateOnly From,DateOnly? To,bool Selectable)
{
    public string Label=>$"{Code} · {Rate:G29}% · {Context}";
    public bool Applies(DateOnly date)=>From<=date && (To is null || date<To);
}
public sealed record SalesProductCompletion(int Line,decimal? Mrp,Guid? TaxAssignmentId);
public sealed record SalesRequest(SalesAction Action,Guid? Company=null,Guid? Id=null,long Version=0,SalesDraft? Draft=null,
    string? Search=null,bool? Finalized=null,Guid? Year=null,Guid? Customer=null,Guid? Series=null,DateOnly? From=null,DateOnly? To=null,
    string? Confirmation=null,FinalizeDocumentNumberRequest? ExpectedNumbering=null,string? PrintTemplateCode=null,
    IReadOnlyList<SalesProductCompletion>? ProductCompletions=null,Guid? CreateDraftId=null,
    SalesIntent? Category=null,string? InvoiceNumberSearch=null,string? CustomerSearch=null,
    bool? Cancelled=null,string? CancellationReason=null);
public sealed record SalesResult(CompanyOutcome Outcome,Guid? Company=null,string? Message=null,IReadOnlyList<SalesRow>? Rows=null,
    SalesRow? Row=null,SalesDraft? Draft=null,SalesOptions? Options=null,SalesInvoiceSnapshot? Snapshot=null,string? Confirmation=null,
    FinalizedDocumentNumber? Allocation=null,IReadOnlyList<string>? Audit=null,InvoicePrintModel? PrintModel=null,
    IReadOnlyList<SalesFieldError>? FieldErrors=null,SalesCustomerAddresses? CustomerAddresses=null,
    IReadOnlyList<SalesLineEstimate>? PendingGstLines=null);

public sealed record SalesLineEstimate(decimal Gross,decimal Discount,decimal? Taxable,decimal? Tax,decimal? Total,
    AppliedTaxSnapshot? EstimatedSplit=null,SalesAmounts? EstimatedAmounts=null);

public readonly record struct SalesPriceAmounts(decimal Gross,decimal Discount)
{
    public decimal Net=>Gross-Discount;
}

public sealed record SalesCustomerAddresses(Guid CustomerId,long Version,string Name,string? Gstin,
    IReadOnlyList<CustomerAddressDraft> Addresses);

public sealed record SalesFieldError(string Field,string Message,int? Line=null);

public static class SalesCalculation
{
    private static decimal Money(decimal value)=>decimal.Round(value,2,MidpointRounding.AwayFromZero);
    public static IReadOnlyList<SalesFieldError> ValidateLine(SalesLineInput line,int? index=null,bool validateSubtotal=true)
    {
        var errors=new List<SalesFieldError>();
        void Add(string field,string message)=>errors.Add(new(field,message,index));
        if(line.Quantity<=0 || line.Quantity>1000000) Add(nameof(line.Quantity),"Enter a quantity greater than zero and at most 1,000,000.");
        if(line.Price<0 || line.Price>1000000000) Add(nameof(line.Price),"Enter a unit price between 0 and 1,000,000,000.");
        if(line.DiscountPercent is <0 or >100) Add(nameof(line.DiscountPercent),"Enter a discount percentage from 0 to 100.");
        if(line.DiscountAmount<0 || Money(line.DiscountAmount)!=line.DiscountAmount)
            Add(nameof(line.DiscountAmount),"Enter a nonnegative discount amount with at most two decimal places.");
        if(line.DiscountPercent>0 && line.DiscountAmount>0)
        {
            Add(nameof(line.DiscountPercent),"Use either a discount percentage or a discount amount.");
            Add(nameof(line.DiscountAmount),"Use either a discount amount or a discount percentage.");
        }
        if(validateSubtotal && errors.Count==0 && line.DiscountAmount>Money(line.Quantity*line.Price))
            Add(nameof(line.DiscountAmount),"Discount amount cannot exceed the line subtotal.");
        return errors;
    }
    public static SalesPriceAmounts PriceAmounts(SalesLineInput line)
    {
        var subtotal=Money(line.Quantity*line.Price);
        var discount=line.DiscountPercent>0?Money(subtotal*line.DiscountPercent/100m):line.DiscountAmount;
        return new(subtotal,discount);
    }
    public static SalesLineEstimate Estimate(SalesLineInput line,TaxSnapshot rate,SalesIntent? intent=null)
    {
        var pricing=PriceAmounts(line);
        if(rate.Treatment!=TaxTreatment.Taxable)
            return new(pricing.Gross,pricing.Discount,null,null,null);
        AppliedTaxSnapshot? split=intent switch
        {
            SalesIntent.Local when rate.Cgst+rate.Sgst==rate.Total=>
                new(rate,GstJurisdiction.IntraState,"Estimated local split",0,rate.Cgst,rate.Sgst,0,0,rate.Cess),
            SalesIntent.Interstate when rate.Igst==rate.Total=>
                new(rate,GstJurisdiction.Interstate,"Estimated interstate split",0,0,0,rate.Igst,0,rate.Cess),
            _=>null
        };
        if(split is not null && Calculate(line,split) is { } estimated)
            return new(pricing.Gross,pricing.Discount,estimated.Taxable,estimated.Tax,estimated.Total,
                split with {TaxableValue=estimated.Taxable},estimated);
        var ratio=(rate.Total+rate.Cess)/100m;
        var taxable=Money(line.Inclusive?pricing.Net/(1m+ratio):pricing.Net);
        var tax=line.Inclusive?pricing.Net-taxable:Money(taxable*rate.Total/100m)+Money(taxable*rate.Cess/100m);
        return new(pricing.Gross,pricing.Discount,taxable,tax,taxable+tax);
    }
    public static SalesAmounts? Calculate(SalesLineInput line,AppliedTaxSnapshot rate)
    {
        if(ValidateLine(line).Count>0) return null;
        var pricing=PriceAmounts(line);
        var net=pricing.Net;
        var ratio=(rate.CgstPercentage+rate.SgstPercentage+rate.IgstPercentage+rate.UtgstPercentage+rate.CessPercentage)/100m;
        var taxable=Money(line.Inclusive?net/(1m+ratio):net);
        decimal Component(decimal percent)=>Money(taxable*percent/100m);
        var cg=Component(rate.CgstPercentage);var sg=Component(rate.SgstPercentage);var ig=Component(rate.IgstPercentage);
        var ut=Component(rate.UtgstPercentage);var cess=Component(rate.CessPercentage);
        var total=taxable+cg+sg+ig+ut+cess;
        var roundOff=line.Inclusive?net-total:0;
        return new(pricing.Gross,pricing.Discount,taxable,cg,sg,ig,ut,cess,roundOff,total+roundOff);
    }
    public static SalesAmounts Sum(IEnumerable<SalesAmounts> amounts)
    {
        var rows=amounts.ToArray();
        return new(rows.Sum(x=>x.Subtotal),rows.Sum(x=>x.Discount),rows.Sum(x=>x.Taxable),rows.Sum(x=>x.Cgst),
            rows.Sum(x=>x.Sgst),rows.Sum(x=>x.Igst),rows.Sum(x=>x.Utgst),rows.Sum(x=>x.Cess),rows.Sum(x=>x.RoundOff),rows.Sum(x=>x.Total));
    }
}
public interface ISalesInvoiceStore
{
    Task<SalesResult> ExecuteAsync(SalesRequest request,AuthenticatedIdentity identity,CancellationToken ct);
}
public sealed class SalesInvoiceService(AuthenticationService auth,IApplicationOperationCoordinator lifetime,ISalesInvoiceStore store)
    : IDocumentNumberFinalizer
{
    public async Task<FinalizedDocumentNumber> FinalizeAsync(FinalizeDocumentNumberRequest request,CancellationToken cancellationToken)
    {
        var result=await ExecuteAsync(new(SalesAction.Finalize,request.CompanyId,request.DocumentId,request.DraftVersion,
            Confirmation:request.Confirmation,ExpectedNumbering:request),cancellationToken);
        return result.Allocation??throw new InvalidOperationException(result.Message??"The sales invoice could not be finalized.");
    }

    public async Task<SalesResult> ExecuteAsync(SalesRequest request,CancellationToken ct=default)
    {
        SalesResult result=new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token=>result=await auth.RunAuthenticatedAsync(async user=>
            {
                if(!Enum.IsDefined(request.Action) || request.Search?.Length>100 || request.Draft?.Lines?.Count>250)
                    return new SalesResult(CompanyOutcome.InvalidInput);
                var response=await store.ExecuteAsync(request,user,token);
                if(response.Outcome==CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                return response;
            },token),ct);
        }
        catch(UnauthorizedAccessException) {return new(CompanyOutcome.SessionRequired);}
        catch(ArgumentException) {return new(CompanyOutcome.InvalidInput);}
        catch(Exception e) when(e is IOException or System.Data.Common.DbException or InvalidOperationException or OverflowException)
        {return new(CompanyOutcome.Unavailable);}
        return result;
    }
}
