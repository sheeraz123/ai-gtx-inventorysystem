using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class CompanyPresentationStore(DevelopmentDatabase database) : ICompanyPresentationStore
{
    public async Task<PresentationResult> ExecuteAsync(PresentationRequest request, AuthenticatedIdentity actor,
        ManagedLogoImage? image, CancellationToken ct)
    {
        try
        {
            await using var db = database.OpenExisting();
            await using var transaction = await db.BeginTransactionAsync(ct);
            var installation = await db.Set<Installation>().SingleAsync(ct);
            if (installation.Id != database.ExpectedInstallationId() || installation.SetupState != "Configured" ||
                installation.DatabaseMode != "PlaintextDevelopment") return new(CompanyOutcome.Unavailable);
            var user = await db.Set<User>().SingleOrDefaultAsync(x => x.Id == actor.UserId, ct);
            if (user?.IsActive != true || user.IsLocked || user.MustChangePassword || user.Version != actor.Version) return new(CompanyOutcome.SessionRequired);

            var company = await db.Set<Company>().SingleOrDefaultAsync(x => x.Id == request.CompanyId && x.IsActive &&
                db.Set<CompanySelection>().Any(s => s.CompanyId == x.Id), ct);
            if (company is null) return new(CompanyOutcome.NotFound, "Select this active company as Current in Company Master.");
            var admin=await ScopedAccess.IsSuperAdminAsync(db,actor.UserId,ct);
            var permissions=admin?null:await ScopedAccess.ResolveAsync(db,actor.UserId,company.Id,ct);
            bool Can(string permission)=>admin || permissions!.Contains("Company."+permission);
            var allowed=request.Action switch
            {
                PresentationAction.GetBank=>Can("BankView") || Can("BankManage"),
                PresentationAction.SaveBank or PresentationAction.SetInvoiceBank=>Can("BankManage"),
                PresentationAction.SaveLogo or PresentationAction.RemoveLogo=>Can("BrandingManage"),
                PresentationAction.SaveTax or PresentationAction.SaveDefaults=>Can("InvoiceSettingsManage"),
                PresentationAction.Snapshot=>Can("BankView") || Can("BankManage"),
                _=>true
            };
            if (!Can("View") || !allowed) return new(CompanyOutcome.Forbidden);
            var settings = await db.Set<CompanySettings>().SingleOrDefaultAsync(s => s.CompanyId == company.Id, ct);
            var logos = db.Set<BrandingAsset>().Where(x => x.CompanyId == company.Id && (x.Kind == "Logo1" || x.Kind == "Logo2"));
            var banks = db.Set<CompanyBankAccount>().Where(x => x.CompanyId == company.Id);
            var taxes = db.Set<GstRegistration>().Where(x => x.CompanyId == company.Id);
            var now = DateTime.UtcNow;
            PresentationResult result;
            switch (request.Action)
            {
                case PresentationAction.ListLogos:
                    var primaryLogoId = settings?.Logo1AssetId;
                    var secondaryLogoId = settings?.Logo2AssetId;
                    var logoSearch = request.Search?.Trim().ToUpperInvariant() ?? "";
                    var visibleLogos = logos;
                    if (logoSearch.Length >= 3)
                        visibleLogos = visibleLogos.Where(x =>
                            (x.LogoName ?? (x.Kind == "Logo2" ? "Secondary company logo" : "Company logo")).ToUpper().Contains(logoSearch) ||
                            (x.LogoUsage ?? (x.Kind == "Logo2" ? "Secondary company" : "Primary company")).ToUpper().Contains(logoSearch));
                    var rows = await visibleLogos.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                        .Select(x => new { x.Id, x.LogoName, x.LogoUsage, x.Kind, x.SortOrder, x.IsActive, x.IsRetired,
                            Preview = x.Thumbnail ?? (x.Id == primaryLogoId || x.Id == secondaryLogoId ? x.Content : null) })
                        .ToListAsync(ct);
                    return new(CompanyOutcome.Success, Logos: rows.Select(x => new LogoListItem(x.Id,
                        LogoName(x.LogoName, x.Kind), LogoUsage(x.LogoUsage, x.Kind), x.SortOrder,
                        x.IsRetired ? "Removed" : x.IsActive ? "Active" : "Inactive",
                        x.Id == settings?.Logo1AssetId ? "Primary" : x.Id == settings?.Logo2AssetId ? "Secondary" : "", x.Preview)).ToArray());
                case PresentationAction.ListBanks:
                    var search = request.Search?.Trim().ToUpperInvariant() ?? "";
                    if (search.Length >= 3)
                        banks = banks.Where(x => x.BankName.ToUpper().Contains(search) || x.AccountHolder.ToUpper().Contains(search) ||
                            x.Ifsc.ToUpper().Contains(search) || x.Branch != null && x.Branch.ToUpper().Contains(search));
                    var list = await banks.OrderBy(x => x.DisplayOrder).ThenBy(x => x.BankName).ThenBy(x => x.Id)
                        .Select(x => new BankListItem(x.Id, x.BankName, x.AccountHolder,
                            x.AccountNumber.Length > 4 ? "•••• " + x.AccountNumber.Substring(x.AccountNumber.Length - 4) : "••••",
                            x.Ifsc, x.Branch, x.AccountType, x.IsActive, x.IsDefault, x.DisplayOrder)).ToListAsync(ct);
                    return new(CompanyOutcome.Success, Banks: list);
                case PresentationAction.GetLogo:
                    var logo = await logos.SingleOrDefaultAsync(x => x.Id == request.Id, ct);
                    return logo is null ? new(CompanyOutcome.NotFound) : new(CompanyOutcome.Success, Logo: Detail(logo));
                case PresentationAction.GetBank:
                    var bank = await banks.SingleOrDefaultAsync(x => x.Id == request.Id, ct);
                    return bank is null ? new(CompanyOutcome.NotFound) : new(CompanyOutcome.Success, Bank: Detail(bank));
                case PresentationAction.GetDefaults:
                    return new(CompanyOutcome.Success, Defaults: await ReadDefaults(db, settings, banks, ct), CompanyVersion: company.Version);
                case PresentationAction.Snapshot:
                    return await Snapshot(db, company, settings, logos, banks, taxes, now, ct);
                case PresentationAction.ListTax:
                    var taxQuery=request.Search?.Trim().ToUpperInvariant() ?? "";
                    var matching=taxQuery.Length>=3
                        ? taxes.Where(x=>x.Gstin.Contains(taxQuery) || x.StateCode.Contains(taxQuery)) : taxes;
                    var taxRows=await matching.OrderByDescending(x=>x.IsInvoiceDefault).ThenBy(x=>x.Gstin).ToListAsync(ct);
                    return new(CompanyOutcome.Success,TaxRows:taxRows.Select(TaxDetail).ToArray());
                case PresentationAction.GetTax:
                    var registration=await taxes.SingleOrDefaultAsync(x=>x.Id==request.Id,ct);
                    return registration is null ? new(CompanyOutcome.NotFound) : new(CompanyOutcome.Success,Tax:TaxDetail(registration));
                case PresentationAction.SaveTax:
                    result=await WriteTax(db,request,company.Id,taxes,ct);
                    break;
                case PresentationAction.SaveLogo:
                case PresentationAction.RemoveLogo:
                    result = await WriteLogo(db, request, image, company.Id, actor.UserId, settings, logos, now, ct);
                    break;
                case PresentationAction.SaveBank:
                    result = await WriteBank(db, request, company.Id, actor.UserId, banks, now, ct);
                    break;
                case PresentationAction.SetInvoiceBank:
                    if (request.Version!=company.Version) return new(CompanyOutcome.Conflict);
                    result=await SetDefaultBankAsync(db,banks,request.Id,actor.UserId,ct);
                    break;
                case PresentationAction.SaveDefaults:
                    if (request.Version != company.Version) return new(CompanyOutcome.Conflict);
                    if (!Can("BankManage") && request.Defaults!.BankId != await banks.Where(x=>x.IsDefault && x.IsActive)
                        .Select(x=>(Guid?)x.Id).SingleOrDefaultAsync(ct)) return new(CompanyOutcome.Forbidden);
                    result = await WriteDefaults(db, request.Defaults!, company.Id, actor.UserId, settings, logos, banks, ct);
                    break;
                default:
                    return new(CompanyOutcome.InvalidInput);
            }

            if (result.Outcome != CompanyOutcome.Success) return result;
            company.UpdatedUtc = now;
            company.UpdatedByUserId = actor.UserId;
            db.Entry(company).Property(x => x.UpdatedUtc).IsModified = true;
            db.Add(new AuditEvent { CompanyId = company.Id, ActorUserId = actor.UserId, ActorKind = "User",
                Action = "CompanyPresentation" + request.Action, RecordType = "Company",
                RecordId = company.Id.ToString(), ChangesJson = "{}" });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result with { CompanyVersion=company.Version };
        }
        catch (OperationCanceledException) { throw; }
        catch (DbUpdateConcurrencyException) { return new(CompanyOutcome.Conflict); }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 }) { return new(CompanyOutcome.Conflict); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { return new(CompanyOutcome.Conflict); }
        catch (Exception e) when (e is SqliteException or DbUpdateException or IOException or InvalidOperationException or OverflowException)
        { return new(CompanyOutcome.Unavailable); }
    }

    private static CompanyTaxDetail TaxDetail(GstRegistration row) =>
        new(row.Id,row.Version,new(row.Gstin,row.StateCode,row.IsActive,row.IsInvoiceDefault));

    private static async Task<PresentationResult> WriteTax(BillingDbContext db,PresentationRequest request,Guid company,
        IQueryable<GstRegistration> taxes,CancellationToken ct)
    {
        var draft=request.Tax!;
        var row=request.Id is Guid id ? await taxes.SingleOrDefaultAsync(x=>x.Id==id,ct) : null;
        if (request.Id is not null && row is null) return new(CompanyOutcome.NotFound);
        if (row is not null && row.Version!=request.Version) return new(CompanyOutcome.Conflict);
        var gstin=draft.Gstin.Trim().ToUpperInvariant();
        if (row is not null && (row.Gstin!=gstin || row.StateCode!=draft.StateCode))
            return Invalid("GSTIN and state are fixed after saving. Add the corrected registration and deactivate this one.");
        var ownId=row?.Id;
        if (await db.Set<GstRegistration>().AnyAsync(x=>x.Gstin==gstin && x.Id!=ownId,ct))
            return Invalid("That GSTIN is already registered. Open the existing registration to make changes.");
        if (draft.Default)
        {
            var previous=await taxes.SingleOrDefaultAsync(x=>x.IsInvoiceDefault && x.Id!=ownId,ct);
            if (previous is not null)
            {
                previous.IsInvoiceDefault=false;
                await db.SaveChangesAsync(ct);
            }
        }
        if (row is null)
        {
            row=new GstRegistration { CompanyId=company };
            db.Add(row);
        }
        row.Gstin=gstin;
        row.StateCode=draft.StateCode;
        row.IsActive=draft.Active;
        row.IsInvoiceDefault=draft.Default;
        if(draft.LinkDefaultBillingAddress)
        {
            var matches=await db.Set<CompanyAddress>().Where(x=>x.CompanyId==company &&
                x.IsDefaultBilling && x.StateCode==draft.StateCode && x.CountryCode=="IN").Select(x=>x.Id).Take(2).ToArrayAsync(ct);
            if(matches.Length!=1)
                return Invalid("Set one default company billing address in the same state as this GST registration before linking.");
            var addressId=matches[0];
            if(!await db.Set<GstRegistrationAddress>().AnyAsync(x=>x.CompanyId==company &&
                x.GstRegistrationId==row.Id && x.AddressId==addressId,ct))
                db.Add(new GstRegistrationAddress {CompanyId=company,GstRegistrationId=row.Id,AddressId=addressId});
        }
        return new(CompanyOutcome.Success);
    }

    private static string LogoName(string? name, string kind) => name ?? (kind == "Logo2" ? "Secondary company logo" : "Company logo");
    private static string LogoUsage(string? usage, string kind) => usage ?? (kind == "Logo2" ? "Secondary company" : "Primary company");
    private static LogoDetail Detail(BrandingAsset x) => new(x.Id, x.Version,
        new(LogoName(x.LogoName, x.Kind), LogoUsage(x.LogoUsage, x.Kind), x.SortOrder, x.IsActive), x.Content, x.IsRetired);
    private static BankDetail Detail(CompanyBankAccount x) => new(x.Id, x.Version,
        new(x.BankName, x.AccountHolder, x.AccountNumber, x.Ifsc, x.Branch, x.AccountType, x.UpiId,
            x.BankAddress, x.PaymentInstructions, x.DisplayOrder, x.IsActive));

    private static async Task<PresentationResult> WriteLogo(BillingDbContext db, PresentationRequest request, ManagedLogoImage? image,
        Guid company, Guid actor, CompanySettings? settings, IQueryable<BrandingAsset> logos, DateTime now, CancellationToken ct)
    {
        var existing = request.Id is null ? null : await logos.SingleOrDefaultAsync(x => x.Id == request.Id, ct);
        if (request.Id is not null && existing is null) return new(CompanyOutcome.NotFound);
        if (existing is not null && existing.Version != request.Version) return new(CompanyOutcome.Conflict);
        if (existing?.IsRetired == true) return Invalid("Removed logos are retained for history and cannot be edited.");

        if (request.Action == PresentationAction.RemoveLogo)
        {
            if (existing is null) return new(CompanyOutcome.NotFound);
            if (settings?.Logo1AssetId == existing.Id || settings?.Logo2AssetId == existing.Id ||
                await db.Set<SnapshotAsset>().AnyAsync(x => x.CompanyId == company && x.BrandingAssetId == existing.Id, ct))
                return Invalid("Remove the invoice selection first. Logos referenced by historical snapshots can be deactivated but cannot be removed.");
            existing.IsActive = false;
            existing.IsRetired = true;
            existing.UpdatedUtc = now;
            existing.UpdatedByUserId = actor;
            return new(CompanyOutcome.Success);
        }

        var draft = request.Logo!;
        if (existing is null && image is null) return Invalid("Choose a PNG or JPEG for the new logo.");
        if (draft.Active && image is null && existing?.Content is null) return Invalid("Upload an image before activating this logo.");

        var target = existing!;
        if (image is not null)
        {
            var kind = existing?.Kind ?? "Logo1";
            var revision = (await logos.Where(x => x.Kind == kind).MaxAsync(x => (int?)x.Revision, ct) ?? 0) + 1;
            target = new BrandingAsset { CompanyId = company, Kind = kind, Revision = revision, StorageId = Guid.NewGuid().ToString("N"),
                Sha256 = Convert.ToHexString(SHA256.HashData(image.Content)), Content = image.Content, ContentType = image.ContentType,
                Thumbnail = image.Thumbnail, CreatedUtc = now, ChangedByUserId = actor };
            db.Add(target);
        }

        if (existing is not null && image is not null) await db.SaveChangesAsync(ct);
        if (existing is not null && settings is not null)
        {
            var nextId = draft.Active ? (Guid?)target.Id : null;
            if (settings.Logo1AssetId == existing.Id) settings.Logo1AssetId = nextId;
            if (settings.Logo2AssetId == existing.Id) settings.Logo2AssetId = nextId;
        }
        // Save the new asset and any selection change before altering the active flag on the old asset.
        if (existing is not null && (image is not null || !draft.Active)) await db.SaveChangesAsync(ct);
        if (existing is not null && image is not null)
        {
            existing.IsActive = false;
            existing.IsRetired = true;
            existing.UpdatedUtc = now;
            existing.UpdatedByUserId = actor;
        }
        target.LogoName = draft.Name.Trim();
        target.LogoUsage = draft.Usage;
        target.SortOrder = draft.Order;
        target.IsActive = draft.Active;
        target.UpdatedUtc = now;
        target.UpdatedByUserId = actor;
        return new(CompanyOutcome.Success);
    }

    private static async Task<PresentationResult> WriteBank(BillingDbContext db, PresentationRequest request,
        Guid company, Guid actor, IQueryable<CompanyBankAccount> banks, DateTime now, CancellationToken ct)
    {
        var bank = request.Id is null ? null : await banks.SingleOrDefaultAsync(x => x.Id == request.Id, ct);
        if (request.Id is not null && bank is null) return new(CompanyOutcome.NotFound);
        if (bank is not null && bank.Version != request.Version) return new(CompanyOutcome.Conflict);
        var draft = request.Bank!;
        var number = draft.AccountNumber.Trim().Replace(" ", "").Replace("-", "");
        var ifsc = draft.Ifsc.Trim().ToUpperInvariant();
        var upi = Clean(draft.Upi);
        if (draft.AccountNumber != bank?.AccountNumber && !Regex.IsMatch(number, @"\A[0-9]{6,34}\z"))
            return Invalid("Enter a 6–34 digit account number. Spaces and hyphens are allowed as separators.");
        if (!string.IsNullOrEmpty(ifsc) && draft.Ifsc != bank?.Ifsc && !Regex.IsMatch(ifsc, @"\A[A-Z]{4}0[A-Z0-9]{6}\z"))
            return Invalid("IFSC must be 11 characters: four letters, zero, then six letters or digits.");
        if (upi is not null && draft.Upi != bank?.UpiId && !Regex.IsMatch(upi, @"\A[A-Za-z0-9._-]{2,100}@[A-Za-z0-9.-]{2,60}\z"))
            return Invalid("Enter a UPI ID in name@provider format.");
        var changeIdentity = bank is null || draft.AccountNumber != bank.AccountNumber || draft.Ifsc != bank.Ifsc;
        var editingId = bank?.Id;
        if (changeIdentity && await banks.AnyAsync(x => x.Id != editingId &&
            x.AccountNumber.Trim().Replace(" ", "").Replace("-", "") == number &&
            x.Ifsc.Trim().ToUpper() == ifsc, ct))
            return Invalid("An account with that number and IFSC already exists in this company.");

        if (bank is null)
        {
            bank = new CompanyBankAccount { CompanyId = company, CreatedUtc = now, CreatedByUserId = actor };
            db.Add(bank);
        }
        if (changeIdentity)
        {
            if (draft.AccountNumber != bank.AccountNumber) bank.AccountNumber = number;
            if (draft.Ifsc != bank.Ifsc) bank.Ifsc = ifsc;
        }
        bank.BankName = draft.BankName.Trim();
        bank.AccountHolder = draft.AccountHolder.Trim();
        bank.Branch = Clean(draft.Branch);
        bank.AccountType = Clean(draft.AccountType);
        bank.UpiId = upi;
        bank.BankAddress = Clean(draft.Address);
        bank.PaymentInstructions = Clean(draft.Instructions);
        bank.DisplayOrder = draft.Order;
        bank.IsActive = draft.Active;
        if (!bank.IsActive) bank.IsDefault = false;
        bank.UpdatedUtc = now;
        bank.UpdatedByUserId = actor;
        return new(CompanyOutcome.Success);
    }

    private static async Task<InvoiceDefaults> ReadDefaults(BillingDbContext db, CompanySettings? settings,
        IQueryable<CompanyBankAccount> banks, CancellationToken ct)
    {
        var bankId = await banks.Where(x => x.IsDefault && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        var terms = settings?.TermsVersionId is Guid id ? await db.Set<TermsVersion>()
            .Where(x => x.CompanyId == settings.CompanyId && x.Id == id).Select(x => x.Text).SingleAsync(ct) : null;
        return new(settings?.Logo1AssetId, settings?.Logo2AssetId, bankId, settings?.ShowPrimaryLogo == true, settings?.ShowSecondaryLogo == true,
            bankId is not null, settings?.ShowUpi == true, settings?.InvoiceHeader, settings?.InvoiceFooter, settings?.SignatoryName, terms);
    }

    private static async Task<PresentationResult> WriteDefaults(BillingDbContext db, InvoiceDefaults draft, Guid company, Guid actor,
        CompanySettings? settings, IQueryable<BrandingAsset> logos, IQueryable<CompanyBankAccount> banks, CancellationToken ct)
    {
        if (draft.PrimaryLogo is Guid primary && draft.SecondaryLogo == primary) return Invalid("Choose two distinct logos, or leave either slot empty.");
        var ids = new[] { draft.PrimaryLogo, draft.SecondaryLogo }.OfType<Guid>().ToArray();
        if (await logos.CountAsync(x => ids.Contains(x.Id) && x.IsActive && !x.IsRetired && x.Content != null, ct) != ids.Length)
            return Invalid("Invoice logos must be active images from the current company.");
        var bankResult=await SetDefaultBankAsync(db,banks,draft.BankId,actor,ct);
        if (bankResult.Outcome!=CompanyOutcome.Success) return bankResult;
        if (settings is null)
        {
            settings = new CompanySettings { CompanyId = company };
            db.Add(settings);
        }
        settings.Logo1AssetId = draft.PrimaryLogo;
        settings.Logo2AssetId = draft.SecondaryLogo;
        settings.ShowPrimaryLogo = draft.ShowPrimary;
        settings.ShowSecondaryLogo = draft.ShowSecondary;
        settings.ShowBankDetails = draft.BankId is not null;
        settings.ShowUpi = draft.ShowUpi;
        settings.InvoiceHeader = Clean(draft.Header);
        settings.InvoiceFooter = Clean(draft.Footer);
        settings.SignatoryName = Clean(draft.Signatory);
        var currentTerms = settings.TermsVersionId is Guid termsId ?
            await db.Set<TermsVersion>().SingleAsync(x => x.CompanyId == company && x.Id == termsId, ct) : null;
        var terms = Clean(draft.Terms);
        if (terms != currentTerms?.Text)
        {
            settings.TermsVersionId = null;
            if (terms is not null)
            {
                var revision = (await db.Set<TermsVersion>().Where(x => x.CompanyId == company).MaxAsync(x => (int?)x.Revision, ct) ?? 0) + 1;
                var next = new TermsVersion { CompanyId = company, Revision = revision, Text = terms, ChangedByUserId = actor };
                db.Add(next);
                settings.TermsVersionId = next.Id;
            }
        }
        return new(CompanyOutcome.Success);
    }

    private static async Task<PresentationResult> SetDefaultBankAsync(BillingDbContext db,IQueryable<CompanyBankAccount> banks,
        Guid? bankId,Guid actor,CancellationToken ct)
    {
        var selected=bankId is Guid id?await banks.SingleOrDefaultAsync(x=>x.Id==id&&x.IsActive,ct):null;
        if(bankId is not null && selected is null) return Invalid("Choose an active bank from the current company or clear the default.");
        var prior = await banks.SingleOrDefaultAsync(x => x.IsDefault, ct);
        if (prior?.Id != selected?.Id)
        {
            if (prior is not null)
            {
                prior.IsDefault = false;
                prior.UpdatedUtc = DateTime.UtcNow;
                prior.UpdatedByUserId = actor;
                await db.SaveChangesAsync(ct);
            }
            if (selected is not null)
            {
                selected.IsDefault = true;
                selected.UpdatedUtc = DateTime.UtcNow;
                selected.UpdatedByUserId = actor;
            }
        }
        return new(CompanyOutcome.Success);
    }

    internal static async Task<PresentationResult> Snapshot(BillingDbContext db, Company company, CompanySettings? settings,
        IQueryable<BrandingAsset> logos, IQueryable<CompanyBankAccount> banks, IQueryable<GstRegistration> taxes, DateTime now, CancellationToken ct)
    {
        var defaults = await ReadDefaults(db, settings, banks, ct);
        var presentation=await SettingsStore.ReadCompanyAsync(db,company.Id,ct);
        async Task<InvoiceLogoImage?> Image(Guid? id, bool show, string? kind = null)
        {
            if (!show || id is null) return null;
            var assets = kind is null ? logos : db.Set<BrandingAsset>().Where(x => x.CompanyId == company.Id && x.Kind == kind);
            var row = await assets.AsNoTracking().Where(x => x.Id == id && x.IsActive && !x.IsRetired)
                .Select(x => new { x.Id, x.Content, x.ContentType }).SingleOrDefaultAsync(ct);
            return row is { Content: { } data, ContentType: "image/png" or "image/jpeg" } ?
                new(row.Id, row.ContentType, ImmutableArray.CreateRange(data)) : null;
        }
        InvoiceBankInformation? bank = null;
        if (defaults.BankId is Guid bankId && (presentation.Get(SettingsRegistry.ShowBank) || defaults.ShowUpi))
        {
            var row = await banks.AsNoTracking().SingleAsync(x => x.Id == bankId && x.IsActive, ct);
            var show=presentation.Get(SettingsRegistry.ShowBank);
            if(show || defaults.ShowUpi && !string.IsNullOrWhiteSpace(row.UpiId))
                bank = new(row.Id,show?row.BankName:null,
                    show && presentation.Get(SettingsRegistry.ShowAccountHolder)?row.AccountHolder:null,
                    show && presentation.Get(SettingsRegistry.ShowAccountNumber)?row.AccountNumber:null,
                    show && presentation.Get(SettingsRegistry.ShowIfsc)?row.Ifsc:null,
                    show && presentation.Get(SettingsRegistry.ShowBranch)?row.Branch:null,show?row.BankAddress:null,
                    show && presentation.Get(SettingsRegistry.ShowPaymentInstructions)?row.PaymentInstructions:null,
                    defaults.ShowUpi?row.UpiId:null);
        }
        var tax=await taxes.Where(x=>x.IsActive && x.IsInvoiceDefault)
            .Select(x=>new InvoiceSellerTax(x.Id,x.Gstin,x.StateCode)).SingleOrDefaultAsync(ct);
        var options=SettingsStore.InvoiceOptions(company,presentation);
        return new(CompanyOutcome.Success, Snapshot: new(company.Id, now, await Image(defaults.PrimaryLogo, defaults.ShowPrimary),
            await Image(defaults.SecondaryLogo, defaults.ShowSecondary), bank, defaults.Header, defaults.Footer, defaults.Signatory, defaults.Terms,tax,
            options,await Image(settings?.SignatureAssetId,options.ShowSection("Invoice.Optional.Signatory",false),"Signature")));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static PresentationResult Invalid(string message) => new(CompanyOutcome.InvalidInput, message);
}
