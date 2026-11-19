# Milestone 3.2 — Administrator Windows readiness report

Assessment completed on 2026-11-15 in an actual elevated PowerShell 7 process (`AdministratorProcess=true`, exit 0). The original `assess-isolation.ps1` was run unchanged by the supplemental `assess-windows-readiness.ps1`. The saved [sanitized Administrator evidence](../examples/windows-readiness-admin.json) omits machine/user identifiers, product keys, tokens and unrelated configuration. Raw reports remain in ignored `AI-Harness/workspace/windows-readiness` and `workspace/isolation-assessment`.

## Compatibility results

| Area | Verified result |
| --- | --- |
| Windows | Windows 10 Pro / Professional, 22H2, build 19045.6466, 64-bit |
| Hyper-V | All, Hypervisor, Services and Management PowerShell features Enabled |
| Runtime | HypervisorPresent=true; vmms and vmcompute Running; management module present |
| Boot / DEP | hypervisorlaunchtype=Auto, nx=OptIn, DEPAvailable=true; no settings changed |
| CPU | 4 cores, 8 logical processors, 64-bit |
| RAM | 16,190 MiB total, 6,211 MiB free during the latest observation (about 15.8/6.1 GiB) |
| Storage | NTFS: C: 203.0 GiB free; D: 183.1 GiB free |
| Other features | Windows Sandbox and VirtualMachinePlatform Enabled; WSL Disabled |
| Restart indicators | CBS and Windows Update restart-required indicators both false; this does not prove every possible restart condition absent |
| ESU licensing projection | No products with LicenseStatus=1 reported; consumer enrollment/current coverage remains unconfirmed. No keys or activation material queried |
| Isolation backend | Still unverified; no guest/probe/workload was run |

**Conclusion: an offline Hyper-V VM is technically supported by the edition and current enabled/running role. No Hyper-V enablement, boot modification or reboot is indicated by the observed feature state.** Microsoft documents Windows Pro and the hardware prerequisites in its [Hyper-V requirements](https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/host-hardware-requirements).

The current elevated results supersede the earlier missing-tool/service observations for readiness decisions. We did not enable these features or determine why the observations differ. The raw WMI CPU extension flags in this active-hypervisor session returned false; they should not independently be interpreted as proof of missing firmware capability. Microsoft documents that hardware requirement display changes when a hypervisor is already running: [hypervisor detection guidance](https://learn.microsoft.com/en-us/troubleshoot/windows-client/application-management/virtualization-apps-not-work-with-hyper-v). No firmware or boot setting was changed to retest.

## Servicing and capacity blockers

The installed build is behind Microsoft's published Windows 10 ESU release baseline: the release page lists 19045.7850 on 2026-11-13, while this host reports 19045.6466. The same page associates 19045.6466 with KB5071959 from November 2025. Recent hotfix installation timestamps do not establish that the cumulative OS build is current. [Microsoft release information](https://learn.microsoft.com/en-us/windows/release-health/release-information).

Windows 10 standard support ended on October 14, 2025; an applicable supported servicing route/ESU entitlement must be confirmed before using this host to contain hostile workloads. The read-only licensing query projected only Name and LicenseStatus and reported no licensed ESU entries. That is not a definitive consumer-enrollment determination and does not authorize enrollment or activation. [Lifecycle notice](https://learn.microsoft.com/en-us/lifecycle/announcements/windows-10-end-of-support), [ESU verification guidance](https://learn.microsoft.com/en-us/windows/whats-new/enable-extended-security-updates).

The proposed fixed 4 GiB VM would leave roughly 2.1 GiB of currently free physical memory at the latest observation; the earlier observation had only about 5.7 GiB free. Memory availability is dynamic. Recheck headroom before provisioning and do not start the guest under avoidable host memory pressure. Storage is adequate for the proposed 20 GiB guest disk plus reasonable working artifacts, but image/cache sizing still needs review.

## Recommended backend and next approval

Retain the offline disposable Hyper-V VM recommendation: minimal non-root guest, no host repository/profile/credential mappings or network adapter, explicit resource limits, and a narrow trusted management broker. No unsupported-Hyper-V fallback is necessary on this host. Windows Sandbox is enabled as an alternative, but its unattended lifecycle and CPU/process controls still need validation; WSL is disabled, and installing it would not itself create a proven isolation boundary.

**Next smallest setup decision:** choose and approve a supported host-servicing remediation path before VM creation—confirm applicable ESU/update eligibility and prepare the exact security-update plan, or separately assess/approve a supported Windows upgrade. Do not blindly install a particular KB, enroll/purchase ESU, change update policy or reboot based on this report. Once servicing and RAM headroom are satisfactory, request separate approval for a reviewed guest image and one offline VM. Hyper-V installation is not needed.

No system changes are authorized by this assessment or the earlier sample-plan approval. VM provisioning and real positive/negative confinement verification are still unfinished prerequisites. Existing plan/change gates remain in force.

## Exact rerun commands

From a normal PowerShell session, launch the verified bundled PowerShell 7 executable; Windows will request UAC consent. This runs only the original assessment:

```powershell
$pwsh7 = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe'
Start-Process -FilePath $pwsh7 -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile -File "D:\Projects\ai-gtx-inventorysystem\AI-Harness\assess-isolation.ps1"'
```

For the full supplemental readiness report used here:

```powershell
Start-Process -FilePath $pwsh7 -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile -File "D:\Projects\ai-gtx-inventorysystem\AI-Harness\assess-windows-readiness.ps1"'
```

Or, in an already-open **Administrator PowerShell 7** console:

```powershell
Set-Location 'D:\Projects\ai-gtx-inventorysystem\AI-Harness'
.\assess-isolation.ps1
.\assess-windows-readiness.ps1
```

There is no execution-policy override. Both scripts query only status and write new local report files. The supplemental script reads feature states, selected OS/hardware/memory/storage fields, recent KB IDs/dates, non-secret ESU status codes, restart indicators and only the boot prerequisite flags. It does not install, activate, enable, change boot settings, reboot, create a VM, run generated code or modify the execution gate.

Validation: PowerShell syntax parsing passed; actual Administrator execution exited 0 and all selected facts were collected. No build/test workloads were run because no harness runtime code changed and this milestone is a read-only host assessment.
