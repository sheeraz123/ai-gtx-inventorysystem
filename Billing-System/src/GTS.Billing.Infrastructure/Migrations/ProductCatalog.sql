CREATE TABLE "ProductCategories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ProductCategories" PRIMARY KEY,
    "Code" TEXT COLLATE NOCASE NOT NULL,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "SortOrder" INTEGER NOT NULL,
    "Active" INTEGER NOT NULL,
    "ParentId" TEXT NULL,
    "NormalizedName" TEXT NOT NULL,
    "CreatedUtc" TEXT NOT NULL,
    "UpdatedUtc" TEXT NOT NULL,
    "CreatedByUserId" TEXT NOT NULL,
    "UpdatedByUserId" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_ProductCategories_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Category_Code" CHECK (length(Code) BETWEEN 1 AND 30 AND Code = upper(Code) AND Code NOT GLOB '*[^A-Z0-9_-]*'),
    CONSTRAINT "CK_Category_NameOrder" CHECK (length(trim(Name)) BETWEEN 1 AND 150 AND SortOrder >= 0),
    CONSTRAINT "CK_Category_NotSelf" CHECK (ParentId IS NULL OR ParentId <> Id),
    CONSTRAINT "FK_ProductCategories_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProductCategories_ProductCategories_CompanyId_ParentId" FOREIGN KEY ("CompanyId", "ParentId") REFERENCES "ProductCategories" ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProductCategories_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ProductCategories_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "Products" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Products" PRIMARY KEY,
    "Code" TEXT COLLATE NOCASE NOT NULL,
    "Name" TEXT NOT NULL,
    "ShortName" TEXT NULL,
    "Description" TEXT NULL,
    "Kind" TEXT NOT NULL,
    "Active" INTEGER NOT NULL,
    "Brand" TEXT NULL,
    "Sku" TEXT NULL,
    "Barcode" TEXT NULL,
    "PartNumber" TEXT NULL,
    "ModelNumber" TEXT NULL,
    "BaseUnit" TEXT NOT NULL,
    "SalesUnit" TEXT NULL,
    "SalesFactor" INTEGER NOT NULL,
    "PurchaseUnit" TEXT NULL,
    "PurchaseFactor" INTEGER NOT NULL,
    "QuantityPrecision" INTEGER NOT NULL,
    "Hsn" TEXT NULL,
    "GstRate" INTEGER NULL,
    "TaxExempt" INTEGER NOT NULL,
    "TaxNotes" TEXT NULL,
    "PurchaseRate" INTEGER NULL,
    "SalesRate" INTEGER NULL,
    "Mrp" INTEGER NULL,
    "SalesInclusive" INTEGER NOT NULL,
    "PurchaseInclusive" INTEGER NOT NULL,
    "Currency" TEXT NOT NULL,
    "PricingDate" TEXT NULL,
    "StockTracked" INTEGER NOT NULL,
    "ReorderLevel" INTEGER NULL,
    "MinimumStock" INTEGER NULL,
    "MaximumStock" INTEGER NULL,
    "StorageLocation" TEXT NULL,
    "Specifications" TEXT NULL,
    "Warranty" TEXT NULL,
    "Remarks" TEXT NULL,
    "CategoryId" TEXT NOT NULL,
    "NormalizedName" TEXT NOT NULL,
    "CreatedUtc" TEXT NOT NULL,
    "UpdatedUtc" TEXT NOT NULL,
    "CreatedByUserId" TEXT NOT NULL,
    "UpdatedByUserId" TEXT NOT NULL,
    "Image" BLOB NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_Products_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Product_Code" CHECK (length(Code) BETWEEN 1 AND 30 AND Code = upper(Code) AND Code NOT GLOB '*[^A-Z0-9_-]*'),
    CONSTRAINT "CK_Product_Gst" CHECK (GstRate IS NULL OR GstRate BETWEEN 0 AND 1000000),
    CONSTRAINT "CK_Product_Image" CHECK (Image IS NULL OR length(Image) BETWEEN 45 AND 1048576),
    CONSTRAINT "CK_Product_KindName" CHECK (Kind IN ('Goods','Services') AND length(trim(Name)) BETWEEN 1 AND 150),
    CONSTRAINT "CK_Product_Prices" CHECK ((PurchaseRate IS NULL OR PurchaseRate >= 0) AND (SalesRate IS NULL OR SalesRate >= 0) AND (Mrp IS NULL OR Mrp >= 0)),
    CONSTRAINT "CK_Product_Stock" CHECK ((Kind = 'Goods' OR StockTracked = 0) AND ReorderLevel >= 0 OR (Kind = 'Goods' OR StockTracked = 0) AND ReorderLevel IS NULL),
    CONSTRAINT "CK_Product_Thresholds" CHECK (MinimumStock >= 0 AND MaximumStock >= 0 AND MaximumStock >= MinimumStock OR MinimumStock IS NULL AND (MaximumStock IS NULL OR MaximumStock >= 0) OR MaximumStock IS NULL AND MinimumStock >= 0),
    CONSTRAINT "CK_Product_Units" CHECK (length(trim(BaseUnit)) > 0 AND SalesFactor > 0 AND PurchaseFactor > 0 AND QuantityPrecision BETWEEN 0 AND 4),
    CONSTRAINT "FK_Products_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Products_ProductCategories_CompanyId_CategoryId" FOREIGN KEY ("CompanyId", "CategoryId") REFERENCES "ProductCategories" ("CompanyId", "Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Products_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Products_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX "IX_ProductCategories_CompanyId_Code" ON "ProductCategories" ("CompanyId", "Code");
CREATE UNIQUE INDEX "IX_ProductCategories_CompanyId_NormalizedName" ON "ProductCategories" ("CompanyId", "NormalizedName") WHERE ParentId IS NULL;
CREATE UNIQUE INDEX "IX_ProductCategories_CompanyId_ParentId_NormalizedName" ON "ProductCategories" ("CompanyId", "ParentId", "NormalizedName") WHERE ParentId IS NOT NULL;
CREATE INDEX "IX_ProductCategories_CreatedByUserId" ON "ProductCategories" ("CreatedByUserId");
CREATE INDEX "IX_ProductCategories_UpdatedByUserId" ON "ProductCategories" ("UpdatedByUserId");
CREATE INDEX "IX_Products_CompanyId_Barcode" ON "Products" ("CompanyId", "Barcode");
CREATE INDEX "IX_Products_CompanyId_CategoryId_Active" ON "Products" ("CompanyId", "CategoryId", "Active");
CREATE UNIQUE INDEX "IX_Products_CompanyId_Code" ON "Products" ("CompanyId", "Code");
CREATE INDEX "IX_Products_CompanyId_NormalizedName" ON "Products" ("CompanyId", "NormalizedName");
CREATE INDEX "IX_Products_CompanyId_Sku" ON "Products" ("CompanyId", "Sku");
CREATE INDEX "IX_Products_CreatedByUserId" ON "Products" ("CreatedByUserId");
CREATE INDEX "IX_Products_UpdatedByUserId" ON "Products" ("UpdatedByUserId");

CREATE TRIGGER Catalog_CategoryHierarchy_INSERT BEFORE INSERT ON ProductCategories
WHEN NEW.ParentId IS NOT NULL AND (
    EXISTS (WITH RECURSIVE ancestors(Id) AS (
        SELECT NEW.ParentId
        UNION SELECT p.ParentId FROM ProductCategories p JOIN ancestors a ON p.Id = a.Id
         WHERE p.CompanyId = NEW.CompanyId AND p.ParentId IS NOT NULL
    ) SELECT 1 FROM ancestors WHERE Id = NEW.Id)
    OR (NEW.Active = 1 AND NOT EXISTS (
        SELECT 1 FROM ProductCategories p WHERE p.CompanyId = NEW.CompanyId AND p.Id = NEW.ParentId AND p.Active = 1))
)
BEGIN SELECT RAISE(ABORT,'Invalid category hierarchy'); END;

CREATE TRIGGER Catalog_CategoryHierarchy_UPDATE BEFORE UPDATE OF ParentId, Active ON ProductCategories
WHEN NEW.ParentId IS NOT NULL AND (
    EXISTS (WITH RECURSIVE ancestors(Id) AS (
        SELECT NEW.ParentId
        UNION SELECT p.ParentId FROM ProductCategories p JOIN ancestors a ON p.Id = a.Id
         WHERE p.CompanyId = NEW.CompanyId AND p.ParentId IS NOT NULL
    ) SELECT 1 FROM ancestors WHERE Id = NEW.Id)
    OR (NEW.Active = 1 AND NOT EXISTS (
        SELECT 1 FROM ProductCategories p WHERE p.CompanyId = NEW.CompanyId AND p.Id = NEW.ParentId AND p.Active = 1))
)
BEGIN SELECT RAISE(ABORT,'Invalid category hierarchy'); END;

CREATE TRIGGER Catalog_CategoryActiveChildren BEFORE UPDATE OF Active ON ProductCategories
WHEN NEW.Active = 0 AND EXISTS (
    SELECT 1 FROM ProductCategories child WHERE child.CompanyId = OLD.CompanyId AND child.ParentId = OLD.Id AND child.Active = 1)
BEGIN SELECT RAISE(ABORT,'Active child categories must be moved or deactivated first'); END;

CREATE TRIGGER Catalog_ProductCategory_INSERT BEFORE INSERT ON Products
WHEN NOT EXISTS (
    SELECT 1 FROM ProductCategories c WHERE c.CompanyId = NEW.CompanyId AND c.Id = NEW.CategoryId AND c.Active = 1)
BEGIN SELECT RAISE(ABORT,'An active category is required for a new assignment'); END;

CREATE TRIGGER Catalog_ProductCategory_UPDATE BEFORE UPDATE OF CategoryId ON Products
WHEN NEW.CategoryId IS NOT OLD.CategoryId AND NOT EXISTS (
    SELECT 1 FROM ProductCategories c WHERE c.CompanyId = NEW.CompanyId AND c.Id = NEW.CategoryId AND c.Active = 1)
BEGIN SELECT RAISE(ABORT,'An active category is required for a new assignment'); END;

CREATE TRIGGER Catalog_ProductCategories_Retain BEFORE DELETE ON ProductCategories
BEGIN SELECT RAISE(ABORT,'Catalog records are retained; deactivate instead'); END;
CREATE TRIGGER Catalog_ProductCategories_Identity BEFORE UPDATE OF Id, CompanyId, Code ON ProductCategories
WHEN NEW.Id IS NOT OLD.Id OR NEW.CompanyId IS NOT OLD.CompanyId OR NEW.Code IS NOT OLD.Code
BEGIN SELECT RAISE(ABORT,'Catalog identity is immutable'); END;

CREATE TRIGGER Catalog_Products_Retain BEFORE DELETE ON Products
BEGIN SELECT RAISE(ABORT,'Catalog records are retained; deactivate instead'); END;
CREATE TRIGGER Catalog_Products_Identity BEFORE UPDATE OF Id, CompanyId, Code ON Products
WHEN NEW.Id IS NOT OLD.Id OR NEW.CompanyId IS NOT OLD.CompanyId OR NEW.Code IS NOT OLD.Code
BEGIN SELECT RAISE(ABORT,'Catalog identity is immutable'); END;
