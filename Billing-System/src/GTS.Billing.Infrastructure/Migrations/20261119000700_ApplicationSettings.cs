using Microsoft.EntityFrameworkCore.Migrations;

namespace GTS.Billing.Infrastructure.Migrations;

public sealed partial class ApplicationSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE SettingOverrides (
                Id TEXT NOT NULL CONSTRAINT PK_SettingOverrides PRIMARY KEY,
                Scope TEXT NOT NULL,
                Key TEXT NOT NULL,
                CompanyId TEXT NULL,
                UserId TEXT NULL,
                Value TEXT NULL,
                UpdatedUtc TEXT NOT NULL,
                UpdatedByUserId TEXT NOT NULL,
                Version INTEGER NOT NULL,
                CONSTRAINT CK_Settings_Owner CHECK((Scope = 'Application' AND CompanyId IS NULL AND UserId IS NULL) OR (Scope = 'Company' AND CompanyId IS NOT NULL AND UserId IS NULL) OR (Scope = 'User' AND CompanyId IS NULL AND UserId IS NOT NULL)),
                CONSTRAINT CK_Settings_Value CHECK(length(Key) BETWEEN 1 AND 150 AND (Value IS NULL OR length(Value) <= 800)),
                FOREIGN KEY(CompanyId) REFERENCES Companies(Id) ON DELETE RESTRICT,
                FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE RESTRICT,
                FOREIGN KEY(UpdatedByUserId) REFERENCES Users(Id) ON DELETE RESTRICT
            );
            CREATE UNIQUE INDEX IX_SettingOverrides_Key ON SettingOverrides(Key) WHERE Scope = 'Application';
            CREATE UNIQUE INDEX IX_SettingOverrides_CompanyId_Key ON SettingOverrides(CompanyId,Key) WHERE Scope = 'Company';
            CREATE UNIQUE INDEX IX_SettingOverrides_UserId_Key ON SettingOverrides(UserId,Key) WHERE Scope = 'User';
            CREATE INDEX IX_SettingOverrides_UpdatedByUserId ON SettingOverrides(UpdatedByUserId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Stored settings are retained.");
}
