# Milestone 3 — gated development pipeline

Status: persisted approval workflow and mocked end-to-end pipeline implemented. **The live end-to-end milestone is incomplete and blocked by unverified operating-system isolation.** A real Planner created a sample plan; no live Developer, generated build/test, Reviewer, or source application was performed.

Milestone 3.1 inspected host technologies and confirmed the missing runtime prerequisites. See [the capability assessment and required approvals](secure-execution-milestone3-1.md). The approved sample plan remains preserved; no live workload was enabled.

## Baseline and critical security assessment

Before changes, `verify-milestone2.ps1` passed all 80 tests and startup checks using the existing local SDK 11.0.100. The installed Codex CLI remains `0.169.0-alpha.11.1`; `exec --help` and `sandbox --help` were inspected, and `login status` confirmed ChatGPT authentication. No global tool was installed or upgraded. No AGENTS.md was found in the repository or checked ancestor locations.

The previous single-file proposal service has no multi-file rollback, durable approvals, or durable recovery. Existing path checks reject traversal and links at rest but cannot establish OS confinement or prevent hostile concurrent path replacement. Codex advertises a Windows restricted-token sandbox and read-only/workspace-write execution controls. Those flags do not prove the generated process cannot read credentials or production directories. No Docker or Podman executable was found. This implementation does not enable/install virtualization or a sandbox, create machine accounts, change ACLs/network rules or assert that WSL availability is sufficient isolation.

Accordingly, `UnavailableExecutionIsolation` reports unverified status. Both the workflow controller and Codex structured-role provider stop live Developer/Reviewer execution. `UnavailableIsolatedCommandExecutor` never falls back to the normal host process runner. Human plan approval cannot bypass that gate. A trusted verified implementation, not a configuration boolean, is required to enable these stages.

## Components and data flow

```mermaid
flowchart LR
    Goal --> Planner[Existing Codex Planner]
    Planner --> Plan[Versioned plan + source baseline + scope hash]
    Plan --> HumanPlan[Explicit plan decision]
    HumanPlan --> Gate[Verified OS isolation gate]
    Gate --> Developer[Structured Developer proposal]
    Developer --> Stage[Scoped staging + diff + manifest]
    Stage --> Tester[Fixed isolated build/test templates]
    Tester --> Reviewer[Structured review + acceptance criteria]
    Reviewer --> Pause[Awaiting separate human change approval]
    Pause --> Apply[Hash-checked atomic file replacements]
    Apply --> Recovery[Journal and rollback on partial failure]
```

Core adds workflow contracts, plan packages, approval records, manifests, command/test/review evidence and states. Tools owns snapshots, scope validation, staging, full-file diffs and the application broker. Infrastructure owns durable JSON storage, DI, the sample initializer, strict TRX parsing and the unavailable execution-backend implementations. Agents owns the typed Developer, Tester and Reviewer adapters. Orchestrator owns the persisted workflow transitions. Host exposes explicit preview/decision/run/apply/cancel commands.

The existing Planner/provider, factory, legacy task manager and original commands are retained. The existing Codex process adapter is generalized to schema-constrained responses, with the same argument safety, ChatGPT authentication, read-only/tool-disabled invocation, bounded captures and error handling. Developer and Reviewer use that shared adapter, but cannot call it live without verified isolation. Model-generated code is returned as typed file contents, never as shell commands or direct main-workspace writes.

This smallest workflow accepts exactly one Developer, one Tester and one Reviewer task. Dependencies must be acyclic and each selected task must be eligible before running. No task is marked complete merely because a model describes work. Other role sequences and multiple Developer tasks need a later scheduler extension. Model/command failures stop the workflow; potentially side-effecting stages are not automatically replayed. The existing Planner retry behavior remains unchanged.

## Persisted plans and decisions

`WorkflowStore` creates a revision directory containing immutable `plan.json` and mutable `progress.json`. Plan packages use schema version 1, a stable plan ID, monotonically increasing revision, previous-revision hash, structured plan, concrete target root, exact source scope, and the whole target file baseline. SHA256 binds the complete package. Loading verifies the hash and semantic task dependencies. A new revision gets no inherited approval.

`plan-decision.json` and `changes-decision.json` are separate latest-decision pointers. Append-only `decision-<GUID>.json` receipts retain actor, UTC time, subject, content hash and approved/rejected decision. A plan receipt approves execution of that exact scope/baseline only. A change receipt hashes the proposal, manifest, diff, test evidence and review evidence. It does not authorize a different proposal or later revision. Missing, rejected or mismatched decisions fail closed. The console requires exact typed approval after displaying the relevant plan or change evidence.

These are local, single-user audit records, not cryptographic user authentication. Someone who controls the harness binaries or can rewrite all state and approvals as the same OS user is outside the trust model. State directories need OS protection before multi-user deployment. File locks serialize cooperating workflow commands. There is no distributed queue, external identity provider, signed approval, or hostile-local-user protection.

## Workspaces and manifests

The approved target and staging/state roots must be separate. The sample uses generated target and staging directories under the host's ignored `workspace/milestone3` folder; Billing is never selected. Preparation verifies the whole target baseline and copies only explicitly scoped files into a new GUID staging directory. Source changes are validated as a complete list before staging: no absolute paths, traversal, alternate streams, protected control/credential directories, Windows aliases, duplicate paths, source deletions, files outside the approved scope or oversized content.

