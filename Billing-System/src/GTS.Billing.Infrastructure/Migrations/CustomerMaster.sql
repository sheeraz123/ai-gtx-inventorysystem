CREATE TABLE "Customers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "LegalName" TEXT NOT NULL,
    "NormalizedName" TEXT NOT NULL,
    "TradeName" TEXT NULL,
    "NormalizedTradeName" TEXT NULL,
    "DisplayName" TEXT NULL,
    "CustomerType" TEXT NOT NULL,
    "GroupLabel" TEXT NULL,
    "CategoryLabel" TEXT NULL,
    "Status" TEXT NOT NULL,
    "SinceDate" TEXT NULL,
    "Mobile" TEXT NULL,
    "NormalizedMobile" TEXT NULL,
    "Email" TEXT NULL,
    "RegistrationType" TEXT NOT NULL,
    "Gstin" TEXT NULL,
    "Pan" TEXT NULL,
    "GstLegalName" TEXT NULL,
    "GstStateCode" TEXT NULL,
    "SupplyClassification" TEXT NULL,
    "ExemptionNotes" TEXT NULL,
    "GstEffectiveDate" TEXT NULL,
    "PaymentTerms" TEXT NULL,
    "CreditDays" INTEGER NOT NULL,
    "CreditLimit" INTEGER NOT NULL,
    "CreditHold" INTEGER NOT NULL,
    "PaymentMode" TEXT NULL,
    "DeliveryMethod" TEXT NOT NULL,
    "Currency" TEXT NOT NULL,
    "PurchaseOrderRequired" INTEGER NOT NULL,
    "BillingInstructions" TEXT NULL,
    "Notes" TEXT NULL,
    "InternalRemarks" TEXT NULL,
    "CreatedUtc" TEXT NOT NULL,
    "UpdatedUtc" TEXT NOT NULL,
    "CreatedByUserId" TEXT NOT NULL,
    "UpdatedByUserId" TEXT NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    CONSTRAINT "AK_Customers_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_Customer_Credit" CHECK (typeof(CreditLimit) = 'integer' AND CreditLimit BETWEEN 0 AND 999999999999999999 AND typeof(CreditDays) = 'integer' AND CreditDays BETWEEN 0 AND 36500),
    CONSTRAINT "CK_Customer_Currency" CHECK (length(Currency) = 3 AND Currency NOT GLOB '*[^A-Z]*'),
    CONSTRAINT "CK_Customer_Delivery" CHECK (DeliveryMethod IN ('Print','Email','Sms','Whatsapp','Other')),
    CONSTRAINT "CK_Customer_Flags" CHECK (CreditHold IN (0,1) AND PurchaseOrderRequired IN (0,1)),
    CONSTRAINT "CK_Customer_Identity" CHECK (length(Code) BETWEEN 1 AND 40 AND Code = upper(trim(Code)) AND Code NOT GLOB '*[^A-Z0-9/_-]*' AND substr(Code,1,1) GLOB '[A-Z0-9]' AND length(trim(LegalName)) BETWEEN 1 AND 200 AND length(NormalizedName) > 0),
    CONSTRAINT "CK_Customer_Registration" CHECK (RegistrationType IN ('Regular','Composition','Unregistered','Consumer','SezUnit','SezDeveloper','GovernmentSpecial')),
    CONSTRAINT "CK_Customer_Status" CHECK (Status IN ('Active','Inactive','Blocked')),
    CONSTRAINT "CK_Customer_Type" CHECK (CustomerType IN ('Individual','Business','Government','Other')),
    CONSTRAINT "FK_Customers_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Customers_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Customers_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CustomerAddresses" (
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
    CONSTRAINT "CK_CustomerAddress_IndianPostal" CHECK (Country <> 'IN' OR (length(PostalCode) = 6 AND PostalCode NOT GLOB '*[^0-9]*' AND substr(PostalCode,1,1) <> '0' AND StateCode IS NOT NULL AND length(StateCode) = 2)),
    CONSTRAINT "CK_CustomerAddress_Required" CHECK (length(trim(Label)) > 0 AND length(trim(RecipientName)) > 0 AND length(trim(Line1)) > 0 AND length(trim(City)) > 0 AND length(Country) = 2 AND Country NOT GLOB '*[^A-Z]*'),
    CONSTRAINT "CK_CustomerAddress_Type" CHECK (AddressType IN ('Billing','Shipping','Both')),
    CONSTRAINT "FK_CustomerAddresses_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CustomerAddresses_Customers_CompanyId_CustomerId" FOREIGN KEY ("CompanyId", "CustomerId") REFERENCES "Customers" ("CompanyId", "Id") ON DELETE RESTRICT
);

