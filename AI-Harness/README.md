# AI-GTX Development Harness — Milestone 1

## Milestone 3.2: Administrator readiness verified

The actual Administrator assessment confirms Windows 10 Pro 22H2 x64 and **Hyper-V/Sandbox already enabled**, with the hypervisor and management services running. No feature, boot or reboot change was performed. This supersedes the earlier non-admin availability observations. Host servicing is behind the published ESU OS build and ESU coverage remains unconfirmed; free RAM needs a fresh capacity check before provisioning. No VM was created and generated-code execution remains disabled.

See [the compatibility report and exact Administrator commands](../docs/requirements/windows-readiness-milestone3-2.md) and [sanitized evidence](../docs/examples/windows-readiness-admin.json). The new `assess-windows-readiness.ps1` runs the original read-only inventory and adds readiness facts without changing system settings.

## Milestone 3.1: isolation capability assessment

The host has virtualization-capable hardware but no usable verified VM/container backend. **Live generated-code execution remains blocked.** No software/features/security settings were installed or changed. The approved sample plan is preserved and has not resumed.

Use `./assess-isolation.ps1` for a read-only Windows inventory, or `./verify-milestone3-1.ps1` for all 129 regression tests plus inventory. PowerShell 7 is required for inventory; exact Windows feature queries need an explicitly approved Administrator session. Reports never act as isolation attestations.

See [the assessment, threat model and exact next approvals](../docs/architecture/secure-execution-milestone3-1.md), [recorded evidence](../docs/examples/isolation-capability-assessment.json) and [results](../docs/requirements/harness-milestone3-1-results.md). The recommended candidate is an offline disposable Hyper-V VM, but it must be provisioned and pass real confinement tests before use.

## Milestone 3: gated development workflow

Persisted versioned plans, separate plan/change decisions, scoped staged changes, build/test evidence contracts, structured review, and journaled multi-file application/rollback are implemented and tested with mocked providers. **The live Developer/Tester/Reviewer sequence is blocked: this machine has no verified OS isolation backend for untrusted code.** No environment variable can turn that security gate on.

```powershell
./verify-milestone3.ps1 # repeatable tests and verification that live execution stays blocked
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --m3-check
# Explicit read-only Planner request and disposable baseline sample; does not approve or execute a plan:
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --m3-demo
```

See [the Milestone 3 architecture/security guide](../docs/architecture/development-milestone3.md) and [results and remaining blockers](../docs/requirements/harness-milestone3-results.md). The actual sample plan is available [for review](../docs/examples/milestone3-sample-plan.json). The user approved revision 1 of that plan only; the decision is recorded, source application remains unapproved, and live execution remains blocked by unverified OS isolation. The older milestone descriptions below remain historical; the existing Milestone 1/2 commands and tests are preserved.

## Milestone 2: operational Codex Planner

The foundation below is preserved. Milestone 2 adds a tested **Planner-only** integration using the installed Codex CLI and its existing ChatGPT login, plus a single-file change proposal workflow. The other five agent roles remain placeholders. Billing is not implemented.

```powershell
cd AI-Harness
./verify-milestone2.ps1              # repeatable tests; no AI requests
./verify-milestone2.ps1 -RunCodexSmoke # explicit live request using ChatGPT Codex allowance
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --codex-check
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --plan 'Plan a small formatter library and its tests'
```

`--plan` uses the existing factory, orchestrator, task queue and retry policy, executing only Planner. It prints and saves a typed JSON plan with dependencies and acceptance criteria. No proposed task is automatically executed. The separate `--run` command retains the original configured six-role workflow and defaults to placeholders. Plans are written under the host output directory's ignored `workspace/plans` folder.

Compatibility, provider configuration, commands, security boundaries and remaining work are documented in [the Milestone 2 guide](../docs/architecture/codex-milestone2.md). [The saved smoke-test plan](../docs/examples/codex-planner-smoke.json) is actual Codex output, not a hand-written example. The original Milestone 1 description below is historical; its statements about unavailable AI execution apply to the original default workflow, not the new `--plan` command.

The Milestone 1 foundation is **AI.GTX.Harness.slnx**. It is a separate, modular development tool inside the existing repository. It does not implement or change Billing, Inventory or ERP business functionality.

The superseded three-project prototype was removed during the user-requested cleanup. `AI.GTX.Harness.slnx` is the only solution. This README replaces the obsolete prototype guide; the active source, configuration and local SDK remain intact.

## Prerequisites and verification

The existing `global.json` pins stable .NET SDK **11.0.100**, targeting `net11.0` with the stable C# compiler supplied by that SDK (`latestMajor`, no preview opt-in). The system SDK detected during implementation was 10.0.403. SDK 11.0.100 was already present in `AI-Harness/.dotnet`; no SDK was installed or updated for this milestone.

Install the SDK required by `global.json` if unavailable. Verification never downloads or installs an SDK. The ASP.NET Core shared framework is used for Microsoft hosting, options, configuration, logging and dependency injection; this harness is a console application with no web server. NuGet restore downloads xUnit, its Visual Studio adapter and Microsoft.NET.Test.Sdk on first build. Package versions are pinned in the test project.

From `AI-Harness`:

```powershell
./verify-milestone1.ps1
# Optional explicit SDK location:
./verify-milestone1.ps1 -DotnetPath 'C:/Program Files/dotnet/dotnet.exe'
```

