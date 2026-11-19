# ADR 0001: Preserve existing work and add a modular foundation

Status: Adopted for Milestone 1.

## Context

The repository initially contained only a tracked README. At the latest inspection, the user had moved the earlier harness into top-level `AI-Harness`. It contains an existing solution, README, source, custom test runner and a local .NET 11 SDK. The latest instruction prohibits overwriting existing project files and limits work to the foundation.

## Decision

Add `AI.GTX.Harness.slnx`, six `AI.GTX.Harness.*` projects and a separate xUnit test project beside the existing solution. Keep its files byte-for-byte unchanged. Use `README.Milestone1.md` instead of replacing its README. New root documentation describes the foundation and records Billing requirements without implementing them. Use the existing SDK 11.0.100 and existing stable-language build settings; do not install or update SDKs. A machine without a compatible SDK must install the pinned stable SDK explicitly.

Use Microsoft framework DI, configuration, options and JSON logging, plus xUnit tests. Use a placeholder provider for credential-free startup. Real model text cannot certify completed engineering work. Keep tool execution unavailable until approvals, execution isolation and observed verification are implemented together.

## Consequences

The initial implementation temporarily retained two solutions. After the user requested removal of unused earlier files, the superseded prototype, its configuration, verification script, installer script, logs and empty workspace were removed. The foundation README was promoted to `AI-Harness/README.md`; the redundant nested ignore file was removed because the root ignore rules cover it. The active SDK, build settings, foundation source and documentation remain. `AI.GTX.Harness.slnx` is now the only solution.

The foundation intentionally lacks a live agent execution loop, durable queue and mutation/terminal adapters. Those belong in a tested Milestone 2.
