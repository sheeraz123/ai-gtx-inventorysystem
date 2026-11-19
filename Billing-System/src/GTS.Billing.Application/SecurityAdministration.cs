using System.Text.RegularExpressions;

namespace GTS.Billing.Application;

public enum SecurityAction { Access, Options, Users, User, SaveUser, ResetPassword, Roles, Role, SaveRole, Audit }

public sealed record SecurityRequest(SecurityAction Action, Guid? Id=null,long? Version=null,string? Search=null,
    bool? Active=null,Guid? CompanyFilter=null,int Page=0,UserDraft? User=null,RoleDraft? Role=null,AuditFilter? Audit=null,int PageSize=50);

public sealed record RoleGrant(Guid RoleId,Guid CompanyId);
public sealed record UserDraft(string Username,string FullName,string? DisplayName=null,string? Email=null,string? MobileNumber=null,
    string? EmployeeCode=null,string? Designation=null,string? Department=null,bool IsActive=true,bool IsLocked=false,
    bool RequireChange=false,IReadOnlyList<Guid>? Companies=null,IReadOnlyList<RoleGrant>? Roles=null);
public sealed record RoleDraft(string Code,string Name,string? Description=null,bool IsActive=true,IReadOnlyList<string>? Permissions=null);
public sealed record UserSummary(Guid Id,long Version,string Username,string FullName,string? EmployeeCode,string RoleNames,
    string CompanyNames,bool IsActive,bool IsLocked,bool IsSuperAdmin,bool MustChangePassword,DateTime? LastLogin)
{
    public string Status => IsActive ? "Active" : "Inactive";
}
public sealed record UserDetails(UserSummary Summary,UserDraft Profile,DateTime? LastPasswordChange,DateTime? CreatedUtc,
    DateTime? UpdatedUtc,string? CreatedBy,string? UpdatedBy,bool LoginThrottled);
public sealed record RoleSummary(Guid Id,long Version,string Code,string Name,string? Description,bool IsActive,bool IsSystemRole,int MemberCount)
{
    public string Status => IsActive ? "Active" : "Inactive";
}
public sealed record RoleMember(string Username,string Company);
public sealed record RoleDetails(RoleSummary Summary,RoleDraft Draft,IReadOnlyList<RoleMember> Members,
    DateTime? CreatedUtc,DateTime? UpdatedUtc,string? CreatedBy,string? UpdatedBy);
public sealed record SecurityOption(Guid Id,string Label,bool Active);
public sealed record SecurityOptions(IReadOnlyList<SecurityOption> Companies,IReadOnlyList<SecurityOption> Roles);
public sealed record AuditFilter(DateTime? FromUtc=null,DateTime? BeforeUtc=null,string? Event=null,string? Actor=null,
    string? Target=null,string? Outcome=null);
public sealed record SecurityAuditRow(DateTime OccurredUtc,string Event,string Actor,string Target,string Outcome,Guid? CompanyId);
public sealed record SecurityResult(CompanyOutcome Outcome,string? Message=null,AccessSnapshot? Access=null,
    SecurityOptions? Options=null,IReadOnlyList<UserSummary>? Users=null,UserDetails? User=null,
    IReadOnlyList<RoleSummary>? Roles=null,RoleDetails? Role=null,IReadOnlyList<SecurityAuditRow>? Audit=null,bool More=false);

public sealed class PasswordInput(string password,string confirmation)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Password { get; }=password;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Confirmation { get; }=confirmation;
    public override string ToString() => "[protected input]";
}

public interface ISecurityAdministrationStore
{
    Task<SecurityResult> ExecuteAsync(SecurityRequest request,AuthenticatedIdentity actor,PasswordInput? password,CancellationToken ct);
}

