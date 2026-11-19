using System.IO;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class CompanyInputRevisionTests : IAsyncLifetime
{
    private readonly DiagnosticsFixture diagnostics=new();
    private readonly string directory=Path.Combine(PersistenceTests.FindBillingRoot(),".local","company-input-tests",Guid.NewGuid().ToString("N"));
    private string PathValue=>Path.Combine(directory,"synthetic.db");
    private DevelopmentDatabase Database=>new(new ConfiguredDatabaseLocation(PathValue));
    public Task InitializeAsync()=>Database.InitializeAsync();
    public Task DisposeAsync() {diagnostics.Dispose();Directory.Delete(directory,true);return Task.CompletedTask;}

    [Theory]
    [InlineData(null,null)]
    [InlineData("free gst","x")]
    [InlineData("29AAAAA0000A1Z0","")]
    public async Task GstAndIfscSaveReloadAndUpdateWithoutFormatOrCrossValidation(string? gstin,string? ifsc)
    {
        var auth=new AuthenticationService(new AuthenticationStore(Database),new Argon2PasswordHasher(),TimeProvider.System);
        await auth.SetupAsync("Synthetic.Admin",AuthenticationTests.Original,AuthenticationTests.Original);
        await auth.LoginAsync("Synthetic.Admin",AuthenticationTests.Original);
        var service=new CompanyService(auth,new CompanyStore(Database));
        var draft=CompanyValidationTests.Valid() with {RegistrationType="Regular",Gstin=gstin,Pan="BBBBB0000B",
            BankName="Synthetic bank",AccountHolder="Synthetic",AccountNumber="1234567890",Ifsc=ifsc};
        var created=await service.ExecuteAsync(new(CompanyAction.Create,Draft:draft));
        Assert.Equal(CompanyOutcome.Success,created.Outcome);
        var row=created.Company!;
        var updated=await service.ExecuteAsync(new(CompanyAction.Update,row.Id,row.Draft with {LegalName="Synthetic updated",StateCode="29"},row.Version));
        Assert.Equal(CompanyOutcome.Success,updated.Outcome);
        var reloaded=await service.ExecuteAsync(new(CompanyAction.Get,row.Id));
        Assert.Equal(gstin?.ToUpperInvariant(),reloaded.Company!.Draft.Gstin);
        Assert.Equal(string.IsNullOrWhiteSpace(ifsc)?null:ifsc.ToUpperInvariant(),reloaded.Company.Draft.Ifsc);
        var vm=new CompanyViewModel(service,new ApplicationLifetime());
        await vm.RefreshAsync();vm.Selected=Assert.Single(vm.Companies);await vm.EditCommand.ExecuteAsync(null);
        var name=vm.Fields.Single(x=>x.Name=="LegalName");var originalName=name.Value;
        name.Value="Synthetic temporary";Assert.True(vm.Dirty);
        name.Value=originalName;Assert.False(vm.Dirty);
        vm.CancelEditor();
        await using var db=Database.OpenExisting();
        Assert.Single(await db.Set<CompanyBankAccount>().ToListAsync());
        await Database.VerifyExistingAsync();
    }

    [Fact]
    public void DateFieldUsesOptionalTypedDateAndStateSearchMatchesNameOrCode()
    {
        var field=new CompanyField("EstablishedOn","Established on");
        Assert.True(field.IsDate);Assert.Null(field.DateValue);
        field.DateValue=new DateTime(2001,12,31);Assert.Equal("2001-12-31",field.Value);
        field.Value="2010-04-01";Assert.Equal(new DateTime(2010,4,1),field.DateValue);
        field.DateValue=null;Assert.Equal("",field.Value);
        var code=new CompanyField("Code","Code") {Value="3000-01-01"};
        Assert.Null(code.DateValue);code.DateValue=null;Assert.Equal("3000-01-01",code.Value);
        var state=new CompanyField("StateCode","State",CompanyRules.States.Select(x=>new CompanyChoice(x.Key,x.Value)).ToArray());
        Assert.True(state.IsSearchable);
        state.FilterChoices("MAHA");Assert.Equal("27",Assert.Single(state.FilteredChoices).Key);
        state.FilterChoices("09");Assert.Equal("09",Assert.Single(state.FilteredChoices).Key);
        state.FilterChoices("not a state");Assert.Empty(state.FilteredChoices);
        state.FilterChoices("");Assert.Equal(CompanyRules.States.Count,state.FilteredChoices.Count);
    }

    [Fact]
    public async Task ConstraintMigrationRetainsRelatedDataAndUnrelatedSecurityTriggers()
    {
        var path=Path.Combine(directory,"old-stage4a.db");
        var id=Guid.NewGuid();
        var company=new Company {Code="UPGRADE_TEST",LegalName="Synthetic retained"};
        var bank=new CompanyBankAccount {CompanyId=company.Id,BankName="Synthetic bank",AccountHolder="Synthetic",AccountNumber="1234567890",Ifsc="TEST0000001",IsDefault=true};
        var gst=new GstRegistration {CompanyId=company.Id,Gstin="27AAAAA0000A1Z2",StateCode="27"};
        var address=new CompanyAddress {CompanyId=company.Id,Lines="Synthetic retained address",StateCode="27"};
        var mapping=new GstRegistrationAddress {CompanyId=company.Id,GstRegistrationId=gst.Id,AddressId=address.Id};
        var year=new FinancialYear {CompanyId=company.Id,Start=new DateOnly(2026,4,1),End=new DateOnly(2027,3,31)};
        var series=new DocumentNumberingSeries {CompanyId=company.Id,GstRegistrationId=gst.Id};
        var ledger=new DocumentNumberLedger {CompanyId=company.Id,GstRegistrationId=gst.Id,FinancialYearId=year.Id,SeriesId=series.Id,Sequence=1,CompleteNumber="TEST/1"};
        var snapshot=new InvoiceSnapshot {CompanyId=company.Id,LedgerId=ledger.Id,SnapshotJson="{\"synthetic\":true}"};
        var audit=new AuditEvent {CompanyId=company.Id,Action="SyntheticMigrationFixture",RecordType="Company"};
        await using(var old=DevelopmentDatabase.CreateContext(path,true))
        {
            await old.GetService<IMigrator>().MigrateAsync("20261118000100_CompanyMaster");
            old.AddRange(new Installation {Id=id},new DatabaseSecurityMetadata {InstallationId=id},company,bank,gst,address,mapping,year,series,ledger,snapshot,audit,
                new CompanySelection {CompanyId=company.Id});
            await old.SaveChangesAsync();
            await old.Database.MigrateAsync();
            Assert.Equal(bank.AccountNumber,(await old.Set<CompanyBankAccount>().AsNoTracking().SingleAsync()).AccountNumber);
            Assert.Equal(gst.Gstin,(await old.Set<GstRegistration>().AsNoTracking().SingleAsync()).Gstin);
            Assert.Equal(gst.Id,(await old.Set<DocumentNumberLedger>().AsNoTracking().SingleAsync()).GstRegistrationId);
            Assert.Equal(snapshot.SnapshotJson,(await old.Set<InvoiceSnapshot>().AsNoTracking().SingleAsync()).SnapshotJson);
            Assert.Equal(id,(await old.Set<Installation>().AsNoTracking().SingleAsync()).Id);
            Assert.Equal(company.Id,(await old.Set<CompanySelection>().AsNoTracking().SingleAsync()).CompanyId);
            await old.Database.OpenConnectionAsync();
            await using var command=old.Database.GetDbConnection().CreateCommand();
            command.CommandText="PRAGMA foreign_keys";Assert.Equal(1L,await command.ExecuteScalarAsync());
            command.CommandText="PRAGMA foreign_key_check";await using(var rows=await command.ExecuteReaderAsync()) Assert.False(await rows.ReadAsync());
            command.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name IN ('GstAddress_State_Insert','GstAddress_State_Update','Address_State_Update','Gst_State_Update')";
            Assert.Equal(0L,await command.ExecuteScalarAsync());
            command.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name IN ('Audit_NoUpdate','Snapshot_NoUpdate','Ledger_NoDelete','Company_Code_Immutable','CurrentCompany_Active_Insert')";
            Assert.Equal(5L,await command.ExecuteScalarAsync());
        }
        await using(var direct=DevelopmentDatabase.CreateContext(path))
        {
            var retained=await direct.Set<CompanyBankAccount>().SingleAsync();retained.Ifsc="";
            (await direct.Set<GstRegistration>().SingleAsync()).Gstin="FREE";
            await direct.SaveChangesAsync();
        }
        await using(var reject=DevelopmentDatabase.CreateContext(path))
        {
            (await reject.Set<AuditEvent>().SingleAsync()).Action="Not permitted";
            await Assert.ThrowsAsync<DbUpdateException>(()=>reject.SaveChangesAsync());
        }
    }
}

