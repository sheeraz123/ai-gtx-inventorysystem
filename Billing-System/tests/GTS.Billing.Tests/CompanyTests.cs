using System.IO;
using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class CompanyTests : IAsyncLifetime
{
    private readonly DiagnosticsFixture diagnostics=new();
    private readonly string directory=Path.Combine(PersistenceTests.FindBillingRoot(),".local","stage4a-tests",Guid.NewGuid().ToString("N"));
    private string DbPath=>Path.Combine(directory,"synthetic.db");
    private DevelopmentDatabase Database=>new(new ConfiguredDatabaseLocation(DbPath));
    private AuthenticationService Auth()=>new(new AuthenticationStore(Database),new Argon2PasswordHasher(),TimeProvider.System);
    private CompanyService Companies(AuthenticationService auth)=>new(auth,new CompanyStore(Database));
    public Task InitializeAsync()=>Database.InitializeAsync();
    public Task DisposeAsync() {diagnostics.Dispose();Directory.Delete(directory,true);return Task.CompletedTask;}
    private async Task<AuthenticationService> SignedIn()
    {
        var auth=Auth();
        await auth.SetupAsync("Synthetic.Admin",AuthenticationTests.Original,AuthenticationTests.Original);
        Assert.Equal(AuthenticationOutcome.Success,(await auth.LoginAsync("Synthetic.Admin",AuthenticationTests.Original)).Outcome);
        return auth;
    }
    private static async Task<CompanyDetails> Create(CompanyService service,string code="SYNTHETIC",CompanyDraft? draft=null)
    {
        var result=await service.ExecuteAsync(new(CompanyAction.Create,Draft:draft ?? CompanyValidationTests.Valid(code)));
        Assert.Equal(CompanyOutcome.Success,result.Outcome);
        return Assert.Single(result.Companies!);
    }
    [Fact]
    public async Task CreateUpdateSearchReopenAndAuditingUseAuthenticatedIdentity()
    {
        var auth=await SignedIn();var service=Companies(auth);
        var first=await Create(service,draft:CompanyValidationTests.Valid() with {Tagline="Synthetic tagline",Email="test@example.invalid",
            BankName="Synthetic bank",AccountHolder="Synthetic",AccountNumber="1234567890",Ifsc="TEST0000001",Terms="Synthetic original terms"});
        Assert.Equal((await auth.GetCurrentAsync())!.UserId,first.CreatedBy);
        Assert.False(first.IsCurrent);
        var result=await service.ExecuteAsync(new(CompanyAction.Update,first.Id,first.Draft with {LegalName="Renamed synthetic",Terms="New synthetic terms"},first.Version));
        Assert.Equal(CompanyOutcome.Success,result.Outcome);
        Assert.True(result.Company!.Version>first.Version);
        Assert.Equal(CompanyOutcome.Success,(await service.ExecuteAsync(new(CompanyAction.Get,first.Id))).Outcome);
        Assert.Single((await service.ExecuteAsync(new(CompanyAction.List,Search:"Renamed"))).Companies!);
        Assert.Empty((await service.ExecuteAsync(new(CompanyAction.List,Search:"NOT_PRESENT"))).Companies!);
        await auth.LogoutAsync();
        var restart=Auth();await restart.LoginAsync("Synthetic.Admin",AuthenticationTests.Original);
        var reloaded=await Companies(restart).ExecuteAsync(new(CompanyAction.Get,first.Id));
        Assert.Equal("Renamed synthetic",reloaded.Company!.Draft.LegalName);
        Assert.Equal("Synthetic bank",reloaded.Company.Draft.BankName);
        await using var db=Database.OpenExisting();
        Assert.Equal(2,await db.Set<TermsVersion>().CountAsync());
        var audit=await db.Set<AuditEvent>().Where(x=>x.CompanyId==first.Id).ToListAsync();
        Assert.Equal(2,audit.Count);Assert.All(audit,x=>Assert.Equal(first.CreatedBy,x.ActorUserId));
        Assert.All(audit,x=>Assert.Equal("{}",x.ChangesJson));
        Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task DuplicateCodeImmutableCodeAndStaleVersionAreRejectedWithoutPartialWrites()
    {
        var service=Companies(await SignedIn());
        var first=await Create(service,"CASE");
        var duplicate=await service.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid("case")));
        Assert.Equal(CompanyOutcome.Conflict,duplicate.Outcome);
        Assert.Equal(CompanyOutcome.InvalidInput,(await service.ExecuteAsync(new(CompanyAction.Update,first.Id,first.Draft with {Code="CHANGED"},first.Version))).Outcome);
        Assert.Equal(CompanyOutcome.Conflict,(await service.ExecuteAsync(new(CompanyAction.Update,first.Id,first.Draft,first.Version+100))).Outcome);
        await using var db=Database.OpenExisting();
        Assert.Single(await db.Set<Company>().ToListAsync());
        Assert.Single(await db.Set<CompanyAddress>().ToListAsync());
        var row=await db.Set<Company>().SingleAsync();row.Code="DIRECT_CHANGE";
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task CurrentCompanyIsSingletonAndPersistsAcrossRestartAndDeactivationClearsIt()
    {
        var auth=await SignedIn();var service=Companies(auth);
        var first=await Create(service,"ONE");var second=await Create(service,"TWO");
        var chosen=await service.ExecuteAsync(new(CompanyAction.Select,first.Id,ExpectedVersion:first.Version));
        Assert.Equal(CompanyOutcome.Success,chosen.Outcome);
        chosen=await service.ExecuteAsync(new(CompanyAction.Select,second.Id,ExpectedVersion:second.Version));
        Assert.Equal(second.Id,chosen.Company!.Id);Assert.True(chosen.Company.IsCurrent);
        await auth.LogoutAsync();var next=Auth();await next.LoginAsync("Synthetic.Admin",AuthenticationTests.Original);
        service=Companies(next);
        Assert.Equal(second.Id,(await service.ExecuteAsync(new(CompanyAction.Current))).Company!.Id);
        var disabled=await service.ExecuteAsync(new(CompanyAction.SetActive,second.Id,ExpectedVersion:chosen.Company.Version,Active:false));
        Assert.Equal(CompanyOutcome.Success,disabled.Outcome);
        Assert.Null((await service.ExecuteAsync(new(CompanyAction.Current))).Company);
        Assert.Equal(CompanyOutcome.InvalidInput,(await service.ExecuteAsync(new(CompanyAction.Select,second.Id,ExpectedVersion:disabled.Company!.Version))).Outcome);
        var activated=await service.ExecuteAsync(new(CompanyAction.SetActive,second.Id,ExpectedVersion:disabled.Company.Version,Active:true));
        Assert.True(activated.Company!.Active);
        await using var db=Database.OpenExisting();
        Assert.Equal(2,await db.Set<Company>().CountAsync());
        db.Add(new CompanySelection {Slot=2,CompanyId=first.Id});
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task UnauthenticatedOrdinaryAndStaleSessionsCannotReadOrMutate()
    {
        var auth=Auth();var service=Companies(auth);
        Assert.Equal(CompanyOutcome.SessionRequired,(await service.ExecuteAsync(new(CompanyAction.List))).Outcome);
        auth=await SignedIn();service=Companies(auth);
        var setupCodes=await auth.RegenerateAsync(AuthenticationTests.Original);
        var other=Auth();
        const string changed="Synthetic company reset passphrase 42";
        await other.RecoverAsync(setupCodes.Codes![0],changed,changed);
        Assert.Equal(CompanyOutcome.SessionRequired,(await service.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid()))).Outcome);
        Assert.Null(await auth.GetCurrentAsync());
        Assert.Equal(AuthenticationOutcome.Success,(await auth.LoginAsync("Synthetic.Admin",changed)).Outcome);
        await auth.LogoutAsync();
        await using(var db=Database.OpenExisting())
        {
            db.Add(new User {Username="Synthetic.Employee",NormalizedUsername="SYNTHETIC.EMPLOYEE",PasswordHash=new Argon2PasswordHasher().Hash(AuthenticationTests.Original)});
            await db.SaveChangesAsync();
        }
        Assert.Equal(AuthenticationOutcome.Success,(await auth.LoginAsync("Synthetic.Employee",AuthenticationTests.Original)).Outcome);
        Assert.Equal(CompanyOutcome.Forbidden,(await service.ExecuteAsync(new(CompanyAction.List))).Outcome);
        Assert.Equal(CompanyOutcome.Forbidden,(await service.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid()))).Outcome);
    }
    [Fact]
    public async Task WriterTransactionRevalidatesIdentityIfRecoveryWinsAfterInitialSessionCheck()
    {
        var auth=await SignedIn();
        var identity=(await auth.GetCurrentAsync())!;
        var codes=await auth.RegenerateAsync(AuthenticationTests.Original);
        var other=Auth();
        const string changed="Synthetic race replacement passphrase";
        await other.RecoverAsync(codes.Codes![0],changed,changed);
        var result=await new CompanyStore(Database).ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid()),identity,default);
        Assert.Equal(CompanyOutcome.SessionRequired,result.Outcome);
        await using var db=Database.OpenExisting();Assert.Empty(await db.Set<Company>().ToListAsync());
    }
    [Fact]
    public async Task CancellationAndShutdownAdmissionDoNotCreateRecords()
    {
        var service=Companies(await SignedIn());
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.ExecuteAsync(new(CompanyAction.Create,Draft:CompanyValidationTests.Valid()),cancelled.Token));
        var lifetime=new ApplicationLifetime();var vm=new CompanyViewModel(service,lifetime);
        await lifetime.ShutdownAsync();
        Assert.False(vm.AddCommand.CanExecute(null));Assert.False(vm.RefreshCommand.CanExecute(null));
        await vm.RefreshAsync();
        await using var db=Database.OpenExisting();Assert.Empty(await db.Set<Company>().ToListAsync());
    }
    [Fact]
    public async Task NewMigrationUpgradesV1DisposableDatabaseWithoutChangingIdentityOrExistingRows()
    {
        var path=Path.Combine(directory,"upgrade.db");
        var id=Guid.NewGuid();var companyId=Guid.NewGuid();
        await using(var before=DevelopmentDatabase.CreateContext(path,true))
        {
            await before.GetService<IMigrator>().MigrateAsync("20261116000100_InitialDevelopmentSchema");
            await before.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Companies(Id,Version,Code,LegalName,IsActive,CreatedUtc) VALUES ({companyId},0,'OLD_SYNTHETIC','Existing synthetic',1,{DateTime.UtcNow})");
            before.Add(new Installation {Id=id});before.Add(new DatabaseSecurityMetadata {InstallationId=id});await before.SaveChangesAsync();
        }
        var marker=path+".installation.json";
        await File.WriteAllTextAsync(marker,JsonSerializer.Serialize(new {InstallationId=id,DatabasePath=path,State="Ready"}));
        var bytes=await File.ReadAllBytesAsync(marker);
        var old=new DevelopmentDatabase(new ConfiguredDatabaseLocation(path));
        Assert.Equal(DatabaseStartupProblem.UpgradeRequired,(await Assert.ThrowsAsync<DatabaseStartupException>(()=>old.VerifyExistingAsync())).Problem);
        await using(var upgrade=old.OpenExisting()) await upgrade.Database.MigrateAsync();
        await old.VerifyExistingAsync();
        Assert.Equal(bytes,await File.ReadAllBytesAsync(marker));
        await using var after=old.OpenExisting();
        Assert.Equal("Existing synthetic",(await after.Set<Company>().SingleAsync()).LegalName);
        Assert.Equal(id,(await after.Set<Installation>().SingleAsync()).Id);
        Assert.Equal("Unregistered",(await after.Set<Company>().SingleAsync()).RegistrationType);
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan(0,16).SequenceEqual("SQLite format 3\0"u8));
    }
    [Fact]
    public async Task GstChangesPreserveMappedAddressesAndVersionedTerms()
    {
        var service=Companies(await SignedIn());
        var first=await Create(service,draft:CompanyValidationTests.Valid() with {RegistrationType="Regular",Gstin=CompanyValidationTests.Gstin(),Pan="AAAAA0000A"});
        var result=await service.ExecuteAsync(new(CompanyAction.Update,first.Id,first.Draft with {
            StateCode="29",City="Bengaluru",Pin="560001",Gstin=CompanyValidationTests.Gstin("29AAAAA0000A1Z")},first.Version));
        Assert.Equal(CompanyOutcome.Success,result.Outcome);
        await using var db=Database.OpenExisting();
        Assert.Equal(2,await db.Set<CompanyAddress>().CountAsync());
        Assert.Equal(2,await db.Set<GstRegistration>().CountAsync());
        Assert.Equal(1,await db.Set<GstRegistration>().CountAsync(x=>x.IsActive));
        Assert.Equal(2,await db.Set<GstRegistrationAddress>().CountAsync());
        await Database.VerifyExistingAsync();
    }
    [Fact]
    public async Task CompanyViewModelSavesReloadsAndClearsSensitiveEditorState()
    {
        var service=Companies(await SignedIn());var lifetime=new ApplicationLifetime();var vm=new CompanyViewModel(service,lifetime);
        vm.AddCommand.Execute(null);Assert.True(vm.EditorVisible);
        var draft=CompanyValidationTests.Valid("VIEWMODEL");
        foreach(var field in vm.Fields) field.Value=(string?)typeof(CompanyDraft).GetProperty(field.Name)!.GetValue(draft) ?? "";
        Assert.True(vm.Dirty);await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.EditorVisible);Assert.Single(vm.Companies);
        vm.Selected=vm.Companies.Single();await vm.SelectCommand.ExecuteAsync(null);
        Assert.Contains("Synthetic Company",vm.CurrentCompany);
        await vm.ViewCommand.ExecuteAsync(null);Assert.True(vm.ViewOnly);Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Clear();Assert.False(vm.EditorVisible);Assert.Empty(vm.Companies);Assert.All(vm.Fields,f=>Assert.Empty(f.Value));
    }
}

