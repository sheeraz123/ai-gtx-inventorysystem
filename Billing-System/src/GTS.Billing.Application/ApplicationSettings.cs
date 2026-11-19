using System.Collections.Immutable;

namespace GTS.Billing.Application;

public enum SettingsAction { Runtime, List, Save, Security }
public sealed record SettingEdit(string Key,string? Text,long? Version);
public sealed record SettingsRequest(SettingsAction Action,SettingScope Scope=SettingScope.Application,
    Guid? ExpectedCompanyId=null,string? Section=null,IReadOnlyList<SettingEdit>? Changes=null);
public sealed record SettingEntry(SettingDefinition Definition,SettingScope Scope,string EffectiveText,string? StoredText,
    long? Version,DateTime? UpdatedUtc,string? UpdatedBy,bool CanEdit)
{
    public string Key=>Definition.Key;
    public string Section=>Definition.Section;
    public string Label=>Definition.Label;
    public string DefaultText=>Definition.DefaultText;
    public string Module
    {
        get
        {
            var parts=Key.Split('.');
            return parts.Length>2 && parts[0]=="Field"?parts[1]:parts[0];
        }
    }
}
public sealed record SettingsResult(CompanyOutcome Outcome,IReadOnlyList<SettingEntry>? Entries=null,ResolvedSettings? Runtime=null,
    string? Message=null,string? InvalidKey=null,Guid? CurrentCompanyId=null);

public interface ISettingsStore
{
    Task<SettingsResult> ExecuteAsync(SettingsRequest request,AuthenticatedIdentity actor,CancellationToken ct);
}
public sealed class ApplicationSettingsService(AuthenticationService authentication,IApplicationOperationCoordinator lifetime,ISettingsStore store)
{
    public async Task<SettingsResult> ExecuteAsync(SettingsRequest request,CancellationToken ct=default)
    {
        SettingsResult result=new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token =>
            {
                result=await authentication.RunAuthenticatedAsync(async actor =>
                {
                    if (!Enum.IsDefined(request.Scope) || !Enum.IsDefined(request.Action))
                        return new SettingsResult(CompanyOutcome.InvalidInput);
                    if (request.Action==SettingsAction.Save)
                    {
                        if(request.Changes is not { Count:>0 } changes || changes.Count>SettingsRegistry.Definitions.Length ||
                            changes.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=changes.Count)
                            return new SettingsResult(CompanyOutcome.InvalidInput,Message:"Select a valid setting or section.");
                        var normalized=new List<SettingEdit>(changes.Count);
                        foreach(var change in changes)
                        {
                            if(!SettingsRegistry.ByKey.TryGetValue(change.Key,out var key) || !key.Scopes.HasFlag(request.Scope) ||
                                !key.IsEditable || key.IsSensitive)
                                return new SettingsResult(CompanyOutcome.InvalidInput,Message:"That setting is not editable in this scope.");
                            if(change.Text is null){normalized.Add(change);continue;}
                            if(change.Text.Length>800 || !key.TryNormalize(change.Text,out var clean))
                                return new SettingsResult(CompanyOutcome.InvalidInput,Message:key.Description,InvalidKey:key.Key);
                            normalized.Add(change with {Text=clean});
                        }
                        request=request with {Changes=normalized};
                    }
                    var response=await store.ExecuteAsync(request,actor,token);
                    if(response.Outcome==CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                    return response;
                },token);
            },ct);
        }
        catch(UnauthorizedAccessException) { return new(CompanyOutcome.SessionRequired); }
        catch(Exception e) when(e is System.Data.Common.DbException or IOException or InvalidOperationException or ArgumentException)
        { return new(CompanyOutcome.Unavailable); }
        return result;
    }
}

public sealed record InvoicePrintOptions(string? PreferredPrinter,string PaperSize,string Orientation,int Copies,
    bool Preview,bool PageNumbers,bool PrintTimestamp,int TopMm,int RightMm,int BottomMm,int LeftMm,
    int HeaderSpacingMm,int FooterSpacingMm,string CopyLabel);

public sealed record InvoicePresentationOptions(string CompanyName,string Tagline,
    ImmutableDictionary<string,string> Captions,ImmutableDictionary<string,bool> OptionalSections,
    string DateFormat,int QuantityDecimals,int RateDecimals,int AmountDecimals,int TaxDecimals,string CurrencySymbol,
    bool GroupThousands,string TaxSummaryStyle,string Remarks,string FooterNotes,InvoicePrintOptions Printing,
    string? TemplateCode=null)
{
    // Document-specific validation supplies required fields; a preference cannot remove one.
    public bool ShowSection(string key,bool required) => required || OptionalSections.GetValueOrDefault(key,true);
}
