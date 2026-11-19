-- Copy all customer address values before replacing the SQLite table constraint.
CREATE TABLE "CustomerAddresses_New" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CustomerAddresses" PRIMARY KEY,
    "Label" TEXT NOT NULL,
    "AddressType" TEXT NOT NULL,
    "RecipientName" TEXT NOT NULL,
    "Organization" TEXT NULL,
    "Line1" TEXT NOT NULL,
    "Line2" TEXT NULL,
    "Landmark" TEXT NULL,
    "Locality" TEXT NULL,
    "City" TEXT NOT NULL,
    "District" TEXT NULL,
    "StateName" TEXT NULL,
    "StateCode" TEXT NULL,
    "PostalCode" TEXT NOT NULL,
    "Country" TEXT NOT NULL,
    "Mobile" TEXT NULL,
    "Email" TEXT NULL,
    "IsDefaultBilling" INTEGER NOT NULL,
    "IsDefaultShipping" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    "CustomerId" TEXT NOT NULL,
    CONSTRAINT "AK_CustomerAddresses_CompanyId_CustomerId_Id" UNIQUE ("CompanyId", "CustomerId", "Id"),
    CONSTRAINT "AK_CustomerAddresses_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_CustomerAddress_DefaultBilling" CHECK (IsDefaultBilling = 0 OR (IsActive = 1 AND AddressType IN ('Billing','Both'))),
    CONSTRAINT "CK_CustomerAddress_DefaultShipping" CHECK (IsDefaultShipping = 0 OR (IsActive = 1 AND AddressType IN ('Shipping','Both'))),
    CONSTRAINT "CK_CustomerAddress_Flags" CHECK (IsActive IN (0,1) AND IsDefaultBilling IN (0,1) AND IsDefaultShipping IN (0,1)),
    CONSTRAINT "CK_CustomerAddress_IndianState" CHECK (Country <> 'IN' OR (StateCode IS NOT NULL AND length(StateCode) = 2)),
    CONSTRAINT "CK_CustomerAddress_Required" CHECK (length(trim(Label)) > 0 AND length(trim(RecipientName)) > 0 AND length(trim(Line1)) > 0 AND length(trim(City)) > 0 AND length(Country) = 2 AND Country NOT GLOB '*[^A-Z]*'),
    CONSTRAINT "CK_CustomerAddress_Type" CHECK (AddressType IN ('Billing','Shipping','Both')),
    CONSTRAINT "FK_CustomerAddresses_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CustomerAddresses_Customers_CompanyId_CustomerId" FOREIGN KEY ("CompanyId", "CustomerId") REFERENCES "Customers" ("CompanyId", "Id") ON DELETE RESTRICT
);
INSERT INTO "CustomerAddresses_New" ("Id", "Label", "AddressType", "RecipientName", "Organization", "Line1", "Line2", "Landmark", "Locality", "City", "District", "StateName", "StateCode", "PostalCode", "Country", "Mobile", "Email", "IsDefaultBilling", "IsDefaultShipping", "IsActive", "Version", "CompanyId", "CustomerId") SELECT "Id", "Label", "AddressType", "RecipientName", "Organization", "Line1", "Line2", "Landmark", "Locality", "City", "District", "StateName", "StateCode", "PostalCode", "Country", "Mobile", "Email", "IsDefaultBilling", "IsDefaultShipping", "IsActive", "Version", "CompanyId", "CustomerId" FROM "CustomerAddresses";
DROP TABLE "CustomerAddresses";
ALTER TABLE "CustomerAddresses_New" RENAME TO "CustomerAddresses";
CREATE INDEX "IX_CustomerAddresses_CompanyId_CustomerId_IsActive" ON "CustomerAddresses" ("CompanyId", "CustomerId", "IsActive");
CREATE UNIQUE INDEX "UX_CustomerAddress_DefaultBilling" ON "CustomerAddresses" ("CompanyId", "CustomerId") WHERE IsDefaultBilling = 1;
CREATE UNIQUE INDEX "UX_CustomerAddress_DefaultShipping" ON "CustomerAddresses" ("CompanyId", "CustomerId", "IsDefaultShipping") WHERE IsDefaultShipping = 1;
CREATE TRIGGER "CustomerAddresses_NoDelete" BEFORE DELETE ON "CustomerAddresses" BEGIN SELECT RAISE(ABORT,'Customer records are retained; deactivate instead'); END;
CREATE TRIGGER "CustomerAddresses_OwnerImmutable" BEFORE UPDATE ON "CustomerAddresses" WHEN NEW.Id <> OLD.Id OR NEW.CompanyId <> OLD.CompanyId OR NEW.CustomerId <> OLD.CustomerId BEGIN SELECT RAISE(ABORT,'Customer ownership is immutable'); END;