The script builds Release, runs xUnit with a TRX report, validates startup, and checks that a placeholder task returns exit code 4. With the local SDK, individual commands are:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.cli-home"
./.dotnet/dotnet.exe build AI.GTX.Harness.slnx -c Release
./.dotnet/dotnet.exe test AI.GTX.Harness.slnx -c Release --no-build
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --self-check
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --run 'Analyze a proposed Billing feature'
```

Exit codes: 0 = self-check passed or a completed workflow; 1 = invalid startup configuration or failed/blocked workflow; 2 = cancellation; 4 = AI execution not configured. The default provider always produces 4 for a submitted task. It never requests paid credentials or sends network requests.

## Structure

```text
AI-Harness/
  AI.GTX.Harness.slnx
  config/harness.json
  src/
    AI.GTX.Harness.Core/             contracts, role IDs, task and permission models
    AI.GTX.Harness.Orchestrator/     dependency execution, bounded retry, task lifecycle
    AI.GTX.Harness.Agents/           six role adapters and validated dynamic factory
    AI.GTX.Harness.Tools/            capability policy, restricted read/list gateway
    AI.GTX.Harness.Infrastructure/   DI wiring, provider resolver and placeholder adapter
    AI.GTX.Harness.Host/             console composition root and JSON logging
  tests/AI.GTX.Harness.Tests/         xUnit unit and DI integration tests
  verify-milestone1.ps1
docs/
  architecture/ai-harness-milestone1.md
  decisions/0001-harness-foundation.md
  requirements/harness-milestone1.md
  requirements/billing-phase1.md
```

## Implemented behavior

- The dynamic factory creates fresh agents from registered role constructors and approved configuration. It snapshots configuration, rejects unknown/disabled roles, validates permission ceilings and limits attempts to 1–3. Future role IDs are strings; add a registration and approved definition without changing the orchestrator.
- The task manager queues tasks in memory and serializes execution. Work follows Planner → Architect → Developer → Tester → Reviewer → Documentation. The orchestrator also supports custom acyclic dependency graphs, validates them before execution and blocks dependents when prerequisites fail.
- Only `RetryableAgentException` triggers bounded retries. Unexpected failures do not retry. Cancellation, a workflow timeout, and terminal task states are recorded. Automatic re-planning and repair are deferred.
- All six specialized adapters supply role-specific instructions through `IAiModelProvider`. `PlaceholderAiProvider` returns `Configured=false`. Even a provider returning text cannot mark engineering work complete: the adapters return `Blocked` until task execution and verification exist.
- Microsoft DI, Options validation, JSON configuration and JSON console logging are operational. Logs contain task IDs, role IDs, attempts and states. Raw task descriptions, model output, exception messages and credentials are not logged or printed by the new host.
- The tool gateway can read and list files in an explicitly scoped workspace, with path traversal, protected-file and existing-link rejection and size limits. It never creates a workspace or source file at startup.

## Configuration, credentials and approval boundaries

Edit the new `config/harness.json` for approved role definitions. It is copied to the host output directory at build. Relative workspace paths resolve against the host output directory, not the application's source directory. An example environment override is `GTX_Harness__TimeoutSeconds=300`; environment variables prefixed `GTX_` override the JSON settings. Unknown providers fail startup instead of silently falling back.

The placeholder has no credential dependency. `Provider:CredentialEnvironmentVariable` is metadata naming a future environment variable, not a secret value. Future adapters must read credentials from environment variables or a secret manager and must not log request authorization or provider bodies. Never put credentials in tasks, source, config, tool output or the selected workspace. Ignore rules reduce accidental additions but do not protect against explicitly forced Git additions or previously tracked secrets.

Role permissions are bounded by registrations. `StageChange`, `.NET build/test`, deletion, merges and production deployment require approval under the policy; these execution adapters are **not implemented** and cannot execute even with granted permission. The default approval service denies requests. No shell or generic executable interface is exposed. Git status/diff are designed capabilities only and are not yet executed.

Workspace validation is defense in depth, not an OS sandbox. Concurrent external filesystem changes can race path checks; use a dedicated, secret-free workspace and later enforce process/container isolation before enabling writes or builds. Do not point the reader at production secrets or the whole user profile.

## Validation and limitations

Milestone 1 verification: Release build passed with zero warnings/errors; **43 xUnit tests passed, zero failed or skipped**. Tests cover dependency ordering and cycles, retry bounds, failure/cancellation, factory configuration and permissions, path protection (including Windows path aliases), disabled execution adapters, all six placeholder agents, DI/config validation and honest placeholder status. The host startup and placeholder-task checks also passed. The preserved prototype's separate 16-test suite also passed.

These results prove the foundation behavior, not autonomous engineering capability. There is no live/local AI adapter, Codex runtime integration, tool-calling loop, code-generation execution, code-review validation, test-execution agent, durable task storage, multi-process lock, approval UI, authenticated service or deployment integration. Codex was used to implement and test this milestone; the custom harness does not invoke Codex.

Recommended Milestone 2: implement reviewed change proposals, an approval-backed execution broker in an isolated workspace, one tested provider adapter (local or cloud), bounded structured tool calls, observed test evidence and review checkpoints. Demonstrate a small non-business sample end to end before authorizing changes to Billing. Requirements discovery for Billing remains separate.

See the repository architecture, decision and requirements documents for details and unresolved questions. `CREATED-FILES.Milestone1.md` lists the foundation files and cleanup history.
