using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class ProductVariantStore(DevelopmentDatabase database) : IProductVariantStore
{
    public async Task<VariantResult> ExecuteAsync(VariantRequest request,AuthenticatedIdentity actor,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var state=await ScopedAccess.VerifyAsync(db,database.ExpectedInstallationId(),actor,ct);
            if(state!=CompanyOutcome.Success) return new(state);
            var access=await ScopedAccess.SnapshotAsync(db,actor.UserId,ct);
            if(access.CurrentCompanyId is not Guid company || !access.Allows("Product.View",company) ||
                !await db.Set<Company>().AnyAsync(x=>x.Id==company && x.IsActive,ct)) return new(CompanyOutcome.Forbidden);
            if(request.ExpectedCompany is Guid expected && expected!=company) return new(CompanyOutcome.Conflict,"The active company changed.");
            var products=db.Set<Product>().Where(x=>x.CompanyId==company);
            if(!await products.AnyAsync(x=>x.Id==request.ProductId,ct)) return new(CompanyOutcome.NotFound);
            var variants=db.Set<ProductVariant>().Where(x=>x.CompanyId==company && x.ProductId==request.ProductId);
            var options=db.Set<VariantOption>().Where(x=>x.CompanyId==company);
            var links=db.Set<ProductVariantOption>().Where(x=>x.CompanyId==company);
            bool Can(string action)=>access.Allows("ProductVariant."+action,company);
            bool ManageOptions(VariantValue input,string action)=>access.Allows(
                (input.Axis.StartsWith("SIZE-",StringComparison.OrdinalIgnoreCase)?"Size":input.Axis.Equals("COLOR",StringComparison.OrdinalIgnoreCase)?
                    "Color":"VariantAttribute")+"."+action,company);
            if(!Can("View")) return new(CompanyOutcome.Forbidden);
            if(request.Action==VariantAction.List)
            {
                var search=CatalogRules.Normalize(request.Search);
                var barcodeSearch=request.Search?.Trim();
                if(search.Count(c=>!char.IsWhiteSpace(c))>=3)
                    variants=variants.Where(x=>x.Code.Contains(search) || x.Sku!=null && x.Sku.Contains(search) ||
                        x.Barcode==barcodeSearch || links.Any(l=>l.VariantId==x.Id && options.Any(o=>o.Id==l.OptionId &&
                            (o.Code.Contains(search) || o.Name.ToUpper().Contains(search)))));
                var rows=await variants.OrderBy(x=>x.Code).ToArrayAsync(ct);
                var ids=rows.Select(x=>x.Id).ToArray();
                var mapping=(await links.Where(x=>ids.Contains(x.VariantId)).ToArrayAsync(ct)).ToLookup(x=>x.VariantId,x=>x.OptionId);
                return new(CompanyOutcome.Success,Company:company,
                    Options:await options.OrderBy(x=>x.Axis).ThenBy(x=>x.SortOrder).ThenBy(x=>x.Code)
                        .Select(x=>new VariantValue(x.Id,x.Version,x.Axis,x.Code,x.Name,x.Hex,x.SortOrder,x.Active)).ToArrayAsync(ct),
                    Variants:rows.Select(x=>new VariantEntry(x.Id,x.Version,x.Code,x.Sku,x.Barcode,x.Mrp,x.SalesRate,x.PurchaseRate,
                        x.Active,mapping[x.Id].ToArray())).ToArray(),
                    CanCreate:Can("Create"),CanEdit:Can("Edit"),CanStatus:Can("StatusChange"),
                    CanPrice:Can("PricingEdit"),CanManageOptions:access.Allows("Size.Edit",company) ||
                        access.Allows("Color.Edit",company) || access.Allows("VariantAttribute.Edit",company));
            }
            if(request.ExpectedCompany!=company) return new(CompanyOutcome.Conflict,"Reopen the variant editor for this company.");
            var recordId=Guid.Empty;
            var recordType="";
            var now=DateTime.UtcNow;
            if(request.Action is VariantAction.SaveOption or VariantAction.OptionStatus && request.Option is { } value)
            {
                var row=await options.SingleOrDefaultAsync(x=>x.Id==value.Id,ct);
                if(value.Id!=Guid.Empty && (row is null || row.Version!=value.Version)) return Conflict();
                if(!ManageOptions(value with {Axis=row?.Axis??CatalogRules.Normalize(value.Axis)},
                    request.Action==VariantAction.OptionStatus?"StatusChange":"Edit")) return new(CompanyOutcome.Forbidden);
                if(request.Action==VariantAction.OptionStatus)
                {
                    if(row is null) return Conflict();
                    row.Active=value.Active;
                }
                else
                {
                    var axis=CatalogRules.Normalize(value.Axis);
                    var code=CatalogRules.Normalize(value.Code);
                    var name=value.Name.Trim();
                    var hex=CatalogRules.Optional(value.Hex)?.ToUpperInvariant();
                    if(!ValidCode(axis) || axis.StartsWith("SIZE-",StringComparison.Ordinal) &&
                        axis is not ("SIZE-UK" or "SIZE-US" or "SIZE-EU" or "SIZE-INDIA-CUSTOM" or "SIZE-APPAREL" or "SIZE-NUMERIC" or "SIZE-OTHER") ||
                        !ValidCode(code) || name.Length is <1 or >100 || name.Any(char.IsControl) ||
                        value.SortOrder is <0 or >9999 || hex is not null &&
                        (axis!="COLOR" || hex.Length!=7 || hex[0]!='#' || !hex.Skip(1).All(char.IsAsciiHexDigit)))
                        return Invalid("Enter an attribute code, option code, name and valid sort order. Color may use #RRGGBB.");
                    if(row is not null && row.Axis!=axis) return Invalid("An existing option keeps its original attribute.");
                    if(await options.AnyAsync(x=>x.Axis==axis && x.Code==code && x.Id!=value.Id,ct))
                        return Invalid("That option code already exists in this attribute.");
                    if(row is null) {row=new VariantOption {CompanyId=company,Axis=axis,CreatedUtc=now,CreatedBy=actor.UserId};db.Add(row);}
                    row.Code=code;row.Name=name;row.Hex=hex;row.SortOrder=value.SortOrder;
                }
                recordId=row.Id;
                row.UpdatedUtc=now; row.UpdatedBy=actor.UserId;
                recordType="VariantOption";
            }
            else if(request.Action is VariantAction.SaveVariant or VariantAction.VariantStatus && request.Variant is { } valueEntry)
            {
                if(!Can(request.Action==VariantAction.VariantStatus?"StatusChange":valueEntry.Id==Guid.Empty?"Create":"Edit"))
                    return new(CompanyOutcome.Forbidden);
                var row=await variants.SingleOrDefaultAsync(x=>x.Id==valueEntry.Id,ct);
                if(valueEntry.Id!=Guid.Empty && (row is null || row.Version!=valueEntry.Version)) return Conflict();
                if(request.Action==VariantAction.VariantStatus)
                {
                    if(row is null) return Conflict();
                    row.Active=valueEntry.Active;
                }
                else
                {
                    var code=CatalogRules.Normalize(valueEntry.Code);
                    var sku=CatalogRules.Optional(valueEntry.Sku) is { } inputSku?CatalogRules.Normalize(inputSku):null;
                    var barcode=CatalogRules.Optional(valueEntry.Barcode);
                    var normalizedBarcode=barcode?.ToUpperInvariant();
                    if(!ValidCode(code) || sku is {Length:>80} || barcode is {Length:>80} ||
                        sku?.Any(char.IsControl)==true || barcode?.Any(char.IsControl)==true ||
                        valueEntry.Options.Count is <1 or >16 || valueEntry.Options.Distinct().Count()!=valueEntry.Options.Count)
                        return Invalid("Enter a valid variant code and select one to sixteen distinct attribute options.");
                    var existing=row is null?[]:await links.Where(x=>x.VariantId==valueEntry.Id).ToArrayAsync(ct);
                    var oldIds=existing.Select(x=>x.OptionId).ToArray();
                    var chosen=await options.Where(x=>valueEntry.Options.Contains(x.Id) && (x.Active || oldIds.Contains(x.Id))).ToArrayAsync(ct);
                    if(chosen.Length!=valueEntry.Options.Count || chosen.Select(x=>x.Axis).Distinct().Count()!=chosen.Length ||
                        chosen.Count(x=>x.Axis.StartsWith("SIZE-"))>1)
                        return Invalid("Choose active options with one value per attribute and one size system.");
                    var identity=string.Join(",",chosen.Select(x=>x.Id.ToString("N")).Order(StringComparer.Ordinal));
                    if(await variants.AnyAsync(x=>x.Id!=valueEntry.Id && (x.Code==code || x.Combination==identity),ct))
                        return Invalid("This variant code or attribute combination already exists for the product.");
                    if(await db.Set<ProductVariant>().AnyAsync(x=>x.CompanyId==company && x.Id!=valueEntry.Id &&
                        (sku!=null && x.Sku==sku || barcode!=null && x.Barcode==barcode),ct) ||
                        await products.AnyAsync(x=>sku!=null && x.Sku!=null && x.Sku.Trim().ToUpper()==sku ||
                            normalizedBarcode!=null && x.Barcode!=null && x.Barcode.Trim().ToUpper()==normalizedBarcode,ct))
                        return Invalid("This SKU or barcode is already used in the company.");
                    if(!Can("PricingEdit") && (row?.Mrp!=valueEntry.Mrp || row?.SalesRate!=valueEntry.SalesRate ||
                        row?.PurchaseRate!=valueEntry.PurchaseRate)) return new(CompanyOutcome.Forbidden);
                    var errors=new Dictionary<string,string>();
                    CatalogRules.CheckDecimal("MRP",valueEntry.Mrp,errors);
                    CatalogRules.CheckDecimal("Selling rate",valueEntry.SalesRate,errors);
                    CatalogRules.CheckDecimal("Purchase rate",valueEntry.PurchaseRate,errors);
                    if(errors.Count>0) return Invalid(errors.First().Key+": "+errors.First().Value);
                    if(row is null)
                    {
                        row=new ProductVariant {CompanyId=company,ProductId=request.ProductId,CreatedUtc=now,CreatedBy=actor.UserId};
                        db.Add(row);
                    }
                    row.Code=code;row.Combination=identity;row.Sku=sku;row.Barcode=barcode;
                    row.Mrp=valueEntry.Mrp;row.SalesRate=valueEntry.SalesRate;row.PurchaseRate=valueEntry.PurchaseRate;
                    foreach(var old in existing)
                    {
                        var next=chosen.SingleOrDefault(x=>x.Axis==old.Axis);
                        if(next is null) db.Remove(old);
                        else old.OptionId=next.Id;
                    }
                    foreach(var option in chosen.Where(x=>existing.All(l=>l.Axis!=x.Axis)))
                        db.Add(new ProductVariantOption {CompanyId=company,VariantId=row.Id,Axis=option.Axis,OptionId=option.Id});
                }
                recordId=row.Id;
                row.UpdatedUtc=now; row.UpdatedBy=actor.UserId;
                recordType="ProductVariant";
            }
            if(recordId==Guid.Empty) return Invalid("Choose a supported variant action.");
            db.Add(new AuditEvent {CompanyId=company,ActorUserId=actor.UserId,ActorKind="User",RecordId=recordId.ToString(),
                RecordType=recordType,Action=request.Action.ToString(),ChangesJson="{}"});
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(CompanyOutcome.Success);
        }
        catch(DbUpdateConcurrencyException) {return Conflict();}
        catch(DbUpdateException e) when(e.InnerException is SqliteException {SqliteErrorCode:19})
        {return Invalid("A duplicate code, combination, SKU or barcode, or a conflicting relationship was rejected.");}
        catch(Exception e) when(e is SqliteException or DbUpdateException or InvalidOperationException or System.IO.IOException)
        {return new(CompanyOutcome.Unavailable,"Product variants are unavailable. Verify the development database schema.");}
    }

    private static bool ValidCode(string value)=>value.Length is >=1 and <=50 &&
        value.All(c=>char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    private static VariantResult Invalid(string message)=>new(CompanyOutcome.InvalidInput,message);
    private static VariantResult Conflict()=>new(CompanyOutcome.Conflict,"This record changed. Refresh before editing.");
}
