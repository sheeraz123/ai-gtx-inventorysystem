using System.Security.Cryptography;
using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GTS.Billing.Infrastructure;

public sealed class SalesInvoiceStore(DevelopmentDatabase database,ILogger<SalesInvoiceStore> logger,SettingsStore? settings=null) : ISalesInvoiceStore
{
    public async Task<SalesResult> ExecuteAsync(SalesRequest request,AuthenticatedIdentity actor,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var state=await ScopedAccess.VerifyAsync(db,database.ExpectedInstallationId(),actor,ct);
            if(state!=CompanyOutcome.Success) return new(state);
            var company=await db.Set<Company>().SingleOrDefaultAsync(c=>c.IsActive && db.Set<CompanySelection>().Any(s=>s.CompanyId==c.Id),ct);
            if(company is null) return Invalid("Select an active company first.");
            if(company.Id!=request.Company && (request.Company.HasValue || request.Action is not (SalesAction.List or SalesAction.Options)))
                return new(CompanyOutcome.Conflict,Message:"The selected company changed.");
            var admin=await ScopedAccess.IsSuperAdminAsync(db,actor.UserId,ct);
            var grants=admin?null:await ScopedAccess.ResolveAsync(db,actor.UserId,company.Id,ct);
            bool Can(string permission)=>admin || grants!.Contains("SalesInvoice."+permission);
            if(!Can("View") || !Can("ViewFinancials") || request.Action==SalesAction.Save && !Can(request.Id.HasValue?"EditDraft":"Create") ||
                request.Action==SalesAction.DeleteDraft && !Can("DeleteDraft") ||
                request.Action==SalesAction.CancelInvoice && !Can("CancelInvoice") ||
                request.Action is SalesAction.Finalize or SalesAction.PrepareFinalization && !Can("Finalize") ||
                request.Action is SalesAction.Print or SalesAction.PrintToPdf && !Can("Print") ||
                request.Action==SalesAction.PrintToPdf && !Can("ExportPdf") ||
                request.Action is SalesAction.PrintPreview or SalesAction.Print or SalesAction.PrintToPdf && request.PrintTemplateCode is not null &&
                !admin && !grants!.Contains("InvoiceTemplate.View")) return new(CompanyOutcome.Forbidden);
            bool Granted(string permission)=>admin || grants!.Contains(permission);
            var canEditProduct=Granted("Product.View") && Granted("Product.Edit");
            var work=new CompanySalesInvoices(db,company,actor.UserId,actor.Username,canEditProduct && Granted("Product.PricingEdit"),
                canEditProduct && Granted("GstRate.Assign"),settings);
            var result=await work.RunAsync(request,ct);
            if(request.Action==SalesAction.Finalize ||
                request.Action is SalesAction.Save or SalesAction.Print or SalesAction.PrintToPdf or SalesAction.DeleteDraft or SalesAction.CancelInvoice &&
                result.Outcome==CompanyOutcome.Success)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                if(result.Row is { } returned && db.Set<SalesInvoice>().Local.FirstOrDefault(x=>x.Id==returned.Id) is { } saved)
                    result=result with {Row=returned with {Version=saved.Version}};
            }
            return result;
        }
        catch(DbUpdateConcurrencyException) {return new(CompanyOutcome.Conflict,Message:"The draft changed. Refresh it before trying again.");}
        catch(Exception error) when(error is SqliteException or DbUpdateException or InvalidOperationException or JsonException)
        {
            if(request.Action is SalesAction.Finalize or SalesAction.PrepareFinalization)
            {
                var reference=Guid.NewGuid();
                logger.LogWarning(new EventId(5101),"Sales invoice finalization failed. Reference {Reference}; exception {ExceptionType}; code {ErrorCode}.",
                    reference,error.GetType().Name,error.HResult);
                return new(CompanyOutcome.Unavailable,Message:$"Finalization could not complete. Refresh the draft and try again. Reference: {reference:N}.");
            }
            return new(CompanyOutcome.Unavailable,Message:"The invoice operation could not complete. Refresh before retrying.");
        }
    }
    internal static SalesResult Invalid(string message)=>new(CompanyOutcome.InvalidInput,Message:message);
}

