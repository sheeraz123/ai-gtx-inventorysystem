using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class CompanyInputFlexibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // These are GST geographic-validation rules, not ownership foreign keys.
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS GstAddress_State_Insert; DROP TRIGGER IF EXISTS GstAddress_State_Update; DROP TRIGGER IF EXISTS Address_State_Update; DROP TRIGGER IF EXISTS Gst_State_Update;");
        migrationBuilder.DropCheckConstraint("CK_Gst_Format", "GstRegistrations");
        migrationBuilder.DropCheckConstraint("CK_Bank_Required", "CompanyBankAccounts");
        migrationBuilder.AddCheckConstraint("CK_Bank_Required", "CompanyBankAccounts",
            "length(trim(BankName)) > 0 AND length(trim(AccountHolder)) > 0 AND length(AccountNumber) > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restoring validation could reject retained values. Automatic downgrade is unavailable.");
}
