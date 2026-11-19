using GTS.Billing.Application;
using GTS.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace GTS.Billing.Infrastructure;

internal static class ScopedAccess
{
    internal static async Task<CompanyOutcome> VerifyAsync(BillingDbContext db, Guid installationId,
        AuthenticatedIdentity actor, CancellationToken ct)
    {
        var installation = await db.Set<Installation>().SingleAsync(ct);
        if (installation.Id != installationId || installation.SetupState != "Configured" ||
            installation.DatabaseMode != "PlaintextDevelopment") return CompanyOutcome.Unavailable;
        var valid = await db.Set<User>().AnyAsync(u => u.Id == actor.UserId && u.Version == actor.Version &&
            u.IsActive && !u.IsLocked && !u.MustChangePassword, ct);
        return valid ? CompanyOutcome.Success : CompanyOutcome.SessionRequired;
    }

    internal static Task<bool> IsSuperAdminAsync(BillingDbContext db, Guid userId, CancellationToken ct) =>
        db.Set<SuperAdminAssignment>().AnyAsync(x=>x.UserId==userId,ct);

    internal static IQueryable<Guid> CompanyIds(BillingDbContext db, Guid userId) =>
        from access in db.Set<UserCompanyAccess>()
        join company in db.Set<Company>() on access.CompanyId equals company.Id
        where access.UserId == userId && access.IsActive && company.IsActive
        select company.Id;

    internal static async Task<IReadOnlySet<string>> ResolveAsync(BillingDbContext db, Guid userId, Guid company, CancellationToken ct)
    {
        if (!await CompanyIds(db,userId).ContainsAsync(company,ct)) return new HashSet<string>();
        var grants = await (
            from assignment in db.Set<UserRole>()
            join role in db.Set<Role>() on assignment.RoleId equals role.Id
            join permission in db.Set<RolePermission>() on role.Id equals permission.RoleId
            where assignment.UserId == userId && assignment.CompanyId == company && role.IsActive
            select new { permission.ModuleCode, permission.Action }
        ).Distinct().ToListAsync(ct);
        var denials = await db.Set<UserPermissionOverride>()
            .Where(x=>x.UserId==userId && x.CompanyId==company && !x.IsAllowed)
            .Select(x=>new { x.ModuleCode,x.Action }).ToListAsync(ct);
        return PermissionCatalog.All.Where(p=>grants.Any(x=>x.ModuleCode==p.StoredModule && x.Action==p.StoredAction) &&
            !denials.Any(x=>x.ModuleCode==p.StoredModule && x.Action==p.StoredAction))
            .Select(p=>p.Code).ToHashSet(StringComparer.Ordinal);
    }

    internal static async Task<AccessSnapshot> SnapshotAsync(BillingDbContext db, Guid userId, CancellationToken ct)
    {
        var admin=await IsSuperAdminAsync(db,userId,ct);
        var selected=await db.Set<CompanySelection>().Select(x=>(Guid?)x.CompanyId).SingleOrDefaultAsync(ct);
        if (admin) return new(true,selected,[]);
        var ids=await CompanyIds(db,userId).ToArrayAsync(ct);
        var companies=new List<CompanyCapability>();
        foreach(var id in ids) companies.Add(new(id,await ResolveAsync(db,userId,id,ct)));
        return new(false,selected is Guid current && ids.Contains(current) ? current : null,companies);
    }
}
