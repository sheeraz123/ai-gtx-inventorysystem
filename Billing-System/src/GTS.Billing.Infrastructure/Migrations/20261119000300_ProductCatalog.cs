using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class ProductCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ProductCatalog).Assembly.GetManifestResourceStream("GTS.Billing.Infrastructure.Migrations.ProductCatalog.sql")
            ?? throw new InvalidOperationException("Product catalog migration resource is missing.");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Product and category records must be retained.");
}
