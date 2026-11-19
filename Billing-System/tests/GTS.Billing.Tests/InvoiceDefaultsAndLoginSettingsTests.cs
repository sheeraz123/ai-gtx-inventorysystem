using System.IO;
using GTS.Billing.Application;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class InvoiceDefaultsAndLoginSettingsTests
{
    [Fact]
    public void DefaultsRespectDateActivityRegistrationAndAmbiguity()
    {
        var start=new DateOnly(2026,4,1);
        var end=new DateOnly(2027,3,31);
        var year=new SalesChoice(Guid.NewGuid(),"FY 2026",Preferred:true,From:start,To:end);
        var closed=new SalesChoice(Guid.NewGuid(),"Closed",Preferred:true,Active:false,From:start,To:end);
        var registration=new SalesChoice(Guid.NewGuid(),"Registration",Preferred:true);
        var choice=new SalesChoice(Guid.NewGuid(),"Configured series",year.Id,registration.Id,Preferred:true);
        var alternative=choice with {Id=Guid.NewGuid(),Preferred=false};
        var otherRegistration=choice with {Id=Guid.NewGuid(),SecondaryScope=Guid.NewGuid()};
        var inactive=choice with {Id=Guid.NewGuid(),Active=false};
        var options=new SalesOptions([year,closed],[choice,alternative,otherRegistration,inactive],[registration],[],[],[],[],[]);
        Assert.Equal(year,options.DefaultYear(start));
        Assert.Equal(year,options.DefaultYear(end));
        Assert.Null(options.DefaultYear(end.AddDays(1)));
        Assert.Equal(registration,options.DefaultRegistration());
        Assert.Equal(choice,options.DefaultSeries(year.Id,registration.Id));
        Assert.Equal(alternative,(options with {Series=[alternative,otherRegistration,inactive]}).DefaultSeries(year.Id,registration.Id));
        Assert.Null((options with {Series=[alternative,alternative with {Id=Guid.NewGuid()}]}).DefaultSeries(year.Id,registration.Id));
        Assert.Null(options.DefaultSeries(closed.Id,registration.Id));
    }

    [Fact]
    public void LoginSettingsRejectUnsafeAssetPathsAndInvalidColors()
    {
        Assert.False(LoginPage.Keys["Logo"].TryNormalize("file:///example-private.png",out _));
        Assert.False(LoginPage.Keys["Background"].TryNormalize("https://example.invalid/logo.png",out _));
        Assert.False(LoginPage.Keys["Primary"].TryNormalize("#FFF",out _));
        Assert.True(LoginPage.Keys["Primary"].TryNormalize("#e2a8ff",out var color));
        Assert.Equal("#E2A8FF",color);
        Assert.All(LoginPage.Keys.Values,key=>Assert.Equal(SettingScope.Application,key.Scopes));
    }

    [Fact]
    public async Task AuthorizedLoginCopyLoadsBeforeSignInAndCanBeReset()
    {
        using var diagnostics=new DiagnosticsFixture();
        var directory=Path.Combine(PersistenceTests.FindBillingRoot(),".local","login-settings-tests",Guid.NewGuid().ToString("N"));
        var path=Path.Combine(directory,"synthetic.db");
        try
        {
            await SalesInvoiceWorkflowTests.ProvisionDisposableDatabaseAsync(path);
            var database=new DevelopmentDatabase(new ConfiguredDatabaseLocation(path));
            var auth=new AuthenticationService(new AuthenticationStore(database),new Argon2PasswordHasher(),TimeProvider.System);
            var store=new SettingsStore(database,TimeProvider.System);
            var service=new ApplicationSettingsService(auth,new ApplicationLifetime(),store);
            var field=LoginPage.Keys["Title"];
            var request=new SettingsRequest(SettingsAction.Save,Section:LoginPage.Section,Changes:[new(field.Key,"Welcome to the test company",null)]);
            Assert.Equal(field.Default,(await store.ReadLoginAsync()).Get("Title"));
            Assert.Equal(CompanyOutcome.SessionRequired,(await service.ExecuteAsync(request)).Outcome);
            await auth.SetupAsync("Synthetic.Login",AuthenticationTests.Original,AuthenticationTests.Original);
            await auth.LoginAsync("Synthetic.Login",AuthenticationTests.Original);
            Assert.Equal(CompanyOutcome.Success,(await service.ExecuteAsync(request)).Outcome);
            var selected=await service.ExecuteAsync(new(SettingsAction.List,Section:LoginPage.Section));
            var saved=Assert.Single(selected.Entries!,entry=>entry.Key==field.Key);
            Assert.Equal(CompanyOutcome.Success,(await service.ExecuteAsync(request with
                {Changes=[new(field.Key,null,saved.Version)]})).Outcome);
            Assert.Equal(field.Default,(await store.ReadLoginAsync()).Get("Title"));
            var retry=request with {Changes=[new(field.Key,"Welcome to the test company",null)]};
            var read=await service.ExecuteAsync(new(SettingsAction.List,Section:LoginPage.Section));
            var version=Assert.Single(read.Entries!,entry=>entry.Key==field.Key).Version;
            Assert.Equal(CompanyOutcome.Success,(await service.ExecuteAsync(retry with
                {Changes=[new(field.Key,"Welcome to the test company",version)]})).Outcome);
            await auth.LogoutAsync();
            Assert.Equal("Welcome to the test company",(await store.ReadLoginAsync()).Get("Title"));
            Assert.Equal(CompanyOutcome.SessionRequired,(await service.ExecuteAsync(request)).Outcome);
            var vm=new AuthenticationViewModel(auth);
            await vm.StartAsync(true);
            await vm.SubmitAsync("Synthetic.Login",AuthenticationTests.Original,"","");
            Assert.Equal(AuthenticationPage.Shell,vm.Page);
            await vm.LogoutAsync();
            Assert.Equal(AuthenticationPage.Login,vm.Page);
            Assert.False(vm.Lifetime.IsStopping);
            Assert.Null(await auth.GetCurrentAsync());
            await vm.SubmitAsync("Synthetic.Login",AuthenticationTests.Original,"","");
            Assert.Equal(AuthenticationPage.Shell,vm.Page);
            await vm.Lifetime.ShutdownAsync();
        }
        finally
        {
            if(Directory.Exists(directory)) Directory.Delete(directory,true);
        }
    }
}