Snapshots validate each directory before recursing and reject existing symlinks/junctions. They bound file count and byte size. Manifests include every proposed file, before/after SHA256 and byte length; the proposal hash binds its plan/task, workspace, manifest and full-file review diff. Unexpected staged files and changed source bytes are rejected. After testing, only known build-output directories derived from scoped project paths and fixed TestResults are ignored in source integrity checks; symlinks remain forbidden even there. Added empty directories, timestamps and ACLs are not part of content manifests.

Real junction/symlink tests demonstrate rejection of pre-existing target links, links inserted into staged workspaces, linked plan snapshots and linked Codex workspaces. These are filesystem validator tests, **not** proof that arbitrary generated processes are confined by the OS. Hostile concurrent replacement, arbitrary local administrators, hard-link provenance and native file metadata remain security assumptions requiring additional production hardening.

## Developer, Tester and Reviewer

The Developer independently requires a persisted approved plan, matching plan hash, eligible task and Running development state. It submits only approved task/source text to Codex, requests a typed list of complete UTF-8 files and validates scope. The broker stages changes; Codex never directly edits application source. Without verified isolation, the real adapter refuses before launching a process.

The Tester accepts only a fixed `TestCommand` enum, not arbitrary executable/argument strings from the model. The backend contract defines build against `Sample.slnx --no-restore --configuration Release` and test against `tests/Sample.Tests/Sample.Tests.csproj --no-build --no-restore --configuration Release --logger trx --results-directory TestResults`. A verified backend must preseed approved dependencies, bound/redact logs, propagate cancellation, kill process trees on timeout, deny network and secret access, and provide actual exit codes and TRX evidence. This backend is not implemented on this machine. The fixed templates alone are not a sandbox.

`TrxEvidenceReader` safely parses one expected result file with DTD/external entities prohibited, document limits and consistent executed/total/pass/fail counts. Zero or skipped/incomplete execution cannot become success. A parsed TRX file alone does not establish process execution, sandbox provenance, test quality or genuine acceptance-criteria satisfaction. Those require trusted execution evidence and code review. The controller requires nonzero passing counts and successful build/test evidence before review.

The Reviewer receives the complete proposed diff/manifest, task criteria and test evidence. Its schema requires a proposal hash, checked criteria, structured findings and ReadyForHumanReview/ChangesRequested/Blocked. A mismatched proposal, missing criteria or high/critical findings combined with ReadyForHumanReview is rejected. The reviewer never writes a human decision. Simulated test evidence cannot authorize real batch application.

## Human change approval and rollback

The workflow pauses at AwaitingChangeApproval after successful test/review evidence. The preview includes target, manifest, complete diff, test results and review. Source/stage hashes are checked both before the prompt and again at application. A separate change decision is recorded for the evidence-bound content hash.

The application broker reloads the persisted plan and independently checks plan/change decisions, proposal identity and successful evidence. It validates paths/scope, preflights the target baseline, persists original bytes and an intent manifest, and uses atomic per-file replacement. Exceptions trigger rollback of attempted files, including an exception after replacement. Rollback restores only files still matching the proposed content; unexpected external edits are preserved and yield RecoveryRequired. Added files are removed when rollback can safely identify them. Rollback may leave empty directories.

This is not a multi-file filesystem transaction. Other processes can observe intermediate files. Process/OS crashes require inspecting the persisted journal; automatic crash recovery is not implemented. File ACLs, alternate streams, metadata and hard links are not preserved as a production deployment tool would require. No application mutation was performed for the live demonstration. The original Milestone 2 single-file command remains for compatibility; use the new broker for versioned reviewed batches.

## Commands and demonstration

From `AI-Harness`:

```powershell
./verify-milestone3.ps1
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --m3-check
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --m3-demo
```

`--m3-check` currently returns 4 to report the isolation blocker. The verification script explicitly expects that block; its success does not mean the live workflow is operational. `--m3-demo` makes an explicit live Planner request, consumes the existing ChatGPT Codex allowance, creates a deterministic disposable baseline sample, and saves a plan in AwaitingPlanApproval. It does not approve, execute, test or apply the plan.

Use the returned ID/revision:

```powershell
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --m3-status <id> 1
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --m3-plan-decision <id> 1
# Type APPROVE PLAN <full displayed hash> only if you approve the complete plan/scope.
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --m3-run <id> 1
# Current result: Blocked, no live Developer or test process.
./.dotnet/dotnet.exe run --project src/AI.GTX.Harness.Host -c Release -- --m3-cancel <id> 1
```

`--m3-apply <id> 1` is only eligible for an actually tested/reviewed proposal at AwaitingChangeApproval and requires `APPROVE CHANGES <full displayed hash>`. It cannot apply the current pending sample plan. Wrong input/EOF records rejection; Ctrl+C cancels a waiting prompt without manufacturing approval. Cancellation is persisted through `--m3-cancel`. Failed/rejected/canceled revisions require a fresh reviewed revision; an isolation-blocked plan can retry only after a verified backend exists.

## Required next security work

Implement and demonstrate a dedicated OS/container/VM execution boundary before enabling live Developer/test/review. Use a separate restricted identity, no host credential/profile or production mounts, an explicit SDK/snapshot/preapproved dependency allowlist, a minimal environment, bounded CPU/memory/time/processes, no direct network, and tested descendant termination. Verify positive allowed operations and negative canary read/write/network attempts outside the task scope, without touching real secrets. Only then replace the unavailable services with the verified backend. Human plan approval alone must never enable it.

No Billing functionality, push, merge, deployment or production data changes are included.
