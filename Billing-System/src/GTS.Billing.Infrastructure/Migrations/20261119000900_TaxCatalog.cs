using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class TaxCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "GstRateDefinitions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GstRateDefinitions" PRIMARY KEY,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Description" TEXT NULL,
                "Treatment" TEXT NOT NULL,
                "Total" INTEGER NOT NULL,
                "Cgst" INTEGER NOT NULL,
                "Sgst" INTEGER NOT NULL,
                "Igst" INTEGER NOT NULL,
                "Utgst" INTEGER NULL,
                "Cess" INTEGER NOT NULL,
                "From" TEXT NOT NULL,
                "To" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                "Active" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "UpdatedByUserId" TEXT NOT NULL,
                CONSTRAINT "AK_GstRateDefinitions_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
                CONSTRAINT "CK_GstRate_Dates" CHECK ("To" IS NULL OR "To" > "From"),
                CONSTRAINT "CK_GstRate_Percentages" CHECK (Total BETWEEN 0 AND 1000000 AND Cgst BETWEEN 0 AND 1000000 AND Sgst BETWEEN 0 AND 1000000 AND Igst BETWEEN 0 AND 1000000 AND Cess BETWEEN 0 AND 1000000 AND (Utgst IS NULL OR Utgst BETWEEN 0 AND 1000000)),
                CONSTRAINT "CK_GstRate_Treatment" CHECK ((Treatment='Taxable' AND Total>0 AND Cgst+Sgst=Total AND Igst=Total AND (Utgst IS NULL OR Cgst+Utgst=Total)) OR (Treatment IN ('NilRated','Exempt','ZeroRated','NonGst') AND Total=0 AND Cgst=0 AND Sgst=0 AND Igst=0 AND Cess=0 AND coalesce(Utgst,0)=0)),
                CONSTRAINT "FK_GstRateDefinitions_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_GstRateDefinitions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_GstRateDefinitions_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );

            CREATE TABLE "TaxClassifications" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_TaxClassifications" PRIMARY KEY,
                "Type" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "ShortDescription" TEXT NULL,
                "Notes" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                "Active" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "UpdatedByUserId" TEXT NOT NULL,
                CONSTRAINT "AK_TaxClassifications_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
                CONSTRAINT "CK_TaxClassification_Code" CHECK (Code NOT GLOB '*[^0-9]*' AND ((Type='HSN' AND length(Code) IN (2,4,6,8)) OR (Type='SAC' AND length(Code)=6 AND substr(Code,1,2)='99'))),
                CONSTRAINT "FK_TaxClassifications_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_TaxClassifications_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_TaxClassifications_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );

            CREATE TABLE "TaxClassificationRateAssignments" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_TaxClassificationRateAssignments" PRIMARY KEY,
                "ClassificationId" TEXT NOT NULL,
                "RateId" TEXT NOT NULL,
                "Context" TEXT NOT NULL,
                "From" TEXT NOT NULL,
                "To" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                "Active" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "UpdatedByUserId" TEXT NOT NULL,
                CONSTRAINT "AK_TaxClassificationRateAssignments_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
                CONSTRAINT "CK_TaxAssignment_Dates" CHECK ("To" IS NULL OR "To" > "From"),
                CONSTRAINT "FK_TaxClassificationRateAssignments_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_TaxClassificationRateAssignments_GstRateDefinitions_CompanyId_RateId" FOREIGN KEY ("CompanyId", "RateId") REFERENCES "GstRateDefinitions" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_TaxClassificationRateAssignments_TaxClassifications_CompanyId_ClassificationId" FOREIGN KEY ("CompanyId", "ClassificationId") REFERENCES "TaxClassifications" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_TaxClassificationRateAssignments_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_TaxClassificationRateAssignments_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );

            CREATE TABLE "ProductTaxProfiles" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProductTaxProfiles" PRIMARY KEY,
                "ProductId" TEXT NOT NULL,
                "ClassificationId" TEXT NOT NULL,
                "Context" TEXT NOT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                CONSTRAINT "FK_ProductTaxProfiles_Products_CompanyId_ProductId" FOREIGN KEY ("CompanyId", "ProductId") REFERENCES "Products" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_ProductTaxProfiles_TaxClassifications_CompanyId_ClassificationId" FOREIGN KEY ("CompanyId", "ClassificationId") REFERENCES "TaxClassifications" ("CompanyId", "Id") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX "IX_GstRateDefinitions_CompanyId_Code_From" ON "GstRateDefinitions" ("CompanyId", "Code", "From");

            CREATE INDEX "IX_GstRateDefinitions_CreatedByUserId" ON "GstRateDefinitions" ("CreatedByUserId");

            CREATE INDEX "IX_GstRateDefinitions_UpdatedByUserId" ON "GstRateDefinitions" ("UpdatedByUserId");

            CREATE INDEX "IX_ProductTaxProfiles_CompanyId_ClassificationId" ON "ProductTaxProfiles" ("CompanyId", "ClassificationId");

            CREATE UNIQUE INDEX "IX_ProductTaxProfiles_CompanyId_ProductId" ON "ProductTaxProfiles" ("CompanyId", "ProductId");

            CREATE UNIQUE INDEX "IX_TaxClassificationRateAssignments_CompanyId_ClassificationId_Context_From" ON "TaxClassificationRateAssignments" ("CompanyId", "ClassificationId", "Context", "From");

            CREATE INDEX "IX_TaxClassificationRateAssignments_CompanyId_RateId" ON "TaxClassificationRateAssignments" ("CompanyId", "RateId");

            CREATE INDEX "IX_TaxClassificationRateAssignments_CreatedByUserId" ON "TaxClassificationRateAssignments" ("CreatedByUserId");

            CREATE INDEX "IX_TaxClassificationRateAssignments_UpdatedByUserId" ON "TaxClassificationRateAssignments" ("UpdatedByUserId");

            CREATE UNIQUE INDEX "IX_TaxClassifications_CompanyId_Type_Code" ON "TaxClassifications" ("CompanyId", "Type", "Code");

            CREATE INDEX "IX_TaxClassifications_CreatedByUserId" ON "TaxClassifications" ("CreatedByUserId");

            CREATE INDEX "IX_TaxClassifications_UpdatedByUserId" ON "TaxClassifications" ("UpdatedByUserId");

            CREATE TRIGGER TR_GstRate_NoOverlap_Insert BEFORE INSERT ON GstRateDefinitions
            WHEN EXISTS (SELECT 1 FROM GstRateDefinitions r
                WHERE r.CompanyId=NEW.CompanyId AND r.Code=NEW.Code
                  AND (NEW."To" IS NULL OR r."From"<NEW."To") AND (r."To" IS NULL OR NEW."From"<r."To"))
            BEGIN SELECT RAISE(ABORT,'Overlapping GST rate versions'); END;

            CREATE TRIGGER TR_GstRate_NoOverlap_Update BEFORE UPDATE OF CompanyId,Code,"From","To" ON GstRateDefinitions
            WHEN EXISTS (SELECT 1 FROM GstRateDefinitions r
                WHERE r.Id<>OLD.Id AND r.CompanyId=NEW.CompanyId AND r.Code=NEW.Code
                  AND (NEW."To" IS NULL OR r."From"<NEW."To") AND (r."To" IS NULL OR NEW."From"<r."To"))
            BEGIN SELECT RAISE(ABORT,'Overlapping GST rate versions'); END;

            CREATE TRIGGER TR_TaxAssignment_NoOverlap_Insert BEFORE INSERT ON TaxClassificationRateAssignments
            WHEN EXISTS (SELECT 1 FROM TaxClassificationRateAssignments a
                WHERE a.CompanyId=NEW.CompanyId AND a.ClassificationId=NEW.ClassificationId AND a.Context=NEW.Context
                  AND (NEW."To" IS NULL OR a."From"<NEW."To") AND (a."To" IS NULL OR NEW."From"<a."To"))
            BEGIN SELECT RAISE(ABORT,'Overlapping tax assignments'); END;

            CREATE TRIGGER TR_TaxAssignment_NoOverlap_Update BEFORE UPDATE OF CompanyId,ClassificationId,Context,"From","To" ON TaxClassificationRateAssignments
            WHEN EXISTS (SELECT 1 FROM TaxClassificationRateAssignments a
                WHERE a.Id<>OLD.Id AND a.CompanyId=NEW.CompanyId AND a.ClassificationId=NEW.ClassificationId AND a.Context=NEW.Context
                  AND (NEW."To" IS NULL OR a."From"<NEW."To") AND (a."To" IS NULL OR NEW."From"<a."To"))
            BEGIN SELECT RAISE(ABORT,'Overlapping tax assignments'); END;

            INSERT INTO Modules (Code, Name) VALUES ('hsnsac', 'HSN/SAC Master'), ('tax', 'GST Rate Master')
            ON CONFLICT(Code) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Tax catalog history and product links must be retained.");
}
