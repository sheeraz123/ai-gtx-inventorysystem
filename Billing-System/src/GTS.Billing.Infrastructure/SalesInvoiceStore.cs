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
                request.Action is SalesAction.Save or SalesAction.Print or SalesAction.PrintToPdf && result.Outcome==CompanyOutcome.Success)
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
        if(request.Action is SalesAction.PrintPreview or SalesAction.Print or SalesAction.PrintToPdf)
        {
            if(row is not {LedgerId:Guid ledger,Number:{ } number})
                return Invalid("Select a finalized invoice to preview or print.");
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
            return request.Action is SalesAction.View or SalesAction.Finalize ? await View(row,ct) :
                Invalid("Finalized invoices are immutable.");
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
        var gstReviewMode=companySettings.Get(SettingsRegistry.SalesGstReviewMode);
        bool CanCompleteGstReview(string place,GstJurisdiction jurisdiction)=>
            (draft.PlaceOfSupply.Length==0 || draft.PlaceOfSupply==place) &&
            (draft.Jurisdiction==GstJurisdiction.Unresolved || draft.Jurisdiction==jurisdiction) &&
            (draft.PlaceOfSupply.Length==0 || draft.Jurisdiction==GstJurisdiction.Unresolved || !draft.DomesticConfirmed);
        if(gstReviewMode!="Manual review" &&
            companySettings.Get(SettingsRegistry.ReviewedSalesGstPlace) is { Length:>0 } defaultPlace &&
            Enum.TryParse<GstJurisdiction>(companySettings.Get(SettingsRegistry.ReviewedSalesGstJurisdiction),out var defaultJurisdiction) &&
            defaultJurisdiction!=GstJurisdiction.Unresolved &&
            CanCompleteGstReview(defaultPlace,defaultJurisdiction) &&
            (draft.EntryDetails?.Intent is not { } selectedIntent ||
                (selectedIntent==SalesIntent.Interstate)==(defaultJurisdiction==GstJurisdiction.Interstate)) &&
            await Registrations.AnyAsync(x=>x.Id==draft.RegistrationId && x.IsActive &&
                (defaultJurisdiction==GstJurisdiction.Interstate?x.StateCode!=defaultPlace:x.StateCode==defaultPlace),ct))
            draft=draft with {PlaceOfSupply=defaultPlace,Jurisdiction=defaultJurisdiction,DomesticConfirmed=true};
        if(finalization && gstReviewMode=="Company workflow" && draft.EntryDetails?.Intent==SalesIntent.Local &&
            CanCompleteGstReview(GraftexLocalState,GstJurisdiction.IntraState) &&
            SalesFormConfiguration.UsesGraftex(companySettings.Get(SalesFormConfiguration.EntryProfile),company.Code,company.LegalName) &&
            await CanReviewGraftexLocalSaleAsync(draft,ct))
            draft=draft with {PlaceOfSupply=GraftexLocalState,Jurisdiction=GstJurisdiction.IntraState,DomesticConfirmed=true};

        // Finalization presents the resolved facts for explicit confirmation and rechecks
        // the same snapshot before assigning a number. The draft checkbox is optional here.
        if(finalization && gstReviewMode!="Manual review" && !form.Field("TaxDetails").Required &&
            !draft.DomesticConfirmed && CompanyRules.States.ContainsKey(draft.PlaceOfSupply) &&
            draft.Jurisdiction is GstJurisdiction.IntraState or GstJurisdiction.IntraUnionTerritory or GstJurisdiction.Interstate)
            draft=draft with {DomesticConfirmed=true};

        var requiredFields=draft.RequiredFields(requireNumbering:request.Action!=SalesAction.Preview);
        var gstPending=requiredFields.Any(error=>SalesFormConfiguration.IsGstInput(error.Field));
        var requireGstForDraft=request.Action==SalesAction.Save && form.Field("TaxDetails").Required;
        if(gstPending && !finalization && !requireGstForDraft)
            requiredFields=requiredFields.Where(error=>!SalesFormConfiguration.IsGstInput(error.Field)).ToArray();
        if(requiredFields.Count>0)
        {
            var message=gstPending && (finalization || requireGstForDraft)
                ?"GST details could not be resolved from the invoice and company setup. Use Review GST details or check Settings → Sales Defaults."
                :requiredFields[0].Message;
            if(finalization && gstReviewMode!="Manual review" && !form.Field("TaxDetails").Required &&
                requiredFields.Any(error=>error.Field is nameof(SalesDraft.PlaceOfSupply) or nameof(SalesDraft.Jurisdiction)))
                requiredFields=requiredFields.Where(error=>error.Field!=nameof(SalesDraft.DomesticConfirmed)).ToArray();
            return Reject(message,requiredFields);
        }
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
        var preview=await CalculateAsync(draft,ct,gstPending,quantityPolicy,row is null?null:ReadDraft(row).Lines);
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
        row.PurchaseOrder=draft.PurchaseOrder;row.Reference=draft.Reference;row.DraftJson=JsonSerializer.Serialize(draft);
        row.UpdatedUtc=now;row.UpdatedBy=actor;
        SetTotals(row,preview.Snapshot?.Amounts);
        if(created) db.Add(row);
        Audit(row.Id,created?"DraftCreated":"DraftUpdated");
        return new(CompanyOutcome.Success,company.Id,preview.Message,Row:Describe(row,"",""),Draft:draft,Snapshot:preview.Snapshot,Confirmation:preview.Confirmation);
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

    private const string GraftexLocalState="03"; // Punjab

    private async Task<bool> CanReviewGraftexLocalSaleAsync(SalesDraft draft,CancellationToken ct)
    {
        const string Punjab=GraftexLocalState;
        if(draft.CustomerId is not Guid customer || draft.Lines is not {Count:>0} ||
            company.RegistrationType!="Regular") return false;
        if(!await Registrations.AnyAsync(x=>x.Id==draft.RegistrationId && x.IsActive && x.StateCode==Punjab,ct))
            return false;
        var buyer=await Customers.SingleOrDefaultAsync(x=>x.Id==customer,ct);
        if(buyer is null || !string.IsNullOrWhiteSpace(buyer.SupplyClassification) ||
            !string.IsNullOrWhiteSpace(buyer.ExemptionNotes)) return false;
        var eligible=buyer.RegistrationType switch
        {
            "Regular"=>(string.IsNullOrEmpty(buyer.GstStateCode) || buyer.GstStateCode==Punjab) &&
                buyer.Gstin?.StartsWith(Punjab,StringComparison.Ordinal)==true,
            "Unregistered" or "Consumer"=>string.IsNullOrWhiteSpace(buyer.Gstin),
            _=>false
        };
        if(!eligible) return false;
        var result=await CustomerStore.Snapshot(db,buyer,new(company.Id,company.Code,company.LegalName),
            new(draft.BillingAddressId,draft.ShippingAddressId),ct);
        if(result.Snapshot is not {BillTo:{Country:"IN",StateCode:Punjab},ShipTo:{Country:"IN",StateCode:Punjab}})
            return false;
        var products=draft.Lines.Select(x=>x.ProductId).Distinct().ToArray();
        return await Products.CountAsync(x=>products.Contains(x.Id) && x.Kind=="Goods",ct)==products.Length;
    }

    private async Task<SalesResult> List(SalesRequest request,CancellationToken ct)
    {
        var search=CatalogRules.Normalize(request.Search);
        if(search.Length<3) search="";
        var number=CatalogRules.Normalize(request.InvoiceNumberSearch);
        var customer=CatalogRules.Normalize(request.CustomerSearch);
        var query=Invoices.Where(x=>(request.Finalized==null || (x.LedgerId!=null)==request.Finalized) &&
            (request.Year==null || x.FinancialYearId==request.Year) && (request.Series==null || x.SeriesId==request.Series) &&
            (request.Customer==null || x.CustomerId==request.Customer) &&
            (request.From==null || x.Date>=request.From) && (request.To==null || x.Date<=request.To) &&
            (number.Length<3 || x.Number!=null && x.Number.ToUpper().Contains(number)) &&
            (customer.Length<3 || x.CustomerName.ToUpper().Contains(customer) ||
                Customers.Any(c=>c.Id==x.CustomerId && c.Code.ToUpper().Contains(customer))) &&
            (search=="" || x.Number!=null && x.Number.ToUpper().Contains(search) || x.CustomerName.ToUpper().Contains(search) ||
             x.PurchaseOrder.ToUpper().Contains(search) || x.Reference.ToUpper().Contains(search)))
            .OrderByDescending(x=>x.Date).ThenByDescending(x=>x.UpdatedUtc).Take(500)
            .Select(x=>new {x.DraftJson,FrozenJson=x.LedgerId==null?null:db.Set<InvoiceSnapshot>()
                .Where(s=>s.CompanyId==x.CompanyId && s.LedgerId==x.LedgerId).Select(s=>s.SnapshotJson).FirstOrDefault(),
                Row=new SalesRow(x.Id,x.Version,x.Date,x.Number,x.CustomerName,
                Years.Where(y=>y.Id==x.FinancialYearId).Select(y=>y.Code).FirstOrDefault()??"",
                Series.Where(s=>s.Id==x.SeriesId).Select(s=>s.Code).FirstOrDefault()??"",x.Taxable,x.Tax,x.Total,x.LedgerId!=null,
                null)});
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

    private async Task<SalesResult> CalculateAsync(SalesDraft draft,CancellationToken ct,bool gstPending=false,
        ResolvedSettings? quantityPolicy=null,IReadOnlyList<SalesLineInput>? originalLines=null)
    {
        if(draft.RegistrationId is null || draft.CustomerId is null ||
            draft.Lines is not {Count:>0}) return Invalid("Choose a supplier registration, customer and at least one product.");
        var customer=await Customers.SingleAsync(x=>x.Id==draft.CustomerId,ct);
        var customerResult=await CustomerStore.Snapshot(db,customer,new(company.Id,company.Code,company.LegalName),
            new(draft.BillingAddressId,draft.ShippingAddressId,IncludeContactDetails:true),ct);
        if(customerResult.Snapshot is not {} buyer) return FieldError(nameof(draft.CustomerId),customerResult.Detail?.Eligibility?.MissingOrBlockingDetails.FirstOrDefault()??"Review customer eligibility and billing/shipping addresses.");
        if(buyer.PurchaseOrderRequired && string.IsNullOrWhiteSpace(draft.PurchaseOrder)) return FieldError(nameof(draft.PurchaseOrder),"Enter the purchase order required by this customer.");
        if(buyer.Currency!="INR" || buyer.BillTo.Country!="IN" || buyer.ShipTo.Country!="IN")
            return Invalid("Stage 5A supports domestic addresses and INR invoices only.");
        var gst=await Registrations.SingleAsync(x=>x.Id==draft.RegistrationId,ct);
        if(!gstPending && draft.EntryDetails?.Intent is { } intent &&
            (intent==SalesIntent.Interstate)!=(draft.Jurisdiction==GstJurisdiction.Interstate))
            return FieldError(nameof(draft.Jurisdiction),"The reviewed GST jurisdiction differs from the selected sale type. Review GST details.");
        if(!gstPending && (!CompanyRules.States.ContainsKey(draft.PlaceOfSupply) || !draft.DomesticConfirmed ||
            (draft.Jurisdiction is GstJurisdiction.IntraState or GstJurisdiction.IntraUnionTerritory) && draft.PlaceOfSupply!=gst.StateCode ||
            draft.Jurisdiction==GstJurisdiction.Interstate && draft.PlaceOfSupply==gst.StateCode))
            return FieldError(!draft.DomesticConfirmed?nameof(draft.DomesticConfirmed):nameof(draft.Jurisdiction),"Review the domestic supply, place of supply and GST jurisdiction for this registration.");
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
        var seller=new SalesSeller(company.LegalName,company.DisplayName,contacts.FirstOrDefault(x=>x.Kind=="Phone")?.Value,
            contacts.FirstOrDefault(x=>x.Kind=="Email")?.Value,gst.Gstin,gst.StateCode,
            string.Join(", ",new[]{selectedAddress.Lines,selectedAddress.Line2,selectedAddress.City,selectedAddress.StateCode,selectedAddress.PostalCode}.Where(x=>!string.IsNullOrWhiteSpace(x))),
            presentation with {Tax=new(gst.Id,gst.Gstin,gst.StateCode)},company.Iec);
        var lines=new List<FinalSalesLine>();
        var resolvedProducts=new Dictionary<(Guid Product,Guid? Unit),CatalogResult>();
        var estimates=new List<SalesLineEstimate>();
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
                    Products,null,false,false,ct);
                resolvedProducts.Add(key,item);
            }
            var index=lines.Count+estimates.Count+1;
            if(item.Snapshot is not {Tax:{ } tax} product || product.Factor<=0 || product.Currency!="INR")
                return item.Errors?.TryGetValue("TaxAssignment",out var taxError)==true
                    ?FieldError("TaxAssignment",taxError,index)
                    :FieldError(nameof(line.ProductId),item.Message??"Resolve the product tax profile and INR sales configuration.",index);
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
            if(gstPending)
            {
                estimates.Add(SalesCalculation.Estimate(line,tax,
                    draft.Jurisdiction==GstJurisdiction.IntraUnionTerritory?null:draft.EntryDetails?.Intent));
                continue;
            }
            var determination=GstDetermination.Determine(tax,new(gst.Id,buyer.CustomerId,draft.PlaceOfSupply,draft.Date,
                draft.Jurisdiction,0,draft.DomesticConfirmed));
            if(determination.Snapshot is not {} applied || SalesCalculation.Calculate(line,applied) is not {} amounts)
                return FieldError(nameof(draft.Jurisdiction),"The selected GST jurisdiction and product tax profile could not be applied.");
            lines.Add(new(index,line,product,applied with {TaxableValue=amounts.Taxable},line.Quantity*product.Factor,amounts));
        }
        if(gstPending) return new(CompanyOutcome.Success,company.Id,SalesFormConfiguration.GstReviewPendingMessage,
            Draft:draft,PendingGstLines:estimates);
        var effectiveDraft=draft with {Terms=string.IsNullOrWhiteSpace(draft.Terms)?buyer.PaymentTerms??"":draft.Terms};
        var snapshot=new SalesInvoiceSnapshot(effectiveDraft,buyer with {CapturedUtc=DateTime.UnixEpoch},seller,lines,SalesCalculation.Sum(lines.Select(x=>x.Amounts)));
        return new(CompanyOutcome.Success,company.Id,Snapshot:snapshot,
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
            if(required.All(error=>SalesFormConfiguration.IsGstInput(error.Field)) &&
                (await CheckReferences(draft,preview:true,ct)).Count==0)
                calculated=await CalculateAsync(draft,ct,gstPending:required.Count>0);
        }
        var snapshot=await db.Set<InvoiceSnapshot>().Where(x=>x.CompanyId==company.Id && x.LedgerId==row.LedgerId)
            .Select(x=>x.SnapshotJson).SingleOrDefaultAsync(ct);
        var ledger=await db.Set<DocumentNumberLedger>().SingleOrDefaultAsync(x=>x.CompanyId==company.Id && x.Id==row.LedgerId,ct);
        var events=await db.Set<AuditEvent>().Where(x=>x.CompanyId==company.Id && x.RecordType=="SalesInvoice" &&
            x.RecordId==row.Id.ToString()).OrderByDescending(x=>x.OccurredUtc).Take(50).ToArrayAsync(ct);
        return new(CompanyOutcome.Success,company.Id,Row:Describe(row,"",""),Draft:draft,
            Snapshot:row.LedgerId is null?calculated?.Snapshot:
                snapshot is null?null:JsonSerializer.Deserialize<SalesInvoiceSnapshot>(snapshot),
            Allocation:ledger is null?null:new(row.Id,ledger.Id,ledger.SeriesId,ledger.Sequence,ledger.CompleteNumber),
            Audit:events.Select(x=>$"{x.OccurredUtc:u} · {x.Action}").ToArray(),
            PendingGstLines:calculated?.PendingGstLines);
    }

    private static SalesDraft ReadDraft(SalesInvoice row)=>JsonSerializer.Deserialize<SalesDraft>(row.DraftJson)!;
    private static SalesRow Describe(SalesInvoice row,string year,string series)=>new(row.Id,row.Version,row.Date,row.Number,row.CustomerName,
        year,series,row.Taxable,row.Tax,row.Total,row.LedgerId.HasValue);
    private static void SetTotals(SalesInvoice row,SalesAmounts? totals)
    {row.Taxable=totals?.Taxable??0;row.Tax=totals?.Tax??0;row.Total=totals?.Total??0;}
    private void Audit(Guid invoice,string action)=>db.Add(new AuditEvent {CompanyId=company.Id,ActorUserId=actor,ActorKind="User",
        RecordId=invoice.ToString(),RecordType="SalesInvoice",Action="SalesInvoice"+action,ChangesJson="{}"});
}
