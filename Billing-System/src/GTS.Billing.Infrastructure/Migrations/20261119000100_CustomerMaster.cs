using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class CustomerMaster : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream=typeof(CustomerMaster).Assembly.GetManifestResourceStream("GTS.Billing.Infrastructure.Migrations.CustomerMaster.sql")
            ?? throw new InvalidOperationException("Customer Master migration resource is missing.");
        using var reader=new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Customer business records cannot be dropped by automatic downgrade.");
}
