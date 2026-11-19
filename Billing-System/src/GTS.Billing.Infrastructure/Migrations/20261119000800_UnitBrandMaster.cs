using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class UnitBrandMaster : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "Brands" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Brands" PRIMARY KEY,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "NormalizedName" TEXT NOT NULL,
                "Description" TEXT NULL,
                "SortOrder" INTEGER NOT NULL,
                "Active" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "UpdatedByUserId" TEXT NOT NULL,
                "Manufacturer" TEXT NULL,
                "Logo" BLOB NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                CONSTRAINT "AK_Brands_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
                CONSTRAINT "FK_Brands_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Brands_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Brands_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );

            CREATE TABLE "Units" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Units" PRIMARY KEY,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "NormalizedName" TEXT NOT NULL,
                "Description" TEXT NULL,
                "SortOrder" INTEGER NOT NULL,
                "Active" INTEGER NOT NULL,
                "CreatedUtc" TEXT NOT NULL,
                "UpdatedUtc" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "UpdatedByUserId" TEXT NOT NULL,
                "Symbol" TEXT NULL,
                "UnitType" TEXT NOT NULL,
                "DecimalPrecision" INTEGER NOT NULL,
                "Uqc" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                CONSTRAINT "AK_Units_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
                CONSTRAINT "CK_Units_Precision" CHECK (DecimalPrecision BETWEEN 0 AND 6),
                CONSTRAINT "FK_Units_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Units_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Units_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );

            CREATE TABLE "ProductMeasureProfiles" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProductMeasureProfiles" PRIMARY KEY,
                "ProductId" TEXT NOT NULL,
                "BrandId" TEXT NULL,
                "BaseUnitId" TEXT NULL,
                "Classification" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                CONSTRAINT "FK_ProductMeasureProfiles_Brands_CompanyId_BrandId" FOREIGN KEY ("CompanyId", "BrandId") REFERENCES "Brands" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_ProductMeasureProfiles_Products_CompanyId_ProductId" FOREIGN KEY ("CompanyId", "ProductId") REFERENCES "Products" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_ProductMeasureProfiles_Units_CompanyId_BaseUnitId" FOREIGN KEY ("CompanyId", "BaseUnitId") REFERENCES "Units" ("CompanyId", "Id") ON DELETE RESTRICT
            );

            CREATE TABLE "ProductUnitConversions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProductUnitConversions" PRIMARY KEY,
                "ProductId" TEXT NOT NULL,
                "UnitId" TEXT NOT NULL,
                "FactorToBaseUnit" TEXT NOT NULL,
                "DefaultSales" INTEGER NOT NULL,
                "DefaultPurchase" INTEGER NOT NULL,
                "Active" INTEGER NOT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                CONSTRAINT "CK_Conversions_Defaults" CHECK (Active = 1 OR (DefaultSales = 0 AND DefaultPurchase = 0)),
                CONSTRAINT "CK_Conversions_Factor" CHECK (CAST(FactorToBaseUnit AS REAL) > 0),
                CONSTRAINT "FK_ProductUnitConversions_Products_CompanyId_ProductId" FOREIGN KEY ("CompanyId", "ProductId") REFERENCES "Products" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_ProductUnitConversions_Units_CompanyId_UnitId" FOREIGN KEY ("CompanyId", "UnitId") REFERENCES "Units" ("CompanyId", "Id") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX "IX_Brands_CompanyId_Code" ON "Brands" ("CompanyId", "Code");

            CREATE UNIQUE INDEX "IX_Brands_CompanyId_NormalizedName" ON "Brands" ("CompanyId", "NormalizedName");

            CREATE INDEX "IX_Brands_CreatedByUserId" ON "Brands" ("CreatedByUserId");

            CREATE INDEX "IX_Brands_UpdatedByUserId" ON "Brands" ("UpdatedByUserId");

            CREATE INDEX "IX_ProductMeasureProfiles_CompanyId_BaseUnitId" ON "ProductMeasureProfiles" ("CompanyId", "BaseUnitId");

            CREATE INDEX "IX_ProductMeasureProfiles_CompanyId_BrandId" ON "ProductMeasureProfiles" ("CompanyId", "BrandId");

            CREATE UNIQUE INDEX "IX_ProductMeasureProfiles_CompanyId_ProductId" ON "ProductMeasureProfiles" ("CompanyId", "ProductId");

            CREATE UNIQUE INDEX "DefaultPurchase" ON "ProductUnitConversions" ("CompanyId", "ProductId") WHERE DefaultPurchase = 1;

            CREATE UNIQUE INDEX "DefaultSales" ON "ProductUnitConversions" ("CompanyId", "ProductId") WHERE DefaultSales = 1;

            CREATE UNIQUE INDEX "IX_ProductUnitConversions_CompanyId_ProductId_UnitId" ON "ProductUnitConversions" ("CompanyId", "ProductId", "UnitId");

            CREATE INDEX "IX_ProductUnitConversions_CompanyId_UnitId" ON "ProductUnitConversions" ("CompanyId", "UnitId");

            CREATE UNIQUE INDEX "IX_Units_CompanyId_Code" ON "Units" ("CompanyId", "Code");

            CREATE UNIQUE INDEX "IX_Units_CompanyId_NormalizedName" ON "Units" ("CompanyId", "NormalizedName");

            CREATE INDEX "IX_Units_CreatedByUserId" ON "Units" ("CreatedByUserId");

            CREATE INDEX "IX_Units_UpdatedByUserId" ON "Units" ("UpdatedByUserId");

            INSERT INTO Modules (Code, Name) VALUES ('unit', 'Unit Master'), ('brand', 'Brand Master')
            ON CONFLICT(Code) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Unit, brand and conversion records must be retained.");
}
