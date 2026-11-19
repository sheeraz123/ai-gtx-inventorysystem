using System.Xml.Linq;
using System.IO;
using GTS.Billing.Infrastructure;
using GTS.Billing.Presentation.Wpf;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void ModelBuildsAndCreatesSql()
    {
        using var db = DevelopmentDatabase.CreateContext(":memory:", true);
        Assert.Contains("CREATE TABLE", db.Database.GenerateCreateScript());
    }
    [Fact]
    public void CompanyMasterIsAvailableAndOtherNavigationEntriesRemainPlanned()
    {
        var vm = new ShellViewModel();
        Assert.Equal(19, vm.Modules.Count);
        Assert.Equal("dashboard", vm.SelectedModule.Code);
        foreach (var module in vm.Modules)
        {
            vm.NavigateCommand.Execute(module);
            Assert.Same(module, vm.SelectedModule);
            Assert.Equal(module.Code == "company" ? "Available" : "Planned", module.Status);
            if (module.Code != "company") Assert.Contains("No business workflow", module.Description);
        }
    }
    [Fact]
    public void UnknownNavigationTargetCannotActivate()
    {
        var vm = new ShellViewModel();
        vm.NavigateCommand.Execute(new PlannedModule("unknown", "Unknown"));
        Assert.Equal("dashboard", vm.SelectedModule.Code);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative.db")]
    public void MissingOrRelativeDatabasePathHasNoPerUserFallback(string? path)
    {
        Assert.Throws<InvalidOperationException>(() => new ConfiguredDatabaseLocation(path).GetAbsolutePath());
    }
    [Fact]
    public void ExactlyFiveApplicationProjectsRespectDependencyDirection()
    {
        var root = PersistenceTests.FindBillingRoot();
        var files = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        Assert.Equal(5, files.Length);
        var expected = new Dictionary<string, string[]> { ["Domain"] = [], ["Application"] = ["Domain"], ["Infrastructure"] = ["Application"], ["Printing"] = ["Application"], ["Presentation.Wpf"] = ["Application", "Infrastructure", "Printing"] };
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file)["GTS.Billing.".Length..];
            var refs = XDocument.Load(file).Descendants("ProjectReference").Select(x => Path.GetFileNameWithoutExtension(x.Attribute("Include")!.Value)["GTS.Billing.".Length..]).Order().ToArray();
            Assert.Equal(expected[name].Order(), refs);
        }
    }
}
