# Milestone 1 file manifest

All paths below are relative to the existing repository root. The foundation comprises **34 files**. Initial implementation preserved the earlier prototype; subsequent user-authorized cleanup removed the unused prototype and promoted the foundation README to `AI-Harness/README.md`. Ignored SDK/build output, local CLI state and test result files are not source deliverables. No new repository, business implementation, deployment or Git merge was created.

```text
.gitignore
AI-Harness/AI.GTX.Harness.slnx
AI-Harness/README.md
AI-Harness/CREATED-FILES.Milestone1.md
AI-Harness/verify-milestone1.ps1
AI-Harness/config/harness.json
AI-Harness/src/AI.GTX.Harness.Core/AI.GTX.Harness.Core.csproj
AI-Harness/src/AI.GTX.Harness.Core/Models.cs
AI-Harness/src/AI.GTX.Harness.Core/Interfaces.cs
AI-Harness/src/AI.GTX.Harness.Orchestrator/AI.GTX.Harness.Orchestrator.csproj
AI-Harness/src/AI.GTX.Harness.Orchestrator/AgentOrchestrator.cs
AI-Harness/src/AI.GTX.Harness.Orchestrator/TaskManager.cs
AI-Harness/src/AI.GTX.Harness.Agents/AI.GTX.Harness.Agents.csproj
AI-Harness/src/AI.GTX.Harness.Agents/Agents.cs
AI-Harness/src/AI.GTX.Harness.Agents/DynamicAgentFactory.cs
AI-Harness/src/AI.GTX.Harness.Tools/AI.GTX.Harness.Tools.csproj
AI-Harness/src/AI.GTX.Harness.Tools/ToolPolicy.cs
AI-Harness/src/AI.GTX.Harness.Tools/WorkspaceReader.cs
AI-Harness/src/AI.GTX.Harness.Infrastructure/AI.GTX.Harness.Infrastructure.csproj
AI-Harness/src/AI.GTX.Harness.Infrastructure/Providers.cs
AI-Harness/src/AI.GTX.Harness.Infrastructure/ServiceCollectionExtensions.cs
AI-Harness/src/AI.GTX.Harness.Host/AI.GTX.Harness.Host.csproj
AI-Harness/src/AI.GTX.Harness.Host/Program.cs
AI-Harness/tests/AI.GTX.Harness.Tests/AI.GTX.Harness.Tests.csproj
AI-Harness/tests/AI.GTX.Harness.Tests/TestSupport.cs
AI-Harness/tests/AI.GTX.Harness.Tests/FactoryTests.cs
AI-Harness/tests/AI.GTX.Harness.Tests/OrchestratorTests.cs
AI-Harness/tests/AI.GTX.Harness.Tests/TaskManagerTests.cs
AI-Harness/tests/AI.GTX.Harness.Tests/ToolTests.cs
AI-Harness/tests/AI.GTX.Harness.Tests/IntegrationTests.cs
docs/architecture/ai-harness-milestone1.md
docs/decisions/0001-harness-foundation.md
docs/requirements/harness-milestone1.md
docs/requirements/billing-phase1.md
```

The active `global.json`, `Directory.Build.props`, local SDK and CLI state remain in use and were retained. The repository root README is unchanged. Removed unused artifacts: `src/Harness.Core`, `src/Harness.Cli`, `tests/Harness.Tests`, `AI-Harness.slnx`, old `appsettings.json`, old `verify.ps1`, `dotnet-install.ps1`, obsolete README, redundant nested `.gitignore`, prototype logs and its empty workspace. See `docs/decisions/0001-harness-foundation.md` for the cleanup decision.
