using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class CompanyMaster : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream=typeof(CompanyMaster).Assembly.GetManifestResourceStream("GTS.Billing.Infrastructure.Migrations.CompanyMaster.sql")
            ?? throw new InvalidOperationException("Company Master migration resource missing.");
        using var reader=new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Destructive downgrade is unavailable.");
}

