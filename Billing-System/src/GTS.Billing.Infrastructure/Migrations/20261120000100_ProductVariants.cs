using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

[DbContext(typeof(BillingDbContext))]
[Migration("20261120000100_ProductVariants")]
public sealed class AddProductVariants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX AK_Products_CompanyId_Id ON Products(CompanyId,Id);
            CREATE TABLE VariantOptions (
                Id TEXT NOT NULL PRIMARY KEY, CompanyId TEXT NOT NULL, Version INTEGER NOT NULL,
                Axis TEXT NOT NULL, Code TEXT NOT NULL, Name TEXT NOT NULL, Hex TEXT,
                SortOrder INTEGER NOT NULL, Active INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL, CreatedBy TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, UpdatedBy TEXT NOT NULL,
                UNIQUE (CompanyId,Axis,Id),
                FOREIGN KEY (CompanyId) REFERENCES Companies(Id) ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IX_VariantOptions_CompanyId_Axis_Code ON VariantOptions(CompanyId,Axis,Code);
            CREATE TABLE ProductVariants (
                Id TEXT NOT NULL PRIMARY KEY, CompanyId TEXT NOT NULL, Version INTEGER NOT NULL, ProductId TEXT NOT NULL,
                Code TEXT NOT NULL, Combination TEXT NOT NULL, Sku TEXT, Barcode TEXT COLLATE NOCASE, Mrp TEXT, SalesRate TEXT, PurchaseRate TEXT,
                Active INTEGER NOT NULL, CreatedUtc TEXT NOT NULL, CreatedBy TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL, UpdatedBy TEXT NOT NULL, UNIQUE (CompanyId,Id),
                FOREIGN KEY (CompanyId,ProductId) REFERENCES Products(CompanyId,Id) ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IX_ProductVariants_CompanyId_ProductId_Code ON ProductVariants(CompanyId,ProductId,Code);
            CREATE UNIQUE INDEX IX_ProductVariants_CompanyId_ProductId_Combination ON ProductVariants(CompanyId,ProductId,Combination);
            CREATE UNIQUE INDEX IX_ProductVariants_CompanyId_Sku ON ProductVariants(CompanyId,Sku) WHERE Sku IS NOT NULL;
            CREATE UNIQUE INDEX IX_ProductVariants_CompanyId_Barcode ON ProductVariants(CompanyId,Barcode) WHERE Barcode IS NOT NULL;
            CREATE TABLE ProductVariantOptions (
                CompanyId TEXT NOT NULL, VariantId TEXT NOT NULL, Axis TEXT NOT NULL, OptionId TEXT NOT NULL,
                PRIMARY KEY (CompanyId,VariantId,Axis),
                FOREIGN KEY (CompanyId,VariantId) REFERENCES ProductVariants(CompanyId,Id) ON DELETE RESTRICT,
                FOREIGN KEY (CompanyId,Axis,OptionId) REFERENCES VariantOptions(CompanyId,Axis,Id) ON DELETE RESTRICT
            );
            CREATE INDEX IX_ProductVariantOptions_CompanyId_Axis_OptionId ON ProductVariantOptions(CompanyId,Axis,OptionId);
            INSERT INTO Modules (Code,Name) VALUES ('product-variants','Product Variants'),('product-sizes','Size Master'),
                ('product-colors','Color Master'),('variant-attributes','Variant Attributes');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Downgrading the product variant schema would discard variant data.");
}
