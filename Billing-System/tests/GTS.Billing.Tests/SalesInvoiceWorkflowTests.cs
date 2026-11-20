using System.IO;
using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class SalesInvoiceWorkflowTests
{
    [Fact]
    public async Task PreviewIsReadOnlyAndSaveCompletesMasterDataAtomicallyAndRejectsReplay()
    {
        using var diagnostics=new DiagnosticsFixture();
        var directory=Path.Combine(PersistenceTests.FindBillingRoot(),".local","invoice-workflow-tests",Guid.NewGuid().ToString("N"));
        var path=Path.Combine(directory,"synthetic.db");
        var database=new DevelopmentDatabase(new ConfiguredDatabaseLocation(path));
        try
        {
            await ProvisionDisposableDatabaseAsync(path);
            var auth=new AuthenticationService(new AuthenticationStore(database),new Argon2PasswordHasher(),TimeProvider.System);
            await auth.SetupAsync("Synthetic.Invoice",AuthenticationTests.Original,AuthenticationTests.Original);
            await auth.LoginAsync("Synthetic.Invoice",AuthenticationTests.Original);
            var lifetime=new ApplicationLifetime();
            var companyService=new CompanyService(auth,new CompanyStore(database));
            var companyResult=await companyService.ExecuteAsync(new(CompanyAction.Create,Draft:new CompanyDraft
            {
                Code="INVOICE_TEST",LegalName="Synthetic Invoice Company",AddressLine1="Synthetic Industrial Estate",
                City="Mumbai",StateCode="27",Pin="400001",Gstin="27DEMOFOOT00000"
            }));
            Assert.Equal(CompanyOutcome.Success,companyResult.Outcome);
            var company=companyResult.Company!;
            var id=company.Id;
            Assert.Equal(CompanyOutcome.Success,(await companyService.ExecuteAsync(new(CompanyAction.Select,id,ExpectedVersion:company.Version))).Outcome);

            var products=new ProductCatalogService(auth,lifetime,new ProductCatalogStore(database),new NoImages());
            Assert.Equal(CompanyOutcome.Success,(await products.ExecuteAsync(new(CatalogAction.Save,false,id,
                CategoryDraft:new() {Code="SYNTHETIC",Name="Synthetic goods"}))).Outcome);
            var categories=await products.ExecuteAsync(new(CatalogAction.List,false,id));
            var category=Assert.Single(categories.Rows!);
            Assert.Equal(CompanyOutcome.Success,(await products.ExecuteAsync(new(CatalogAction.Save,true,id,ProductDraft:new()
            {
                Code="SYN-LINE",Name="Synthetic shoe",Kind="Goods",CategoryId=category.Id,BaseUnit="PAIR",SalesRate=100
            }))).Outcome);
            var customers=new CustomerService(auth,new CustomerStore(database),new UnconfiguredCustomerFinancialSource(),lifetime);
            var buyer=await customers.CreateCustomerAsync(id,new CustomerDraft
            {
                LegalName="Synthetic Buyer",Addresses=[new CustomerAddressDraft {Label="Synthetic store",AddressType="Both",
                    RecipientName="Synthetic Buyer",Line1="Synthetic Market",City="Mumbai",StateCode="27",PostalCode="400001",
                    IsDefaultBilling=true,IsDefaultShipping=true}]
            });
            Assert.Equal(CustomerOutcome.Success,buyer.Outcome);

            var taxes=new TaxCatalogService(auth,lifetime,new TaxCatalogStore(database));
            var day=new DateOnly(2026,11,19);
            async Task<TaxResult> Tax(TaxMaster master,TaxAction action,TaxEntry? entry=null)=>
                await taxes.ExecuteAsync(new(master,action,id,Entry:entry));
            Assert.Equal(CompanyOutcome.Success,(await Tax(TaxMaster.Classifications,TaxAction.Save,
                new(Code:"6404",Type:"HSN",Description:"Synthetic classification"))).Outcome);
            var classification=Assert.Single((await Tax(TaxMaster.Classifications,TaxAction.List)).Rows!);
            Assert.Equal(CompanyOutcome.Success,(await Tax(TaxMaster.Rates,TaxAction.Save,
                new(Code:"SYN-RATE",Name:"Synthetic rate",Total:5,Cgst:2.5m,Sgst:2.5m,Igst:5,From:new DateOnly(2026,4,1)))).Outcome);
            var rate=Assert.Single((await Tax(TaxMaster.Rates,TaxAction.List)).Rows!);
            Assert.Equal(CompanyOutcome.Success,(await Tax(TaxMaster.Assignments,TaxAction.Save,
                new(ClassificationId:classification.Id,RateId:rate.Id,Context:"SYNTHETIC",From:new DateOnly(2026,4,1)))).Outcome);

            var sales=new SalesInvoiceService(auth,lifetime,new SalesInvoiceStore(database,NullLogger<SalesInvoiceStore>.Instance));
            var options=await sales.ExecuteAsync(new(SalesAction.Options,id));
            Assert.Equal(CompanyOutcome.Success,options.Outcome);
            var registration=Assert.Single(options.Options!.Registrations);
            var numbering=new NumberingService(auth,lifetime,new NumberingStore(database));
            Assert.Equal(CompanyOutcome.Success,(await numbering.ExecuteAsync(new(NumberingMaster.Years,NumberingAction.Save,id,
                Entry:new NumberingEntry(Code:"SYN-26",Name:"Synthetic year",Start:new(2026,4,1),End:new(2027,3,31))))).Outcome);
            var year=Assert.Single((await numbering.ExecuteAsync(new(NumberingMaster.Years,NumberingAction.List,id))).Rows!);
            Assert.Equal(CompanyOutcome.Success,(await numbering.ExecuteAsync(new(NumberingMaster.Series,NumberingAction.Save,id,
                Entry:new NumberingEntry(Code:"SYN-INV",Name:"Synthetic invoices",YearId:year.Id,RegistrationId:registration.Id,
                    DocumentType:"TaxInvoice",Prefix:"SYN",Separator:"-",Padding:4)))).Outcome);
            var series=Assert.Single((await numbering.ExecuteAsync(new(NumberingMaster.Series,NumberingAction.List,id))).Rows!);
            var product=Assert.Single(options.Options.Products);
            var mapping=Assert.Single(options.Options.TaxChoices!);
            var draft=new SalesDraft(day,year.Id,series.Id,registration.Id,Assert.Single(options.Options.Customers).Id,
                PlaceOfSupply:"27",Jurisdiction:GstJurisdiction.IntraState,DomesticConfirmed:true,
                Lines:[new(product.Id,null,2,100,false,10),new(product.Id,null,1,100,false,DiscountAmount:10)]);
            SalesProductCompletion[] completions=[new(1,150,mapping.AssignmentId)];
            var key=Guid.NewGuid();
            async Task<SalesResult> Send(SalesAction action,SalesDraft input,Guid? createId=null)=>
                await sales.ExecuteAsync(new(action,id,Draft:input,ProductCompletions:completions,CreateDraftId:createId));

            var preview=await Send(SalesAction.Preview,draft);
            Assert.True(preview.Outcome==CompanyOutcome.Success,preview.Message);
            Assert.Equal(283.50m,preview.Snapshot!.Amounts.Total);
            var failure=await Send(SalesAction.Save,draft with {Lines=[new(product.Id,null,0,100,false)]},key);
            Assert.Equal(CompanyOutcome.InvalidInput,failure.Outcome);
            await using(var db=database.OpenExisting())
            {
                Assert.Null((await db.Set<Product>().SingleAsync()).Mrp);
                Assert.Empty(await db.Set<ProductTaxProfile>().ToArrayAsync());
                Assert.Empty(await db.Set<SalesInvoice>().ToArrayAsync());
            }

            var saved=await Send(SalesAction.Save,draft,key);
            Assert.True(saved.Outcome==CompanyOutcome.Success,saved.Message);
            Assert.Equal(key,saved.Row!.Id);
            Assert.Equal(preview.Snapshot.Amounts,saved.Snapshot!.Amounts);
            Assert.Equal(CompanyOutcome.Conflict,(await Send(SalesAction.Save,draft,key)).Outcome);
            var refreshed=await sales.ExecuteAsync(new(SalesAction.Options,id));
            var configured=Assert.Single(refreshed.Options!.Products);
            Assert.Equal(150m,configured.Mrp);
            Assert.Equal(mapping.ClassificationId,configured.TaxClassificationId);
            var loaded=await sales.ExecuteAsync(new(SalesAction.View,id,key));
            Assert.Equal(draft.Lines,loaded.Draft!.Lines);
            var edits=draft with {Lines=[new(product.Id,null,3,120,false,10)]};
            var editedPreview=await sales.ExecuteAsync(new(SalesAction.Preview,id,key,loaded.Row!.Version,edits));
            Assert.True(editedPreview.Outcome==CompanyOutcome.Success,editedPreview.Message);
            Assert.Equal(340.20m,editedPreview.Snapshot!.Amounts.Total);
            var afterPreview=await sales.ExecuteAsync(new(SalesAction.View,id,key));
            Assert.Equal(draft.Lines,afterPreview.Draft!.Lines);
            var edited=await sales.ExecuteAsync(new(SalesAction.Save,id,key,loaded.Row.Version,edits));
            Assert.True(edited.Outcome==CompanyOutcome.Success,edited.Message);
            Assert.Equal(editedPreview.Snapshot.Amounts,edited.Snapshot!.Amounts);
            Assert.Equal(CompanyOutcome.Conflict,(await sales.ExecuteAsync(new(SalesAction.Save,id,key,loaded.Row.Version,edits))).Outcome);
            var latest=await sales.ExecuteAsync(new(SalesAction.View,id,key));
            Assert.Equal(latest.Row!.Version,edited.Row!.Version);
            var incomplete=await sales.ExecuteAsync(new(SalesAction.Save,id,key,latest.Row.Version,
                edits with {PlaceOfSupply="",Jurisdiction=GstJurisdiction.Unresolved,DomesticConfirmed=false}));
            Assert.Equal(CompanyOutcome.Success,incomplete.Outcome);
            var blocked=await sales.ExecuteAsync(new(SalesAction.PrepareFinalization,id,key,incomplete.Row!.Version));
            Assert.Equal(CompanyOutcome.InvalidInput,blocked.Outcome);
            Assert.Contains(blocked.FieldErrors!,error=>error.Field==nameof(SalesDraft.PlaceOfSupply));
            Assert.False((await sales.ExecuteAsync(new(SalesAction.View,id,key))).Row!.Finalized);
            var layoutFields=SalesFormConfiguration.Fields.Where(field=>field.Key is "TaxDetails" or "ProductCode" or "Sku" or "UnitId");
            var layoutSettings=await auth.RunAuthenticatedAsync(actor=>new SettingsStore(database,TimeProvider.System)
                .ExecuteAsync(new(SettingsAction.Save,SettingScope.Company,id,SalesFormConfiguration.Section,
                    layoutFields.Select(field=>new SettingEdit(field.Setting.Key,
                        JsonSerializer.Serialize(field.Baseline with {Visible=false}),null)).ToArray()),actor,CancellationToken.None));
            Assert.Equal(CompanyOutcome.Success,layoutSettings.Outcome);
            latest=await sales.ExecuteAsync(new(SalesAction.Save,id,key,incomplete.Row.Version,edits));
            var review=await sales.ExecuteAsync(new(SalesAction.PrepareFinalization,id,key,latest.Row!.Version));
            var finalize=new SalesRequest(SalesAction.Finalize,id,key,latest.Row.Version,Confirmation:review.Confirmation);
            var finalized=await sales.ExecuteAsync(finalize);
            Assert.True(finalized.Outcome==CompanyOutcome.Success,finalized.Message);
            Assert.Equal(review.Snapshot!.Amounts,finalized.Snapshot!.Amounts);
            Assert.Equal(finalized.Row!.Number,(await sales.ExecuteAsync(finalize)).Row!.Number);
            await using(var db=database.OpenExisting())
            {
                Assert.Single(await db.Set<DocumentNumberLedger>().ToArrayAsync());
                var currentProduct=await db.Set<Product>().SingleAsync();
                currentProduct.Mrp=200;
                await db.SaveChangesAsync();
            }
            var historical=await sales.ExecuteAsync(new(SalesAction.PrintPreview,id,key));
            Assert.Equal(150m,Assert.Single(historical.PrintModel!.Lines).Product.Mrp);
            Assert.Equal(finalized.Snapshot.Amounts,historical.PrintModel.Totals);
        }
        finally
        {
            if(Directory.Exists(directory)) Directory.Delete(directory,true);
        }
    }

    internal static async Task ProvisionDisposableDatabaseAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var installation=Guid.NewGuid();
        await using var db=DevelopmentDatabase.CreateContext(path,true);
        var migrator=db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261119000900_TaxCatalog");
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;");
        try
        {
            await migrator.MigrateAsync("20261119001000_FinancialYearNumbering");
        }
        finally {await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");}
        await migrator.MigrateAsync();
        db.Add(new Installation {Id=installation});
        db.Add(new DatabaseSecurityMetadata {InstallationId=installation});
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(path+".installation.json",JsonSerializer.Serialize(new
        {
            InstallationId=installation,DatabasePath=path,State="Ready"
        }));
    }

    private sealed class NoImages : ICompanyImageProcessor
    {
        public ManagedLogoImage? Prepare(byte[] raw,CancellationToken cancellationToken)=>null;
    }
}
