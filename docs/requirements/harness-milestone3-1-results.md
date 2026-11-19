# Milestone 3.1 results

Outcome: capability assessment, recommendation, acceptance specification and regression verification completed. **Operational backend implementation and real OS confinement tests are blocked by missing prerequisites.** Fail-closed behavior is unchanged; no generated code ran.

## Evidence

- Host: Windows 10 Pro 22H2, build 19045.6466; approximately 16 GB RAM.
- Firmware virtualization, VM monitor extensions and SLAT: true. HypervisorPresent: false.
- Sandbox executable, Hyper-V module/management tools and relevant services: not found.
- Docker/Podman/WSL container tools: not on PATH. WSL launcher exists but status exits 50 and distribution listing exits 1; no working WSL2 environment verified.
- Exact optional-feature states: Unknown, because Windows Administrator rights are required. Outside-tool-sandbox execution remained a non-administrator process.
- No OS-isolated guest, network/secret/process denial test or resource-limit test ran. Those outcomes are NotRun, never Passed.
- Release build: zero warnings/errors. Combined tests: **129 passed, zero failed/skipped** (all prior 123 plus six readiness cases).
- The existing sample plan approval remains true for SHA256 `761B4D8D19F4CAF48480171317F6C7E55BD3FECDDF83F8F5779D85D7BA238B46`; no change approval exists. The sample was not resumed.

See [the saved capability evidence](../examples/isolation-capability-assessment.json) and [the complete security assessment](../architecture/secure-execution-milestone3-1.md).

## Changes

- `AI-Harness/assess-isolation.ps1`: reproducible read-only Windows capability report; missing/unknown/verified facts remain distinct.
- `AI-Harness/verify-milestone3-1.ps1`: preserves earlier regression checks and records inventory, without AI or generated-code execution.
- `AI-Harness/config/isolation-requirements.json`: proposed concrete offline VM limits and mandatory evidence; not loaded as runnable configuration and cannot enable execution.
- `AI-Harness/tests/AI.GTX.Harness.Tests/IsolationBackendReadinessTests.cs`: six negative fail-closed tests, explicitly not OS confinement evidence.
- New architecture/results documents and saved evidence; README and previous security guide point to them.

No backend, feature installation, guest image download, service/VM startup, OS/security setting change, reboot, credential inspection, automatic approval, Billing change, push, merge or deployment was performed.

## Next step

Recommended candidate: an offline disposable Hyper-V VM with a non-root guest workload, no host shares/credentials or network adapter, VM resource limits, guest process limits and a narrow host management broker. First obtain a read-only Administrator feature assessment and confirm host security servicing. Then obtain explicit approval for any Hyper-V enablement/reboot and for the reviewed guest/toolchain/broker provisioning. Finally implement and demonstrate actual confinement before registering a live execution backend. A normal WSL install or directory allowlist alone is not an acceptable substitute.
