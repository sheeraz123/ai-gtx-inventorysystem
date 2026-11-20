using System.Collections.Immutable;
using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class SettingsStore(DevelopmentDatabase database,TimeProvider clock) : ISettingsStore
{
    public async Task<LoginPresentation> ReadLoginAsync(CancellationToken ct=default)
    {
        await using var db=database.OpenExisting();
        var allowed=LoginPage.Keys.Values.Select(x=>x.Key).ToArray();
        var rows=await db.Set<SettingOverride>().AsNoTracking().Where(x=>x.Scope=="Application" &&
            x.CompanyId==null && x.UserId==null && allowed.Contains(x.Key)).ToListAsync(ct);
        var values=Resolve(LoginPage.Keys.Values,rows).Values;
        var settings=new ResolvedSettings(values);
        async Task<byte[]?> Asset(string key)
        {
            if(!Guid.TryParse(settings.Get(LoginPage.Keys[key]),out var id)) return null;
            return await db.Set<BrandingAsset>().AsNoTracking().Where(x=>x.Id==id && x.IsActive && !x.IsRetired &&
                db.Set<Company>().Any(c=>c.Id==x.CompanyId && c.IsActive) &&
                (x.Kind=="Logo1" || x.Kind=="Logo2") && (x.ContentType=="image/png" || x.ContentType=="image/jpeg") &&
                x.Content!=null && x.Content.Length<=1024*1024).Select(x=>x.Content).SingleOrDefaultAsync(ct);
        }
        return new(settings,await Asset("Logo"),await Asset("Background"));
    }
    public async Task<SettingsResult> ExecuteAsync(SettingsRequest request,AuthenticatedIdentity actor,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var validity=await ScopedAccess.VerifyAsync(db,database.ExpectedInstallationId(),actor,ct);
            if(validity!=CompanyOutcome.Success) return new(validity);
            var access=await ScopedAccess.SnapshotAsync(db,actor.UserId,ct);
            var company=access.CurrentCompanyId;
            if(company is Guid selected && !await db.Set<Company>().AnyAsync(x=>x.Id==selected && x.IsActive,ct))
                company=null;
            if(request.Action==SettingsAction.Security)
                return new(access.Allows("Settings.SecurityView",company)?CompanyOutcome.Success:CompanyOutcome.Forbidden,
                    CurrentCompanyId:company);
            if(request.Scope==SettingScope.Company && request.Action!=SettingsAction.Runtime &&
                (company is null || request.ExpectedCompanyId!=company))
                return new(CompanyOutcome.Conflict,Message:"Select the active company and reopen Settings.");
            if(request.Action!=SettingsAction.Runtime && request.Scope!=SettingScope.User &&
                !access.Allows("Settings.View",company)) return new(CompanyOutcome.Forbidden);
            if(request.Action==SettingsAction.SalesFormHistory)
            {
                if(company is null || request.Scope!=SettingScope.Company || !access.Allows("Settings.InvoiceEdit",company))
                    return new(CompanyOutcome.Forbidden);
                var history=await db.Set<AuditEvent>().AsNoTracking().Where(x=>x.CompanyId==company && x.Action=="SalesInvoiceLayoutSaved")
                    .OrderByDescending(x=>x.OccurredUtc).ThenByDescending(x=>x.Id).Take(50)
                    .Select(x=>new SalesFormVersion(x.Id,x.OccurredUtc,x.ActorUserId,x.ChangesJson)).ToArrayAsync(ct);
                return new(CompanyOutcome.Success,CurrentCompanyId:company,SalesFormVersions:history);
            }

            var values=await ReadValues(db,company,actor.UserId,ct);
            if(request.Action==SettingsAction.Runtime)
            {
                var definitions=SettingsRegistry.Definitions.Where(x=>(!x.AffectsPrint || x.Section==SettingsRegistry.Sales) &&
                    (x.Section!=SettingsRegistry.Product || access.Allows("Product.View",company)) &&
                    (x.Section!=SettingsRegistry.Sales || access.Allows("Customer.View",company)));
                return new(CompanyOutcome.Success,Runtime:Resolve(definitions,values),CurrentCompanyId:company);
            }
            if(request.Action==SettingsAction.Save)
            {
                foreach(var change in request.Changes??[])
                {
                    if(change.Key is not ("Login.Logo" or "Login.Background") || string.IsNullOrEmpty(change.Text)) continue;
                    if(!access.IsSuperAdmin) return new(CompanyOutcome.Forbidden);
                    if(company is not Guid owner || !Guid.TryParse(change.Text,out var asset) ||
                        !await db.Set<BrandingAsset>().AnyAsync(x=>x.Id==asset && x.CompanyId==owner && x.IsActive &&
                            !x.IsRetired && (x.Kind=="Logo1" || x.Kind=="Logo2") &&
                            (x.ContentType=="image/png" || x.ContentType=="image/jpeg") && x.Content!=null &&
                            x.Content.Length<=1024*1024,ct))
                        return new(CompanyOutcome.InvalidInput,Message:"Choose an active branding image from the selected company.",InvalidKey:change.Key);
                }
                var outcome=Save(db,actor.UserId,access,company,request,values);
                if(outcome.Outcome!=CompanyOutcome.Success) return outcome;
                await db.SaveChangesAsync(ct);
                if(request.Scope==SettingScope.Company && request.Changes?.Any(x=>x.Key.StartsWith("SalesForm.",StringComparison.Ordinal))==true)
                {
                    var changed=await ReadCompanyAsync(db,company!.Value,ct);
                    var layout=SalesFormConfiguration.From(changed);
                    if(layout.HiddenRequiredField() is { } invalid)
                        return new(CompanyOutcome.InvalidInput,InvalidKey:invalid.Setting.Key,
                            Message:"Show the payment, transport and notes section before making a field in it required.");
                    db.Add(new AuditEvent {CompanyId=company,ActorUserId=actor.UserId,ActorKind="User",RecordType="Settings",
                        RecordId=SalesFormConfiguration.Section,Action="SalesInvoiceLayoutSaved",OccurredUtc=clock.GetUtcNow().UtcDateTime,
                        ChangesJson=JsonSerializer.Serialize(layout.Rules)});
                    await db.SaveChangesAsync(ct);
                }
                await transaction.CommitAsync(ct);
                return outcome;
            }

            var eligible=SettingsRegistry.Definitions.Where(d=>d.IsEditable && d.Scopes.HasFlag(request.Scope) &&
                (request.Section is null || d.Section==request.Section) &&
                (d.Section!=LoginPage.Section || access.IsSuperAdmin) &&
                (d.Key!=SettingsRegistry.InvoiceTemplate.Key || access.Allows("InvoiceTemplate.View",company))).ToArray();
            var rows=values.Where(v=>v.Scope==request.Scope.ToString()).ToDictionary(x=>x.Key,StringComparer.Ordinal);
            var editorIds=rows.Values.Select(x=>x.UpdatedByUserId).Distinct().ToArray();
            var editors=await db.Set<User>().Where(u=>editorIds.Contains(u.Id)).ToDictionaryAsync(u=>u.Id,u=>u.Username,ct);
            var resolution=Resolve(eligible,values.Where(v=>InScope(v.Scope,request.Scope)));
            return new(CompanyOutcome.Success,Entries:eligible.Select(d=>
            {
                var stored=rows.GetValueOrDefault(d.Key);
                return new SettingEntry(d,request.Scope,resolution.Text(d.Key,d.DefaultText),stored?.Value,stored?.Version,
                    stored?.UpdatedUtc,stored is null?null:editors.GetValueOrDefault(stored.UpdatedByUserId),
                    CanEdit(d,request.Scope,access,company));
            }).ToArray(),CurrentCompanyId:company);
        }
        catch(DbUpdateConcurrencyException) { return new(CompanyOutcome.Conflict); }
        catch(DbUpdateException e) when(e.InnerException is SqliteException {SqliteErrorCode:19}) { return new(CompanyOutcome.Conflict); }
        catch(Exception e) when(e is SqliteException or DbUpdateException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
    }

    private SettingsResult Save(BillingDbContext db,Guid actor,AccessSnapshot access,Guid? company,
        SettingsRequest request,List<SettingOverride> values)
    {
        var scope=request.Scope.ToString();
        var rows=values.Where(x=>x.Scope==scope).ToDictionary(x=>x.Key,StringComparer.Ordinal);
        foreach(var change in request.Changes ?? [])
        {
            if(!SettingsRegistry.ByKey.TryGetValue(change.Key,out var definition) ||
                !definition.Scopes.HasFlag(request.Scope) || !CanEdit(definition,request.Scope,access,company))
                return new(CompanyOutcome.Forbidden);
            if(request.Section is not null && request.Section!=definition.Section) return new(CompanyOutcome.InvalidInput);
            var row=rows.GetValueOrDefault(change.Key);
            if(row?.Version!=change.Version) return new(CompanyOutcome.Conflict,InvalidKey:change.Key);
            if(change.Text is not null && (!definition.TryNormalize(change.Text,out var normalized) || normalized!=change.Text))
                return new(CompanyOutcome.InvalidInput,InvalidKey:change.Key);
            if(string.Equals(row?.Value,change.Text,StringComparison.Ordinal)) continue;
            var previous=row?.Value;
            if(row is null)
            {
                row=new SettingOverride {Scope=scope,Key=change.Key,CompanyId=request.Scope==SettingScope.Company?company:null,
                    UserId=request.Scope==SettingScope.User?actor:null};
                db.Add(row);
            }
            row.Value=change.Text;
            row.UpdatedUtc=clock.GetUtcNow().UtcDateTime;
            row.UpdatedByUserId=actor;
            var safeValues=definition.Kind is SettingKind.Boolean or SettingKind.Number or SettingKind.Choice ||
                definition.Section==SalesFormConfiguration.Section;
            db.Add(new AuditEvent {ActorUserId=actor,ActorKind="User",CompanyId=row.CompanyId,RecordType="Settings",
                RecordId=change.Key,Action=change.Text is null?"SettingRestored":"SettingUpdated",OccurredUtc=row.UpdatedUtc,
                ChangesJson=JsonSerializer.Serialize(new {outcome="Success",scope,oldValue=safeValues?previous:null,newValue=safeValues?change.Text:null})});
        }
        return new(CompanyOutcome.Success,CurrentCompanyId:company);
    }

    private static bool CanEdit(SettingDefinition definition,SettingScope scope,AccessSnapshot access,Guid? company) =>
        definition.IsEditable && !definition.IsSensitive && scope switch
        {
            SettingScope.Application=>access.IsSuperAdmin,
            SettingScope.User=>definition.Scopes.HasFlag(SettingScope.User),
            SettingScope.Company=>company is not null && definition.EditPermission is not null && access.Allows(definition.EditPermission,company) &&
                (definition.Key!=SettingsRegistry.InvoiceTemplate.Key || access.Allows("InvoiceTemplate.View",company)),
            _=>false
        };

    private static bool InScope(string value,SettingScope scope)=>value==SettingScope.Application.ToString() ||
        value==scope.ToString() || scope==SettingScope.User && value==SettingScope.Company.ToString();

    private static Task<List<SettingOverride>> ReadValues(BillingDbContext db,Guid? company,Guid? user,CancellationToken ct)=>
        db.Set<SettingOverride>().Where(v=>v.Scope=="Application" ||
            company!=null && v.Scope=="Company" && v.CompanyId==company ||
            user!=null && v.Scope=="User" && v.UserId==user).ToListAsync(ct);

    private static ResolvedSettings Resolve(IEnumerable<SettingDefinition> definitions,IEnumerable<SettingOverride> rows)
    {
        var result=ImmutableDictionary.CreateBuilder<string,string>(StringComparer.Ordinal);
        var byScope=rows.ToLookup(x=>x.Key,StringComparer.Ordinal);
        foreach(var key in definitions)
        {
            var valid=byScope[key.Key].Where(x=>x.Value is not null && Enum.TryParse<SettingScope>(x.Scope,out var scope) &&
                key.Scopes.HasFlag(scope)).OrderByDescending(x=>x.Scope=="User"?3:x.Scope=="Company"?2:1)
                .FirstOrDefault(x=>key.TryNormalize(x.Value!,out _));
            var value=valid?.Value ?? key.DefaultText;
            result[key.Key]=key.Key==SettingsRegistry.InvoiceTemplate.Key?InvoiceTemplates.ForNewInvoice(value):value;
        }
        return new(result.ToImmutable());
    }

    internal static async Task<ResolvedSettings> ReadCompanyAsync(BillingDbContext db,Guid company,CancellationToken ct) =>
        Resolve(SettingsRegistry.Definitions.Where(x=>x.Scopes.HasFlag(SettingScope.Company)),await ReadValues(db,company,null,ct));

    internal static InvoicePresentationOptions InvoiceOptions(Company company,ResolvedSettings settings)
    {
        var captions=SettingsRegistry.Definitions.Where(x=>x.IsDisplayText && x.AffectsPrint)
            .ToImmutableDictionary(x=>x.Key,x=>settings.Text(x.Key,x.DefaultText),StringComparer.Ordinal);
        var optional=SettingsRegistry.OptionalSections.ToImmutableDictionary(x=>x.Key,x=>settings.Get(x),StringComparer.Ordinal);
        string Get(SettingKey<string> key)=>settings.Get(key);
        var margins=SettingsRegistry.PrintMargins.Select(settings.Get).ToArray();
        var tagline=Get(SettingsRegistry.InvoiceTagline);
        return new(Get(SettingsRegistry.PrintedCompanyName)=="Display name" && !string.IsNullOrWhiteSpace(company.DisplayName)
                ?company.DisplayName:company.LegalName,
            tagline.Length==0?company.Tagline ?? "":tagline,captions,optional,Get(SettingsRegistry.InvoiceDateFormat),
            settings.Get(SettingsRegistry.InvoiceQuantity),settings.Get(SettingsRegistry.InvoiceRate),settings.Get(SettingsRegistry.InvoiceAmount),
            settings.Get(SettingsRegistry.TaxPlaces),Get(SettingsRegistry.InvoiceCurrency),settings.Get(SettingsRegistry.Thousands),
            Get(SettingsRegistry.TaxSummary),Get(SettingsRegistry.InvoiceRemarks),Get(SettingsRegistry.FooterNotes),
            new(Get(SettingsRegistry.Printer) is {Length:>0} printer?printer:null,Get(SettingsRegistry.Paper),Get(SettingsRegistry.Orientation),
                settings.Get(SettingsRegistry.Copies),settings.Get(SettingsRegistry.Preview),settings.Get(SettingsRegistry.PageNumbers),
                settings.Get(SettingsRegistry.PrintTime),margins[0],margins[1],margins[2],margins[3],settings.Get(SettingsRegistry.HeaderSpacing),
            settings.Get(SettingsRegistry.FooterSpacing),Get(SettingsRegistry.CopyLabel)),Get(SettingsRegistry.InvoiceTemplate),
            SalesFormConfiguration.From(settings));
    }
}
