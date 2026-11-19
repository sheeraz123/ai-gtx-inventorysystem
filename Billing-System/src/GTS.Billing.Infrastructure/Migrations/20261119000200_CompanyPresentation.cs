using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class CompanyPresentation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(CompanyPresentation).Assembly.GetManifestResourceStream(
            "GTS.Billing.Infrastructure.Migrations.CompanyPresentation.sql")
            ?? throw new InvalidOperationException("Company presentation migration resource is missing.");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Company presentation records must be retained.");
}