internal sealed class CompanySalesInvoices(BillingDbContext db,Company company,Guid actor,string username,bool canFillMrp,bool canLinkTax,
    SettingsStore? settings)
{
    private IQueryable<SalesInvoice> Invoices=>db.Set<SalesInvoice>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<FinancialYear> Years=>db.Set<FinancialYear>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<DocumentNumberingSeries> Series=>db.Set<DocumentNumberingSeries>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<GstRegistration> Registrations=>db.Set<GstRegistration>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<Customer> Customers=>db.Set<Customer>().Where(x=>x.CompanyId==company.Id);
    private IQueryable<Product> Products=>db.Set<Product>().Where(x=>x.CompanyId==company.Id);
    private static SalesResult Invalid(string message)=>SalesInvoiceStore.Invalid(message);

    internal async Task<SalesResult> RunAsync(SalesRequest request,CancellationToken ct)
    {
        if(request.Action==SalesAction.Options) return await Options(ct);
        if(request.Action==SalesAction.List) return await List(request,ct);
        if(request.Action==SalesAction.CustomerAddresses) return await CustomerAddressesAsync(request.Customer,ct);
        var row=await Invoices.SingleOrDefaultAsync(x=>x.Id==request.Id,ct);
        if(request.Action is SalesAction.DeleteDraft or SalesAction.CancelInvoice)
            return await ChangeStatusAsync(row,request,ct);
        if(request.Action is SalesAction.PrintPreview or SalesAction.Print or SalesAction.PrintToPdf)
        {
            if(row is not {LedgerId:Guid ledger,Number:{ } number})
                return Invalid("Select a finalized invoice to preview or print.");
            if(!await db.Set<DocumentNumberLedger>().AnyAsync(x=>x.CompanyId==company.Id && x.Id==ledger && x.Status=="Issued",ct))
                return Invalid("Cancelled invoices are available in View. Printing and PDF export are unavailable.");
            var stored=await db.Set<InvoiceSnapshot>().Where(x=>x.CompanyId==company.Id && x.LedgerId==ledger)
                .Select(x=>x.SnapshotJson).SingleOrDefaultAsync(ct);
            if(stored is null || JsonSerializer.Deserialize<SalesInvoiceSnapshot>(stored) is not { } frozen)
                return Invalid("The finalized invoice snapshot is unavailable.");
            var model=InvoicePrintModel.FromFinalized(frozen,number);
            if(request.PrintTemplateCode is { } selected)
            {
                var template=InvoiceTemplates.Selectable.SingleOrDefault(x=>x.Code==selected);
                if(template is null) return Invalid("The selected invoice template is unavailable.");
                model=model with {Template=template};
            }
            if(request.Action==SalesAction.Print) Audit(row.Id,"PrintAuthorized");
            if(request.Action==SalesAction.PrintToPdf) Audit(row.Id,"PrintToPdfAuthorized");
            return new(CompanyOutcome.Success,company.Id,PrintModel:model);
        }
        if(request.Id.HasValue && row is null || !request.Id.HasValue &&
            request.Action is SalesAction.Finalize or SalesAction.View or SalesAction.PrepareFinalization)
            return new(CompanyOutcome.NotFound);
        if(request.ExpectedNumbering is {} required && (row is null || required.DocumentType!="TaxInvoice" ||
            row.Date!=required.DocumentDate || row.FinancialYearId!=required.FinancialYearId ||
            row.RegistrationId!=required.RegistrationId || row.SeriesId!=required.SeriesId))
            return Invalid("Numbering context does not match the persisted sales invoice.");
        if(row?.LedgerId is not null)
        {
            if(request.Action==SalesAction.View) return await View(row,ct);
            if(request.Action==SalesAction.Finalize)
            {
                var viewed=await View(row,ct);
                return viewed.Row?.Cancelled==true?Invalid("A cancelled invoice cannot be finalized again."):viewed;
            }
            return Invalid("Finalized invoices are immutable.");
        }
        if(request.Action==SalesAction.View) return await View(row!,ct);
        if(row is not null && row.Version!=request.Version)
            return new(CompanyOutcome.Conflict,Message:"The draft changed. Reopen the current version.");
        if(request.Action==SalesAction.Finalize) Audit(row!.Id,"FinalizationAttempted");
        SalesResult Reject(string message,IReadOnlyList<SalesFieldError>? fields=null)
        {
            if(request.Action==SalesAction.Finalize) Audit(row!.Id,"FinalizationRejected");
            return Invalid(message) with {FieldErrors=fields};
        }
        var finalization=request.Action is SalesAction.Finalize or SalesAction.PrepareFinalization;
        var draft=finalization?ReadDraft(row!):request.Draft;
        if(request.Action==SalesAction.Save && draft is not null)
            draft=draft with {Transport=(draft.Transport??new("","","","","","","","")) with {PreparedBy=username}};
        if(draft?.Transport is { } transport &&
            new[]{transport.GoodsReceipt,transport.Vehicle,transport.Weight,transport.Carrier,transport.OrderThrough,
                transport.PrivateMark,transport.Document,transport.PreparedBy,transport.Distance,transport.TransportId}
                .Any(value=>value?.Length>120 || value?.Any(char.IsControl)==true))
            return Reject("Enter single-line transport and order details of up to 120 characters per field.");
        if(draft?.EntryDetails is { } entry && (entry.Intent is { } intent && !Enum.IsDefined(intent) ||
            entry.PaymentMode is not ("" or "Cash" or "Credit") ||
            new[]{entry.Tcs,entry.Reversal,entry.Ern}.Any(value=>value?.Length>120 || value?.Any(char.IsControl)==true)))
            return Reject("Review the sale type, payment mode and optional invoice references.");
        if(draft is null || draft.Lines?.Count>250 ||
            new[]{draft.PurchaseOrder,draft.Reference,draft.Terms,draft.Notes,draft.InternalRemarks,draft.PlaceOfSupply}.Any(x=>x.Length>4000))
            return Reject("Enter an invoice date and review the field lengths.");
        var companySettings=await SettingsStore.ReadCompanyAsync(db,company.Id,ct);
        var form=SalesFormConfiguration.From(companySettings);
        var requiredFields=draft.RequiredFields(requireNumbering:request.Action!=SalesAction.Preview);
        if(requiredFields.Count>0) return Reject(requiredFields[0].Message,requiredFields);
        var formErrors=form.ValidateDraft(draft,row is null?null:ReadDraft(row));
        if(formErrors.Count>0) return Reject(formErrors[0].Message,formErrors);
        if(request.Action==SalesAction.Save && row is null && request.CreateDraftId is Guid draftId)
        {
            if(draftId==Guid.Empty) return Reject("A valid draft submission ID is required.");
            if(await db.Set<SalesInvoice>().AnyAsync(x=>x.Id==draftId,ct))
                return new(CompanyOutcome.Conflict,Message:"This draft submission already exists. Refresh Sales Invoice to open it.");
        }
        var refs=await CheckReferences(draft,request.Action==SalesAction.Preview,ct);
        if(refs.Count>0) return Reject(refs[0].Message,refs);
        if(request.ProductCompletions is {Count:>0} completions)
        {
            if(request.Action is not (SalesAction.Save or SalesAction.Preview))
                return Reject("Missing product details can be supplied while editing a draft.");
            var completion=await StageProductCompletionsAsync(draft,completions,ct);
            if(completion.Outcome!=CompanyOutcome.Success) return completion;
        }
        var quantityPolicy=settings is not null && request.Action is SalesAction.Save or SalesAction.Preview
            ? await settings.EffectiveGeneralAsync(db,company.Id,ct):null;
        var preview=await CalculateAsync(draft,ct,quantityPolicy,row is null?null:ReadDraft(row).Lines,
            resolveFinalSaleType:finalization);
        if(request.Action==SalesAction.Preview) return preview;
        if(finalization && preview.Snapshot is { } reviewed && reviewed.Lines.Any(x=>x.Tax.Rate.Treatment!=TaxTreatment.Taxable))
            return Reject("This workflow finalizes standard taxable lines only. Review the product tax treatment.");
        if(request.Action==SalesAction.PrepareFinalization) return preview;
        if(request.Action==SalesAction.Finalize)
        {
            if(preview.Snapshot is not { } snapshot)
                return Reject(preview.Message??"The invoice could not be calculated.",preview.FieldErrors);
            if(request.Confirmation!=preview.Confirmation)
                return Reject("Invoice details changed. Review a new preview before finalizing.");
            return await FinalizeAsync(row!,snapshot,ct);
        }

        if(preview.Outcome!=CompanyOutcome.Success) return preview;
        var created=row is null;
        var now=DateTime.UtcNow;
        row??=new SalesInvoice {Id=request.CreateDraftId??Guid.NewGuid(),CompanyId=company.Id,CreatedUtc=now,CreatedBy=actor};
        row.Date=draft.Date;row.FinancialYearId=draft.YearId;row.SeriesId=draft.SeriesId;row.RegistrationId=draft.RegistrationId;
        row.CustomerId=draft.CustomerId;row.CustomerName=await Customers.Where(x=>x.Id==draft.CustomerId).Select(x=>x.LegalName).SingleOrDefaultAsync(ct)??"";
        row.PurchaseOrder=draft.PurchaseOrder;row.Reference=draft.Reference;row.DraftJson=JsonSerializer.Serialize(preview.Snapshot?.Draft??draft);
        row.UpdatedUtc=now;row.UpdatedBy=actor;
        SetTotals(row,preview.Snapshot?.Amounts);
        if(created) db.Add(row);
        Audit(row.Id,created?"DraftCreated":"DraftUpdated");
        return new(CompanyOutcome.Success,company.Id,preview.Message,Row:Describe(row,"",""),Draft:draft,Snapshot:preview.Snapshot,Confirmation:preview.Confirmation);
    }

    private async Task<SalesResult> ChangeStatusAsync(SalesInvoice? row,SalesRequest request,CancellationToken ct)
    {
        if(row is null) return new(CompanyOutcome.NotFound);
        if(row.Version!=request.Version) return new(CompanyOutcome.Conflict,Message:"The invoice changed. Refresh and try again.");
        if(request.Action==SalesAction.DeleteDraft)
        {
            if(row.LedgerId.HasValue) return Invalid("Only unissued drafts can be deleted.");
            db.Remove(row);
            Audit(row.Id,"DraftDeleted");
            return new(CompanyOutcome.Success,company.Id);
        }
        if(row.LedgerId is not Guid ledgerId) return Invalid("Only issued invoices can be cancelled.");
        var reason=request.CancellationReason?.Trim();
        if(string.IsNullOrWhiteSpace(reason) || reason.Length>250 || reason.Any(char.IsControl))
            return Invalid("Enter a cancellation reason of up to 250 characters on a single line.");
        var ledger=await db.Set<DocumentNumberLedger>().SingleOrDefaultAsync(x=>x.CompanyId==company.Id && x.Id==ledgerId,ct);
        if(ledger is not {Status:"Issued"}) return Invalid("This invoice is no longer issued.");
        ledger.Status="Cancelled";
        Audit(row.Id,"Cancelled",reason);
        return new(CompanyOutcome.Success,company.Id,Row:Describe(row,"","") with {Cancelled=true});
    }

    private async Task<SalesResult> CustomerAddressesAsync(Guid? customerId,CancellationToken ct)
    {
        var customer=await Customers.Where(x=>x.Id==customerId).Select(x=>new {x.Id,x.Version,x.LegalName,x.Gstin}).SingleOrDefaultAsync(ct);
        if(customer is null) return new(CompanyOutcome.NotFound);
        var addresses=await db.Set<CustomerAddress>().AsNoTracking().Where(x=>x.CompanyId==company.Id &&
            x.CustomerId==customer.Id && x.IsActive).OrderBy(x=>x.Label).Select(x=>new CustomerAddressDraft
            {
                Id=x.Id,Version=x.Version,Label=x.Label,AddressType=x.AddressType,RecipientName=x.RecipientName,
                Organization=x.Organization,Line1=x.Line1,Line2=x.Line2,Landmark=x.Landmark,Locality=x.Locality,
                City=x.City,District=x.District,StateName=x.StateName,StateCode=x.StateCode,PostalCode=x.PostalCode,
                Country=x.Country,Mobile=x.Mobile,Email=x.Email,IsDefaultBilling=x.IsDefaultBilling,IsDefaultShipping=x.IsDefaultShipping
            }).ToArrayAsync(ct);
        return new(CompanyOutcome.Success,company.Id,CustomerAddresses:new(customer.Id,customer.Version,customer.LegalName,customer.Gstin,addresses));
    }

    private async Task<SalesResult> List(SalesRequest request,CancellationToken ct)
    {
        var search=CatalogRules.Normalize(request.Search);
        if(search.Length<3) search="";
        var number=CatalogRules.Normalize(request.InvoiceNumberSearch);
        var customer=CatalogRules.Normalize(request.CustomerSearch);
        var cancelled=db.Set<DocumentNumberLedger>().Where(x=>x.CompanyId==company.Id && x.Status=="Cancelled");
        var query=Invoices.Where(x=>(request.Finalized==null || (x.LedgerId!=null)==request.Finalized) &&
            (request.Cancelled==null || cancelled.Any(ledger=>ledger.Id==x.LedgerId)==request.Cancelled) &&
            (request.Year==null || x.FinancialYearId==request.Year) && (request.Series==null || x.SeriesId==request.Series) &&
            (request.Customer==null || x.CustomerId==request.Customer) &&
            (request.From==null || x.Date>=request.From) && (request.To==null || x.Date<=request.To) &&
            (number.Length<3 || x.Number!=null && x.Number.ToUpper().Contains(number)) &&
            (customer.Length<3 || x.CustomerName.ToUpper().Contains(customer) ||
                Customers.Any(c=>c.Id==x.CustomerId && c.Code.ToUpper().Contains(customer))) &&
            (search=="" || x.Number!=null && x.Number.ToUpper().Contains(search) || x.CustomerName.ToUpper().Contains(search) ||
             x.PurchaseOrder.ToUpper().Contains(search) || x.Reference.ToUpper().Contains(search)))
            .OrderByDescending(x=>x.Date).ThenByDescending(x=>x.UpdatedUtc)
            .Select(x=>new {x.DraftJson,FrozenJson=x.LedgerId==null?null:db.Set<InvoiceSnapshot>()
                .Where(s=>s.CompanyId==x.CompanyId && s.LedgerId==x.LedgerId).Select(s=>s.SnapshotJson).FirstOrDefault(),
                Row=new SalesRow(x.Id,x.Version,x.Date,x.Number,x.CustomerName,
                Years.Where(y=>y.Id==x.FinancialYearId).Select(y=>y.Code).FirstOrDefault()??"",
                Series.Where(s=>s.Id==x.SeriesId).Select(s=>s.Code).FirstOrDefault()??"",x.Taxable,x.Tax,x.Total,x.LedgerId!=null,
                null,cancelled.Any(ledger=>ledger.Id==x.LedgerId))});
        if(request.Category is null) query=query.Take(500);
        var rows=new List<SalesRow>();
        await foreach(var item in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            SalesIntent? intent=null;
            try
            {
                var draft=item.Row.Finalized
                    ?item.FrozenJson is { } json?JsonSerializer.Deserialize<SalesInvoiceSnapshot>(json)?.Draft:null
                    :JsonSerializer.Deserialize<SalesDraft>(item.DraftJson);
                if(draft is not null)
                    intent=draft.Jurisdiction switch
                    {
                        GstJurisdiction.IntraState or GstJurisdiction.IntraUnionTerritory=>SalesIntent.Local,
                        GstJurisdiction.Interstate=>SalesIntent.Interstate,
                        _=>item.Row.Finalized?null:draft.EntryDetails?.Intent
                    };
            }
            catch(JsonException) { }
            if(request.Category is not null && request.Category!=intent) continue;
            rows.Add(item.Row with {Intent=intent});
            if(rows.Count==500) break;
        }
        return new(CompanyOutcome.Success,company.Id,Rows:rows);
    }

    private async Task<SalesResult> Options(CancellationToken ct)
    {
        var years=(await Years.OrderByDescending(x=>x.Start).Select(x=>new {x.Id,x.Code,x.Status,x.IsCurrent,x.Start,x.End})
            .ToArrayAsync(ct)).Select(x=>new SalesChoice(x.Id,x.Code+(x.Status=="Closed"?" (closed)":""),
                Preferred:x.IsCurrent,Active:x.Status=="Open",From:x.Start,To:x.End)).ToArray();
        var series=(await Series.Where(x=>x.DocumentType=="TaxInvoice").OrderBy(x=>x.Code)
            .Select(x=>new {x.Id,x.Code,x.Name,x.IsActive,x.FinancialYearId,x.GstRegistrationId,x.IsDefault,
                x.Prefix,x.Suffix,x.Separator,x.Padding,x.StartingNumber,
                YearCode=Years.Where(y=>y.Id==x.FinancialYearId).Select(y=>y.Code).FirstOrDefault(),
                Counter=db.Set<NumberCounter>().Where(n=>n.CompanyId==company.Id && n.SeriesId==x.Id &&
                    n.FinancialYearId==x.FinancialYearId).Select(n=>(long?)n.NextNumber).SingleOrDefault()}).ToArrayAsync(ct))
            .Select(x=>new SalesChoice(x.Id,x.Code+" — "+x.Name+(x.IsActive?"":" (inactive)"),x.FinancialYearId,x.GstRegistrationId,
                Preferred:x.IsDefault,Active:x.IsActive,NextNumberPreview:PreviewNumber(x.Code,x.Prefix,x.Suffix,x.Separator,
                    x.Padding,x.YearCode,x.Counter??x.StartingNumber))).ToArray();
        var registrations=(await Registrations.OrderByDescending(x=>x.IsInvoiceDefault)
            .Select(x=>new {x.Id,x.Gstin,x.IsInvoiceDefault,x.IsActive}).ToArrayAsync(ct))
            .Select(x=>new SalesChoice(x.Id,x.Gstin+(x.IsActive?"":" (inactive)"),Preferred:x.IsInvoiceDefault,Active:x.IsActive)).ToArray();
        var customerRows=await Customers.OrderBy(x=>x.LegalName)
            .Select(x=>new {x.Id,x.Code,x.LegalName,x.Status,x.PurchaseOrderRequired}).ToArrayAsync(ct);
        var customers=customerRows.Select(x=>new SalesChoice(x.Id,
            x.Code+" — "+x.LegalName+(x.Status=="Active"?"":" ("+x.Status+")"),
            PurchaseOrderRequired:x.PurchaseOrderRequired)).ToArray();
        var addresses=db.Set<CustomerAddress>().Where(x=>x.CompanyId==company.Id && x.IsActive);
        var billing=await addresses.Where(x=>x.AddressType=="Billing" || x.AddressType=="Both")
            .Select(x=>new SalesChoice(x.Id,x.Label+" — "+x.City,x.CustomerId,null)).ToArrayAsync(ct);
        var shipping=await addresses.Where(x=>x.AddressType=="Shipping" || x.AddressType=="Both")
            .Select(x=>new SalesChoice(x.Id,x.Label+" — "+x.City,x.CustomerId,null)).ToArrayAsync(ct);
        var profiles=db.Set<ProductMeasureProfile>().Where(x=>x.CompanyId==company.Id);
        var brands=db.Set<Brand>().Where(x=>x.CompanyId==company.Id);
        var categories=db.Set<ProductCategory>().Where(x=>x.CompanyId==company.Id);
        var taxProfiles=db.Set<ProductTaxProfile>().Where(x=>x.CompanyId==company.Id);
        var productRows=await Products.Where(p=>p.Active).OrderBy(p=>p.Name).Select(p=>new
        {
            p.Id,p.Code,p.Name,p.Sku,p.Barcode,p.Mrp,p.SalesInclusive,p.SalesRate,p.SalesUnit,p.BaseUnit,p.SalesFactor,p.Kind,p.GstRate,
            Hsn=(from profile in taxProfiles join classification in db.Set<TaxClassification>() on
                new {profile.CompanyId,Id=profile.ClassificationId} equals new {classification.CompanyId,classification.Id}
                where profile.ProductId==p.Id select classification.Code).FirstOrDefault()??p.Hsn,
            TaxId=taxProfiles.Where(t=>t.ProductId==p.Id).Select(t=>(Guid?)t.ClassificationId).FirstOrDefault(),
            TaxContext=taxProfiles.Where(t=>t.ProductId==p.Id).Select(t=>t.Context).FirstOrDefault(),
            Category=categories.Where(x=>x.Id==p.CategoryId).Select(x=>x.Name).FirstOrDefault(),
            Brand=(from profile in profiles join brand in brands on profile.BrandId equals (Guid?)brand.Id
                where profile.ProductId==p.Id select brand.Name).FirstOrDefault()??p.Brand
        }).ToArrayAsync(ct);
        var managedUnits=await (from c in db.Set<ProductUnitConversion>() join u in db.Set<UnitOfMeasure>()
            on new {c.CompanyId,Id=c.UnitId} equals new {u.CompanyId,u.Id}
            where c.CompanyId==company.Id && c.Active && c.DefaultSales && u.Active
            select new {c.ProductId,c.FactorToBaseUnit,u.Code,u.Name}).ToDictionaryAsync(x=>x.ProductId,ct);
        var products=productRows.Select(p=>new SalesChoice(p.Id,p.Code+" — "+p.Name+(p.Sku==null?"":" · "+p.Sku)+
            (p.Barcode==null?"":" · "+p.Barcode)+" · "+(p.Category??"")+" · "+(p.Brand??""),
            null,null,!managedUnits.TryGetValue(p.Id,out var unit) ||
                ProductCatalogStore.SameCommercialUnit(p.SalesUnit??p.BaseUnit,p.SalesFactor,unit.Code,unit.Name,unit.FactorToBaseUnit)
                ?p.SalesRate:null,p.SalesInclusive,p.Code,p.Sku,p.Name,p.Brand,p.Mrp,p.Kind,p.Hsn,p.GstRate,p.TaxId,p.TaxContext)).ToArray();
        var units=await (from c in db.Set<ProductUnitConversion>() join u in db.Set<UnitOfMeasure>() on
            new {c.CompanyId,Id=c.UnitId} equals new {u.CompanyId,u.Id}
            where c.CompanyId==company.Id && c.Active && u.Active
            select new SalesChoice(u.Id,u.Code+" — "+u.Name,c.ProductId,null)).ToArrayAsync(ct);
        var variants=await db.Set<ProductVariant>().Where(x=>x.CompanyId==company.Id).OrderBy(x=>x.Code).ToArrayAsync(ct);
        var attributes=(await VariantAttributesAsync(ct)).ToLookup(x=>x.VariantId,x=>x.Value);
        var variantChoices=variants.Select(x=>new SalesVariantChoice(x.Id,x.ProductId,x.Code,x.Sku,x.Barcode,
            string.Join(" / ",attributes[x.Id].Select(a=>a.Name)),x.Mrp,x.SalesRate,x.Active)).ToArray();
        var entrySettings=await SettingsStore.ReadCompanyAsync(db,company.Id,ct);
        var graftex=SalesFormConfiguration.UsesGraftex(entrySettings.Get(SalesFormConfiguration.EntryProfile),company.Code,company.LegalName);
        var form=SalesFormConfiguration.From(entrySettings);
        return new(CompanyOutcome.Success,company.Id,Options:new(years,series,registrations,customers,billing,shipping,products,
            units,company.LegalName,await TaxChoicesAsync(ct),canFillMrp,canLinkTax,variantChoices,
            graftex?form.ApplyGraftexDefaults():form,username,entrySettings.Get(SalesFormConfiguration.EntryLayout),graftex));
    }

    private string? PreviewNumber(string code,string prefix,string suffix,string separator,int padding,string? year,long sequence)
    {
        if(year is null || sequence<1) return null;
        var format=DocumentNumberFormat.Create(new(Code:code,Prefix:prefix,Suffix:suffix,Separator:separator,Padding:padding),
            company.Code,year);
        return format is not null && sequence<=format.Maximum?format.Render(sequence):null;
    }

    private sealed record VariantAttributeRow(Guid VariantId,VariantAttributeSnapshot Value);
    private Task<VariantAttributeRow[]> VariantAttributesAsync(CancellationToken ct)=>
        (from link in db.Set<ProductVariantOption>()
         join option in db.Set<VariantOption>() on new {link.CompanyId,link.Axis,Id=link.OptionId}
             equals new {option.CompanyId,option.Axis,option.Id}
         where link.CompanyId==company.Id
         orderby link.Axis
         select new VariantAttributeRow(link.VariantId,new(option.Axis,option.Code,option.Name))).ToArrayAsync(ct);

    private async Task<SalesTaxChoice[]> TaxChoicesAsync(CancellationToken ct)
    {
        var rows=await (from assignment in db.Set<TaxClassificationRateAssignment>()
            join classification in db.Set<TaxClassification>() on assignment.ClassificationId equals classification.Id
            join rate in db.Set<GstRateDefinition>() on assignment.RateId equals rate.Id
            where assignment.CompanyId==company.Id && classification.CompanyId==company.Id && rate.CompanyId==company.Id && assignment.Active
            orderby classification.Code,assignment.Context,assignment.From
            select new {assignment.Id,assignment.ClassificationId,classification.Type,classification.Code,assignment.Context,
                rate.Total,assignment.From,assignment.To,RateFrom=rate.From,RateTo=rate.To,Selectable=classification.Active && rate.Active})
            .ToArrayAsync(ct);
        return rows.Select(x=>new SalesTaxChoice(x.Id,x.ClassificationId,x.Type,x.Code,x.Context,x.Total,
            x.From>x.RateFrom?x.From:x.RateFrom,x.To is null || x.RateTo<x.To?x.RateTo:x.To,x.Selectable)).ToArray();
    }

    private async Task<SalesResult> StageProductCompletionsAsync(SalesDraft draft,IReadOnlyList<SalesProductCompletion> completions,CancellationToken ct)
    {
        var lines=draft.Lines??[];
        if(completions.Count>lines.Count || completions.Any(x=>x.Line<1 || x.Line>lines.Count ||
            x.Mrp is null && x.TaxAssignmentId is null) || completions.Select(x=>x.Line).Distinct().Count()!=completions.Count)
            return Invalid("Supply missing product details against an existing invoice line.");
        var catalog=new CompanyTaxCatalog(db,company.Id);
        var choices=completions.Any(x=>x.TaxAssignmentId.HasValue)?await TaxChoicesAsync(ct):[];
        foreach(var group in completions.GroupBy(x=>lines[x.Line-1].ProductId))
        {
            var product=await Products.SingleAsync(x=>x.Id==group.Key,ct);
            var mrps=group.Where(x=>x.Mrp.HasValue).Select(x=>x.Mrp!.Value).Distinct().ToArray();
            var assignments=group.Where(x=>x.TaxAssignmentId.HasValue).Select(x=>x.TaxAssignmentId!.Value).Distinct().ToArray();
            var line=group.First().Line;
            if(mrps.Length>1) return FieldError(nameof(Product.Mrp),"Enter the same MRP for repeated lines of this product.",line);
            if(assignments.Length>1) return FieldError("TaxAssignment","Choose the same tax mapping for repeated lines of this product.",line);
            if(mrps.Length==1)
            {
                var mrpLine=group.First(x=>x.Mrp.HasValue).Line;
                if(!canFillMrp) return FieldError(nameof(Product.Mrp),"You do not have permission to update product MRP.",mrpLine);
                var errors=new Dictionary<string,string>();
                CatalogRules.CheckDecimal(nameof(Product.Mrp),mrps[0],errors);
                if(errors.TryGetValue(nameof(Product.Mrp),out var error)) return FieldError(nameof(Product.Mrp),error,mrpLine);
                if(product.Mrp.HasValue && product.Mrp!=mrps[0])
                    return FieldError(nameof(Product.Mrp),"Product MRP was already set. Refresh the invoice before changing it.",mrpLine);
                if(product.Mrp is null)
                {
                    product.Mrp=mrps[0];
                    product.UpdatedUtc=DateTime.UtcNow;
                    product.UpdatedByUserId=actor;
                    db.Add(new AuditEvent {CompanyId=company.Id,ActorUserId=actor,ActorKind="User",RecordId=product.Id.ToString(),
                        RecordType="Product",Action="ProductMrpCompletedFromInvoice",ChangesJson="{}"});
                }
            }
            if(assignments.Length==0) continue;
            var taxLine=group.First(x=>x.TaxAssignmentId.HasValue).Line;
            if(!canLinkTax) return FieldError("TaxAssignment","You do not have permission to link product GST profiles.",taxLine);
            var choice=choices.SingleOrDefault(x=>x.AssignmentId==assignments[0] && x.Selectable && x.Applies(draft.Date));
            if(choice is null || choice.Type!=(product.Kind=="Services"?"SAC":"HSN"))
                return FieldError("TaxAssignment","Choose an active same-company HSN/SAC and GST assignment for the invoice date.",taxLine);
            var profile=await db.Set<ProductTaxProfile>().SingleOrDefaultAsync(x=>x.CompanyId==company.Id && x.ProductId==product.Id,ct);
            if(profile is not null && (profile.ClassificationId!=choice.ClassificationId || profile.Context!=choice.Context))
                return FieldError("TaxAssignment","This product already has a GST profile. Refresh the invoice to use the saved profile.",taxLine);
            if(profile is null && (CatalogRules.Optional(product.Hsn) is {} hsn && hsn!=choice.Code ||
                product.GstRate is decimal gst && gst!=choice.Rate))
                return FieldError("TaxAssignment","Choose a mapping that agrees with the product's existing HSN/SAC and legacy GST rate.",taxLine);
            var resolved=await catalog.ResolveAsync(choice.ClassificationId,choice.Type,choice.Context,draft.Date,ct);
            if(resolved.Snapshot is not { } rate || rate.AssignmentId!=choice.AssignmentId)
                return FieldError("TaxAssignment",resolved.Message??"The chosen tax mapping is no longer applicable.",taxLine);
            if(profile is null)
            {
                var saved=await catalog.StageProductProfileAsync(product,null,new(choice.ClassificationId,choice.Context),actor,ct);
                if(saved.Outcome!=CompanyOutcome.Success)
                    return FieldError("TaxAssignment",saved.Message??"The selected tax mapping could not be linked.",taxLine);
                product.UpdatedUtc=DateTime.UtcNow;
                product.UpdatedByUserId=actor;
                db.Entry(product).Property(x=>x.UpdatedUtc).IsModified=true;
            }
        }
        return new(CompanyOutcome.Success);
    }

    private async Task<IReadOnlyList<SalesFieldError>> CheckReferences(SalesDraft draft,bool preview,CancellationToken ct)
    {
        var errors=new List<SalesFieldError>();
        if(!preview && draft.YearId.HasValue && !await Years.AnyAsync(x=>x.Id==draft.YearId && x.Status=="Open" && x.Start<=draft.Date && x.End>=draft.Date,ct))
            errors.Add(new(nameof(draft.YearId),"Choose an open financial year containing the invoice date."));
        if(draft.RegistrationId.HasValue && !await Registrations.AnyAsync(x=>x.Id==draft.RegistrationId && x.IsActive,ct))
            errors.Add(new(nameof(draft.RegistrationId),"Choose an active supplier GST registration."));
        if(!preview && draft.SeriesId.HasValue && !await Series.AnyAsync(x=>x.Id==draft.SeriesId && x.FinancialYearId==draft.YearId &&
            x.GstRegistrationId==draft.RegistrationId && x.DocumentType=="TaxInvoice" && x.IsActive,ct))
            errors.Add(new(nameof(draft.SeriesId),"Choose an active tax invoice series for this year and registration."));
        if(draft.CustomerId.HasValue && !await Customers.AnyAsync(x=>x.Id==draft.CustomerId && x.Status=="Active",ct))
            errors.Add(new(nameof(draft.CustomerId),"Choose an active customer in this company."));
        foreach(var (id,type) in new[]{(draft.BillingAddressId,"Billing"),(draft.ShippingAddressId,"Shipping")})
            if(id.HasValue && !await db.Set<CustomerAddress>().AnyAsync(x=>x.Id==id && x.CompanyId==company.Id &&
                x.CustomerId==draft.CustomerId && x.IsActive && (x.AddressType==type || x.AddressType=="Both"),ct))
                errors.Add(new(type=="Billing"?nameof(draft.BillingAddressId):nameof(draft.ShippingAddressId),"Choose an active address belonging to the selected customer."));
        var index=0;
        foreach(var line in draft.Lines??[])
        {
            index++;
            if(!await Products.AnyAsync(x=>x.Id==line.ProductId && x.Active,ct)) errors.Add(new(nameof(line.ProductId),"Choose an active product in this company.",index));
            if(line.UnitId.HasValue && !await db.Set<ProductUnitConversion>().AnyAsync(x=>x.CompanyId==company.Id &&
                x.ProductId==line.ProductId && x.UnitId==line.UnitId && x.Active,ct)) errors.Add(new(nameof(line.UnitId),"Choose a unit configured for this product.",index));
            var variants=db.Set<ProductVariant>().Where(x=>x.CompanyId==company.Id && x.ProductId==line.ProductId && x.Active);
            if(line.ProductVariantId is Guid variantId ? !await variants.AnyAsync(x=>x.Id==variantId,ct)
                : await variants.AnyAsync(ct))
                errors.Add(new(nameof(line.ProductVariantId),"Choose an active variant belonging to this product.",index));
        }
        return errors;
    }

    private static SalesResult FieldError(string field,string message,int? line=null)=>
        Invalid(message) with {FieldErrors=[new(field,message,line)]};

    private async Task<SalesResult> CalculateAsync(SalesDraft draft,CancellationToken ct,
        ResolvedSettings? quantityPolicy=null,IReadOnlyList<SalesLineInput>? originalLines=null,bool resolveFinalSaleType=false)
    {
        if(draft.RegistrationId is null || draft.CustomerId is null ||
            draft.Lines is not {Count:>0}) return Invalid("Choose a supplier registration, customer and at least one product.");
        var customer=await Customers.SingleAsync(x=>x.Id==draft.CustomerId,ct);
        var customerResult=await CustomerStore.Snapshot(db,customer,new(company.Id,company.Code,company.LegalName),
            new(draft.BillingAddressId,draft.ShippingAddressId,IncludeContactDetails:true,IncludeAadhaarSuffix:true),ct);
        if(customerResult.Snapshot is not {} buyer) return FieldError(nameof(draft.CustomerId),customerResult.Detail?.Eligibility?.MissingOrBlockingDetails.FirstOrDefault()??"Review customer eligibility and billing/shipping addresses.");
        if(buyer.PurchaseOrderRequired && string.IsNullOrWhiteSpace(draft.PurchaseOrder)) return FieldError(nameof(draft.PurchaseOrder),"Enter the purchase order required by this customer.");
        if(buyer.Currency!="INR" || buyer.BillTo.Country!="IN" || buyer.ShipTo.Country!="IN")
            return Invalid("Stage 5A supports domestic addresses and INR invoices only.");
        var gst=await Registrations.SingleAsync(x=>x.Id==draft.RegistrationId,ct);
        var deliveryState=buyer.ShipTo.StateCode;
        if(deliveryState is null || !CompanyRules.States.ContainsKey(deliveryState))
            return FieldError(nameof(draft.ShippingAddressId),"Select a shipping address with a valid Indian state.");
        if(!CompanyRules.States.ContainsKey(gst.StateCode))
            return FieldError(nameof(draft.RegistrationId),"Select a supplier registration with a valid Indian state.");
        if(buyer.RegistrationType is not ("Regular" or "Composition" or "Unregistered" or "Consumer") ||
            company.RegistrationType=="Composition" || !string.IsNullOrWhiteSpace(buyer.SupplyClassification))
            return FieldError(nameof(draft.CustomerId),"This sales workflow supports standard domestic goods. Check the customer tax registration and supply classification.");
        var intent=draft.EntryDetails?.Intent??(draft.Jurisdiction switch
        {
            GstJurisdiction.IntraState=>SalesIntent.Local,GstJurisdiction.Interstate=>SalesIntent.Interstate,_=>(SalesIntent?)null
        });
        if(intent is null && !resolveFinalSaleType)
            return FieldError("SaleIntent","Choose Local Sale or Inter-State Sale in the invoice editor.");
        var interstate=resolveFinalSaleType?deliveryState!=gst.StateCode:intent==SalesIntent.Interstate;
        var appliedIntent=interstate?SalesIntent.Interstate:SalesIntent.Local;
        var saleTypeNotice=resolveFinalSaleType && intent!=appliedIntent
            ?$"The supplier registration and delivery state resolve to {(interstate?"Interstate Sale (IGST)":"Local Sale (CGST and SGST)")}. Review the recalculated totals before issuing the invoice."
            :null;
        if(!interstate && deliveryState is "01" or "04" or "07" or "26" or "31" or "34" or "35" or "38")
            return FieldError(nameof(draft.RegistrationId),"Local sales for this Union Territory require a separate tax workflow.");
        draft=draft with {PlaceOfSupply=deliveryState,Jurisdiction=interstate?GstJurisdiction.Interstate:GstJurisdiction.IntraState,
            DomesticConfirmed=true,EntryDetails=(draft.EntryDetails??new()) with
            {Intent=appliedIntent}};
        var address=await (from link in db.Set<GstRegistrationAddress>() join a in db.Set<CompanyAddress>()
            on new {link.CompanyId,Id=link.AddressId} equals new {a.CompanyId,a.Id}
            where link.CompanyId==company.Id && link.GstRegistrationId==gst.Id
            orderby a.IsDefaultBilling descending,a.Id
            select a).ToArrayAsync(ct);
        if(address.Length==0) return FieldError(nameof(draft.RegistrationId),
            "In Company Master, edit the selected GST registration and choose Link the default company billing address.");
        var defaults=address.Where(x=>x.IsDefaultBilling).ToArray();
        var selectedAddress=address.Length==1?address[0]:defaults.Length==1?defaults[0]:null;
        if(selectedAddress is null) return FieldError(nameof(draft.RegistrationId),
            "The selected supplier GST registration has multiple linked addresses. Set exactly one linked address as Default Billing in Company Master.");
        var settings=await db.Set<CompanySettings>().SingleOrDefaultAsync(x=>x.CompanyId==company.Id,ct);
        var companyResult=await CompanyPresentationStore.Snapshot(db,company,settings,
            db.Set<BrandingAsset>().Where(x=>x.CompanyId==company.Id),db.Set<CompanyBankAccount>().Where(x=>x.CompanyId==company.Id),
            Registrations,DateTime.UnixEpoch,ct);
        if(companyResult.Snapshot is not {} presentation) return Invalid("Company presentation could not be resolved.");
        var contacts=await db.Set<CompanyContact>().Where(x=>x.CompanyId==company.Id).OrderBy(x=>x.Id).ToArrayAsync(ct);
        var primaryPhone=contacts.FirstOrDefault(x=>x.Purpose=="Mobile")?.Value ??
            contacts.FirstOrDefault(x=>x.Kind=="Phone" && x.Purpose!="AlternateMobile")?.Value;
        var seller=new SalesSeller(company.LegalName,company.DisplayName,primaryPhone,
            contacts.FirstOrDefault(x=>x.Kind=="Email")?.Value,gst.Gstin,gst.StateCode,
            FormatSellerAddress(selectedAddress),presentation with {Tax=new(gst.Id,gst.Gstin,gst.StateCode)},company.Iec,company.Tagline,
            contacts.FirstOrDefault(x=>x.Purpose=="AlternateMobile")?.Value);
        var lines=new List<FinalSalesLine>();
        var resolvedProducts=new Dictionary<(Guid Product,Guid? Unit),CatalogResult>();
        SalesTaxChoice[]? taxChoices=null;
        var variantIds=draft.Lines.Where(x=>x.ProductVariantId.HasValue).Select(x=>x.ProductVariantId!.Value).Distinct().ToArray();
        var selectedVariants=await db.Set<ProductVariant>().Where(x=>x.CompanyId==company.Id && variantIds.Contains(x.Id))
            .ToDictionaryAsync(x=>x.Id,ct);
        var variantAttributes=variantIds.Length==0?null:(await VariantAttributesAsync(ct)).ToLookup(x=>x.VariantId,x=>x.Value);
        foreach(var line in draft.Lines)
        {
            var key=(line.ProductId,line.UnitId);
            if(!resolvedProducts.TryGetValue(key,out var item))
            {
                item=await ProductCatalogStore.ProductAsync(db,new(CatalogAction.Snapshot,true,company.Id,line.ProductId,
                    SelectedUnitId:line.UnitId,TaxDate:draft.Date),company.Id,actor,db.Set<ProductCategory>().Where(x=>x.CompanyId==company.Id),
                    Products,null,false,false,ct,allowUnlinkedTax:true);
                resolvedProducts.Add(key,item);
            }
            var index=lines.Count+1;
            if(item.Snapshot is not { } product || product.Factor<=0 || product.Currency!="INR")
                return item.Errors?.TryGetValue("TaxAssignment",out var taxError)==true
                    ?FieldError("TaxAssignment",taxError,index)
                    :FieldError(nameof(line.ProductId),item.Message??"Resolve the product tax profile and INR sales configuration.",index);
            var tax=product.Tax;
            if(tax is null && product.Kind=="Goods" && !string.IsNullOrWhiteSpace(product.Hsn))
            {
                taxChoices??=await TaxChoicesAsync(ct);
                var configured=taxChoices.Where(x=>x.Type=="HSN" && x.Code==product.Hsn.Trim() && x.Selectable &&
                    x.Applies(draft.Date)).Take(2).ToArray();
                if(configured.Length>1)
                    return FieldError("TaxAssignment","More than one configured rate matches this HSN. Select the applicable tax mapping.",index);
                if(configured.Length==1)
                {
                    var match=configured[0];
                    var resolved=await new CompanyTaxCatalog(db,company.Id).ResolveAsync(match.ClassificationId,match.Type,match.Context,draft.Date,ct);
                    if(resolved.Snapshot is not { } applicable)
                        return FieldError("TaxAssignment",resolved.Message??"The configured HSN rate could not be resolved.",index);
                    tax=applicable;
                    product=product with {Hsn=tax.Code,Tax=tax};
                }
            }
            if(tax is null)
            {
                if(!canLinkTax || product.Kind!="Goods" || product.TaxExempt)
                    return FieldError("TaxAssignment","Choose a configured HSN and GST profile for this product.",index);
                var hsn=(line.ManualHsn??product.Hsn)?.Trim();
                if(hsn is null || hsn.Length is not (2 or 4 or 6 or 8) || !hsn.All(char.IsAsciiDigit) ||
                    (!string.IsNullOrWhiteSpace(product.Hsn) && !string.Equals(hsn,product.Hsn.Trim(),StringComparison.Ordinal)))
                    return FieldError(nameof(line.ManualHsn),"Enter a valid goods HSN that agrees with the product's saved HSN.",index);
                if(line.ManualGstRate is not decimal rate || rate<=0 || rate>100 || decimal.Round(rate,4)!=rate)
                    return FieldError(nameof(line.ManualGstRate),"Enter a GST percentage greater than zero and up to 100, with up to four decimal places.",index);
                tax=new(Guid.Empty,"HSN",hsn,"Manual invoice tax entry",Guid.Empty,Guid.Empty,"Manual",draft.Date,
                    TaxTreatment.Taxable,rate,rate/2m,rate/2m,rate,null,0,draft.Date,"Invoice only");
                product=product with {Hsn=hsn,Tax=tax};
            }
            if(line.ProductVariantId is Guid variantId)
            {
                var variant=selectedVariants[variantId];
                product=product with {Sku=variant.Sku??product.Sku,Barcode=variant.Barcode??product.Barcode,
                    Mrp=variant.Mrp??product.Mrp,Variant=new(variant.Id,variant.Code,variant.Sku,variant.Barcode,
                        variantAttributes![variantId].ToArray())};
            }
            if(decimal.Round(line.Quantity,product.QuantityPrecision)!=line.Quantity)
                return FieldError(nameof(line.Quantity),"Quantity exceeds the precision of the selected unit.",index);
            var original=originalLines?.ElementAtOrDefault(index-1);
            if(quantityPolicy is not null && ConfigurationModule.UnitPrecision(quantityPolicy,product.Unit) is int precision &&
                (original is null || original.ProductId!=line.ProductId || original.UnitId!=line.UnitId ||
                    original.ProductVariantId!=line.ProductVariantId || original.Quantity!=line.Quantity) &&
                decimal.Round(line.Quantity,precision)!=line.Quantity)
                return FieldError(nameof(line.Quantity),$"{product.Unit} allows up to {precision} decimal places in this company.",index);
            var validation=SalesCalculation.ValidateLine(line,index);
            if(validation.Count>0) return Invalid(validation[0].Message) with {FieldErrors=validation};
            if(tax.Type!="HSN") return FieldError(nameof(line.ProductId),
                "This invoice workflow supports goods with an HSN tax profile.",index);
            var determination=GstDetermination.Determine(tax,new(gst.Id,buyer.CustomerId,draft.PlaceOfSupply,draft.Date,
                draft.Jurisdiction,0,draft.DomesticConfirmed));
            if(determination.Snapshot is not {} applied || SalesCalculation.Calculate(line,applied) is not {} amounts)
                return FieldError("TaxAssignment","The configured GST rate could not be applied to this product and sale type.",index);
            lines.Add(new(index,line,product,applied with {TaxableValue=amounts.Taxable},line.Quantity*product.Factor,amounts));
        }
        var effectiveDraft=draft with {Terms=string.IsNullOrWhiteSpace(draft.Terms)?buyer.PaymentTerms??"":draft.Terms};
        var snapshot=new SalesInvoiceSnapshot(effectiveDraft,buyer with {CapturedUtc=DateTime.UnixEpoch},seller,lines,SalesCalculation.Sum(lines.Select(x=>x.Amounts)));
        return new(CompanyOutcome.Success,company.Id,Message:saleTypeNotice,Snapshot:snapshot,
            Confirmation:Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot))));
    }

    private async Task<SalesResult> FinalizeAsync(SalesInvoice row,SalesInvoiceSnapshot snapshot,CancellationToken ct)
    {
        var draft=snapshot.Draft;
        var series=await Series.SingleAsync(x=>x.Id==draft.SeriesId,ct);
        var year=await Years.SingleAsync(x=>x.Id==draft.YearId,ct);
        var format=DocumentNumberFormat.Create(new(Code:series.Code,Prefix:series.Prefix,Suffix:series.Suffix,
            Separator:series.Separator,Padding:series.Padding),company.Code,year.Code);
        var counter=await db.Set<NumberCounter>().SingleOrDefaultAsync(x=>x.CompanyId==company.Id && x.SeriesId==series.Id && x.FinancialYearId==year.Id,ct);
        var sequence=counter?.NextNumber??series.StartingNumber;
        if(format is null || sequence<1 || sequence>format.Maximum)
        {Audit(row.Id,"FinalizationRejected");return Invalid("The series format or next sequence is unavailable. Review Document Series Master.");}
        var number=format.Render(sequence);
        var ledger=new DocumentNumberLedger {CompanyId=company.Id,SeriesId=series.Id,FinancialYearId=year.Id,
            GstRegistrationId=series.GstRegistrationId,Sequence=sequence,CompleteNumber=number};
        if(counter is null) db.Add(new NumberCounter {CompanyId=company.Id,SeriesId=series.Id,FinancialYearId=year.Id,NextNumber=checked(sequence+1)});
        else counter.NextNumber=checked(sequence+1);
        db.Add(ledger);
        var now=DateTime.UtcNow;
        snapshot=snapshot with {Number=number,FinancialYearCode=year.Code,DocumentSeriesCode=series.Code,
            Customer=snapshot.Customer with {CapturedUtc=now},
            Seller=snapshot.Seller with {Presentation=snapshot.Seller.Presentation with {CapturedUtc=now}}};
        db.Add(new InvoiceSnapshot {CompanyId=company.Id,LedgerId=ledger.Id,SnapshotJson=JsonSerializer.Serialize(snapshot)});
        await db.SaveChangesAsync(ct);
        row.LedgerId=ledger.Id;row.Number=number;row.FinalizedUtc=now;row.FinalizedBy=actor;row.UpdatedUtc=now;row.UpdatedBy=actor;
        row.CustomerName=snapshot.Customer.LegalName;
        SetTotals(row,snapshot.Amounts);
        Audit(row.Id,"Finalized");
        return new(CompanyOutcome.Success,company.Id,Row:Describe(row,year.Code,series.Code),Snapshot:snapshot,Draft:draft,
            Allocation:new(row.Id,ledger.Id,series.Id,sequence,number));
    }

    private async Task<SalesResult> View(SalesInvoice row,CancellationToken ct)
    {
        var draft=ReadDraft(row);
        SalesResult? calculated=null;
        if(row.LedgerId is null)
        {
            var required=draft.RequiredFields(requireNumbering:false);
            if(required.Count==0 &&
                (await CheckReferences(draft,preview:true,ct)).Count==0)
                calculated=await CalculateAsync(draft,ct);
        }
        var snapshot=await db.Set<InvoiceSnapshot>().Where(x=>x.CompanyId==company.Id && x.LedgerId==row.LedgerId)
            .Select(x=>x.SnapshotJson).SingleOrDefaultAsync(ct);
        var ledger=await db.Set<DocumentNumberLedger>().SingleOrDefaultAsync(x=>x.CompanyId==company.Id && x.Id==row.LedgerId,ct);
        var events=await db.Set<AuditEvent>().Where(x=>x.CompanyId==company.Id && x.RecordType=="SalesInvoice" &&
            x.RecordId==row.Id.ToString()).OrderByDescending(x=>x.OccurredUtc).Take(50).ToArrayAsync(ct);
        return new(CompanyOutcome.Success,company.Id,Row:Describe(row,"","") with {Cancelled=ledger?.Status=="Cancelled"},Draft:draft,
            Snapshot:row.LedgerId is null?calculated?.Snapshot:
                snapshot is null?null:JsonSerializer.Deserialize<SalesInvoiceSnapshot>(snapshot),
            Allocation:ledger is null?null:new(row.Id,ledger.Id,ledger.SeriesId,ledger.Sequence,ledger.CompleteNumber),
            Audit:events.Select(DescribeAudit).ToArray(),
            PendingGstLines:calculated?.PendingGstLines);
    }

    private static string DescribeAudit(AuditEvent audit)
    {
        var summary=$"{audit.OccurredUtc:u} · {audit.Action}";
        if(audit.Action!="SalesInvoiceCancelled") return summary;
        using var changes=JsonDocument.Parse(audit.ChangesJson);
        return changes.RootElement.ValueKind==JsonValueKind.Object &&
            changes.RootElement.TryGetProperty("Reason",out var reason) && reason.ValueKind==JsonValueKind.String
            ?summary+" · "+reason.GetString():summary;
    }

    private static string FormatSellerAddress(CompanyAddress address)
    {
        var streetAndCity=string.Join(", ",new[]{address.Lines,address.Line2,address.City}
            .Where(value=>!string.IsNullOrWhiteSpace(value)).Select(value=>value!.Trim()));
        var cityAndPin=string.Join(" - ",new[]{streetAndCity,address.PostalCode.Trim()}
            .Where(value=>value.Length>0));
        var state=CompanyRules.StateAbbreviations.GetValueOrDefault(address.StateCode) ??
            CompanyRules.States.GetValueOrDefault(address.StateCode,address.StateCode);
        var country=string.Equals(address.CountryCode,"IN",StringComparison.OrdinalIgnoreCase)?"INDIA":address.CountryCode;
        return string.Join(" ",new[]{cityAndPin,string.IsNullOrWhiteSpace(state)?"":$"({state})",
            string.IsNullOrWhiteSpace(country)?"":$"({country})"}.Where(value=>value.Length>0));
    }

    private static SalesDraft ReadDraft(SalesInvoice row)=>JsonSerializer.Deserialize<SalesDraft>(row.DraftJson)!;
    private static SalesRow Describe(SalesInvoice row,string year,string series)=>new(row.Id,row.Version,row.Date,row.Number,row.CustomerName,
        year,series,row.Taxable,row.Tax,row.Total,row.LedgerId.HasValue);
    private static void SetTotals(SalesInvoice row,SalesAmounts? totals)
    {row.Taxable=totals?.Taxable??0;row.Tax=totals?.Tax??0;row.Total=totals?.Total??0;}
    private void Audit(Guid invoice,string action,string? reason=null)=>db.Add(new AuditEvent {CompanyId=company.Id,ActorUserId=actor,ActorKind="User",
        RecordId=invoice.ToString(),RecordType="SalesInvoice",Action="SalesInvoice"+action,
        ChangesJson=reason is null?"{}":JsonSerializer.Serialize(new {Reason=reason})});
}