CREATE TABLE "CustomerContacts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CustomerContacts" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Designation" TEXT NULL,
    "Department" TEXT NULL,
    "Mobile" TEXT NULL,
    "NormalizedMobile" TEXT NULL,
    "AlternatePhone" TEXT NULL,
    "Email" TEXT NULL,
    "Whatsapp" TEXT NULL,
    "IsPrimary" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "Notes" TEXT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    "CustomerId" TEXT NOT NULL,
    CONSTRAINT "AK_CustomerContacts_CompanyId_CustomerId_Id" UNIQUE ("CompanyId", "CustomerId", "Id"),
    CONSTRAINT "AK_CustomerContacts_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_CustomerContact_Flags" CHECK (IsActive IN (0,1) AND IsPrimary IN (0,1)),
    CONSTRAINT "CK_CustomerContact_Name" CHECK (length(trim(Name)) > 0),
    CONSTRAINT "CK_CustomerContact_PrimaryActive" CHECK (IsPrimary = 0 OR IsActive = 1),
    CONSTRAINT "FK_CustomerContacts_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CustomerContacts_Customers_CompanyId_CustomerId" FOREIGN KEY ("CompanyId", "CustomerId") REFERENCES "Customers" ("CompanyId", "Id") ON DELETE RESTRICT
);

CREATE TABLE "CustomerBankDetails" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CustomerBankDetails" PRIMARY KEY,
    "BankName" TEXT NULL,
    "AccountHolder" TEXT NULL,
    "AccountNumber" TEXT NULL,
    "Ifsc" TEXT NULL,
    "Branch" TEXT NULL,
    "UpiId" TEXT NULL,
    "ReferenceNotes" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    "CustomerId" TEXT NOT NULL,
    CONSTRAINT "AK_CustomerBankDetails_CompanyId_CustomerId_Id" UNIQUE ("CompanyId", "CustomerId", "Id"),
    CONSTRAINT "AK_CustomerBankDetails_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_CustomerBank_Active" CHECK (IsActive IN (0,1)),
    CONSTRAINT "FK_CustomerBankDetails_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CustomerBankDetails_Customers_CompanyId_CustomerId" FOREIGN KEY ("CompanyId", "CustomerId") REFERENCES "Customers" ("CompanyId", "Id") ON DELETE RESTRICT
);

