using System.IO;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class CompanySafetyTests : IAsyncLifetime
{
    private readonly DiagnosticsFixture diagnostics=new();
    private readonly string dir=Path.Combine(PersistenceTests.FindBillingRoot(),".local","stage4a-safety",Guid.NewGuid().ToString("N"));
    private DevelopmentDatabase Database=>new(new ConfiguredDatabaseLocation(Path.Combine(dir,"synthetic.db")));
    private AuthenticationService Auth()=>new(new AuthenticationStore(Database),new Argon2PasswordHasher(),TimeProvider.System);
    private CompanyService Service(AuthenticationService auth)=>new(auth,new CompanyStore(Database));
    public Task InitializeAsync()=>Database.InitializeAsync();
    public Task DisposeAsync() {diagnostics.Dispose();Directory.Delete(dir,true);return Task.CompletedTask;}
    private async Task<AuthenticationService> Login()
    {
        var auth=Auth();await auth.SetupAsync("Synthetic.Admin",AuthenticationTests.Original,AuthenticationTests.Original);
        await auth.LoginAsync("Synthetic.Admin",AuthenticationTests.Original);return auth;
    }
    [Fact]
    public async Task AuditFailureRollsBackAllCompanyAndChildWrites()
    {
        var service=Service(await Login());
        await using(var db=Database.OpenExisting())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER Test_CompanyAuditFailure BEFORE INSERT ON AuditEvents WHEN NEW.Action='CompanyCreated' BEGIN SELECT RAISE(ABORT,'synthetic fault'); END;");
        var result=await service.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid()));
        Assert.Equal(CompanyOutcome.Conflict,result.Outcome);
        await using var after=Database.OpenExisting();
        Assert.Empty(await after.Set<Company>().ToListAsync());Assert.Empty(await after.Set<CompanyAddress>().ToListAsync());
        Assert.Empty(await after.Set<CompanySettings>().ToListAsync());
        Assert.Empty(await after.Set<AuditEvent>().Where(x=>x.CompanyId!=null).ToListAsync());
    }
    [Fact]
    public async Task ConcurrentSelectionsNeverCreateTwoCurrentCompanies()
    {
        var firstAuth=await Login();var first=Service(firstAuth);
        var one=(await first.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid("ONE")))).Company!;
        var two=(await first.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid("TWO")))).Company!;
        var secondAuth=Auth();await secondAuth.LoginAsync("Synthetic.Admin",AuthenticationTests.Original);
        var second=Service(secondAuth);
        var results=await Task.WhenAll(Task.Run(()=>first.ExecuteAsync(new(CompanyAction.Select,one.Id,ExpectedVersion:one.Version))),
            Task.Run(()=>second.ExecuteAsync(new(CompanyAction.Select,two.Id,ExpectedVersion:two.Version))));
        Assert.All(results,x=>Assert.Equal(CompanyOutcome.Success,x.Outcome));
        await using var db=Database.OpenExisting();Assert.Single(await db.Set<CompanySelection>().ToListAsync());
        Assert.Single((await first.ExecuteAsync(new(CompanyAction.List))).Companies!,x=>x.IsCurrent);
        var current=await db.Set<CompanySelection>().SingleAsync();
        var row=await db.Set<Company>().SingleAsync(x=>x.Id==current.CompanyId);row.IsActive=false;
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task LogoBytesAreBoundedPersistedVersionedAndNeverExternalPaths()
    {
        var service=Service(await Login());
        var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lX8AAAAASUVORK5CYII=");
        Assert.True(CompanyLogo.IsBoundedPng(png));
        var created=await service.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid(),Logo:png));
        Assert.Equal(CompanyOutcome.Success,created.Outcome);
        var row=created.Company!;
        Assert.Equal(png,row.Logo);
        Assert.Equal(CompanyOutcome.InvalidInput,(await service.ExecuteAsync(new(CompanyAction.Update,row.Id,row.Draft,row.Version,Logo:new byte[CompanyLogo.MaximumBytes+1]))).Outcome);
        var replaced=await service.ExecuteAsync(new(CompanyAction.Update,row.Id,row.Draft,row.Version,Logo:png));
        Assert.Equal(CompanyOutcome.Success,replaced.Outcome);
        var removed=await service.ExecuteAsync(new(CompanyAction.Update,row.Id,row.Draft,replaced.Company!.Version,RemoveLogo:true));
        Assert.Null(removed.Company!.Logo);
        await using var db=Database.OpenExisting();
        var assets=await db.Set<BrandingAsset>().ToListAsync();Assert.Equal(2,assets.Count);
        Assert.All(assets,x=>{Assert.Equal(png,x.Content);Assert.False(Path.IsPathRooted(x.StorageId));Assert.Equal("image/png",x.ContentType);});
        assets[0].Content=png.Concat(new byte[]{1}).ToArray();
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task CompanyOperationDrainsBeforeShutdownCompletes()
    {
        var auth=await Login();var blocked=new GatedStore(new CompanyStore(Database));
        var lifetime=new ApplicationLifetime();
        var vm=new CompanyViewModel(new CompanyService(auth,blocked),lifetime);
        vm.AddCommand.Execute(null);var draft=CompanyValidationTests.Valid();
        foreach(var field in vm.Fields) field.Value=(string?)typeof(CompanyDraft).GetProperty(field.Name)!.GetValue(draft) ?? "";
        var saving=vm.SaveCommand.ExecuteAsync(null);
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var shutdown=lifetime.ShutdownAsync();
        Assert.False(shutdown.IsCompleted);Assert.True(lifetime.IsWaitingForOperations);
        Assert.False(vm.AddCommand.CanExecute(null));
        blocked.Release.SetResult();
        await Task.WhenAll(saving,shutdown);
        Assert.True(lifetime.IsStopped);
        await using var db=Database.OpenExisting();Assert.Empty(await db.Set<Company>().ToListAsync());
    }
    [Fact]
    public async Task AdditionalBankContactsAddressesAndSecondLogoArePreserved()
    {
        var service=Service(await Login());
        var created=(await service.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid() with {
            BankName="Synthetic default bank",AccountHolder="Synthetic",AccountNumber="1234567890",Ifsc="TEST0000001"}))).Company!;
        var extraBank=new CompanyBankAccount {CompanyId=created.Id,BankName="Synthetic second bank",AccountHolder="Synthetic",
            AccountNumber="9876543210",Ifsc="TEST0000002",IsActive=true,IsDefault=false};
        var extraContact=new CompanyContact {CompanyId=created.Id,Kind="Other",Value="Synthetic legacy contact"};
        var extraAddress=new CompanyAddress {CompanyId=created.Id,Purpose="Factory",Lines="Synthetic other address",StateCode="27"};
        var extraLogo=new BrandingAsset {CompanyId=created.Id,Kind="Logo2",StorageId="synthetic-metadata-only",Sha256=new string('0',64)};
        await using(var seed=Database.OpenExisting())
        {
            seed.AddRange(extraBank,extraContact,extraAddress,extraLogo);
            var settings=await seed.Set<CompanySettings>().SingleAsync();settings.Logo2AssetId=extraLogo.Id;
            await seed.SaveChangesAsync();
        }
        var result=await service.ExecuteAsync(new(CompanyAction.Update,created.Id,created.Draft with {LegalName="Synthetic updated"},created.Version));
        Assert.Equal(CompanyOutcome.Success,result.Outcome);
        var rejected=await service.ExecuteAsync(new(CompanyAction.Update,created.Id,result.Company!.Draft with {
            BankName=null,AccountHolder=null,AccountNumber=null,Ifsc=null},result.Company.Version));
        Assert.Equal(CompanyOutcome.InvalidInput,rejected.Outcome);
        Assert.Contains("BankName",rejected.Errors!.Keys);
        await using var after=Database.OpenExisting();
        Assert.True((await after.Set<CompanyBankAccount>().SingleAsync(x=>x.Id==extraBank.Id)).IsActive);
        Assert.Single(await after.Set<CompanyBankAccount>().Where(x=>x.IsDefault).ToListAsync());
        Assert.NotNull(await after.Set<CompanyContact>().FindAsync(extraContact.Id));
        Assert.NotNull(await after.Set<CompanyAddress>().FindAsync(extraAddress.Id));
        Assert.Equal(extraLogo.Id,(await after.Set<CompanySettings>().SingleAsync()).Logo2AssetId);
    }

    private sealed class GatedStore(ICompanyStore inner) : ICompanyStore
    {
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CompanyResult> ExecuteAsync(CompanyRequest request,AuthenticatedIdentity actor,CancellationToken ct)
        {Entered.SetResult();await Release.Task;return await inner.ExecuteAsync(request,actor,ct);}
    }
}

