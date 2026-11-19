using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class FinancialYearNumbering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // All DDL and EF's history insert must share the same transaction.
        // A separately approved maintenance connection must disable its own foreign-key enforcement
        // before the transaction and restore it afterward. The normal startup path is not authorized.
        migrationBuilder.Sql("""
            CREATE TEMP TABLE Stage4I_Guard (Pass INTEGER NOT NULL CHECK(Pass=1));
            INSERT INTO Stage4I_Guard SELECT CASE WHEN foreign_keys=0 THEN 1 ELSE 0 END FROM pragma_foreign_keys;
            INSERT INTO Stage4I_Guard SELECT CASE WHEN EXISTS(SELECT 1 FROM pragma_foreign_key_check) THEN 0 ELSE 1 END;
            CREATE TEMP TABLE Stage4I_SeriesYear AS
            SELECT CompanyId, SeriesId, FinancialYearId FROM NumberCounters
            UNION
            SELECT CompanyId, SeriesId, FinancialYearId FROM DocumentNumberLedger;
            INSERT INTO Stage4I_Guard
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM DocumentNumberingSeries s
                WHERE (SELECT count(*) FROM Stage4I_SeriesYear m WHERE m.CompanyId=s.CompanyId AND m.SeriesId=s.Id)<>1
            ) THEN 0 ELSE 1 END;
            INSERT INTO Stage4I_Guard
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM FinancialYears a JOIN FinancialYears b ON a.CompanyId=b.CompanyId AND a.Id<b.Id
                WHERE a.Start<=b.End AND b.Start<=a.End
            ) THEN 0 ELSE 1 END;

            CREATE TABLE "Stage4I_FinancialYears" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_FinancialYears" PRIMARY KEY,
                "Start" TEXT NOT NULL,
                "End" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "IsCurrent" INTEGER NOT NULL,
                "ClosedUtc" TEXT NULL,
                "ClosedBy" TEXT NULL,
                "ReopenedUtc" TEXT NULL,
                "ReopenedBy" TEXT NULL,
                "ReopenReason" TEXT NULL,
                "CreatedUtc" TEXT NULL,
                "UpdatedUtc" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                CONSTRAINT "AK_FinancialYears_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
                CONSTRAINT "CK_FY_Boundaries" CHECK (date(Start) IS NOT NULL AND date(End) IS NOT NULL AND End > Start),
                CONSTRAINT "CK_FY_Current" CHECK (IsCurrent IN (0,1) AND (IsCurrent=0 OR Status='Open')),
                CONSTRAINT "CK_FY_Status" CHECK (Status IN ('Open','Closed')),
                CONSTRAINT "FK_FinancialYears_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_FinancialYears_Users_ClosedBy" FOREIGN KEY ("ClosedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_FinancialYears_Users_CreatedBy" FOREIGN KEY ("CreatedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_FinancialYears_Users_ReopenedBy" FOREIGN KEY ("ReopenedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_FinancialYears_Users_UpdatedBy" FOREIGN KEY ("UpdatedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );

            CREATE TABLE "Stage4I_DocumentNumberingSeries" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_DocumentNumberingSeries" PRIMARY KEY,
                "GstRegistrationId" TEXT NOT NULL,
                "DocumentType" TEXT NOT NULL,
                "Prefix" TEXT NOT NULL,
                "StartingNumber" INTEGER NOT NULL,
                "Padding" INTEGER NOT NULL,
                "FinancialYearId" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Separator" TEXT NOT NULL,
                "Suffix" TEXT NOT NULL,
                "IsDefault" INTEGER NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "CreatedUtc" TEXT NULL,
                "UpdatedUtc" TEXT NULL,
                "CreatedBy" TEXT NULL,
                "UpdatedBy" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CompanyId" TEXT NOT NULL,
                CONSTRAINT "AK_DocumentNumberingSeries_CompanyId_Id" UNIQUE ("CompanyId", "Id"),
                CONSTRAINT "AK_DocumentNumberingSeries_CompanyId_Id_GstRegistrationId" UNIQUE ("CompanyId", "Id", "GstRegistrationId"),
                CONSTRAINT "CK_Series_Default" CHECK (IsDefault IN (0,1) AND IsActive IN (0,1) AND (IsDefault=0 OR IsActive=1)),
                CONSTRAINT "CK_Series_Range" CHECK (StartingNumber > 0 AND Padding BETWEEN 1 AND 9 AND length(Prefix)<=80 AND length(Suffix)<=80 AND Separator IN ('','-','/')),
                CONSTRAINT "FK_DocumentNumberingSeries_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_DocumentNumberingSeries_FinancialYears_CompanyId_FinancialYearId" FOREIGN KEY ("CompanyId", "FinancialYearId") REFERENCES "FinancialYears" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_DocumentNumberingSeries_GstRegistrations_CompanyId_GstRegistrationId" FOREIGN KEY ("CompanyId", "GstRegistrationId") REFERENCES "GstRegistrations" ("CompanyId", "Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_DocumentNumberingSeries_Users_CreatedBy" FOREIGN KEY ("CreatedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_DocumentNumberingSeries_Users_UpdatedBy" FOREIGN KEY ("UpdatedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );

            INSERT INTO Stage4I_FinancialYears (Id, CompanyId, Version, Start, End, Status, Code, Name, IsCurrent)
            SELECT Id, CompanyId, Version, Start, End, Status,
                   'FY-'||substr(Start,1,4)||'-'||substr(End,3,2),
                   'Financial year '||substr(Start,1,4)||'–'||substr(End,3,2), 0
            FROM FinancialYears;

            INSERT INTO Stage4I_DocumentNumberingSeries
                (Id, CompanyId, Version, GstRegistrationId, DocumentType, Prefix, StartingNumber, Padding,
                 FinancialYearId, Code, Name, Separator, Suffix, IsDefault, IsActive)
            SELECT s.Id, s.CompanyId, s.Version, s.GstRegistrationId, s.DocumentType, s.Prefix, s.StartingNumber, s.Padding,
                   m.FinancialYearId, 'LEG-'||upper(replace(s.Id,'-','')), 'Legacy '||s.DocumentType, '', '', 0, 1
            FROM DocumentNumberingSeries s JOIN Stage4I_SeriesYear m ON m.CompanyId=s.CompanyId AND m.SeriesId=s.Id;

            INSERT INTO Stage4I_Guard SELECT CASE WHEN
              (SELECT count(*) FROM FinancialYears)=(SELECT count(*) FROM Stage4I_FinancialYears)
              AND NOT EXISTS(SELECT Id,CompanyId,Version,Start,End,Status FROM FinancialYears
                             EXCEPT SELECT Id,CompanyId,Version,Start,End,Status FROM Stage4I_FinancialYears)
              AND (SELECT count(*) FROM DocumentNumberingSeries)=(SELECT count(*) FROM Stage4I_DocumentNumberingSeries)
              AND NOT EXISTS(SELECT Id,CompanyId,Version,GstRegistrationId,DocumentType,Prefix,StartingNumber,Padding FROM DocumentNumberingSeries
                             EXCEPT SELECT Id,CompanyId,Version,GstRegistrationId,DocumentType,Prefix,StartingNumber,Padding FROM Stage4I_DocumentNumberingSeries)
              THEN 1 ELSE 0 END;

            DROP TABLE DocumentNumberingSeries;
            DROP TABLE FinancialYears;
            ALTER TABLE Stage4I_FinancialYears RENAME TO FinancialYears;
            ALTER TABLE Stage4I_DocumentNumberingSeries RENAME TO DocumentNumberingSeries;

            CREATE UNIQUE INDEX "IX_DocumentNumberingSeries_CompanyId_FinancialYearId_GstRegistrationId_DocumentType" ON "DocumentNumberingSeries" ("CompanyId", "FinancialYearId", "GstRegistrationId", "DocumentType") WHERE IsDefault=1;

            CREATE UNIQUE INDEX "IX_DocumentNumberingSeries_CompanyId_FinancialYearId_GstRegistrationId_DocumentType_Code" ON "DocumentNumberingSeries" ("CompanyId", "FinancialYearId", "GstRegistrationId", "DocumentType", "Code");

            CREATE INDEX "IX_DocumentNumberingSeries_CompanyId_GstRegistrationId" ON "DocumentNumberingSeries" ("CompanyId", "GstRegistrationId");

            CREATE INDEX "IX_DocumentNumberingSeries_CreatedBy" ON "DocumentNumberingSeries" ("CreatedBy");

            CREATE INDEX "IX_DocumentNumberingSeries_UpdatedBy" ON "DocumentNumberingSeries" ("UpdatedBy");

            CREATE UNIQUE INDEX "CurrentFinancialYear" ON "FinancialYears" ("CompanyId") WHERE IsCurrent=1;

            CREATE INDEX "IX_FinancialYears_ClosedBy" ON "FinancialYears" ("ClosedBy");

            CREATE UNIQUE INDEX "IX_FinancialYears_CompanyId_Code" ON "FinancialYears" ("CompanyId", "Code");

            CREATE UNIQUE INDEX "IX_FinancialYears_CompanyId_Start" ON "FinancialYears" ("CompanyId", "Start");

            CREATE INDEX "IX_FinancialYears_CreatedBy" ON "FinancialYears" ("CreatedBy");

            CREATE INDEX "IX_FinancialYears_ReopenedBy" ON "FinancialYears" ("ReopenedBy");

            CREATE INDEX "IX_FinancialYears_UpdatedBy" ON "FinancialYears" ("UpdatedBy");

            CREATE UNIQUE INDEX UX_NumberLedger_Rendered_NoCase
            ON DocumentNumberLedger(CompanyId, GstRegistrationId, FinancialYearId, CompleteNumber COLLATE NOCASE);

            CREATE TRIGGER FinancialYear_NoOverlap_Insert BEFORE INSERT ON FinancialYears
            WHEN EXISTS(SELECT 1 FROM FinancialYears y WHERE y.CompanyId=NEW.CompanyId AND NEW.Start<=y.End AND y.Start<=NEW.End)
            BEGIN SELECT RAISE(ABORT,'Financial year dates overlap'); END;

            CREATE TRIGGER FinancialYear_NoOverlap_Update BEFORE UPDATE OF CompanyId,Start,End ON FinancialYears
            WHEN EXISTS(SELECT 1 FROM FinancialYears y WHERE y.CompanyId=NEW.CompanyId AND y.Id<>OLD.Id AND NEW.Start<=y.End AND y.Start<=NEW.End)
            BEGIN SELECT RAISE(ABORT,'Financial year dates overlap'); END;

            CREATE TRIGGER FinancialYear_ReferencedDates BEFORE UPDATE OF Start,End ON FinancialYears
            WHEN (NEW.Start<>OLD.Start OR NEW.End<>OLD.End) AND (OLD.Status='Closed'
                OR EXISTS(SELECT 1 FROM DocumentNumberingSeries s WHERE s.CompanyId=OLD.CompanyId AND s.FinancialYearId=OLD.Id)
                OR EXISTS(SELECT 1 FROM NumberCounters c WHERE c.CompanyId=OLD.CompanyId AND c.FinancialYearId=OLD.Id)
                OR EXISTS(SELECT 1 FROM DocumentNumberLedger l WHERE l.CompanyId=OLD.CompanyId AND l.FinancialYearId=OLD.Id))
            BEGIN SELECT RAISE(ABORT,'Referenced financial year dates are fixed'); END;

            CREATE TRIGGER DocumentSeries_FixedIdentity BEFORE UPDATE ON DocumentNumberingSeries
            WHEN NEW.Id<>OLD.Id OR NEW.CompanyId<>OLD.CompanyId OR NEW.FinancialYearId<>OLD.FinancialYearId
                 OR NEW.GstRegistrationId<>OLD.GstRegistrationId OR NEW.DocumentType<>OLD.DocumentType OR NEW.Code<>OLD.Code
            BEGIN SELECT RAISE(ABORT,'Document series identity is fixed'); END;

            CREATE TRIGGER DocumentSeries_FixedUsedFormat BEFORE UPDATE ON DocumentNumberingSeries
            WHEN (NEW.Prefix<>OLD.Prefix OR NEW.Suffix<>OLD.Suffix OR NEW.Separator<>OLD.Separator
                  OR NEW.Padding<>OLD.Padding OR NEW.StartingNumber<>OLD.StartingNumber) AND
                 (EXISTS(SELECT 1 FROM NumberCounters c WHERE c.CompanyId=OLD.CompanyId AND c.SeriesId=OLD.Id)
                  OR EXISTS(SELECT 1 FROM DocumentNumberLedger l WHERE l.CompanyId=OLD.CompanyId AND l.SeriesId=OLD.Id))
            BEGIN SELECT RAISE(ABORT,'Used document series format is fixed'); END;

            CREATE TRIGGER NumberCounter_SeriesYear_Insert BEFORE INSERT ON NumberCounters
            WHEN NOT EXISTS(SELECT 1 FROM DocumentNumberingSeries s JOIN FinancialYears y
                ON y.CompanyId=s.CompanyId AND y.Id=s.FinancialYearId WHERE s.Id=NEW.SeriesId AND s.CompanyId=NEW.CompanyId
                AND s.FinancialYearId=NEW.FinancialYearId AND y.Status='Open' AND s.IsActive=1)
            BEGIN SELECT RAISE(ABORT,'Counter requires an active series and open year'); END;

            CREATE TRIGGER NumberCounter_Monotonic BEFORE UPDATE ON NumberCounters
            WHEN NEW.SeriesId<>OLD.SeriesId OR NEW.FinancialYearId<>OLD.FinancialYearId OR NEW.CompanyId<>OLD.CompanyId
                 OR NEW.NextNumber<OLD.NextNumber OR
                 (NEW.NextNumber<>OLD.NextNumber AND NOT EXISTS(SELECT 1 FROM DocumentNumberingSeries s JOIN FinancialYears y
                  ON y.CompanyId=s.CompanyId AND y.Id=s.FinancialYearId WHERE s.Id=NEW.SeriesId AND s.CompanyId=NEW.CompanyId
                  AND s.FinancialYearId=NEW.FinancialYearId AND y.Status='Open' AND s.IsActive=1))
            BEGIN SELECT RAISE(ABORT,'Counter cannot be reset or advanced outside an open series'); END;

            CREATE TRIGGER NumberCounter_NoDelete BEFORE DELETE ON NumberCounters
            BEGIN SELECT RAISE(ABORT,'Number counters are retained'); END;

            CREATE TRIGGER NumberLedger_OpenSeries BEFORE INSERT ON DocumentNumberLedger
            WHEN NOT EXISTS(SELECT 1 FROM DocumentNumberingSeries s JOIN FinancialYears y
                ON y.CompanyId=s.CompanyId AND y.Id=s.FinancialYearId WHERE s.Id=NEW.SeriesId AND s.CompanyId=NEW.CompanyId
                AND s.FinancialYearId=NEW.FinancialYearId AND s.GstRegistrationId=NEW.GstRegistrationId AND y.Status='Open' AND s.IsActive=1)
            BEGIN SELECT RAISE(ABORT,'Number requires an active series and open year'); END;

            INSERT INTO Modules (Code,Name) VALUES ('financialyear','Financial Year Master'),('documentseries','Document Series Master')
            ON CONFLICT(Code) DO NOTHING;

            INSERT INTO Stage4I_Guard SELECT CASE WHEN EXISTS(SELECT 1 FROM pragma_foreign_key_check) THEN 0 ELSE 1 END;
            INSERT INTO Stage4I_Guard SELECT CASE WHEN (SELECT count(*) FROM pragma_integrity_check)=1
                AND (SELECT integrity_check FROM pragma_integrity_check LIMIT 1)='ok' THEN 1 ELSE 0 END;
            DROP TABLE Stage4I_SeriesYear;
            DROP TABLE Stage4I_Guard;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Financial year and series history must be preserved.");
}