CREATE TABLE "CustomerOpeningBalanceDrafts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CustomerOpeningBalanceDrafts" PRIMARY KEY,
    "Amount" INTEGER NOT NULL,
    "Nature" TEXT NOT NULL,
    "AsOfDate" TEXT NULL,
    "Remarks" TEXT NULL,
    "Reference" TEXT NULL,
    "Version" INTEGER NOT NULL,
    "CompanyId" TEXT NOT NULL,
    "CustomerId" TEXT NOT NULL,
    CONSTRAINT "AK_CustomerOpeningBalanceDrafts_CompanyId_CustomerId_Id" UNIQUE ("CompanyId", "CustomerId", "Id"),
    CONSTRAINT "AK_CustomerOpeningBalanceDrafts_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
    CONSTRAINT "CK_CustomerOpening_Money" CHECK (typeof(Amount) = 'integer' AND Amount BETWEEN 0 AND 999999999999999999),
    CONSTRAINT "CK_CustomerOpening_Nature" CHECK ((Amount = 0 AND Nature = 'Zero') OR (Amount > 0 AND Nature IN ('Debit','Credit') AND AsOfDate IS NOT NULL)),
    CONSTRAINT "FK_CustomerOpeningBalanceDrafts_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CustomerOpeningBalanceDrafts_Customers_CompanyId_CustomerId" FOREIGN KEY ("CompanyId", "CustomerId") REFERENCES "Customers" ("CompanyId", "Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_CustomerAddresses_CompanyId_CustomerId_IsActive" ON "CustomerAddresses" ("CompanyId", "CustomerId", "IsActive");

CREATE UNIQUE INDEX "UX_CustomerAddress_DefaultBilling" ON "CustomerAddresses" ("CompanyId", "CustomerId") WHERE IsDefaultBilling = 1;

CREATE UNIQUE INDEX "UX_CustomerAddress_DefaultShipping" ON "CustomerAddresses" ("CompanyId", "CustomerId", "IsDefaultShipping") WHERE IsDefaultShipping = 1;

CREATE UNIQUE INDEX "IX_CustomerBankDetails_CompanyId_CustomerId" ON "CustomerBankDetails" ("CompanyId", "CustomerId");

CREATE INDEX "IX_CustomerContacts_CompanyId_CustomerId_NormalizedMobile" ON "CustomerContacts" ("CompanyId", "CustomerId", "NormalizedMobile");

CREATE UNIQUE INDEX "UX_CustomerContact_Primary" ON "CustomerContacts" ("CompanyId", "CustomerId") WHERE IsPrimary = 1;

CREATE UNIQUE INDEX "IX_CustomerOpeningBalanceDrafts_CompanyId_CustomerId" ON "CustomerOpeningBalanceDrafts" ("CompanyId", "CustomerId");

CREATE UNIQUE INDEX "IX_Customers_CompanyId_Code" ON "Customers" ("CompanyId", "Code");

CREATE INDEX "IX_Customers_CompanyId_Gstin" ON "Customers" ("CompanyId", "Gstin");

CREATE INDEX "IX_Customers_CompanyId_NormalizedMobile" ON "Customers" ("CompanyId", "NormalizedMobile");

CREATE INDEX "IX_Customers_CompanyId_NormalizedName" ON "Customers" ("CompanyId", "NormalizedName");

CREATE INDEX "IX_Customers_CompanyId_Pan" ON "Customers" ("CompanyId", "Pan");

CREATE INDEX "IX_Customers_CompanyId_Status_RegistrationType" ON "Customers" ("CompanyId", "Status", "RegistrationType");

CREATE INDEX "IX_Customers_CreatedByUserId" ON "Customers" ("CreatedByUserId");

CREATE INDEX "IX_Customers_UpdatedByUserId" ON "Customers" ("UpdatedByUserId");

CREATE TRIGGER "Customers_NoDelete" BEFORE DELETE ON "Customers" BEGIN SELECT RAISE(ABORT,'Customer records are retained; deactivate instead'); END;
CREATE TRIGGER "Customers_OwnerImmutable" BEFORE UPDATE ON "Customers" WHEN NEW.Id <> OLD.Id OR NEW.CompanyId <> OLD.CompanyId BEGIN SELECT RAISE(ABORT,'Customer ownership is immutable'); END;
CREATE TRIGGER "CustomerAddresses_NoDelete" BEFORE DELETE ON "CustomerAddresses" BEGIN SELECT RAISE(ABORT,'Customer records are retained; deactivate instead'); END;
CREATE TRIGGER "CustomerAddresses_OwnerImmutable" BEFORE UPDATE ON "CustomerAddresses" WHEN NEW.Id <> OLD.Id OR NEW.CompanyId <> OLD.CompanyId OR NEW.CustomerId <> OLD.CustomerId BEGIN SELECT RAISE(ABORT,'Customer ownership is immutable'); END;
CREATE TRIGGER "CustomerContacts_NoDelete" BEFORE DELETE ON "CustomerContacts" BEGIN SELECT RAISE(ABORT,'Customer records are retained; deactivate instead'); END;
CREATE TRIGGER "CustomerContacts_OwnerImmutable" BEFORE UPDATE ON "CustomerContacts" WHEN NEW.Id <> OLD.Id OR NEW.CompanyId <> OLD.CompanyId OR NEW.CustomerId <> OLD.CustomerId BEGIN SELECT RAISE(ABORT,'Customer ownership is immutable'); END;
CREATE TRIGGER "CustomerBankDetails_NoDelete" BEFORE DELETE ON "CustomerBankDetails" BEGIN SELECT RAISE(ABORT,'Customer records are retained; deactivate instead'); END;
CREATE TRIGGER "CustomerBankDetails_OwnerImmutable" BEFORE UPDATE ON "CustomerBankDetails" WHEN NEW.Id <> OLD.Id OR NEW.CompanyId <> OLD.CompanyId OR NEW.CustomerId <> OLD.CustomerId BEGIN SELECT RAISE(ABORT,'Customer ownership is immutable'); END;
CREATE TRIGGER "CustomerOpeningBalanceDrafts_NoDelete" BEFORE DELETE ON "CustomerOpeningBalanceDrafts" BEGIN SELECT RAISE(ABORT,'Customer records are retained; deactivate instead'); END;
CREATE TRIGGER "CustomerOpeningBalanceDrafts_OwnerImmutable" BEFORE UPDATE ON "CustomerOpeningBalanceDrafts" WHEN NEW.Id <> OLD.Id OR NEW.CompanyId <> OLD.CompanyId OR NEW.CustomerId <> OLD.CustomerId BEGIN SELECT RAISE(ABORT,'Customer ownership is immutable'); END;

