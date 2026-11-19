using System.Globalization;
using System.Text.RegularExpressions;
using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public enum NumberingMaster { Years, Series }
public enum NumberingAction { List, View, Options, Save, SetCurrent, Close, Reopen, SetDefault, Status, Preview }
public sealed record NumberingChoice(Guid Id,string Label);
public sealed record DocumentTypeChoice(string Code,string Label);
public static class DocumentTypes
{
    // TaxInvoice is the existing stored identity. Keep it stable for prior series.
    public static IReadOnlyList<DocumentTypeChoice> All {get;}=
    [
        new("TaxInvoice","Sales Tax Invoice"),new("BILL_OF_SUPPLY","Bill of Supply"),new("PROFORMA","Proforma Invoice"),
        new("QUOTATION","Quotation"),new("DELIVERY_CHALLAN","Delivery Challan"),new("SALES_CREDIT_NOTE","Sales Credit Note"),
        new("SALES_DEBIT_NOTE","Sales Debit Note"),new("PURCHASE_INVOICE","Purchase Invoice"),new("PURCHASE_RETURN","Purchase Return"),
        new("STOCK_ADJUSTMENT","Stock Adjustment"),new("PRODUCTION_DOCUMENT","Production Document")
    ];
    public static string Label(string code)=>All.FirstOrDefault(x=>x.Code==code)?.Label??code;
}
public sealed record NumberingEntry(Guid Id=default,long Version=0,string Code="",string Name="",
    DateOnly? Start=null,DateOnly? End=null,bool Open=true,bool IsCurrent=false,
    Guid? YearId=null,Guid? RegistrationId=null,string DocumentType="",string Prefix="",string Separator="",
    int Padding=4,long Starting=1,long Next=1,string Suffix="",bool Active=true,bool IsDefault=false,
    bool Used=false,string? YearLabel=null,DateTime? CreatedUtc=null,DateTime? UpdatedUtc=null,
    DateTime? ClosedUtc=null,DateTime? ReopenedUtc=null,string? Reason=null,string? Details=null);
public sealed record NumberingRequest(NumberingMaster Master,NumberingAction Action,Guid? Company=null,Guid? Id=null,
    string? Search=null,bool? Active=null,NumberingEntry? Entry=null);
public sealed record NumberingResult(CompanyOutcome Outcome,Guid? Company=null,string? Message=null,
    IReadOnlyList<NumberingEntry>? Rows=null,NumberingEntry? Detail=null,IReadOnlyList<NumberingChoice>? Years=null,
    IReadOnlyList<NumberingChoice>? Registrations=null,string? Preview=null,string? CurrentYear=null);

public sealed record FinalizeDocumentNumberRequest(Guid FinancialYearId,Guid RegistrationId,string DocumentType,
    Guid? SeriesId,DateOnly DocumentDate,Guid DocumentId,Guid CompanyId,long DraftVersion,string Confirmation);
public sealed record FinalizedDocumentNumber(Guid DocumentId,Guid LedgerId,Guid SeriesId,long Sequence,string Number);

// Implementations must commit the document, ledger association and counter in one transaction.
public interface IDocumentNumberFinalizer
{
    Task<FinalizedDocumentNumber> FinalizeAsync(FinalizeDocumentNumberRequest request,CancellationToken cancellationToken);
}

public sealed partial record DocumentNumberFormat(string Before,string After,int Padding,int MaximumDigits)
{
    public long Maximum=>long.Parse(new string('9',MaximumDigits),CultureInfo.InvariantCulture);
    public string Render(long number)
    {
        if(number<1 || number>Maximum) throw new ArgumentOutOfRangeException(nameof(number));
        return Before+number.ToString("D"+Padding,CultureInfo.InvariantCulture)+After;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9/\-]*\z",RegexOptions.CultureInvariant)]
    private static partial Regex Literal();

    public static DocumentNumberFormat? Create(NumberingEntry entry,string company,string year)
    {
        if(entry.Prefix.Length>80 || entry.Suffix.Length>80 || entry.Padding is <1 or >9 ||
            entry.Separator is not ("" or "-" or "/")) return null;
        string Expand(string source)=>source.Replace("{Company}",company,StringComparison.Ordinal)
            .Replace("{Year}",year,StringComparison.Ordinal).Replace("{Series}",entry.Code,StringComparison.Ordinal);
        var prefix=Expand(entry.Prefix);
        var suffix=Expand(entry.Suffix);
        if(!Literal().IsMatch(prefix) || !Literal().IsMatch(suffix)) return null;
        if(prefix.Length>0) prefix+=entry.Separator;
        if(suffix.Length>0) suffix=entry.Separator+suffix;
        var maximum=Math.Min(9,16-prefix.Length-suffix.Length);
        return maximum<entry.Padding ? null : new(prefix.ToUpperInvariant(),suffix.ToUpperInvariant(),entry.Padding,maximum);
    }

    // A conservative intersection of the rendered shapes. Numeric positions are digit wildcards.
    public bool Overlaps(DocumentNumberFormat other)
    {
        for(var length=Padding;length<=MaximumDigits;length++)
        for(var otherLength=other.Padding;otherLength<=other.MaximumDigits;otherLength++)
        {
            var left=Before+new string('#',length)+After;
            var right=other.Before+new string('#',otherLength)+other.After;
            if(left.Length==right.Length && left.Zip(right).All(x=>x.First==x.Second ||
                x.First=='#' && char.IsAsciiDigit(x.Second) || x.Second=='#' && char.IsAsciiDigit(x.First)))
                return true;
        }
        return false;
    }

    public bool CouldRender(string value)
    {
        if(value.Length<Before.Length+After.Length+Padding ||
            !value.StartsWith(Before,StringComparison.OrdinalIgnoreCase) || !value.EndsWith(After,StringComparison.OrdinalIgnoreCase)) return false;
        var digits=value.AsSpan(Before.Length,value.Length-Before.Length-After.Length);
        return digits.Length<=MaximumDigits && long.TryParse(digits,NumberStyles.None,CultureInfo.InvariantCulture,out var number) &&
            number>0 && string.Equals(Render(number),value,StringComparison.OrdinalIgnoreCase);
    }
}

public interface INumberingStore
{
    Task<NumberingResult> ExecuteAsync(NumberingRequest request,AuthenticatedIdentity user,CancellationToken ct);
}
public sealed class NumberingService(AuthenticationService authentication,IApplicationOperationCoordinator lifetime,INumberingStore store)
{
    public async Task<NumberingResult> ExecuteAsync(NumberingRequest request,CancellationToken ct=default)
    {
        NumberingResult result=new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token=>
            {
                result=await authentication.RunAuthenticatedAsync(async user=>
                {
                    if(request.Search?.Length>100 || !Enum.IsDefined(request.Master) || !Enum.IsDefined(request.Action))
                        return new NumberingResult(CompanyOutcome.InvalidInput);
                    var response=await store.ExecuteAsync(request,user,token);
                    if(response.Outcome==CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                    return response;
                },token);
            },ct);
        }
        catch(UnauthorizedAccessException) {return new(CompanyOutcome.SessionRequired);}
        catch(ArgumentException) {return new(CompanyOutcome.InvalidInput);}
        catch(Exception e) when(e is System.Data.Common.DbException or IOException or InvalidOperationException)
        {return new(CompanyOutcome.Unavailable);}
        return result;
    }
}
