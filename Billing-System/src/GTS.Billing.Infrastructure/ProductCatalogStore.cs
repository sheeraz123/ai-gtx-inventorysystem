using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class ProductCatalogStore(DevelopmentDatabase database) : IProductCatalogStore
{
    private const int PageSize = 50;

    public async Task<CatalogResult> ExecuteAsync(CatalogRequest request, AuthenticatedIdentity actor, byte[]? image, CancellationToken ct)
    {
        try
        {
            await using var db = database.OpenExisting();
            await using var transaction = await db.BeginTransactionAsync(ct);
            var installation = await db.Set<Installation>().SingleAsync(ct);
            if (installation.Id != database.ExpectedInstallationId() || installation.DatabaseMode != "PlaintextDevelopment" ||
                installation.SetupState != "Configured") return new(CompanyOutcome.Unavailable);
            var user = await db.Set<User>().SingleOrDefaultAsync(x => x.Id == actor.UserId, ct);
            if (user?.IsActive != true || user.Version != actor.Version) return new(CompanyOutcome.SessionRequired);
            if (!await db.Set<SuperAdminAssignment>().AnyAsync(x => x.UserId == actor.UserId, ct)) return new(CompanyOutcome.Forbidden);
            var company = await db.Set<Company>().Where(x => x.IsActive &&
                db.Set<CompanySelection>().Any(s => s.CompanyId == x.Id)).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (company is null) return new(CompanyOutcome.NotFound, Message:"Select an active company in Company Master.");
            if (request.Action != CatalogAction.Context && request.Company != company)
                return new(CompanyOutcome.Conflict, Message:"Current company changed. Refresh the list.");
            var categories = db.Set<ProductCategory>().Where(x => x.CompanyId == company);
            var products = db.Set<Product>().Where(x => x.CompanyId == company);
            if (request.Action == CatalogAction.Context)
                return new(CompanyOutcome.Success, company, Categories: await Choices(categories, ct));
            if (request.Action is CatalogAction.List or CatalogAction.Lookup)
                return await List(request, company.Value, categories, products, ct);
            var result = request.Products
                ? await ProductAsync(db, request, company.Value, actor.UserId, categories, products, image, ct)
                : await CategoryAsync(db, request, company.Value, actor.UserId, categories, ct);
            if (result.Outcome == CompanyOutcome.Success && request.Action is CatalogAction.Save or CatalogAction.Status && !result.DuplicateWarning)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return result;
        }
        catch (DbUpdateConcurrencyException) { return new(CompanyOutcome.Conflict); }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
        { return new(CompanyOutcome.Conflict, Message:"The record violates a category or product constraint. Refresh and review the values."); }
        catch (Exception e) when (e is SqliteException or DbUpdateException or IOException or InvalidOperationException or OverflowException)
        { return new(CompanyOutcome.Unavailable); }
    }

    private static async Task<IReadOnlyList<CatalogCategory>> Choices(IQueryable<ProductCategory> categories, CancellationToken ct)
    {
        var rows = await categories.OrderBy(x => x.SortOrder).ThenBy(x => x.NormalizedName)
            .Select(x => new { x.Id, x.ParentId, x.Name, x.Code, x.Active }).ToListAsync(ct);
        var byId = rows.ToDictionary(x => x.Id);
        return rows.Select(row =>
        {
            var path = new Stack<string>();
            var seen = new HashSet<Guid>();
            for (var node = row; node is not null && seen.Add(node.Id); node = node.ParentId is Guid parent ? byId.GetValueOrDefault(parent) : null)
                path.Push(node.Name);
            return new CatalogCategory(row.Id, string.Join(" / ", path) + $" [{row.Code}]" + (row.Active ? "" : " (inactive)"), row.Active);
        }).ToArray();
    }

    private static async Task<CatalogResult> List(CatalogRequest request, Guid company, IQueryable<ProductCategory> categories,
        IQueryable<Product> products, CancellationToken ct)
    {
        var search = CatalogRules.Normalize(request.Search);
        var skip = request.Page * PageSize;
        if (request.Products)
        {
            if (request.Action == CatalogAction.Lookup) products = products.Where(x => x.Active);
            else if (request.Active is bool active) products = products.Where(x => x.Active == active);
            if (request.Category is Guid category) products = products.Where(x => x.CategoryId == category);
            if (request.Kind is { Length: > 0 } kind) products = products.Where(x => x.Kind == kind);
            if (search.Length >= 3 || request.Action == CatalogAction.Lookup && search.Length > 0)
                products = products.Where(x => x.Code.Contains(search) || x.NormalizedName.Contains(search) ||
                    x.ShortName != null && x.ShortName.ToUpper().Contains(search) || x.Sku != null && x.Sku.ToUpper().Contains(search) ||
                    x.Barcode != null && x.Barcode.ToUpper().Contains(search) || x.Hsn != null && x.Hsn.Contains(search));
            var rows = await products.OrderBy(x => x.Code).Skip(skip).Take(PageSize + 1)
                .Select(x => new CatalogRow(x.Id, x.Version, x.Code, x.Name, x.Active,
                    categories.Where(c => c.Id == x.CategoryId).Select(c => c.Name).FirstOrDefault(),
                    null, null, x.Brand, x.Kind, x.BaseUnit, x.Hsn, x.SalesRate, x.Mrp, x.StockTracked,
                    x.SalesUnit ?? x.BaseUnit, x.GstRate, x.TaxExempt)).ToListAsync(ct);
            return new(CompanyOutcome.Success, company, rows.Take(PageSize).ToArray(), More:rows.Count > PageSize);
        }
        var filtered = categories;
        if (request.Active is bool categoryActive) filtered = filtered.Where(x => x.Active == categoryActive);
        if (search.Length >= 3)
            filtered = filtered.Where(x => x.Code.Contains(search) || x.NormalizedName.Contains(search) ||
                x.Description != null && x.Description.ToUpper().Contains(search));
        var list = await filtered.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).Skip(skip).Take(PageSize + 1)
            .Select(x => new CatalogRow(x.Id, x.Version, x.Code, x.Name, x.Active,
                categories.Where(c => c.Id == x.ParentId).Select(c => c.Name).FirstOrDefault(),
                x.Description, x.SortOrder, null, null, null, null, null, null, null, null, null, null)).ToListAsync(ct);
        return new(CompanyOutcome.Success, company, list.Take(PageSize).ToArray(), More:list.Count > PageSize);
    }

    private static async Task<CatalogResult> CategoryAsync(BillingDbContext db, CatalogRequest request, Guid company, Guid actor,
        IQueryable<ProductCategory> categories, CancellationToken ct)
    {
        var category = await categories.SingleOrDefaultAsync(x => x.Id == request.Id, ct);
        if (request.Id.HasValue && category is null) return new(CompanyOutcome.NotFound);
        if (request.Action == CatalogAction.Get && category is not null)
        {
            var parent = await categories.Where(x => x.Id == category.ParentId).Select(x => x.Name).SingleOrDefaultAsync(ct);
            var draft = new CategoryDraft { Code=category.Code, Name=category.Name, Description=category.Description,
                SortOrder=category.SortOrder, Active=category.Active, ParentId=category.ParentId };
            var names = await Actors(db, category.CreatedByUserId, category.UpdatedByUserId, ct);
            return new(CompanyOutcome.Success, company, Detail:new(
                new(category.Id, category.Version, category.Code, category.Name, category.Active, parent, category.Description, category.SortOrder),
                draft, null, null, category.CreatedUtc, category.UpdatedUtc, names.Created, names.Updated));
        }
        if (category is not null && category.Version != request.Version) return new(CompanyOutcome.Conflict);
        if (request.Action == CatalogAction.Status && category is null) return new(CompanyOutcome.NotFound);
        if (request.Action is not (CatalogAction.Save or CatalogAction.Status)) return new(CompanyOutcome.InvalidInput);
        var isNew = category is null;
        category ??= new ProductCategory { CompanyId=company, CreatedUtc=DateTime.UtcNow, CreatedByUserId=actor };
        var input = request.CategoryDraft;
        var parentId = request.Action == CatalogAction.Save ? input!.ParentId : category.ParentId;
        var active = request.Action == CatalogAction.Save ? input!.Active : request.Active!.Value;
        var code = input is null ? category.Code : input.Code.Trim().ToUpperInvariant();
        var name = input is null ? category.Name : input.Name.Trim();
        var normalized = CatalogRules.Normalize(name);
        if (!isNew && code != category.Code) return Invalid("Code", "Category code cannot be changed.");
        if (!active && await categories.AnyAsync(x => x.ParentId == category.Id && x.Active, ct))
            return Invalid("Active", "Deactivate or move the active child categories first.");

        if (parentId is Guid pid)
        {
            var parent = await categories.Where(x => x.Id == pid).Select(x => new { x.Id, x.ParentId, x.Active }).SingleOrDefaultAsync(ct);
            if (parent is null || !parent.Active && (active || isNew || parentId != category.ParentId))
                return Invalid("ParentId", "Choose an active parent in the current company.");
            var visited = new HashSet<Guid> { category.Id };
            while (parent is not null)
            {
                if (!visited.Add(parent.Id)) return Invalid("ParentId", "A category cannot be its own ancestor.");
                if (parent.ParentId is not Guid ancestor) break;
                parent = await categories.Where(x => x.Id == ancestor).Select(x => new { x.Id, x.ParentId, x.Active }).SingleOrDefaultAsync(ct);
            }
        }
        if (await categories.AnyAsync(x => x.Id != category.Id && x.Code == code, ct))
            return Invalid("Code", "This category code is already in use.");
        if (await categories.AnyAsync(x => x.Id != category.Id && x.ParentId == parentId && x.NormalizedName == normalized, ct))
            return Invalid("Name", "A category with this name already exists under that parent.");
        category.Code=code;
        category.Name=name;
        category.NormalizedName=normalized;
        category.ParentId=parentId;
        category.Active=active;
        if (input is not null) { category.Description=CatalogRules.Optional(input.Description); category.SortOrder=input.SortOrder; }
        category.UpdatedUtc=DateTime.UtcNow;
        category.UpdatedByUserId=actor;
        if (isNew) db.Add(category);
        Audit(db, company, actor, category.Id, "ProductCategory", isNew ? "Created" : request.Action == CatalogAction.Status ? "StatusChanged" : "Updated");
        return new(CompanyOutcome.Success, company);
    }

    private static async Task<CatalogResult> ProductAsync(BillingDbContext db, CatalogRequest request, Guid company, Guid actor,
        IQueryable<ProductCategory> categories, IQueryable<Product> products, byte[]? image, CancellationToken ct)
    {
        // Load the bounded image payload only for a single master detail.
        var product = request.Id is Guid id ? await products.SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        if (request.Id.HasValue && product is null) return new(CompanyOutcome.NotFound);
        if (request.Action == CatalogAction.Get && product is not null)
        {
            var name = await categories.Where(x => x.Id == product.CategoryId).Select(x => x.Name).SingleAsync(ct);
            var draft = new ProductDraft
            {
                CategoryId=product.CategoryId,
                Code=product.Code,
                Name=product.Name,
                ShortName=product.ShortName,
                Description=product.Description,
                Kind=product.Kind,
                Active=product.Active,
                Brand=product.Brand,
                Sku=product.Sku,
                Barcode=product.Barcode,
                PartNumber=product.PartNumber,
                ModelNumber=product.ModelNumber,
                BaseUnit=product.BaseUnit,
                SalesUnit=product.SalesUnit,
                SalesFactor=product.SalesFactor,
                PurchaseUnit=product.PurchaseUnit,
                PurchaseFactor=product.PurchaseFactor,
                QuantityPrecision=product.QuantityPrecision,
                Hsn=product.Hsn,
                GstRate=product.GstRate,
                TaxExempt=product.TaxExempt,
                TaxNotes=product.TaxNotes,
                PurchaseRate=product.PurchaseRate,
                SalesRate=product.SalesRate,
                Mrp=product.Mrp,
                SalesInclusive=product.SalesInclusive,
                PurchaseInclusive=product.PurchaseInclusive,
                Currency=product.Currency,
                PricingDate=product.PricingDate,
                StockTracked=product.StockTracked,
                ReorderLevel=product.ReorderLevel,
                MinimumStock=product.MinimumStock,
                MaximumStock=product.MaximumStock,
                StorageLocation=product.StorageLocation,
                Specifications=product.Specifications,
                Warranty=product.Warranty,
                Remarks=product.Remarks,
            };
            var names = await Actors(db, product.CreatedByUserId, product.UpdatedByUserId, ct);
            return new(CompanyOutcome.Success, company, Detail:new(
                new(product.Id, product.Version, product.Code, product.Name, product.Active, name),
                null, draft, product.Image, product.CreatedUtc, product.UpdatedUtc, names.Created, names.Updated));
        }
        if (request.Action == CatalogAction.Snapshot)
        {
            if (product is not { Active:true }) return new(CompanyOutcome.InvalidInput, Message:"Choose an active product.");
            var purchase = request.Unit == CatalogUnit.Purchase;
            var (unit, factor) = request.Unit switch
            {
                CatalogUnit.Purchase => (product.PurchaseUnit ?? product.BaseUnit, product.PurchaseFactor),
                CatalogUnit.Sales => (product.SalesUnit ?? product.BaseUnit, product.SalesFactor),
                _ => (product.BaseUnit, 1m)
            };
            // Rates are per configured commercial unit. Base selection only inherits a rate when the sales unit is the base.
            var rate = purchase ? product.PurchaseRate :
                request.Unit == CatalogUnit.Base && CatalogRules.Normalize(product.SalesUnit ?? product.BaseUnit) != CatalogRules.Normalize(product.BaseUnit)
                ? null : product.SalesRate;
            return new(CompanyOutcome.Success, company, Snapshot:new(company, product.Id, product.Version, product.Code, product.Name,
                product.Description, product.Kind, product.Sku, product.Barcode, product.Hsn, unit, product.QuantityPrecision,
                factor, rate, product.Currency, purchase ? product.PurchaseInclusive : product.SalesInclusive,
                product.GstRate, product.TaxExempt, product.TaxNotes));
        }
        if (product is not null && product.Version != request.Version) return new(CompanyOutcome.Conflict);
        var isNew = product is null;
        if (request.Action == CatalogAction.Status && !isNew) product!.Active = request.Active!.Value;
        else if (request.Action == CatalogAction.Save && request.ProductDraft is { } input)
        {
            var target = await categories.SingleOrDefaultAsync(x => x.Id == input.CategoryId, ct);
            if (target is null || !target.Active && (isNew || product!.CategoryId != input.CategoryId))
                return Invalid("CategoryId", "Choose an active category from the current company.");
            var code = input.Code.Trim().ToUpperInvariant();
            if (!isNew && product!.Code != code) return Invalid("Code", "Product code cannot be changed.");
            var recordId = product?.Id;
            if (await products.AnyAsync(x => x.Id != recordId && x.Code == code, ct))
                return Invalid("Code", "This product code is already in use.");
            var name = CatalogRules.Normalize(input.Name);
            var sku = CatalogRules.Optional(input.Sku)?.ToUpperInvariant();
            var barcode = CatalogRules.Optional(input.Barcode)?.ToUpperInvariant();
            var identityChanged = isNew || product!.NormalizedName != name ||
                product.Sku?.ToUpperInvariant() != sku || product.Barcode?.ToUpperInvariant() != barcode;
            if (identityChanged && !request.AcceptDuplicates && await products.AnyAsync(x => x.Id != recordId &&
                (x.NormalizedName == name || sku != null && x.Sku != null && x.Sku.ToUpper() == sku ||
                 barcode != null && x.Barcode != null && x.Barcode.ToUpper() == barcode), ct))
                return new(CompanyOutcome.Conflict, company, Message:"A product with the same name, SKU or barcode exists. Save a separate record?", DuplicateWarning:true);

            product ??= new Product { CompanyId=company, CreatedUtc=DateTime.UtcNow, CreatedByUserId=actor };
            product.CategoryId=input.CategoryId;
            product.NormalizedName=name;
            product.Code=code;
            product.Name=input.Name.Trim();
            product.ShortName=CatalogRules.Optional(input.ShortName);
            product.Description=CatalogRules.Optional(input.Description);
            product.Kind=input.Kind.Trim();
            product.Active=input.Active;
            product.Brand=CatalogRules.Optional(input.Brand);
            product.Sku=CatalogRules.Optional(input.Sku);
            product.Barcode=CatalogRules.Optional(input.Barcode);
            product.PartNumber=CatalogRules.Optional(input.PartNumber);
            product.ModelNumber=CatalogRules.Optional(input.ModelNumber);
            product.BaseUnit=input.BaseUnit.Trim();
            product.SalesUnit=CatalogRules.Optional(input.SalesUnit);
            product.SalesFactor=input.SalesFactor;
            product.PurchaseUnit=CatalogRules.Optional(input.PurchaseUnit);
            product.PurchaseFactor=input.PurchaseFactor;
            product.QuantityPrecision=input.QuantityPrecision;
            product.Hsn=CatalogRules.Optional(input.Hsn);
            product.GstRate=input.GstRate;
            product.TaxExempt=input.TaxExempt;
            product.TaxNotes=CatalogRules.Optional(input.TaxNotes);
            product.PurchaseRate=input.PurchaseRate;
            product.SalesRate=input.SalesRate;
            product.Mrp=input.Mrp;
            product.SalesInclusive=input.SalesInclusive;
            product.PurchaseInclusive=input.PurchaseInclusive;
            product.Currency=input.Currency.Trim().ToUpperInvariant();
            product.PricingDate=input.PricingDate;
            product.StockTracked=input.StockTracked;
            product.ReorderLevel=input.ReorderLevel;
            product.MinimumStock=input.MinimumStock;
            product.MaximumStock=input.MaximumStock;
            product.StorageLocation=CatalogRules.Optional(input.StorageLocation);
            product.Specifications=CatalogRules.Optional(input.Specifications);
            product.Warranty=CatalogRules.Optional(input.Warranty);
            product.Remarks=CatalogRules.Optional(input.Remarks);
            if (image is not null || request.RemoveImage) product.Image=image;
            if (isNew) db.Add(product);
        }
        else return new(CompanyOutcome.NotFound);
        product!.UpdatedUtc=DateTime.UtcNow;
        product.UpdatedByUserId=actor;
        Audit(db, company, actor, product.Id, "Product", isNew ? "Created" : request.Action == CatalogAction.Status ? "StatusChanged" : "Updated");
        return new(CompanyOutcome.Success, company);
    }

    private static async Task<(string Created,string Updated)> Actors(BillingDbContext db, Guid created, Guid updated, CancellationToken ct)
    {
        var users = await db.Set<User>().Where(x => x.Id == created || x.Id == updated).ToDictionaryAsync(x => x.Id, x => x.Username, ct);
        return (users.GetValueOrDefault(created, "Unknown"), users.GetValueOrDefault(updated, "Unknown"));
    }

    private static void Audit(BillingDbContext db, Guid company, Guid actor, Guid id, string type, string action) =>
        db.Add(new AuditEvent { CompanyId=company, ActorUserId=actor, ActorKind="User", RecordId=id.ToString(),
            RecordType=type, Action=type+action, ChangesJson="{}" });

    private static CatalogResult Invalid(string field, string message) =>
        new(CompanyOutcome.InvalidInput, Errors:new Dictionary<string,string> { [field]=message });
}
