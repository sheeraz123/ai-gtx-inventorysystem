# Harness Milestone 1 acceptance record

Scope: repository inspection, SDK detection, modular solution and projects, core agent/provider/tool contracts, configured factory, dependent workflow and task lifecycle, Microsoft DI/logging/configuration, placeholder provider, tests and documentation. Codex is the engineer building this foundation; runtime Codex integration is not implemented.

Verified behavior:

- Initial implementation preserved existing source and project files; later user-authorized cleanup removed the superseded prototype and replaced its README with the foundation guide. No business application files were changed and no new Git repository was initialized.
- Six role adapters register with explicit maximum tool permissions; unknown/disabled roles and configuration escalation are rejected.
- Dependency cycles/missing IDs are rejected; failed prerequisites block dependents; transient retries are bounded; cancellation and failure are terminal states.
- Startup does not require an API key, send a model request, create a workspace or execute a command.
- Placeholder task runs return NotConfigured; downstream steps are Blocked. A model returning text alone cannot report engineering completion.
- Read/list are workspace-scoped; mutating and executable tool adapters are absent. The default approval service denies; policy reserves approval checkpoints for high-impact capabilities.
- Release build and xUnit tests run through `AI-Harness/verify-milestone1.ps1`; TRX results are local ignored artifacts.

Excluded: Billing code, autonomous feature implementation, live or local model transport, shell tools, Git changes/merges, production deployment, persistent jobs, background agent service, parallel workers and automatic remediation.

Milestone 2 acceptance should include a reviewable change proposal tied to an immutable approval, isolated build/test execution, recorded test evidence, real provider error/cancellation handling, and one small reproducible end-to-end sample. Business source changes need explicit authorization.
