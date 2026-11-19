using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class InvoiceTaxDefault : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE GstRegistrations ADD COLUMN IsInvoiceDefault INTEGER NOT NULL DEFAULT 0
                CONSTRAINT CK_Gst_InvoiceDefault CHECK(IsInvoiceDefault IN (0,1) AND (IsInvoiceDefault = 0 OR IsActive = 1));
            CREATE UNIQUE INDEX UX_GstRegistrations_InvoiceDefault ON GstRegistrations(CompanyId) WHERE IsInvoiceDefault = 1;
            UPDATE GstRegistrations AS registration SET IsInvoiceDefault = 1
            WHERE registration.IsActive = 1 AND
                (SELECT COUNT(*) FROM GstRegistrations AS active
                 WHERE active.CompanyId = registration.CompanyId AND active.IsActive = 1) = 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Invoice tax defaults must be retained.");
}
