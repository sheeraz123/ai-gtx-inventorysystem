using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class CustomerInputFlexibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream=typeof(CustomerInputFlexibility).Assembly.GetManifestResourceStream(
            "GTS.Billing.Infrastructure.Migrations.CustomerInputFlexibility.sql")
            ?? throw new InvalidOperationException("Customer input migration resource is missing.");
        using var reader=new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restoring the PIN rule could reject retained customer addresses.");
}
