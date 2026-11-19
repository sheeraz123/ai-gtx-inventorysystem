using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class SecurityAdministration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream=typeof(SecurityAdministration).Assembly.GetManifestResourceStream(
            "GTS.Billing.Infrastructure.Migrations.SecurityAdministration.sql")
            ?? throw new InvalidOperationException("Security administration migration is missing.");
        using var reader=new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("User and permission data must be retained.");
}
