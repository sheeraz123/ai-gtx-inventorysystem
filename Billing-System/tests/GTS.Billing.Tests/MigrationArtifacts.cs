using System.Reflection;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

using GTS.Billing.Infrastructure;
using Xunit;

namespace GTS.Billing.Tests;

// Explicit developer tooling only; never called by normal application startup.
public static class MigrationArtifacts
{
    [Fact]
    public static void GenerateMigrationArtifactsForReview()
    {
        var directory = Path.Combine(PersistenceTests.FindBillingRoot(), ".local", "migration-artifacts");
        Directory.CreateDirectory(directory);
        Export(directory);
        Assert.True(File.Exists(Path.Combine(directory, "BillingDbContextModelSnapshot.cs")));
        var committed = File.ReadAllText(Path.Combine(PersistenceTests.FindBillingRoot(), "src", "GTS.Billing.Infrastructure", "Migrations", "BillingDbContextModelSnapshot.cs"));
        Assert.Equal(committed.Replace("\r\n", "\n"), File.ReadAllText(Path.Combine(directory, "BillingDbContextModelSnapshot.cs")).Replace("\r\n", "\n"));
    }
    private static void Export(string directory)
    {
        using var db = DevelopmentDatabase.CreateContext(":memory:", true);
        var services = new ServiceCollection();
        services.AddDbContextDesignTimeServices(db);
        services.AddEntityFrameworkDesignTimeServices();
        var provider = Assembly.Load("Microsoft.EntityFrameworkCore.Sqlite");
        var attribute = provider.GetCustomAttribute<DesignTimeProviderServicesAttribute>()!;
        ((IDesignTimeServices)Activator.CreateInstance(provider.GetType(attribute.TypeName, true)!)!).ConfigureDesignTimeServices(services);
        using var sp = services.BuildServiceProvider();
        var generator = sp.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select("C#");
        var model = db.GetService<IDesignTimeModel>().Model;
        File.WriteAllText(Path.Combine(directory, "BillingDbContextModelSnapshot.cs"), generator.GenerateSnapshot("GTS.Billing.Infrastructure.Migrations", typeof(BillingDbContext), "BillingDbContextModelSnapshot", model));
        File.WriteAllText(Path.Combine(directory, "20261118000200_CompanyInputFlexibility.Designer.cs"), generator.GenerateMetadata("GTS.Billing.Infrastructure.Migrations", typeof(BillingDbContext), "CompanyInputFlexibility", "20261118000200_CompanyInputFlexibility", model));
        if(db.Database.GetMigrations().Contains("20261118000200_CompanyInputFlexibility"))
            File.WriteAllText(Path.Combine(directory,"CompanyInputFlexibility.sql"),db.GetService<IMigrator>().GenerateScript("20261118000100_CompanyMaster","20261118000200_CompanyInputFlexibility"));
    }
}
