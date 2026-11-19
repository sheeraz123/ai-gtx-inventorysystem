using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace GTS.Billing.Presentation.Wpf;

internal sealed record DeploymentIdentity(Guid DeploymentId, string[] AuthorizedSids)
{
    public static string MachinePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GTS Billing", "Installation", "identity.json");

    public static DeploymentIdentity Load(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 16384) throw new InvalidDataException("Deployment identity is missing or invalid.");
        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Deployment identity cannot use a redirected directory.");
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Redirected identity.");
        var identity = JsonSerializer.Deserialize<DeploymentIdentity>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Invalid identity.");
        identity.Validate();
        return identity;
    }

    public string MutexName => @"Global\GTS.Billing." + DeploymentId.ToString("D");

    public void Validate()
    {
        if (DeploymentId == Guid.Empty || AuthorizedSids is null || AuthorizedSids.Length is < 1 or > 32)
            throw new InvalidDataException("An approved deployment identifier and participant SIDs are required.");
        foreach (var sid in AuthorizedSids)
        {
            var parsed = new SecurityIdentifier(sid);
            if (parsed.Value != sid || !(sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) ||
                sid.StartsWith("S-1-5-32-", StringComparison.Ordinal)) || sid == "S-1-5-32-545" ||
                parsed.IsWellKnown(WellKnownSidType.BuiltinGuestsSid))
                throw new InvalidDataException("Use approved account or restricted local group SIDs.");
        }
    }
}

internal enum OwnershipResult { Acquired, Abandoned, Busy, AccessDenied, InvalidConfiguration, Failed }

internal sealed class GlobalInstanceLock : IDisposable
{
    private readonly SafeWaitHandle handle;
    private readonly int ownerThread;
    private bool owns;
    private bool disposed;

    private GlobalInstanceLock(SafeWaitHandle handle)
    {
        this.handle = handle;
        ownerThread = Environment.CurrentManagedThreadId;
        owns = true;
    }

    public static OwnershipResult TryAcquire(DeploymentIdentity identity, out GlobalInstanceLock? ownership)
    {
        ownership = null;
        try { identity.Validate(); }
        catch (Exception e) when (e is ArgumentException or InvalidDataException) { return OwnershipResult.InvalidConfiguration; }

        // No Everyone/Authenticated Users grant. Runtime participants request only SYNCHRONIZE.
        // SYSTEM and administrators retain administration rights. The creator remains a Windows object owner.
        var sddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)" +
            string.Concat(identity.AuthorizedSids.Distinct().Select(sid => $"(A;;0x00100000;;;{sid})"));
        if (!Native.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            return OwnershipResult.InvalidConfiguration;
        try
        {
            var attributes = new Native.SecurityAttributes
            {
                Length = Marshal.SizeOf<Native.SecurityAttributes>(), Descriptor = descriptor, Inherit = false
            };
            var raw = Native.CreateMutexEx(ref attributes, identity.MutexName, 0, 0x00100000);
            if (raw == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                return error switch { 5 => OwnershipResult.AccessDenied, 6 or 87 or 123 => OwnershipResult.InvalidConfiguration, _ => OwnershipResult.Failed };
            }
            var safe = new SafeWaitHandle(raw, ownsHandle: true);
            var wait = Native.WaitForSingleObject(safe, 0);
            if (wait is 0 or 0x80)
            {
                ownership = new GlobalInstanceLock(safe);
                return wait == 0 ? OwnershipResult.Acquired : OwnershipResult.Abandoned;
            }
            safe.Dispose();
            return wait == 0x102 ? OwnershipResult.Busy : OwnershipResult.Failed;
        }
        finally { Native.LocalFree(descriptor); }
    }

    public void Dispose()
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Mutex release must run on its owning thread.");
        if (owns)
        {
            if (!Native.ReleaseMutex(handle)) throw new InvalidOperationException("Windows could not release application ownership.");
            owns = false;
        }
        handle.Dispose();
        disposed = true;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SecurityAttributes
        {
            internal int Length;
            internal IntPtr Descriptor;
            [MarshalAs(UnmanagedType.Bool)] internal bool Inherit;
        }
        [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string value, uint revision, out IntPtr descriptor, out uint size);
        [DllImport("kernel32.dll", EntryPoint = "CreateMutexExW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateMutexEx(ref SecurityAttributes attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseMutex(SafeWaitHandle handle);
        [DllImport("kernel32.dll")]
        internal static extern IntPtr LocalFree(IntPtr memory);
    }
}
