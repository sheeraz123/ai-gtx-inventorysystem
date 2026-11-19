# Milestone 3.1 — execution backend assessment

Status: **no operational isolation backend is available or verified; live Developer/Tester/Reviewer execution remains disabled.** This assessment does not authorize system changes or resume the approved sample plan. No generated code, guest, container, or untrusted test workload was launched.

## Observed host capabilities

Assessment date: 2026-11-15. Machine identifiers, usernames, network configuration and credential values were not collected. The reproducible evidence is [isolation-capability-assessment.json](../examples/isolation-capability-assessment.json).

| Capability | Observation | Interpretation |
| --- | --- | --- |
| Windows | Windows 10 Pro 22H2, build 19045.6466 | Edition supports Hyper-V/Sandbox prerequisites; current support/ESU status is not established |
| Physical RAM | 16,976,740,352 bytes, approximately 15.8 GiB | A proposed 4 GiB single guest is plausible; workload sizing remains to be measured |
| Firmware virtualization | Enabled | Hardware prerequisite observed |
| VM monitor extensions / SLAT | Both true | Hardware prerequisites observed |
| Hypervisor present | False | No active hypervisor was reported by WMI |
| Windows Sandbox | Executable absent at System32 and on PATH | Not available to launch |
| Hyper-V | PowerShell module and vmconnect absent; vmms/vmcompute services not found | No usable management/runtime stack observed; VM inventory not available |
| HNS | Service not found | No host container networking service observed |
| WSL | Launcher present; status exit 50, list exit 1 | A working WSL2 distribution/runtime was not verified; do not infer a precise distribution count from failed queries |
| WSL services | LxssManager/WslService not found | No usable service observed |
| Container runtimes | docker, podman, wslc, runhcs, hcsdiag not on PATH | No approved runnable container backend identified; software elsewhere is not ruled out by PATH alone |
| Windows optional features | All four feature queries require elevation | States remain Unknown, not Disabled: Sandbox, Hyper-V, VirtualMachinePlatform and WSL |
| Current process | Not Administrator, even outside the Codex tool sandbox | Tool sandbox escalation does not confer Windows Administrator/UAC rights |