public sealed class SecurityAdministrationService(AuthenticationService authentication,IApplicationOperationCoordinator lifetime,
    ISecurityAdministrationStore store)
{
    public async Task<SecurityResult> ExecuteAsync(SecurityRequest request,PasswordInput? password=null,CancellationToken ct=default)
    {
        SecurityResult result=new(CompanyOutcome.Unavailable);
        try
        {
            await lifetime.RunAsync(async token =>
            {
                result=await authentication.RunAuthenticatedAsync(async actor =>
                {
                    if (request.Page is <0 or >100000 || request.PageSize is <10 or >100 || request.Search?.Length>100 ||
                        request.Audit is { } filter && (filter.Event?.Length>100 || filter.Actor?.Length>100 || filter.Target?.Length>100 ||
                            filter.Outcome?.Length>40 || filter.FromUtc>=filter.BeforeUtc))
                        return new SecurityResult(CompanyOutcome.InvalidInput,"Review the search and date filters.");
                    if (request.Action is SecurityAction.SaveUser or SecurityAction.ResetPassword or SecurityAction.SaveRole && !actor.IsSuperAdmin)
                        return new SecurityResult(CompanyOutcome.Forbidden);
                    if (request.Action==SecurityAction.SaveUser && !ValidUser(request.User,request.Id is null))
                        return new SecurityResult(CompanyOutcome.InvalidInput,
                            "Enter a full name, a valid username and contact details. Review field lengths and assignments.");
                    if (request.Action==SecurityAction.SaveRole && !ValidRole(request.Role))
                        return new SecurityResult(CompanyOutcome.InvalidInput,
                            "Enter a role name, a 1–40 character code and supported permissions.");
                    if ((request.Action==SecurityAction.ResetPassword || request.Action==SecurityAction.SaveUser && request.Id is null)
                        && (password is null || password.Password.Length>512 || password.Confirmation.Length>512))
                        return new SecurityResult(CompanyOutcome.InvalidInput,"Enter and confirm a temporary password.");
                    var outcome=await store.ExecuteAsync(request,actor,password,token);
                    if (outcome.Outcome==CompanyOutcome.SessionRequired) throw new UnauthorizedAccessException();
                    return outcome;
                },token);
            },ct);
        }
        catch (UnauthorizedAccessException) { return new(CompanyOutcome.SessionRequired); }
        catch (Exception e) when (e is System.Data.Common.DbException or IOException or InvalidOperationException or ArgumentException)
        { return new(CompanyOutcome.Unavailable); }
        return result;
    }

    private static bool ValidUser(UserDraft? user,bool creating) => user is not null &&
        AuthenticationService.Normalize(user.Username) is { } normalized &&
        (!creating || new string(normalized.Where(char.IsAsciiLetterOrDigit).ToArray()) is not
            ("ADMIN" or "ADMINISTRATOR" or "SUPERADMIN" or "SYSTEM" or "ROOT" or "GTS")) &&
        Text(user.FullName,150,true) && Text(user.DisplayName,150) && Text(user.Email,254) &&
        Text(user.MobileNumber,40) && Text(user.EmployeeCode,50) && Text(user.Designation,100) && Text(user.Department,100) &&
        (string.IsNullOrWhiteSpace(user.Email) || System.Net.Mail.MailAddress.TryCreate(user.Email.Trim(),out var email) && email.Address==user.Email.Trim()) &&
        (string.IsNullOrWhiteSpace(user.MobileNumber) || Regex.IsMatch(user.MobileNumber.Trim(),@"\A[+0-9][0-9 ()-]{5,24}\z")) &&
        (user.Companies?.Count ?? 0)<=1000 && (user.Roles?.Count ?? 0)<=5000;

    private static bool ValidRole(RoleDraft? role) => role is not null && Text(role.Name,150,true) && Text(role.Description,500) &&
        Regex.IsMatch(role.Code?.Trim() ?? "",@"\A[A-Za-z0-9_-]{1,40}\z") &&
        !string.Equals(role.Code?.Trim(),"SUPERADMIN",StringComparison.OrdinalIgnoreCase) &&
        (role.Permissions?.Count ?? 0)<=PermissionCatalog.All.Count &&
        (role.Permissions?.All(code=>PermissionCatalog.All.Any(permission=>permission.Code==code)) ?? true);

    private static bool Text(string? value,int limit,bool required=false) =>
        !(required && string.IsNullOrWhiteSpace(value)) && (value is null || value.Length<=limit && !value.Any(char.IsControl));
}
