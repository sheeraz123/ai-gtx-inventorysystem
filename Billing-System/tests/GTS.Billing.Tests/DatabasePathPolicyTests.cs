using System.IO;
using GTS.Billing.Infrastructure;
using Xunit;

namespace GTS.Billing.Tests;

public sealed class DatabasePathPolicyTests
{
    [Theory]
    [InlineData("relative.db")]
    [InlineData(@"C:relative.db")]
    [InlineData(@"\\server\share\synthetic.db")]
    [InlineData(@"\\?\C:\data\synthetic.db")]
    [InlineData(@"\\.\C:\data\synthetic.db")]
    [InlineData(@"C:\data\synthetic.db:stream")]
    public void UnsupportedFormsAreRejected(string path) =>
        Assert.Throws<InvalidOperationException>(() => new ConfiguredDatabaseLocation(path).GetAbsolutePath());

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.Removable)]
    [InlineData(DriveType.CDRom)]
    [InlineData(DriveType.Ram)]
    [InlineData(DriveType.Unknown)]
    [InlineData(DriveType.NoRootDirectory)]
    public void UnsupportedRootsAreRejected(DriveType drive) =>
        Assert.Throws<InvalidOperationException>(() => DatabasePathPolicy.Validate(@"C:\synthetic\fixture.db", _ => drive, _ => null));

    [Fact]
    public void FixedRootIsOnlyAClassificationAndReparseComponentsAreRejected()
    {
        var path = @"C:\synthetic\fixture.db";
        Assert.Equal(path, DatabasePathPolicy.Validate(path, _ => DriveType.Fixed, _ => null));
        Assert.Throws<InvalidOperationException>(() => DatabasePathPolicy.Validate(path, _ => DriveType.Fixed,
            item => item == path ? FileAttributes.Directory : null));
        Assert.Throws<InvalidOperationException>(() => DatabasePathPolicy.Validate(path, _ => DriveType.Fixed,
            item => item.EndsWith("synthetic", StringComparison.OrdinalIgnoreCase) ? FileAttributes.ReparsePoint : null));
        Assert.Throws<InvalidOperationException>(() => DatabasePathPolicy.Validate(path, _ => DriveType.Fixed,
            item => item.EndsWith(".installation.json", StringComparison.OrdinalIgnoreCase) ? FileAttributes.ReparsePoint : null));
    }

    [Fact]
    public void ExplicitInitializationDoesNotTreatMissingTargetAsEstablishedDatabase()
    {
        var directory = Path.Combine(PersistenceTests.FindBillingRoot(), ".local", "stage351-path", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "synthetic.db");
        Assert.Equal(path, new ConfiguredDatabaseLocation(path).GetAbsolutePath());
        Assert.False(Directory.Exists(directory));
        Assert.Throws<DatabaseStartupException>(() =>
            new DevelopmentDatabase(new ConfiguredDatabaseLocation(path)).OpenExisting());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void InstalledPolicyRejectsAllOverridesAndDevelopmentRequiresExplicitOptIn()
    {
        var path = Path.Combine(PersistenceTests.FindBillingRoot(), ".local", "synthetic.db");
        Assert.Equal(path, DatabasePathPolicy.Select(path, null, false, false));
        Assert.Throws<InvalidOperationException>(() => DatabasePathPolicy.Select(path, path, false, false));
        Assert.Throws<InvalidOperationException>(() => DatabasePathPolicy.Select(path, null, false, true));
        Assert.Equal(path, DatabasePathPolicy.Select(null, path, true, true));
        Assert.Null(DatabasePathPolicy.Select(null, null, false, false));
    }
}
