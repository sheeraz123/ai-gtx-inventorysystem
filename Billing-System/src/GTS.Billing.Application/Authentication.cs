using GTS.Billing.Domain;

namespace GTS.Billing.Application;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encoded);
    bool NeedsUpgrade(string encoded);
}

public interface IAuthenticationStore
{
    Task<T> InTransactionAsync<T>(Func<IAuthenticationData, Task<T>> operation, CancellationToken cancellationToken);
}

public interface IAuthenticationData
{
    Task<Installation> InstallationAsync();
    Task<User?> FindUserAsync(string normalized);
    Task<User?> FindUserAsync(Guid id);
    Task<SuperAdminAssignment?> AdminAsync();
    Task<List<RecoveryCode>> UnusedCodesAsync(Guid userId);
    Task<int> RecentFailuresAsync(string action, DateTime since);
    void Add(object record);
}

public enum AuthenticationEntry { Setup, Login }
public enum AuthenticationOutcome { Success, Rejected, Throttled, InvalidInput, AlreadyConfigured, SessionRequired }
public sealed record AuthenticationResult(AuthenticationOutcome Outcome,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<string>? Codes = null)
{
    public override string ToString() => Outcome.ToString();
}
public sealed record AuthenticatedIdentity(Guid UserId, string Username, long Version, bool IsSuperAdmin)
{
    public bool MustChangePassword { get; init; }
}
public interface IApplicationSession
{
    Task<AuthenticatedIdentity?> GetCurrentAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthenticationService(IAuthenticationStore store, IPasswordHasher passwords, TimeProvider clock) : IApplicationSession
{
    private readonly SemaphoreSlim gate = new(1);
    private AuthenticatedIdentity? current;
    private readonly Lazy<string> dummy = new(() => passwords.Hash(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))));

    public async Task<AuthenticationEntry> EntryAsync(CancellationToken ct = default) =>
        await store.InTransactionAsync(async db => await EntryAsync(db, await db.InstallationAsync()), ct);

    private static async Task<AuthenticationEntry> EntryAsync(IAuthenticationData db, Installation installation)
    {
        var admin = await db.AdminAsync();
        if (installation.SetupState == "NotConfigured" && admin is null) return AuthenticationEntry.Setup;
        if (installation.SetupState == "Configured" && admin is not null && (await db.FindUserAsync(admin.UserId))?.IsActive == true)
            return AuthenticationEntry.Login;
        throw new InvalidOperationException("Installation authentication state is inconsistent.");
    }

    public Task<AuthenticationResult> SetupAsync(string username, string password, string confirmation, CancellationToken ct = default) =>
        Serialized(async () => await store.InTransactionAsync(async db =>
        {
            var installation = await db.InstallationAsync();
            if (await EntryAsync(db, installation) != AuthenticationEntry.Setup)
                return Audit(db, installation, "SetupAlreadyConfigured", AuthenticationOutcome.AlreadyConfigured);
            var name = Normalize(username);
            if (name is null || !ValidNewPassword(password, confirmation)) return Result(AuthenticationOutcome.InvalidInput);
            if (await db.FindUserAsync(name) is not null)
                return Audit(db, installation, "SetupRejected", AuthenticationOutcome.Rejected);
            var now=clock.GetUtcNow().UtcDateTime;
            var user = new User { Username = username.Trim(), NormalizedUsername = name, PasswordHash = passwords.Hash(password),
                CreatedUtc=now, LastPasswordChangedUtc=now };
            db.Add(user);
            db.Add(new SuperAdminAssignment { UserId = user.Id });
            var codes = IssueCodes(db, user.Id);
            installation.SetupState = "Configured";
            Audit(db, installation, "SetupCompleted", AuthenticationOutcome.Success, user.Id);
            return new(AuthenticationOutcome.Success, codes);
        }, ct), ct);

    public Task<AuthenticationResult> LoginAsync(string username, string password, CancellationToken ct = default) =>
        Serialized(async () =>
        {
            if (current is not null) return Result(AuthenticationOutcome.Rejected);
            var identity = await store.InTransactionAsync(async db =>
            {
                var inst = await db.InstallationAsync();
                if (await EntryAsync(db, inst) != AuthenticationEntry.Login) return (AuthenticationOutcome.Rejected, (AuthenticatedIdentity?)null);
                if (await Limited(db, "LoginFailed")) return (Audit(db, inst, "LoginThrottled", AuthenticationOutcome.Throttled).Outcome, null);
                var name = Normalize(username);
                var user = name is null ? null : await db.FindUserAsync(name);
                var fallback = dummy.Value;
                var verified = ValidLoginPassword(password) && passwords.Verify(password, user?.PasswordHash ?? fallback);
                if (!verified || user?.IsActive != true || user.IsLocked)
                    return (Audit(db, inst, "LoginFailed", AuthenticationOutcome.Rejected).Outcome, null);
                var admin = (await db.AdminAsync())?.UserId == user.Id;
                // Current encoded format is versioned. Preserve older hashes until a reviewed upgrade is needed.
                user.LastSuccessfulLoginUtc=clock.GetUtcNow().UtcDateTime;
                user.Version=checked(user.Version+1);
                Audit(db, inst, "LoginSucceeded", AuthenticationOutcome.Success, user.Id);
                return (AuthenticationOutcome.Success, new AuthenticatedIdentity(user.Id, user.Username, user.Version, admin)
                    { MustChangePassword=user.MustChangePassword });
            }, ct);
            current = identity.Item2;
            return Result(identity.Item1);
        }, ct);

    public Task<AuthenticatedIdentity?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Serialized(async () =>
        {
            if (current is null) return null;
            try { current = await store.InTransactionAsync(ValidateSessionAsync, cancellationToken); }
            catch { current = null; throw; }
            return current;
        }, cancellationToken);

    // Retain the existing gate across a protected operation so logout cannot race it.
    // The persistence boundary must also recheck the version inside its writer transaction.
    public Task<T> RunAuthenticatedAsync<T>(Func<AuthenticatedIdentity, Task<T>> operation, CancellationToken ct = default) =>
        Serialized(async () =>
        {
            if (current is null) throw new UnauthorizedAccessException();
            try { current = await store.InTransactionAsync(ValidateSessionAsync, ct); }
            catch { current = null; throw; }
            if (current is null) throw new UnauthorizedAccessException();
            if (current.MustChangePassword) throw new UnauthorizedAccessException();
            try { return await operation(current); }
            catch (UnauthorizedAccessException) { current = null; throw; }
        }, ct);

    private async Task<AuthenticatedIdentity?> ValidateSessionAsync(IAuthenticationData db)
    {
        if (current is null) return null;
        _ = await db.InstallationAsync();
        var user = await db.FindUserAsync(current.UserId);
        if (user?.IsActive != true || user.IsLocked || user.Version != current.Version)
        {
            // Callers hold the service gate, so login cannot observe the rejected identity.
            current = null;
            return null;
        }
        return current with { IsSuperAdmin = (await db.AdminAsync())?.UserId == user.Id, MustChangePassword=user.MustChangePassword };
    }

    public Task LogoutAsync(CancellationToken ct = default) => Serialized(async () =>
    {
        var previous = current;
        current = null;
        if (previous is not null)
            await store.InTransactionAsync(async db => Audit(db, await db.InstallationAsync(), "Logout", AuthenticationOutcome.Success, previous.UserId), ct);
        return true;
    }, ct);

    public Task<AuthenticationResult> RecoverAsync(string code, string password, string confirmation, CancellationToken ct = default) =>
        Serialized(async () =>
        {
            var result = await store.InTransactionAsync(async db =>
            {
                var inst = await db.InstallationAsync();
                if (await EntryAsync(db, inst) != AuthenticationEntry.Login) return Result(AuthenticationOutcome.Rejected);
                if (await Limited(db, "RecoveryFailed")) return Audit(db, inst, "RecoveryThrottled", AuthenticationOutcome.Throttled);
                if (!ValidNewPassword(password, confirmation))
                    return Audit(db, inst, "RecoveryFailed", AuthenticationOutcome.InvalidInput);
                var id = (await db.AdminAsync())!.UserId;
                var admin = (await db.FindUserAsync(id))!;
                var match = (await db.UnusedCodesAsync(id)).FirstOrDefault(item => RecoverySecrets.Verify(code, item.CodeHash));
                if (match is null) return Audit(db, inst, "RecoveryFailed", AuthenticationOutcome.Rejected);
                var now = clock.GetUtcNow().UtcDateTime;
                match.ConsumedUtc = now;
                admin.PasswordHash = passwords.Hash(password);
                admin.LastPasswordChangedUtc=now;
                // Always change the concurrency stamp, even if an implementation returns the same hash.
                admin.Version = checked(admin.Version + 1);
                Audit(db, inst, "PasswordChanged", AuthenticationOutcome.Success, id);
                Audit(db, inst, "SessionsInvalidated", AuthenticationOutcome.Success, id);
                return Audit(db, inst, "RecoverySucceeded", AuthenticationOutcome.Success, id);
            }, ct);
            if (result.Outcome == AuthenticationOutcome.Success) current = null;
            return result;
        }, ct);

    public Task<AuthenticationResult> RegenerateAsync(string password, CancellationToken ct = default) =>
        Serialized(async () => await store.InTransactionAsync(async db =>
        {
            var session = await ValidateSessionAsync(db);
            if (session?.IsSuperAdmin != true) return Result(AuthenticationOutcome.SessionRequired);
            var inst = await db.InstallationAsync();
            if (await Limited(db, "ReauthenticationFailed")) return Audit(db, inst, "ReauthenticationThrottled", AuthenticationOutcome.Throttled);
            var user = (await db.FindUserAsync(session.UserId))!;
            if (!ValidLoginPassword(password) || !passwords.Verify(password, user.PasswordHash))
                return Audit(db, inst, "ReauthenticationFailed", AuthenticationOutcome.Rejected, user.Id);
            foreach (var code in await db.UnusedCodesAsync(user.Id)) code.InvalidatedUtc = clock.GetUtcNow().UtcDateTime;
            var codes = IssueCodes(db, user.Id);
            Audit(db, inst, "RecoveryCodesRegenerated", AuthenticationOutcome.Success, user.Id);
            return new(AuthenticationOutcome.Success, codes);
        }, ct), ct);

    public Task<AuthenticationResult> ChangePasswordAsync(string currentPassword,string password,string confirmation,CancellationToken ct=default) =>
        Serialized(async () =>
        {
            var result=await store.InTransactionAsync(async db =>
            {
                var session=await ValidateSessionAsync(db);
                if (session is null) return Result(AuthenticationOutcome.SessionRequired);
                var installation=await db.InstallationAsync();
                if (await Limited(db,"PasswordVerificationFailed"))
                    return Audit(db,installation,"PasswordChangeThrottled",AuthenticationOutcome.Throttled,session.UserId);
                var user=(await db.FindUserAsync(session.UserId))!;
                if (!ValidLoginPassword(currentPassword) || !passwords.Verify(currentPassword,user.PasswordHash))
                    return Audit(db,installation,"PasswordVerificationFailed",AuthenticationOutcome.Rejected,user.Id);
                if (!ValidEmployeePassword(user.Username,password,confirmation) || string.Equals(currentPassword,password,StringComparison.Ordinal))
                    return Result(AuthenticationOutcome.InvalidInput);
                user.PasswordHash=passwords.Hash(password);
                user.LastPasswordChangedUtc=clock.GetUtcNow().UtcDateTime;
                user.MustChangePassword=false;
                user.Version=checked(user.Version+1);
                Audit(db,installation,"PasswordChanged",AuthenticationOutcome.Success,user.Id);
                return Audit(db,installation,"SessionsInvalidated",AuthenticationOutcome.Success,user.Id);
            },ct);
            if (result.Outcome==AuthenticationOutcome.Success) current=null;
            return result;
        },ct);

    private async Task<T> Serialized<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await operation(); }
        finally { gate.Release(); }
    }

    private async Task<bool> Limited(IAuthenticationData db, string action) =>
        await db.RecentFailuresAsync(action, clock.GetUtcNow().UtcDateTime.AddSeconds(-30)) >= 5;

    private AuthenticationResult Audit(IAuthenticationData db, Installation installation, string action, AuthenticationOutcome outcome, Guid? actor = null)
    {
        db.Add(new AuditEvent { ActorUserId = actor, ActorKind = actor.HasValue ? "User" : "Application",
            OccurredUtc = clock.GetUtcNow().UtcDateTime, Action = action, RecordType = "Installation", RecordId = installation.Id.ToString(),
            ChangesJson = System.Text.Json.JsonSerializer.Serialize(new { outcome = outcome.ToString() }) });
        return Result(outcome);
    }

    private static AuthenticationResult Result(AuthenticationOutcome outcome) => new(outcome);
    private static IReadOnlyList<string> IssueCodes(IAuthenticationData db, Guid id)
    {
        var codes = new List<string>(10);
        while (codes.Count < 10)
        {
            var code = RecoverySecrets.Generate();
            if (codes.Contains(code, StringComparer.Ordinal)) continue;
            codes.Add(code);
            db.Add(new RecoveryCode { UserId = id, CodeHash = RecoverySecrets.Hash(code) });
        }
        return codes.AsReadOnly();
    }

    internal static string? Normalize(string? username)
    {
        if (username is null || username.Length > 128) return null;
        var trimmed = username.Trim();
        return trimmed.Length is >= 3 and <= 40 && trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? trimmed.ToUpperInvariant() : null;
    }
    private static bool ValidLoginPassword(string password)
    {
        if (password.Length is 0 or > 512) return false;
        for (var index = 0; index < password.Length; index++)
        {
            if (!char.IsSurrogate(password[index])) continue;
            if (!char.IsHighSurrogate(password[index]) || index + 1 == password.Length || !char.IsLowSurrogate(password[++index])) return false;
        }
        return true;
    }
    private static bool ValidNewPassword(string password, string confirmation) =>
        ValidLoginPassword(password) && string.Equals(password, confirmation, StringComparison.Ordinal) &&
        password.EnumerateRunes().Count() >= 15 && !string.IsNullOrWhiteSpace(password);

    public static bool ValidEmployeePassword(string username,string password,string confirmation) =>
        ValidNewPassword(password,confirmation) &&
        !string.Equals(username.Trim(),password.Trim(),StringComparison.OrdinalIgnoreCase) &&
        !(password.StartsWith(username.Trim(),StringComparison.OrdinalIgnoreCase) &&
          password.AsSpan(username.Trim().Length).IndexOfAnyExcept("0123456789!@#._-".AsSpan())<0);
}
