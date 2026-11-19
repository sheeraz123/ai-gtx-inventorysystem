ALTER TABLE Users ADD COLUMN FullName TEXT NULL;
ALTER TABLE Users ADD COLUMN DisplayName TEXT NULL;
ALTER TABLE Users ADD COLUMN Email TEXT NULL;
ALTER TABLE Users ADD COLUMN MobileNumber TEXT NULL;
ALTER TABLE Users ADD COLUMN EmployeeCode TEXT NULL;
ALTER TABLE Users ADD COLUMN Designation TEXT NULL;
ALTER TABLE Users ADD COLUMN Department TEXT NULL;
ALTER TABLE Users ADD COLUMN LastSuccessfulLoginUtc TEXT NULL;
ALTER TABLE Users ADD COLUMN LastPasswordChangedUtc TEXT NULL;
ALTER TABLE Users ADD COLUMN CreatedUtc TEXT NULL;
ALTER TABLE Users ADD COLUMN UpdatedUtc TEXT NULL;
ALTER TABLE Users ADD COLUMN IsLocked INTEGER NOT NULL DEFAULT 0 CHECK(IsLocked IN (0,1));
ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0 CHECK(MustChangePassword IN (0,1));
ALTER TABLE Users ADD COLUMN CreatedByUserId TEXT NULL REFERENCES Users(Id) ON DELETE RESTRICT;
ALTER TABLE Users ADD COLUMN UpdatedByUserId TEXT NULL REFERENCES Users(Id) ON DELETE RESTRICT;
CREATE INDEX IX_Users_CreatedByUserId ON Users(CreatedByUserId);
CREATE INDEX IX_Users_UpdatedByUserId ON Users(UpdatedByUserId);
CREATE INDEX IX_Users_EmployeeCode ON Users(EmployeeCode);

ALTER TABLE Roles ADD COLUMN Code TEXT COLLATE NOCASE NULL;
ALTER TABLE Roles ADD COLUMN Description TEXT NULL;
ALTER TABLE Roles ADD COLUMN CreatedUtc TEXT NULL;
ALTER TABLE Roles ADD COLUMN UpdatedUtc TEXT NULL;
ALTER TABLE Roles ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1 CHECK(IsActive IN (0,1));
ALTER TABLE Roles ADD COLUMN IsSystemRole INTEGER NOT NULL DEFAULT 0 CHECK(IsSystemRole IN (0,1));
ALTER TABLE Roles ADD COLUMN CreatedByUserId TEXT NULL REFERENCES Users(Id) ON DELETE RESTRICT;
ALTER TABLE Roles ADD COLUMN UpdatedByUserId TEXT NULL REFERENCES Users(Id) ON DELETE RESTRICT;
UPDATE Roles SET Code='LEGACY-' || upper(replace(Id,'-','')) WHERE Code IS NULL;
CREATE UNIQUE INDEX IX_Roles_Code ON Roles(Code);
CREATE INDEX IX_Roles_CreatedByUserId ON Roles(CreatedByUserId);
CREATE INDEX IX_Roles_UpdatedByUserId ON Roles(UpdatedByUserId);

CREATE TABLE UserCompanyAccess (
    UserId TEXT NOT NULL,
    CompanyId TEXT NOT NULL,
    IsActive INTEGER NOT NULL CHECK(IsActive IN (0,1)),
    CreatedUtc TEXT NOT NULL,
    CreatedByUserId TEXT NOT NULL,
    CONSTRAINT PK_UserCompanyAccess PRIMARY KEY (UserId,CompanyId),
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE RESTRICT,
    FOREIGN KEY (CompanyId) REFERENCES Companies(Id) ON DELETE RESTRICT,
    FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id) ON DELETE RESTRICT
);
CREATE INDEX IX_UserCompanyAccess_CompanyId ON UserCompanyAccess(CompanyId);
CREATE INDEX IX_UserCompanyAccess_CreatedByUserId ON UserCompanyAccess(CreatedByUserId);

CREATE TRIGGER Security_KeepUsers BEFORE DELETE ON Users
BEGIN SELECT RAISE(ABORT,'User accounts are retained'); END;
CREATE TRIGGER Security_KeepRoles BEFORE DELETE ON Roles
BEGIN SELECT RAISE(ABORT,'Roles are retained'); END;
CREATE TRIGGER Security_RoleCodeOnInsert BEFORE INSERT ON Roles
WHEN NEW.Code IS NULL OR length(NEW.Code) NOT BETWEEN 1 AND 40 OR NEW.Code GLOB '*[^A-Z0-9_-]*'
BEGIN SELECT RAISE(ABORT,'Invalid role code'); END;
CREATE TRIGGER Security_RoleCodeImmutable BEFORE UPDATE OF Code ON Roles
WHEN NEW.Code IS NOT OLD.Code COLLATE BINARY
BEGIN SELECT RAISE(ABORT,'Role codes are immutable'); END;
CREATE TRIGGER Security_KeepUserIdentity BEFORE UPDATE OF Id, Username, NormalizedUsername ON Users
WHEN NEW.Id IS NOT OLD.Id OR NEW.Username IS NOT OLD.Username OR NEW.NormalizedUsername IS NOT OLD.NormalizedUsername
BEGIN SELECT RAISE(ABORT,'User identity is immutable'); END;
CREATE TRIGGER Security_ProtectSuperAdmin BEFORE UPDATE OF IsActive, IsLocked, MustChangePassword ON Users
WHEN (NEW.IsActive<>1 OR NEW.IsLocked<>0 OR NEW.MustChangePassword<>0)
 AND EXISTS(SELECT 1 FROM SuperAdminAssignments WHERE UserId=OLD.Id)
BEGIN SELECT RAISE(ABORT,'Super Admin status is protected'); END;
CREATE TRIGGER Security_KeepSuperAdmin BEFORE DELETE ON SuperAdminAssignments
BEGIN SELECT RAISE(ABORT,'The Super Admin assignment is permanent'); END;
CREATE TRIGGER Security_KeepSuperAdminIdentity BEFORE UPDATE ON SuperAdminAssignments
BEGIN SELECT RAISE(ABORT,'The Super Admin assignment is immutable'); END;