Windows 10 standard support ended on October 14, 2025. Before using this host to contain hostile code, confirm a supported security-maintenance path/current updates (for example applicable ESU), or separately approve a supported host upgrade. This assessment did not inspect license keys, ESU credentials or installation policy. [Microsoft lifecycle notice](https://learn.microsoft.com/en-us/lifecycle/announcements/windows-10-end-of-support).

## Technology comparison and recommendation

| Candidate | Filesystem / credentials | Process and network | Resource control / automation | Decision |
| --- | --- | --- | --- | --- |
| Disposable Hyper-V VM | Separate guest kernel/disk; can avoid host mounts and credentials entirely | No guest NIC; host and guest process namespaces separated; narrow management broker required | VM CPU/RAM bounds; guest process/disk limits; host-enforced deadline and teardown | Recommended design; not installed/available for verification |
| Windows Sandbox | Separate kernel; mapped folders and redirection must be explicitly constrained | Networking and clipboard are enabled by default; must disable them | Memory configuration exists; this assessment did not establish required CPU/process limits and unattended lifecycle control on this Windows 10 build | Lighter alternative for evaluation, not sufficient verified evidence here |
| WSL2 plus appropriately isolated containers | Default Windows filesystem integration is unsafe for this threat model; guest mounts/runtime sockets must be excluded | WSL itself is not an untrusted-code security boundary from the host; container isolation would require separate validation | Needs a running distribution, runtime and tested limits, none verified | Not selected; installing a distribution alone would not solve the problem |
| Codex restricted-token sandbox | Existing flags/path checks do not prove denial of all sensitive host reads | No validated process/network/credential confinement evidence for generated .NET/MSBuild code | Existing process timeout is not a complete security boundary | Keep existing read-only planning use; do not use as the generated-code backend |
| AppContainer/restricted tokens/job objects | Potential Windows primitives, but no approved backend/ACL policy is implemented or verified | A job object alone does not provide the required filesystem/network boundary | Resource and process controls alone are insufficient | Not presented as a working substitute |

**Recommendation:** an offline disposable Hyper-V VM, using a supported minimal Linux guest for the current platform-independent .NET sample. This is a design recommendation, not a claim of an already secure VM. The guest workload should be non-root, while the harness remains a normal Windows user. A narrowly scoped, trusted management broker should control only the designated disposable VM, rather than granting the AI harness general Hyper-V administrative authority. Windows-specific workloads would require a separately reviewed Windows guest design.

Why select it: there is no need to install a general-purpose desktop container runtime or expose the Windows profile/repository into a Linux integration environment. Explicit guest disks, absent network adapters and VM resource settings make the required boundary and lifecycle easier to inspect. Provisioning still needs Administrator approval and verification. Microsoft documents Hyper-V support/hardware requirements for Windows Pro and configurable VM CPU/memory controls: [requirements](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/host-hardware-requirements), [CPU controls](https://learn.microsoft.com/en-us/powershell/module/hyper-v/set-vmprocessor), [memory controls](https://learn.microsoft.com/en-us/powershell/module/hyper-v/set-vmmemory).

Windows Sandbox is a legitimate alternative with a separate kernel, but must not be launched with defaults for this task. Microsoft documents its networking, clipboard and mapped-folder settings: [Sandbox overview](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-overview), [configuration](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file). Microsoft's [WSL security model](https://github.com/microsoft/WSL/blob/master/doc/docs/technical-documentation/security.md) explicitly distinguishes WSL from a security sandbox for untrusted code.

## Proposed backend configuration — not enabled

`AI-Harness/config/isolation-requirements.json` records the concrete acceptance target. It is **not loaded as executable configuration**, contains no installation commands and cannot turn `IsolationVerified` on.

- One disposable VM/workload at a time; 2 virtual processors, fixed 4 GiB RAM, 20 GiB guest disk and a guest-enforced 128-process ceiling.
- Per-command 120-second deadline, bounded output of one million characters per stream, and at most 10 MiB returned artifacts. These are proposed limits, not measured enforcement.
- No guest NIC or network switch attachment, no host drives/shares, shared clipboard, enhanced-session redirection, privileged runtime socket, or host management endpoint in the guest.
- A minimal non-secret guest environment. No Codex installation/login, user profile, API keys, SSH keys, cloud credentials, host repository or production mounts in the guest.
- Approved, hash-pinned, read-only source snapshot and trusted toolchain/dependency inputs, copied to guest-private work storage. Only the approved scope is copied; the actual repository is never mapped.
- Trusted offline .NET SDK and approved package cache prepared before running untrusted code; build/test use the existing fixed no-restore templates. Preparation itself requires approved image/package provenance.
- A bounded artifact broker must return data only; do not execute guest-returned files or automatically mount guest-controlled filesystems as trusted host disks. Reject paths, symlinks, malformed XML/JSON, oversized results and unexpected artifacts.
- The host-side Codex controller retains its existing ChatGPT authentication. It only produces typed proposals; no generated code is executed in that credential-bearing process. Provider tool restrictions and guest execution confinement are separate controls that both need validation.

No guest can be recommended as verified merely because a configuration file contains these values. CPU/memory/process/disk limits, cancellation and whole-guest teardown must be measured on the actual selected backend. Host/guest patching and the hypervisor vendor security boundary remain assumptions; this is not proof against all hypervisor vulnerabilities or a malicious host administrator.

## Threat model

Treat generated C#, project files, MSBuild targets, NuGet hooks, test code, native dependencies and returned artifacts as untrusted. They may try to read host files/credentials, modify source, enumerate or signal host processes, exfiltrate over DNS/TCP/UDP, reach metadata/management services, exhaust resources, survive cancellation, or forge test artifacts.

Trust the reviewed harness/broker/probe code, approved image/toolchain provenance and the configured OS/hypervisor boundary. Do not give the model authority to select mounts, executable arguments, network policy, VM identity or resource limits. Host management requires separate least-privilege authorization. Plan approval remains scoped to its exact hash and baseline; it does not approve infrastructure installation or source application. Change approval remains separate and mandatory.

## Required negative security verification

All tests below are **NotRun** on an isolated backend in this assessment. They must run with trusted probe code before any generated workload. Never test against actual credentials; use synthetic canaries. Each failure must keep the existing unavailable backend registered.

| Probe | Required evidence |
| --- | --- |
| Positive control | Approved snapshot read, guest-private write and trusted dotnet probe succeed, proving the probes actually ran |
| Repository/profile/auth/secret access | Synthetic host canaries at protected locations cannot be read or modified; host hashes unchanged; no host mounts exposed |
| Cross-boundary process access | Synthetic host process identity/handle cannot be enumerated or controlled from guest; host canary stays alive. Avoid unsafe PID-only kill tests because IDs may collide |
| Credential/environment leakage | No host auth files or secret variables; minimal explicit environment verified without publishing real values |
| Network egress | No guest NIC; DNS/TCP/UDP attempts fail, including host/metadata destinations; no runtime socket or management service exposed |
| Resource exhaustion | Trusted bounded probes measure memory, disk, process and CPU limits; no unbounded stress on the host |
| Timeout/cancellation | Host broker terminates guest/workload and descendants within the deadline, with no surviving workload |
| Artifact escape | Traversal, absolute paths, links, oversized files and malicious XML/JSON rejected; artifacts remain data |
| Evidence freshness | Record backend/image/policy/snapshot hashes and actual outcomes; never use arbitrary self-reported JSON/config booleans as an attestation |

## What was verified here

The full harness regression suite passes: **129 tests, zero failures/skips**, with zero Release build warnings/errors. This includes the original 123 tests, six additional readiness/fail-closed cases and the existing real temporary-directory junction/symlink attacks.

The added tests prove that unavailable execution requests are rejected, no evidence is manufactured, and configuration strings cannot switch the DI-registered backend to verified. They do **not** demonstrate actual guest filesystem, network or process confinement. The evidence report explicitly records `NegativeOsTests=NotRun` and `IsolationVerified=false`. No successful OS denial or enforced resource limit is claimed.

The existing sample plan remains approved, with no change approval. Its progress/source files were not altered, and live Developer/Tester execution was not resumed.

## Reproduce the assessment

Use PowerShell 7 from `AI-Harness`:

```powershell
./assess-isolation.ps1
./verify-milestone3-1.ps1
```

The inventory writes a new timestamp/GUID report under ignored `workspace/isolation-assessment`. It performs only read-only registry/WMI/feature/service/module/PATH/WSL queries. It does not auto-elevate, start a VM/service, install anything, expose credential files or change settings. WSL queries use fixed literal arguments with a deadline. An Administrator PowerShell 7 session is required for exact optional-feature states; without it those facts remain Unknown. A successful script run is inventory/test success, not a secure backend attestation.

## Exact remaining approvals and blockers

1. **Read-only Administrator inventory:** run `assess-isolation.ps1` in an explicitly approved Administrator PowerShell 7 session to confirm optional-feature states. The current tool process is not Windows Administrator; ordinary tool sandbox escalation did not fix that.
2. **Host support:** confirm current security servicing/ESU or separately approve a supported Windows upgrade if required. No upgrade or enrollment was attempted.
3. **Provisioning approval:** if Hyper-V is not enabled, explicitly approve enabling it and any reboot. Separately approve creation of the dedicated disposable VM, its supported guest image, offline SDK/package inputs and narrow management broker. Image/source/digest and provisioning commands must be reviewed before execution; no global feature/install command is included or run by this milestone.
4. **Backend implementation and OS tests:** only after those prerequisites are available, implement the actual broker/guest runner and run the complete positive/negative probe suite with resource/deadline evidence. No current backend can produce that proof.
5. **Resume authorization:** only after verification may the approved sample plan run. Source application still requires its own reviewed change approval. Do not push, merge, deploy, or work on Billing as part of that verification.

Nothing in the earlier sample-plan approval authorizes the installation, reboot or security changes above.
