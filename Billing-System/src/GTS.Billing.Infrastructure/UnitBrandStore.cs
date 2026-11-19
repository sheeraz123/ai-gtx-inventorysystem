using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class UnitBrandStore(DevelopmentDatabase database) : IUnitBrandStore
{
    public async Task<MeasureResult> ExecuteAsync(MeasureRequest request, AuthenticatedIdentity identity, CancellationToken ct)
    {
        try
        {
            await using var db = database.OpenExisting();
            await using var transaction = await db.BeginTransactionAsync(ct);
            var verification = await ScopedAccess.VerifyAsync(db, database.ExpectedInstallationId(), identity, ct);
            if (verification != CompanyOutcome.Success) return new(verification);
            var company = await db.Set<CompanySelection>().Where(s => db.Set<Company>()
                .Any(c => c.Id == s.CompanyId && c.IsActive)).Select(x => (Guid?)x.CompanyId).SingleOrDefaultAsync(ct);
            if (company is not Guid companyId) return Fail("Select an active company first.");
            if ((request.Action != MeasureAction.List || request.Company.HasValue) && request.Company != companyId)
                return new(CompanyOutcome.Conflict, Message:"The current company changed. Refresh and try again.");
            var actor = identity.UserId;
            var admin = await ScopedAccess.IsSuperAdminAsync(db, actor, ct);
            var permissions = admin ? null : await ScopedAccess.ResolveAsync(db, actor, companyId, ct);
            bool Can(string permission) => admin || permissions!.Contains(permission);
            var product = request.Action is MeasureAction.Product or MeasureAction.SaveProduct;
            var module = product ? "Product" : request.Brands ? "Brand" : "Unit";
            var action = request.Action switch
            {
                MeasureAction.Save => request.Id is null ? "Create" : "Edit",
                MeasureAction.SaveProduct => "Edit",
                MeasureAction.Status => "StatusChange",
                _ => "View"
            };
            if (!Can(module + ".View") || !Can(module + "." + action)) return new(CompanyOutcome.Forbidden);
            var result = product ? await Integrate(db, request, companyId, actor, ct)
                : request.Brands ? await Master<Brand>(db, request, companyId, actor, ct)
                : await Master<UnitOfMeasure>(db, request, companyId, actor, ct);
            if (result.Outcome == CompanyOutcome.Success && request.Action is MeasureAction.Save or MeasureAction.Status or MeasureAction.SaveProduct)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return result;
        }
        catch (DbUpdateConcurrencyException) { return new(CompanyOutcome.Conflict); }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
        { return new(CompanyOutcome.Conflict, Message:"A duplicate or conflicting reference was detected. Refresh and review the values."); }
        catch (Exception e) when (e is SqliteException or DbUpdateException or IOException or InvalidOperationException)
        { return new(CompanyOutcome.Unavailable); }
    }

    private static async Task<MeasureResult> Master<T>(BillingDbContext db, MeasureRequest request, Guid company, Guid actor, CancellationToken ct)
        where T : CompanyRecord, ICatalogMaster, new()
    {
        var records = db.Set<T>().Where(x => x.CompanyId == company);
        if (request.Action == MeasureAction.List)
        {
            var query = CatalogRules.Normalize(request.Search);
            if (request.Active is bool active) records = records.Where(x => x.Active == active);
            if (query.Length >= 3) records = records.Where(x => x.Code.Contains(query) || x.NormalizedName.Contains(query));
            // The list excludes binary logos. Only a single detail request loads logo bytes.
            var rows = await records.OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
                .Select(x => new MeasureMaster(x.Id, x.Version, x.Code, x.Name, x.Active, x.Description, x.SortOrder,
                    null, "Count", 0, null, null, null, x.CreatedUtc, x.UpdatedUtc)).ToArrayAsync(ct);
            return new(CompanyOutcome.Success, company, Rows:rows);
        }
        var existing = request.Id is Guid id ? await records.SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (request.Id.HasValue && existing is null) return new(CompanyOutcome.NotFound);
        if (request.Action == MeasureAction.View)
            return existing is null ? new(CompanyOutcome.NotFound) : new(CompanyOutcome.Success, company, Rows:[Detail(existing)]);
        if (existing is not null && request.Version != existing.Version) return new(CompanyOutcome.Conflict);
        var now = DateTime.UtcNow;
        if (request.Action == MeasureAction.Status)
        {
            if (existing is null || request.Active is not bool active) return new(CompanyOutcome.InvalidInput);
            if (!active && existing is UnitOfMeasure unit &&
                (await db.Set<ProductMeasureProfile>().AnyAsync(x => x.CompanyId == company && x.BaseUnitId == unit.Id, ct) ||
                 await db.Set<ProductUnitConversion>().AnyAsync(x => x.CompanyId == company && x.UnitId == unit.Id && x.Active, ct)))
                return Fail("Remove active product references before deactivating this unit.");
            existing.Active = active;
            existing.UpdatedUtc = now;
            existing.UpdatedByUserId = actor;
            Audit(db, company, actor, existing.Id, typeof(T).Name, "StatusChanged");
            return new(CompanyOutcome.Success, company);
        }

        if (request.Master is not { } input) return new(CompanyOutcome.InvalidInput);
        var errors = new Dictionary<string,string>();
        CatalogRules.ValidateIdentity(input.Code, input.Name, errors);
        CatalogRules.CheckText("Description", input.Description, 500, errors);
        CatalogRules.CheckText("Manufacturer", input.Manufacturer, 160, errors);
        CatalogRules.CheckText("Symbol", input.Symbol, 20, errors);
        CatalogRules.CheckText("UQC", input.Uqc, 20, errors);
        if (errors.Count > 0) return Fail(string.Join(" ", errors.Values));
        if (input.SortOrder < 0) return Fail("Enter a nonnegative sort order.");
        var code = input.Code.Trim().ToUpperInvariant();
        var name = input.Name.Trim();
        var normalized = CatalogRules.Normalize(name);
        if (existing is not null && code != existing.Code) return Fail("The code is fixed once the record is created.");
        if (await records.AnyAsync(x => x.Id != request.Id && (x.Code == code || x.NormalizedName == normalized), ct))
            return Fail("This code or name is already used in the current company.");
        var entity = existing ?? new T { CompanyId=company, CreatedUtc=now, CreatedByUserId=actor };
        if (entity is UnitOfMeasure measure)
        {
            if (!UnitBrandService.UnitTypes.Contains(input.UnitType) || input.Precision is < 0 or > 6)
                return Fail("Choose a unit type and enter decimal precision from 0 to 6.");
            if (existing is not null && (measure.UnitType != input.UnitType || measure.DecimalPrecision != input.Precision ||
                measure.Name != name || measure.Symbol != CatalogRules.Optional(input.Symbol) || measure.Uqc != CatalogRules.Optional(input.Uqc)?.ToUpperInvariant()) &&
                (await db.Set<ProductMeasureProfile>().AnyAsync(x => x.CompanyId == company && x.BaseUnitId == entity.Id, ct) ||
                 await db.Set<ProductUnitConversion>().AnyAsync(x => x.CompanyId == company && x.UnitId == entity.Id, ct)))
                return Fail("Referenced units cannot change name, type, symbol, precision or UQC.");
            measure.UnitType = input.UnitType;
            measure.DecimalPrecision = input.Precision;
            measure.Symbol = CatalogRules.Optional(input.Symbol);
            measure.Uqc = CatalogRules.Optional(input.Uqc)?.ToUpperInvariant();
        }
        if (entity is Brand brand)
        {
            if (existing is not null && brand.Name != name &&
                await db.Set<ProductMeasureProfile>().AnyAsync(x => x.CompanyId == company && x.BrandId == brand.Id, ct))
                return Fail("A referenced brand's name is fixed so existing product text remains consistent.");
            brand.Manufacturer = CatalogRules.Optional(input.Manufacturer);
            if (request.RemoveLogo || request.Upload is not null) brand.Logo = request.Upload;
        }
        entity.Code = code;
        entity.Name = name;
        entity.NormalizedName = normalized;
        entity.Description = CatalogRules.Optional(input.Description);
        entity.SortOrder = input.SortOrder;
        entity.UpdatedUtc = now;
        entity.UpdatedByUserId = actor;
        if (existing is null) db.Add(entity);
        Audit(db, company, actor, entity.Id, typeof(T).Name, existing is null ? "Created" : "Updated");
        return new(CompanyOutcome.Success, company);
    }

    private static MeasureMaster Detail<T>(T item) where T : CompanyRecord, ICatalogMaster
    {
        var unit = item as UnitOfMeasure;
        var brand = item as Brand;
        return new(item.Id, item.Version, item.Code, item.Name, item.Active, item.Description, item.SortOrder,
            unit?.Symbol, unit?.UnitType ?? "Count", unit?.DecimalPrecision ?? 0, unit?.Uqc, brand?.Manufacturer,
            brand?.Logo, item.CreatedUtc, item.UpdatedUtc);
    }

    private static async Task<MeasureResult> Integrate(BillingDbContext db, MeasureRequest request, Guid company, Guid actor, CancellationToken ct)
    {
        var product = await db.Set<Product>().SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == request.Id, ct);
        if (product is null) return new(CompanyOutcome.NotFound);
        var profile = await db.Set<ProductMeasureProfile>().SingleOrDefaultAsync(x => x.CompanyId == company && x.ProductId == product.Id, ct);
        var saved = await db.Set<ProductUnitConversion>().Where(x => x.CompanyId == company && x.ProductId == product.Id).ToListAsync(ct);
        var units = await db.Set<UnitOfMeasure>().Where(x => x.CompanyId == company).OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => new MeasureChoice(x.Id, x.Code, x.Name, x.Active)).ToArrayAsync(ct);
        var brands = await db.Set<Brand>().Where(x => x.CompanyId == company).OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => new MeasureChoice(x.Id, x.Code, x.Name, x.Active)).ToArrayAsync(ct);
        if (request.Action == MeasureAction.Product)
        {
            var legacy = new List<string>();
            void Report(string label, string? value, bool linked, MeasureChoice[] choices)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (linked) { legacy.Add(label + ": " + value + " — original value retained; master link in use"); return; }
                var key = CatalogRules.Normalize(value);
                var matches = choices.Where(x => x.Active && (CatalogRules.Normalize(x.Code) == key || CatalogRules.Normalize(x.Name) == key)).ToArray();
                legacy.Add(label + ": " + value + (matches.Length == 1 ? $" — exact candidate {matches[0].Label}" :
                    matches.Length == 0 ? " — no active match" : " — ambiguous match"));
            }
            Report("Brand", product.Brand, profile?.BrandId is not null, brands);
            Report("Base", product.BaseUnit, profile?.BaseUnitId is not null, units);
            Report("Sales", product.SalesUnit ?? product.BaseUnit, saved.Any(x => x.DefaultSales), units);
            Report("Purchase", product.PurchaseUnit ?? product.BaseUnit, saved.Any(x => x.DefaultPurchase), units);
            legacy.Add($"Original factors — Sales: {product.SalesFactor}, Purchase: {product.PurchaseFactor}");
            return new(CompanyOutcome.Success, company, Product:new(product.Id, product.Version, profile?.BrandId, profile?.BaseUnitId,
                profile?.Classification, saved.Select(x => new ConversionInput(x.UnitId,x.FactorToBaseUnit,x.DefaultSales,x.DefaultPurchase,x.Active)).ToArray()),
                Units:units, Brands:brands, Legacy:legacy);
        }

        if (request.Product is not { } input || input.ProductId != product.Id) return new(CompanyOutcome.InvalidInput);
        if (input.Version != product.Version) return new(CompanyOutcome.Conflict);
        if (input.BrandId is Guid brandId && !brands.Any(x => x.Id == brandId && (x.Active || profile?.BrandId == brandId)))
            return Fail("Choose an active brand in the current company.");
        if (input.Classification is not null && !UnitBrandService.Classifications.Contains(input.Classification))
            return Fail("Choose a supported manufacturing or trading classification.");
        if (input.BaseUnitId is Guid baseId && !units.Any(x => x.Id == baseId && x.Active))
            return Fail("Choose an active base unit in the current company.");
        if (input.Conversions.Count > 200 || input.Conversions.GroupBy(x => x.UnitId).Any(x => x.Count() > 1) ||
            input.Conversions.Count(x => x.Sales) > 1 || input.Conversions.Count(x => x.Purchase) > 1 ||
            input.Conversions.Any(x => x.Factor <= 0 || x.Factor > 1000000000 || x.Factor != decimal.Round(x.Factor, 6) ||
                !x.Active && (x.Sales || x.Purchase) || x.UnitId == input.BaseUnitId && (x.Factor != 1 || !x.Active) ||
                !units.Any(u => u.Id == x.UnitId && (u.Active || !x.Active))))
            return Fail("Use one row per unit, positive factors with up to six decimals and at most one active default of each kind. The base factor must be 1.");
        if (input.Conversions.Count > 0 && input.BaseUnitId is null)
            return Fail("Choose a base unit before setting conversions.");
        if (profile?.BaseUnitId != input.BaseUnitId && saved.Any(x => x.Active && x.UnitId != profile?.BaseUnitId) &&
            input.Conversions.Any(x => x.Active && x.UnitId != input.BaseUnitId))
            return Fail("Deactivate existing non-base conversions before changing the base unit. Configure the new factors after the base changes.");
        if (input.BaseUnitId is Guid selectedBase && !input.Conversions.Any(x => x.UnitId == selectedBase))
            input = input with { Conversions = [.. input.Conversions, new(selectedBase,1,false,false,true)] };
        if (profile is null)
        {
            profile = new ProductMeasureProfile { CompanyId=company, ProductId=product.Id };
            db.Add(profile);
        }
        profile.BaseUnitId = input.BaseUnitId;
        profile.BrandId = input.BrandId;
        profile.Classification = input.Classification;
        // Flush removed flags before setting new defaults to respect the filtered unique indexes.
        foreach (var row in saved)
        {
            row.DefaultSales = false;
            row.DefaultPurchase = false;
            row.Active = false;
        }
        await db.SaveChangesAsync(ct);
        foreach (var entry in input.Conversions)
        {
            var row = saved.SingleOrDefault(x => x.UnitId == entry.UnitId);
            if (row is null)
            {
                row = new ProductUnitConversion { CompanyId=company, ProductId=product.Id, UnitId=entry.UnitId };
                db.Add(row);
            }
            row.FactorToBaseUnit = entry.Factor;
            row.DefaultSales = entry.Sales;
            row.DefaultPurchase = entry.Purchase;
            row.Active = entry.Active;
        }
        product.UpdatedUtc = DateTime.UtcNow;
        product.UpdatedByUserId = actor;
        db.Entry(product).Property(x => x.UpdatedUtc).IsModified = true;
        Audit(db, company, actor, product.Id, "Product", "UnitBrandChanged");
        return new(CompanyOutcome.Success, company);
    }

    private static MeasureResult Fail(string message) => new(CompanyOutcome.InvalidInput, Message:message);

    private static void Audit(BillingDbContext db, Guid company, Guid actor, Guid id, string type, string action) =>
        db.Add(new AuditEvent { CompanyId=company, ActorUserId=actor, ActorKind="User", RecordId=id.ToString(),
            RecordType=type, Action=type+action, ChangesJson="{}" });
}
