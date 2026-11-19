using System.Text.Json;
using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

public sealed class SecurityAdministrationStore(DevelopmentDatabase database,IPasswordHasher passwords,TimeProvider clock) : ISecurityAdministrationStore
{
    public async Task<SecurityResult> ExecuteAsync(SecurityRequest request,AuthenticatedIdentity actor,PasswordInput? password,CancellationToken ct)
    {
        try
        {
            await using var db=database.OpenExisting();
            await using var transaction=await db.BeginTransactionAsync(ct);
            var validity=await ScopedAccess.VerifyAsync(db,database.ExpectedInstallationId(),actor,ct);
            if (validity!=CompanyOutcome.Success) return new(validity);
            var access=await ScopedAccess.SnapshotAsync(db,actor.UserId,ct);
            if (request.Action==SecurityAction.Access) return new(CompanyOutcome.Success,Access:access);
            var allowed=request.Action switch
            {
                SecurityAction.Users or SecurityAction.User => access.Allows("User.View"),
                SecurityAction.Roles or SecurityAction.Role => access.Allows("Role.View"),
                SecurityAction.Audit => access.Allows("SecurityAudit.View"),
                _ => access.IsSuperAdmin
            };
            if (!allowed)
            {
                Audit(db,actor.UserId,"SecurityAccessDenied","Security",request.Id,access.CurrentCompanyId,"Denied",
                    new { Operation=request.Action.ToString() });
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new(CompanyOutcome.Forbidden);
            }

            switch (request.Action)
            {
                case SecurityAction.Options:
                    return new(CompanyOutcome.Success,Options:new(
                        await db.Set<Company>().OrderBy(x=>x.Code).Select(x=>new SecurityOption(x.Id,x.Code+" — "+x.LegalName,x.IsActive)).ToListAsync(ct),
                        await db.Set<Role>().OrderBy(x=>x.Name).Select(x=>new SecurityOption(x.Id,x.Name,x.IsActive)).ToListAsync(ct)));
                case SecurityAction.Users:
                case SecurityAction.User:
                    return await ReadUsers(db,request,access,ct);
                case SecurityAction.Roles:
                case SecurityAction.Role:
                    return await ReadRoles(db,request,access,ct);
                case SecurityAction.Audit:
                    return await ReadAudit(db,request,access,ct);
            }

            var result=request.Action switch
            {
                SecurityAction.SaveUser => await SaveUser(db,request,actor.UserId,password,ct),
                SecurityAction.ResetPassword => await ResetPassword(db,request,actor.UserId,password,ct),
                SecurityAction.SaveRole => await SaveRole(db,request,actor.UserId,ct),
                _ => new SecurityResult(CompanyOutcome.InvalidInput)
            };
            if (result.Outcome==CompanyOutcome.Success)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return result;
        }
        catch (DbUpdateConcurrencyException) { return new(CompanyOutcome.Conflict); }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode:19 }) { return new(CompanyOutcome.Conflict); }
        catch (Exception e) when (e is SqliteException or DbUpdateException or InvalidOperationException or IOException or OverflowException)
        { return new(CompanyOutcome.Unavailable); }
    }

    private async Task<SecurityResult> ReadUsers(BillingDbContext db,SecurityRequest request,AccessSnapshot access,CancellationToken ct)
    {
        var query=db.Set<User>().AsNoTracking();
        var scope=access.IsSuperAdmin ? request.CompanyFilter : access.CurrentCompanyId;
        if (scope is Guid company)
            query=query.Where(u=>db.Set<UserCompanyAccess>().Any(x=>x.UserId==u.Id && x.CompanyId==company && x.IsActive));
        var adminId=await db.Set<SuperAdminAssignment>().Select(x=>(Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (!access.IsSuperAdmin) query=query.Where(x=>x.Id!=adminId);
        if (request.Action==SecurityAction.User) query=query.Where(u=>u.Id==request.Id);
        else
        {
            if (request.Active is bool active) query=query.Where(x=>x.IsActive==active);
            if (request.Search?.Trim().ToUpperInvariant() is { Length:>=3 } term)
                query=query.Where(u=>u.NormalizedUsername.Contains(term) || u.FullName!=null && u.FullName.ToUpper().Contains(term) ||
                    u.EmployeeCode!=null && u.EmployeeCode.ToUpper().Contains(term) || u.Email!=null && u.Email.ToUpper().Contains(term) ||
                    u.MobileNumber!=null && u.MobileNumber.Contains(term));
            query=query.OrderBy(x=>x.NormalizedUsername).Skip(request.Page*request.PageSize);
        }
        // Never materialize credentials for list or detail reads.
        var profiles=await query.Take(request.PageSize+1).Select(u=>new
        {
            u.Id,u.Version,u.Username,u.FullName,u.DisplayName,u.Email,u.MobileNumber,u.EmployeeCode,u.Designation,u.Department,
            u.IsActive,u.IsLocked,u.MustChangePassword,u.LastSuccessfulLoginUtc,u.LastPasswordChangedUtc,u.CreatedUtc,u.UpdatedUtc,
            u.CreatedByUserId,u.UpdatedByUserId
        }).ToListAsync(ct);
        var ids=profiles.Take(request.PageSize).Select(x=>x.Id).ToArray();
        var grants=await (from x in db.Set<UserCompanyAccess>()
            join c in db.Set<Company>() on x.CompanyId equals c.Id
            where ids.Contains(x.UserId) && x.IsActive && (access.IsSuperAdmin || c.Id==scope)
            orderby c.Code
            select new { x.UserId,x.CompanyId,c.Code,c.LegalName }).ToListAsync(ct);
        var roles=await (from x in db.Set<UserRole>()
            join r in db.Set<Role>() on x.RoleId equals r.Id
            join c in db.Set<Company>() on x.CompanyId equals c.Id
            where ids.Contains(x.UserId) && (access.IsSuperAdmin || c.Id==scope) &&
                db.Set<UserCompanyAccess>().Any(a=>a.UserId==x.UserId && a.CompanyId==x.CompanyId && a.IsActive)
            orderby c.Code,r.Name
            select new { x.UserId,x.CompanyId,x.RoleId,r.Name,c.Code,r.IsActive }).ToListAsync(ct);
        var summaries=profiles.Take(request.PageSize).Select(u=>new UserSummary(u.Id,u.Version,u.Username,u.FullName ?? u.Username,u.EmployeeCode,
            u.Id==adminId ? "Super Admin (protected)" : string.Join(", ",roles.Where(x=>x.UserId==u.Id)
                .Select(x=>x.Name+" ("+x.Code+(x.IsActive ? ")" : ", inactive)"))),
            u.Id==adminId ? "All companies" : string.Join(", ",grants.Where(x=>x.UserId==u.Id).Select(x=>x.Code)),
            u.IsActive,u.IsLocked,u.Id==adminId,u.MustChangePassword,u.LastSuccessfulLoginUtc)).ToArray();
        if (request.Action==SecurityAction.Users) return new(CompanyOutcome.Success,Users:summaries,More:profiles.Count>request.PageSize);
        if (summaries.Length==0) return new(CompanyOutcome.NotFound);
        var p=profiles[0];
        var draft=new UserDraft(p.Username,p.FullName ?? p.Username,p.DisplayName,p.Email,p.MobileNumber,p.EmployeeCode,p.Designation,
            p.Department,p.IsActive,p.IsLocked,p.MustChangePassword,grants.Select(x=>x.CompanyId).ToArray(),
            roles.Select(x=>new RoleGrant(x.RoleId,x.CompanyId)).ToArray());
        var authors=await Authors(db,p.CreatedByUserId,p.UpdatedByUserId,ct);
        var installationId=database.ExpectedInstallationId().ToString();
        var since=clock.GetUtcNow().UtcDateTime.AddSeconds(-30);
        var throttled=await db.Set<AuditEvent>().Where(x=>x.RecordType=="Installation" && x.RecordId==installationId &&
            x.Action=="LoginFailed" && x.CompanyId==null && x.OccurredUtc>=since).Take(5).CountAsync(ct)>=5;
        return new(CompanyOutcome.Success,User:new(summaries[0],draft,p.LastPasswordChangedUtc,p.CreatedUtc,p.UpdatedUtc,
            authors.Created,authors.Updated,throttled));
    }

    private static async Task<SecurityResult> ReadRoles(BillingDbContext db,SecurityRequest request,AccessSnapshot access,CancellationToken ct)
    {
        var query=db.Set<Role>().AsNoTracking();
        var scope=access.CurrentCompanyId;
        var scopedAssignments=db.Set<UserRole>().Where(x=>access.IsSuperAdmin ||
            x.CompanyId==scope && db.Set<UserCompanyAccess>().Any(a=>a.UserId==x.UserId && a.CompanyId==x.CompanyId && a.IsActive));
        if (!access.IsSuperAdmin)
            query=query.Where(r=>scopedAssignments.Any(x=>x.RoleId==r.Id));
        if (request.Action==SecurityAction.Role) query=query.Where(x=>x.Id==request.Id);
        else
        {
            if (request.Active is bool active) query=query.Where(x=>x.IsActive==active);
            if (request.Search?.Trim().ToUpperInvariant() is { Length:>=3 } term)
                query=query.Where(x=>x.Code!=null && x.Code.Contains(term) || x.Name.ToUpper().Contains(term) ||
                    x.Description!=null && x.Description.ToUpper().Contains(term));
            query=query.OrderBy(x=>x.Code).ThenBy(x=>x.Name).Skip(request.Page*request.PageSize);
        }
        var rows=await query.Take(request.PageSize+1).Select(r=>new RoleSummary(r.Id,r.Version,r.Code ?? "",r.Name,r.Description,r.IsActive,r.IsSystemRole,
            scopedAssignments.Where(x=>x.RoleId==r.Id)
                .Select(x=>x.UserId).Distinct().Count())).ToListAsync(ct);
        if (request.Action==SecurityAction.Roles) return new(CompanyOutcome.Success,Roles:rows.Take(request.PageSize).ToArray(),More:rows.Count>request.PageSize);
        if (rows.Count==0) return new(CompanyOutcome.NotFound);
        var row=rows[0];
        var role=await db.Set<Role>().SingleAsync(x=>x.Id==row.Id,ct);
        var permissions=await db.Set<RolePermission>().Where(x=>x.RoleId==row.Id).Select(x=>new { x.ModuleCode,x.Action }).ToListAsync(ct);
        var codes=PermissionCatalog.All.Where(x=>permissions.Any(p=>p.ModuleCode==x.StoredModule && p.Action==x.StoredAction)).Select(x=>x.Code).ToArray();
        var members=await (from x in scopedAssignments
            join u in db.Set<User>() on x.UserId equals u.Id
            join c in db.Set<Company>() on x.CompanyId equals c.Id
            where x.RoleId==row.Id && (access.IsSuperAdmin || x.CompanyId==scope)
            orderby c.Code,u.NormalizedUsername
            select new RoleMember(u.Username,c.Code)).ToListAsync(ct);
        var authors=await Authors(db,role.CreatedByUserId,role.UpdatedByUserId,ct);
        return new(CompanyOutcome.Success,Role:new(row,new(row.Code,row.Name,row.Description,row.IsActive,codes),members,
            role.CreatedUtc,role.UpdatedUtc,authors.Created,authors.Updated));
    }

    private async Task<SecurityResult> SaveUser(BillingDbContext db,SecurityRequest request,Guid actor,PasswordInput? password,CancellationToken ct)
    {
        var draft=request.User!;
        var creating=request.Id is null;
        var user=creating ? new User() : await db.Set<User>().SingleOrDefaultAsync(x=>x.Id==request.Id,ct);
        if (user is null) return new(CompanyOutcome.NotFound);
        if (!creating)
        {
            if (await ScopedAccess.IsSuperAdminAsync(db,user.Id,ct)) return new(CompanyOutcome.Forbidden,"The Super Admin account is protected.");
            if (user.Version!=request.Version) return new(CompanyOutcome.Conflict);
            if (!string.Equals(user.Username,draft.Username.Trim(),StringComparison.Ordinal))
                return Invalid("The username cannot be changed.");
        }
        else
        {
            var normalized=draft.Username.Trim().ToUpperInvariant();
            if (await db.Set<User>().AnyAsync(x=>x.NormalizedUsername==normalized,ct)) return Invalid("That username is already in use.");
            if (!IsValidPassword(draft.Username,password)) return BadPassword();
            user.Username=draft.Username.Trim();
            user.NormalizedUsername=normalized;
            user.PasswordHash=passwords.Hash(password!.Password);
            user.CreatedUtc=user.LastPasswordChangedUtc=clock.GetUtcNow().UtcDateTime;
            user.CreatedByUserId=actor;
            db.Add(user);
        }

        var desiredCompanies=(draft.Companies ?? []).ToHashSet();
        var desiredRoles=(draft.Roles ?? []).ToHashSet();
        if (desiredRoles.Any(r=>!desiredCompanies.Contains(r.CompanyId))) return Invalid("Grant company access before assigning a role there.");
        var grants=await db.Set<UserCompanyAccess>().Where(x=>x.UserId==user.Id).ToListAsync(ct);
        var assignments=await db.Set<UserRole>().Where(x=>x.UserId==user.Id).ToListAsync(ct);
        var companyAdds=desiredCompanies.Where(id=>!grants.Any(x=>x.CompanyId==id && x.IsActive)).ToArray();
        if (await db.Set<Company>().CountAsync(x=>companyAdds.Contains(x.Id) && x.IsActive,ct)!=companyAdds.Length)
            return Invalid("New company access requires an active company.");
        var distinctNewRoleIds=desiredRoles.Where(r=>!assignments.Any(x=>x.RoleId==r.RoleId && x.CompanyId==r.CompanyId))
            .Select(x=>x.RoleId).Distinct().ToArray();
        if (await db.Set<Role>().CountAsync(x=>distinctNewRoleIds.Contains(x.Id) && x.IsActive,ct)!=distinctNewRoleIds.Length)
            return Invalid("New assignments require an active role.");

        if (!creating && user.IsLocked!=draft.IsLocked)
            Audit(db,actor,draft.IsLocked?"UserLocked":"UserUnlocked","User",user.Id);
        if (!creating && user.IsActive!=draft.IsActive)
            Audit(db,actor,draft.IsActive?"UserActivated":"UserDeactivated","User",user.Id);
        if (!creating && !user.MustChangePassword && draft.RequireChange)
            Audit(db,actor,"UserPasswordChangeRequired","User",user.Id);
        user.FullName=draft.FullName.Trim();
        user.DisplayName=Clean(draft.DisplayName);
        user.Email=Clean(draft.Email);
        user.MobileNumber=Clean(draft.MobileNumber);
        user.EmployeeCode=Clean(draft.EmployeeCode);
        user.Designation=Clean(draft.Designation);
        user.Department=Clean(draft.Department);
        user.IsActive=draft.IsActive;
        user.IsLocked=draft.IsLocked;
        // Only a verified own-password change clears an outstanding requirement.
        user.MustChangePassword=creating || user.MustChangePassword || draft.RequireChange;
        user.UpdatedUtc=clock.GetUtcNow().UtcDateTime;
        user.UpdatedByUserId=actor;
        if (!creating) db.Entry(user).Property(x=>x.UpdatedUtc).IsModified=true;
        Audit(db,actor,creating?"UserCreated":"UserUpdated","User",user.Id);

        foreach (var id in companyAdds)
        {
            var grant=grants.SingleOrDefault(x=>x.CompanyId==id);
            if (grant is null)
                db.Add(new UserCompanyAccess { UserId=user.Id,CompanyId=id,IsActive=true,CreatedUtc=clock.GetUtcNow().UtcDateTime,CreatedByUserId=actor });
            else grant.IsActive=true;
            Audit(db,actor,"UserCompanyAccessGranted","User",user.Id,id);
        }
        foreach (var grant in grants.Where(x=>x.IsActive && !desiredCompanies.Contains(x.CompanyId)))
        {
            grant.IsActive=false;
            Audit(db,actor,"UserCompanyAccessRevoked","User",user.Id,grant.CompanyId);
        }
        foreach (var assignment in assignments.Where(x=>!desiredRoles.Contains(new(x.RoleId,x.CompanyId))))
        {
            db.Remove(assignment);
            Audit(db,actor,"UserRoleRemoved","User",user.Id,assignment.CompanyId,details:new { assignment.RoleId });
        }
        foreach (var grant in desiredRoles.Where(r=>!assignments.Any(x=>x.RoleId==r.RoleId && x.CompanyId==r.CompanyId)))
        {
            db.Add(new UserRole { UserId=user.Id,CompanyId=grant.CompanyId,RoleId=grant.RoleId });
            Audit(db,actor,"UserRoleAssigned","User",user.Id,grant.CompanyId,details:new { grant.RoleId });
        }
        return new(CompanyOutcome.Success);
    }

    private async Task<SecurityResult> ResetPassword(BillingDbContext db,SecurityRequest request,Guid actor,PasswordInput? password,CancellationToken ct)
    {
        var user=await db.Set<User>().SingleOrDefaultAsync(x=>x.Id==request.Id,ct);
        if (user is null) return new(CompanyOutcome.NotFound);
        if (await ScopedAccess.IsSuperAdminAsync(db,user.Id,ct)) return new(CompanyOutcome.Forbidden,"Use the protected Super Admin recovery or password-change flow.");
        if (user.Version!=request.Version) return new(CompanyOutcome.Conflict);
        if (!IsValidPassword(user.Username,password)) return BadPassword();
        if (passwords.Verify(password!.Password,user.PasswordHash)) return Invalid("Choose a different temporary password.");
        user.PasswordHash=passwords.Hash(password.Password);
        user.MustChangePassword=true;
        user.LastPasswordChangedUtc=user.UpdatedUtc=clock.GetUtcNow().UtcDateTime;
        user.UpdatedByUserId=actor;
        Audit(db,actor,"UserPasswordReset","User",user.Id);
        return new(CompanyOutcome.Success);
    }

    private async Task<SecurityResult> SaveRole(BillingDbContext db,SecurityRequest request,Guid actor,CancellationToken ct)
    {
        var draft=request.Role!;
        var creating=request.Id is null;
        var role=creating ? new Role { CreatedUtc=clock.GetUtcNow().UtcDateTime,CreatedByUserId=actor } :
            await db.Set<Role>().SingleOrDefaultAsync(x=>x.Id==request.Id,ct);
        if (role is null) return new(CompanyOutcome.NotFound);
        if (!creating && role.Version!=request.Version) return new(CompanyOutcome.Conflict);
        if (role.IsSystemRole) return new(CompanyOutcome.Forbidden,"This system role is read-only.");
        var code=draft.Code.Trim().ToUpperInvariant();
        if (!creating && code!=role.Code) return Invalid("The role code cannot be changed.");
        var name=draft.Name.Trim();
        var normalizedName=name.ToUpperInvariant();
        if (await db.Set<Role>().AnyAsync(x=>x.Id!=role.Id && (x.Code==code || x.Name.ToUpper()==normalizedName),ct))
            return Invalid("The role code or name already exists.");
        role.Code=code;
        role.Name=name;
        role.Description=Clean(draft.Description);
        if (!creating && role.IsActive!=draft.IsActive) Audit(db,actor,draft.IsActive?"RoleActivated":"RoleDeactivated","Role",role.Id);
        role.IsActive=draft.IsActive;
        role.UpdatedUtc=clock.GetUtcNow().UtcDateTime;
        role.UpdatedByUserId=actor;
        if (creating) db.Add(role);
        else db.Entry(role).Property(x=>x.UpdatedUtc).IsModified=true;
        Audit(db,actor,creating?"RoleCreated":"RoleUpdated","Role",role.Id);

        var current=await db.Set<RolePermission>().Where(x=>x.RoleId==role.Id).ToListAsync(ct);
        var selected=(draft.Permissions ?? []).ToHashSet(StringComparer.Ordinal);
        var moduleCodes=(await db.Set<Module>().Select(x=>x.Code).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in PermissionCatalog.All)
        {
            var existing=current.SingleOrDefault(x=>x.ModuleCode==definition.StoredModule && x.Action==definition.StoredAction);
            if (selected.Contains(definition.Code) && existing is null)
            {
                if (moduleCodes.Add(definition.StoredModule))
                    db.Add(new Module {Code=definition.StoredModule,Name=definition.Module});
                db.Add(new RolePermission { RoleId=role.Id,ModuleCode=definition.StoredModule,Action=definition.StoredAction });
                Audit(db,actor,"RolePermissionGranted","Role",role.Id,details:new { Permission=definition.Code });
            }
            else if (!selected.Contains(definition.Code) && existing is not null)
            {
                db.Remove(existing);
                Audit(db,actor,"RolePermissionRevoked","Role",role.Id,details:new { Permission=definition.Code });
            }
        }
        return new(CompanyOutcome.Success);
    }

    private static async Task<SecurityResult> ReadAudit(BillingDbContext db,SecurityRequest request,AccessSnapshot access,CancellationToken ct)
    {
        var scope=access.CurrentCompanyId;
        var filters=request.Audit ?? new();
        var query=from audit in db.Set<AuditEvent>()
            join user in db.Set<User>() on audit.ActorUserId equals (Guid?)user.Id into authors
            from author in authors.DefaultIfEmpty()
            where (audit.RecordType=="User" || audit.RecordType=="Role" || audit.RecordType=="Installation" || audit.RecordType=="Security" ||
                audit.RecordType=="Settings") &&
                (access.IsSuperAdmin || audit.CompanyId==scope)
            select new { Audit=audit,Actor=author==null ? "(system)" : author.Username,
                Target=audit.RecordType=="User" && audit.RecordId!=null ?
                    db.Set<User>().Where(u=>u.Id.ToString().ToUpper()==audit.RecordId.ToUpper()).Select(u=>u.Username).FirstOrDefault() : null };
        if (filters.FromUtc is DateTime from) query=query.Where(x=>x.Audit.OccurredUtc>=from);
        if (filters.BeforeUtc is DateTime before) query=query.Where(x=>x.Audit.OccurredUtc<before);
        if (Clean(filters.Event)?.ToUpperInvariant() is { } action) query=query.Where(x=>x.Audit.Action.ToUpper().Contains(action));
        if (Clean(filters.Actor)?.ToUpperInvariant() is { } actor) query=query.Where(x=>x.Actor.ToUpper().Contains(actor));
        if (Clean(filters.Target)?.ToUpperInvariant() is { } target) query=query.Where(x=>x.Target!=null && x.Target.ToUpper().Contains(target));
        if (Clean(filters.Outcome)?.ToUpperInvariant() is { } outcome)
        {
            var key="\"OUTCOME\":\""+outcome+"\"";
            query=query.Where(x=>x.Audit.ChangesJson.ToUpper().Contains(key));
        }
        var list=await query.OrderByDescending(x=>x.Audit.OccurredUtc).ThenBy(x=>x.Audit.Id)
            .Skip(request.Page*request.PageSize).Take(request.PageSize+1)
            .Select(x=>new { x.Audit.OccurredUtc,x.Audit.Action,x.Actor,x.Target,x.Audit.ChangesJson,x.Audit.CompanyId }).ToListAsync(ct);
        return new(CompanyOutcome.Success,Audit:list.Take(request.PageSize).Select(x=>new SecurityAuditRow(x.OccurredUtc,x.Action,x.Actor,
            x.Target ?? "",Outcome(x.ChangesJson),x.CompanyId)).ToArray(),More:list.Count>request.PageSize);
    }

    private static string Outcome(string json)
    {
        try
        {
            using var parsed=JsonDocument.Parse(json);
            return parsed.RootElement.TryGetProperty("outcome",out var value) && value.ValueKind==JsonValueKind.String &&
                value.GetString() is ("Success" or "Rejected" or "Throttled" or "InvalidInput" or "Denied" or "AlreadyConfigured") ?
                value.GetString()! : "—";
        }
        catch (JsonException) { return "—"; }
    }

    private static async Task<(string? Created,string? Updated)> Authors(BillingDbContext db,Guid? created,Guid? updated,CancellationToken ct)
    {
        var names=await db.Set<User>().Where(x=>x.Id==created || x.Id==updated).ToDictionaryAsync(x=>x.Id,x=>x.Username,ct);
        return (created is Guid first ? names.GetValueOrDefault(first) : null,updated is Guid second ? names.GetValueOrDefault(second) : null);
    }

    private void Audit(BillingDbContext db,Guid actor,string action,string type,Guid? target,Guid? company=null,
        string outcome="Success",object? details=null) =>
        db.Add(new AuditEvent { ActorUserId=actor,ActorKind="User",Action=action,RecordType=type,RecordId=target?.ToString(),
            CompanyId=company,OccurredUtc=clock.GetUtcNow().UtcDateTime,ChangesJson=JsonSerializer.Serialize(new { outcome,details }) });

    private static bool IsValidPassword(string username,PasswordInput? input) =>
        input is not null && AuthenticationService.ValidEmployeePassword(username,input.Password,input.Confirmation);
    private static SecurityResult BadPassword() => Invalid("Use a confirmed passphrase of 15 or more characters, up to 512 UTF-16 units, and avoid username-based passwords.");
    private static SecurityResult Invalid(string message) => new(CompanyOutcome.InvalidInput,message);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

