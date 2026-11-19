using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class SalesInvoiceEntry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "SalesInvoices" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SalesInvoices" PRIMARY KEY,
                "Date" TEXT NOT NULL,
                "FinancialYearId" TEXT NULL,
                "SeriesId" TEXT NULL,
                "RegistrationId" TEXT NULL,
                "CustomerId" TEXT NULL,
                "LedgerId" TEXT NULL,
                "Number" TEXT NULL,
                "CustomerName" TEXT NOT NULL,
                "PurchaseOrder" TEXT NOT NULL,
                "Reference" TEXT NOT NULL,
                "DraftJson" TEXT NOT NULL,
                "Taxable" TEXT NOT NULL,
                "Tax" TEXT NOT NULL,
                "Total" TEXT NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                "CreatedBy" TEXT NOT NULL,
                "UpdatedBy" TEXT NOT NULL,
                "FinalizedUtc" TEXT NULL,
                "FinalizedBy" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                CONSTRAINT "CK_Sales_Finalization" CHECK ((LedgerId IS NULL AND Number IS NULL AND FinalizedUtc IS NULL AND FinalizedBy IS NULL) OR (LedgerId IS NOT NULL AND Number IS NOT NULL AND FinalizedUtc IS NOT NULL AND FinalizedBy IS NOT NULL AND FinancialYearId IS NOT NULL AND SeriesId IS NOT NULL AND RegistrationId IS NOT NULL AND CustomerId IS NOT NULL)),
                CONSTRAINT "CK_Sales_Json" CHECK (json_valid(DraftJson)),
                CONSTRAINT "FK_SalesInvoices_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SalesInvoices_Customers_CompanyId_CustomerId" FOREIGN KEY ("CompanyId", "CustomerId") REFERENCES "Customers" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SalesInvoices_DocumentNumberLedger_CompanyId_LedgerId" FOREIGN KEY ("CompanyId", "LedgerId") REFERENCES "DocumentNumberLedger" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SalesInvoices_DocumentNumberingSeries_CompanyId_SeriesId" FOREIGN KEY ("CompanyId", "SeriesId") REFERENCES "DocumentNumberingSeries" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SalesInvoices_FinancialYears_CompanyId_FinancialYearId" FOREIGN KEY ("CompanyId", "FinancialYearId") REFERENCES "FinancialYears" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SalesInvoices_GstRegistrations_CompanyId_RegistrationId" FOREIGN KEY ("CompanyId", "RegistrationId") REFERENCES "GstRegistrations" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SalesInvoices_Users_CreatedBy" FOREIGN KEY ("CreatedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SalesInvoices_Users_FinalizedBy" FOREIGN KEY ("FinalizedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SalesInvoices_Users_UpdatedBy" FOREIGN KEY ("UpdatedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );

            CREATE INDEX "IX_SalesInvoices_CompanyId_CustomerId" ON "SalesInvoices" ("CompanyId", "CustomerId");

            CREATE INDEX "IX_SalesInvoices_CompanyId_Date" ON "SalesInvoices" ("CompanyId", "Date");

            CREATE INDEX "IX_SalesInvoices_CompanyId_FinancialYearId" ON "SalesInvoices" ("CompanyId", "FinancialYearId");

            CREATE INDEX "IX_SalesInvoices_CompanyId_LedgerId" ON "SalesInvoices" ("CompanyId", "LedgerId");

            CREATE INDEX "IX_SalesInvoices_CompanyId_RegistrationId" ON "SalesInvoices" ("CompanyId", "RegistrationId");

            CREATE INDEX "IX_SalesInvoices_CompanyId_SeriesId" ON "SalesInvoices" ("CompanyId", "SeriesId");

            CREATE INDEX "IX_SalesInvoices_CreatedBy" ON "SalesInvoices" ("CreatedBy");

            CREATE INDEX "IX_SalesInvoices_FinalizedBy" ON "SalesInvoices" ("FinalizedBy");

            CREATE UNIQUE INDEX "IX_SalesInvoices_LedgerId" ON "SalesInvoices" ("LedgerId") WHERE LedgerId IS NOT NULL;

            CREATE INDEX "IX_SalesInvoices_UpdatedBy" ON "SalesInvoices" ("UpdatedBy");

            CREATE TRIGGER SalesInvoice_DraftInsert BEFORE INSERT ON SalesInvoices
            WHEN NEW.LedgerId IS NOT NULL
            BEGIN SELECT RAISE(ABORT,'Sales invoices must originate as drafts'); END;

            CREATE TRIGGER SalesInvoice_FinalizeAssociation BEFORE UPDATE OF LedgerId ON SalesInvoices
            WHEN NEW.LedgerId IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM DocumentNumberLedger l JOIN InvoiceSnapshots s ON s.CompanyId=l.CompanyId AND s.LedgerId=l.Id
                WHERE l.Id=NEW.LedgerId AND l.CompanyId=NEW.CompanyId AND l.FinancialYearId=NEW.FinancialYearId
                  AND l.SeriesId=NEW.SeriesId AND l.GstRegistrationId=NEW.RegistrationId
                  AND l.CompleteNumber=NEW.Number AND l.Status='Issued')
            BEGIN SELECT RAISE(ABORT,'The finalized invoice requires its own issued number and immutable snapshot'); END;

            CREATE TRIGGER SalesInvoice_FinalizedNoUpdate BEFORE UPDATE ON SalesInvoices
            WHEN OLD.LedgerId IS NOT NULL
            BEGIN SELECT RAISE(ABORT,'Finalized sales invoices are immutable'); END;

            CREATE TRIGGER SalesInvoice_FinalizedNoDelete BEFORE DELETE ON SalesInvoices
            WHEN OLD.LedgerId IS NOT NULL
            BEGIN SELECT RAISE(ABORT,'Finalized sales invoices are retained'); END;

            INSERT INTO Modules (Code, Name) VALUES ('invoice', 'Sales Invoice')
            ON CONFLICT(Code) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Sales invoices and their references must be retained.");
}
