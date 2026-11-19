using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public enum TaxMaster { Classifications, Rates, Assignments, Product }
public enum TaxAction { List, View, Save, Status, Options, Preview }
public enum GstJurisdiction { Unresolved, IntraState, IntraUnionTerritory, Interstate, Unsupported }
public enum TaxDeterminationStatus { Resolved, MissingFacts, Unsupported }
public sealed record TaxFacts(Guid? SupplierRegistrationId, Guid? CustomerId, string? PlaceOfSupply,
    DateOnly TransactionDate, GstJurisdiction Jurisdiction, decimal? TaxableValue,
    bool StandardDomesticSupplyConfirmed, string? TransactionAttributes = null);
public sealed record TaxSnapshot(Guid ClassificationId, string Type, string Code, string Description,
    Guid AssignmentId, Guid RateId, string RateCode, DateOnly RateFrom, TaxTreatment Treatment,
    decimal Total, decimal Cgst, decimal Sgst, decimal Igst, decimal? Utgst, decimal Cess,
    DateOnly EffectiveDate, string Context);
public sealed record AppliedTaxSnapshot(TaxSnapshot Rate, GstJurisdiction Jurisdiction, string Basis,
    decimal TaxableValue, decimal CgstPercentage, decimal SgstPercentage, decimal IgstPercentage,
    decimal UtgstPercentage, decimal CessPercentage);
public sealed record TaxDetermination(TaxDeterminationStatus Status, AppliedTaxSnapshot? Snapshot = null);
public static class GstDetermination
{
    // Amounts remain unrounded. The future invoice finalizer owns currency rounding and monetary snapshots.
    public static TaxDetermination Determine(TaxSnapshot rate, TaxFacts facts)
    {
        if (!facts.SupplierRegistrationId.HasValue || !facts.CustomerId.HasValue || string.IsNullOrWhiteSpace(facts.PlaceOfSupply) ||
            facts.TaxableValue is null or < 0 || rate.EffectiveDate != facts.TransactionDate || facts.Jurisdiction == GstJurisdiction.Unresolved)
            return new(TaxDeterminationStatus.MissingFacts);
        if (!facts.StandardDomesticSupplyConfirmed || facts.Jurisdiction == GstJurisdiction.Unsupported ||
            !Enum.IsDefined(facts.Jurisdiction) || rate.Treatment == TaxTreatment.ZeroRated ||
            facts.Jurisdiction == GstJurisdiction.IntraUnionTerritory && rate.Treatment == TaxTreatment.Taxable && rate.Utgst is null)
            return new(TaxDeterminationStatus.Unsupported);
        var state = facts.Jurisdiction == GstJurisdiction.IntraState;
        var territory = facts.Jurisdiction == GstJurisdiction.IntraUnionTerritory;
        return new(TaxDeterminationStatus.Resolved,new(rate,facts.Jurisdiction,"Percent of taxable value",facts.TaxableValue.Value,
            state || territory ? rate.Cgst : 0,state ? rate.Sgst : 0,
            facts.Jurisdiction == GstJurisdiction.Interstate ? rate.Igst : 0,territory ? rate.Utgst ?? 0 : 0,rate.Cess));
    }
}

public sealed record TaxEntry(Guid Id = default, long Version = 0, string Code = "", string Name = "", string Type = "",
    string? Description = null, string? Notes = null, bool Active = true, TaxTreatment Treatment = TaxTreatment.Taxable,
    decimal Total = 0, decimal Cgst = 0, decimal Sgst = 0, decimal Igst = 0, decimal? Utgst = null, decimal Cess = 0,
    DateOnly? From = null, DateOnly? To = null, Guid? ClassificationId = null, Guid? RateId = null, string Context = "",
    string? Summary = null);
public sealed record TaxChoice(Guid Id, string Label);
public sealed record TaxRequest(TaxMaster Master, TaxAction Action, Guid? Company = null, Guid? Id = null,
    string? Search = null, bool? Active = null, TaxEntry? Entry = null, DateOnly? Date = null);
public sealed record TaxResult(CompanyOutcome Outcome, Guid? Company = null, string? Message = null,
    IReadOnlyList<TaxEntry>? Rows = null, TaxEntry? Detail = null, IReadOnlyList<TaxChoice>? Classifications = null,
    IReadOnlyList<TaxChoice>? Rates = null, IReadOnlyList<string>? Contexts = null, TaxSnapshot? Snapshot = null);
public interface ITaxCatalogStore
{
    Task<TaxResult> ExecuteAsync(TaxRequest request, AuthenticatedIdentity actor, CancellationToken ct);
}
public sealed class TaxCatalogService(AuthenticationService auth, IApplicationOperationCoordinator lifetime, ITaxCatalogStore store)
{
    public async Task<TaxResult> ExecuteAsync(TaxRequest request,CancellationToken ct=default)
    {
        TaxResult result = new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token =>
            {
                result = await auth.RunAuthenticatedAsync(async actor =>
                {
                    if(request.Search?.Length>100 || !Enum.IsDefined(request.Master) || !Enum.IsDefined(request.Action))
                        return new TaxResult(CompanyOutcome.InvalidInput);
                    var response = await store.ExecuteAsync(request,actor,token);
                    if(response.Outcome==CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                    return response;
                },token);
            },ct);
        }
        catch(UnauthorizedAccessException) { return new(CompanyOutcome.SessionRequired); }
        catch(ArgumentException) { return new(CompanyOutcome.InvalidInput); }
        catch(Exception e) when(e is System.Data.Common.DbException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
        return result;
    }
}
