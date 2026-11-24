using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

[DbContext(typeof(BillingDbContext))]
[Migration("20261124000100_CustomerAadhaarSuffix")]
public sealed class CustomerAadhaarSuffix : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE Customers ADD COLUMN AadhaarLastFour TEXT
                CONSTRAINT CK_Customer_AadhaarSuffix CHECK (AadhaarLastFour IS NULL OR AadhaarLastFour GLOB '[0-9][0-9][0-9][0-9]');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE Customers DROP COLUMN AadhaarLastFour;");
    }
}
