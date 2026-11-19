using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using AI.GTX.Harness.Tools;
using AI.GTX.Harness.Infrastructure;
using AI.GTX.Harness.Infrastructure.Codex;
using AI.GTX.Harness.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AI.GTX.Harness.Tests;

public sealed class WorkspaceLinkAttackTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gtx-link-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> links = [];
    private string MakeLink(string parent)
    {
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "canary.txt"), "not-authorized");
        Directory.CreateDirectory(parent); var link = Path.Combine(parent, "linked");
        if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(link, outside);
        else
        {
            // Directory junctions do not require symlink privilege. No shell or machine-wide settings.
            Directory.CreateDirectory(link);
            using var handle = CreateFile(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var substitute = Encoding.Unicode.GetBytes(@"\??\" + outside); var print = Encoding.Unicode.GetBytes(outside);
            var buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
            BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0); BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
            BitConverter.GetBytes((ushort)substitute.Length).CopyTo(buffer, 10);
            BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(buffer, 12); BitConverter.GetBytes((ushort)print.Length).CopyTo(buffer, 14);
            substitute.CopyTo(buffer, 16); print.CopyTo(buffer, 16 + substitute.Length + 2);
            if (!DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        links.Add(link); return link;
    }
    [Fact] public void ExistingLinkCannotExposeFilesOutsideAuthorizedRoot()
    {
        var target = Path.Combine(root, "target"); MakeLink(target);
        Assert.Throws<UnauthorizedAccessException>(() => new WorkspaceReader(target).Resolve("linked/canary.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => WorkflowFiles.Snapshot(target));
        Assert.Equal("not-authorized", File.ReadAllText(Path.Combine(root, "outside", "canary.txt")));
    }
    [Fact] public void LinkedTargetCannotBeApprovedAsSourceSnapshot()
    {
        var target = Path.Combine(root, "target"); MakeLink(target);
        Assert.Throws<UnauthorizedAccessException>(() => new WorkflowStore(Path.Combine(root, "state")).Create(Plan(), target, ["linked/canary.txt"]));
    }
    [Fact] public void LinkAddedToStagedWorkspaceIsDetected()
    {
        var target = Path.Combine(root, "target"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "a.cs"), "original");
        var plan = new WorkflowStore(Path.Combine(root, "state")).Create(Plan(), target, ["a.cs"]);
        var workspace = new DevelopmentWorkspace(Path.Combine(root, "stage")); var staged = workspace.Prepare(plan); MakeLink(staged);
        Assert.Throws<UnauthorizedAccessException>(() => workspace.Stage(plan, "D1", staged, [new("a.cs", "candidate")]));
    }
    [Fact] public async Task LinkedCodexWorkspaceIsRejectedWithoutLaunching()
    {
        var link = MakeLink(Path.Combine(root, "parent")); var runner = new CodexTests.FakeRunner();
        var provider = new CodexExecutionProvider(runner, Options.Create(new CodexOptions { Executable = Environment.ProcessPath!, WorkspaceRoot = link }), new(), NullLogger<CodexExecutionProvider>.Instance);
        Assert.Equal(ProviderError.WorkspaceDenied, (await provider.CheckAvailabilityAsync(default)).Error); Assert.Empty(runner.Calls);
    }
    private static DevelopmentPlan Plan() => new() { Title = "Link test", Goal = "Test only", Assumptions = [], Questions = [], Tasks = [new() { Id = "D1", Role = "developer", Description = "Test", Dependencies = [], AcceptanceCriteria = ["Cannot escape"] }] };
    public void Dispose()
    {
        foreach (var link in links) if (Directory.Exists(link)) Directory.Delete(link);
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputLength, IntPtr output, int outputLength, out int returned, IntPtr overlapped);
}
