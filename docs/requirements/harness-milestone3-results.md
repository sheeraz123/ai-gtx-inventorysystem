# Milestone 3 results

Status: **partial implementation, blocked live end-to-end execution**. The security stop is intentional and required by the milestone. Do not describe live Developer, Tester or Reviewer as operational on this machine.

## Verified before implementation

- Existing Milestone 2 verification passed all 80 tests.
- Local .NET SDK 11.0.100 and Codex CLI 0.169.0-alpha.11.1 were already installed.
- Supported CLI help and sandbox help were inspected; ChatGPT login was confirmed without reading credential files.
- No Docker/Podman executable or verified OS-confined build/test backend was available. Read-only/workspace-write flags were not treated as proof of read isolation.

## Implemented and tested

Release build: zero warnings/errors. Combined suite: **123 passed, zero failed or skipped**, including all original 80 tests. Startup and explicit live-execution-block checks passed. The script validates safe behavior; it does not certify a completed live pipeline.

- Versioned persisted plans, semantic dependencies, scope/baseline hashing and non-inherited approvals.
- Separate durable plan/change decisions, human-readable preview, explicit approval/rejection, cancellation and task status.
- Codex structured Developer/Reviewer adapters, with an independent hard gate against unverified live execution.
- Scoped source snapshots, staged UTF-8 proposals, full-file diffs, before/after file manifests and unauthorized modification detection.
- Fixed-command Tester adapter and strict TRX evidence parsing. The actual OS-isolated command backend remains unavailable.
- Review decision/criteria/proposal validation and separation from human approval.
- Independent apply approval checks, source/stage stale-hash detection, atomic per-file replacement, recovery journal, rollback, and external-edit conflict preservation.
- Repeatable mocked workflow through AwaitingChangeApproval; application/rollback tests use only disposable unit-test targets and TEST-DOUBLE decisions.
- Real temporary-directory junction/symlink attacks are rejected. These validate the path checks, not OS process confinement.

## Live demonstration

The existing read-only Codex Planner generated the disposable greeting sample plan on 2026-11-15. The saved example is `docs/examples/milestone3-sample-plan.json`.

- Plan ID: `e880baa4-2a10-4679-862f-e75ddc3197bc`
- Revision: `1`
- Complete package SHA256: `761B4D8D19F4CAF48480171317F6C7E55BD3FECDDF83F8F5779D85D7BA238B46`
- State: Ready (plan-only approval recorded; independent isolation gate still blocks execution)
- Task chain: D1 Developer → T1 Tester → R1 Reviewer
- Scope: disposable Sample.slnx, its library/test project files, Greeting.cs and GreetingTests.cs.
- The user explicitly approved this exact plan and hash through the chat approval question. The normal plan-decision command recorded the receipt. No change approval exists, no live Developer/test/Reviewer ran, and no proposed source changes were applied.

The complete package, progress and plan-decision receipt are in the ignored host-output `workspace/milestone3/state/<plan-id-without-dashes>/v1` directory. The package hash binds the concrete local target root and baseline; the portable example contains only the model's plan and is not an approval record. The explicit user response approved the plan only. The OS-isolation check was repeated and remains unverified. Execution was not attempted and the workflow stays Ready; source application still requires its own later approval.

## Files

New implementation files: Core `DevelopmentWorkflow.cs`; Agents `DevelopmentAgents.cs`; Orchestrator `DevelopmentWorkflowController.cs`; Tools `WorkflowFiles.cs`, `DevelopmentWorkspace.cs`, `ApprovedBatchApplier.cs`; Infrastructure `WorkflowStore.cs`, `ExecutionIsolation.cs`, `TrxEvidenceReader.cs`, `DevelopmentDemo.cs`.

New tests: `DevelopmentWorkflowTests.cs`, `WorkspaceLinkAttackTests.cs`, `TrxEvidenceTests.cs`, `LiveExecutionGateTests.cs`. New script: `verify-milestone3.ps1`. New documentation: the architecture guide, this results record and the actual sample plan. Existing provider, DI and host were extended; README points to the new workflow. All Milestone 1/2 test source and verification scripts were preserved.

## Work required to finish Milestone 3

First implement a verified OS-isolated execution backend, not broader autonomy. Demonstrate negative access/network canaries and resource/process limits, then request approval for the existing plan before any live Developer execution. Preseed approved build dependencies without mounting user credentials. Generate the real proposal, run actual isolated tests, perform live structured review, and stop again for separate human change approval. These are unfinished Milestone 3 requirements, not completed deliverables.

Recommended Milestone 4, after that live demonstration: automatic crash recovery, authenticated/signed approval storage, production-quality metadata handling and broader multi-task scheduling before applying changes beyond disposable samples.

No global installation/upgrade, automatic approval, Billing implementation, push, merge, deployment or production modification was performed.
