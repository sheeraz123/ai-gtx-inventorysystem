using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public partial class InitialDevelopmentSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var resource in new[] { "InitialDevelopmentSchema.sql", "InvariantTriggers.sql" })
        {
            using var stream = typeof(InitialDevelopmentSchema).Assembly.GetManifestResourceStream("GTS.Billing.Infrastructure.Migrations." + resource)
                ?? throw new InvalidOperationException("Initial migration resource is missing.");
            using var reader = new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Destructive downgrade is intentionally unavailable. Use disposable development databases.");
}
