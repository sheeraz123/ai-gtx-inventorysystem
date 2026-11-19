# Milestone 2 verification and file changes

## Results

- Baseline: original verification passed with all 43 Milestone 1 tests before implementation.
- Final Release build: zero warnings and zero errors.
- Combined test suite: 80 passed, zero failed, zero skipped (the original 43 tests plus 37 additional cases).
- Baseline startup/default-placeholder checks and new host-help check passed.
- Real provider compatibility check passed with installed `codex-cli 0.169.0-alpha.11.1` and ChatGPT login.
- Live Planner smoke passed: workflow `dca0ccfa-4d59-4286-868c-e708f2420a51`, one attempt, CLI exit 0, Planner Completed, validated three-task plan. See `docs/examples/codex-planner-smoke.json`.
- No global tools were installed/upgraded. No credentials were printed or added to source. No Billing code, production data, push, merge or deployment was performed.

The original test source and Milestone 1 verification script were retained. The original architecture and README remain with a pointer to the Milestone 2 extension. Default placeholder startup still works without credentials. Live tests are explicit opt-in and use ChatGPT Codex allowance.

## New files

- `AI-Harness/src/AI.GTX.Harness.Core/Planning.cs`: provider interface/errors and typed, validated planning contract/schema.
- `AI-Harness/src/AI.GTX.Harness.Core/Changes.cs`: reviewable proposal/service contracts.
- `AI-Harness/src/AI.GTX.Harness.Infrastructure/Codex/ControlledProcessRunner.cs`: safe process invocation, bounds, cancellation and redaction.
- `AI-Harness/src/AI.GTX.Harness.Infrastructure/Codex/CodexOptions.cs`: approved execution options and native executable discovery.
- `AI-Harness/src/AI.GTX.Harness.Infrastructure/Codex/CodexExecutionProvider.cs`: compatibility/auth probes, fixed read-only Planner command and provider bridge.
- `AI-Harness/src/AI.GTX.Harness.Tools/ChangeProposalService.cs`: staged one-file diffs, explicit approvals and stale/tampered-content checks.
- `AI-Harness/config/codex.json`: non-secret provider, planning and change-workspace configuration.
- `AI-Harness/verify-milestone2.ps1`: repeatable regression verification and opt-in live smoke.
- `AI-Harness/tests/AI.GTX.Harness.Tests/CodexTests.cs`, `ProcessRunnerTests.cs`, `ChangeProposalTests.cs`: new test cases.
- `AI-Harness/tests/AI.GTX.Harness.ProcessProbe/AI.GTX.Harness.ProcessProbe.csproj` and `Program.cs`: trusted test-only child executable.
- `docs/architecture/codex-milestone2.md`: compatibility, provider setup, workflow, security and limitations.
- `docs/examples/codex-planner-smoke.json`: actual reviewed smoke output.
- This results document.

## Extended files

- Core models/interfaces: optional plan/error results and a planner-only queue entry point.
- Existing Planner adapter: validates structured plan evidence while keeping other roles unavailable.
- Existing Task Manager and Orchestrator: reuse lifecycle/retry machinery, carry typed results and expose planner-only execution.
- Infrastructure DI: registers Codex options, process runner, provider and model bridge.
- Host and its project file: new check/plan/propose commands, copied non-secret config and review artifacts.
- Solution and test project: include the trusted process-test helper.
- Workspace reader: protects additional `.codex`, `.agents` and `.aws` paths.
- Harness README and original architecture guide: point to current functionality while retaining milestone history.

## Milestone 3 recommendation

Add an approved source-snapshot workflow and typed Developer proposals tied to a validated plan. Then add durable human approval records, isolated allowlisted build/test execution and Reviewer evidence. Demonstrate a small non-business sample before authorizing changes to Billing. Multi-file atomic apply/rollback, durable queue state, container isolation and broader model support remain deferred.
