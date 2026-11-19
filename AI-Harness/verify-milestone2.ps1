param([string]$DotnetPath = '', [switch]$RunCodexSmoke)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & ./verify-milestone1.ps1 -DotnetPath $DotnetPath
    if ($LASTEXITCODE -ne 4) { throw 'Baseline verification did not finish with the expected placeholder status' }
    if (!$DotnetPath) { $DotnetPath = if (Test-Path '.dotnet/dotnet.exe') { Join-Path $PSScriptRoot '.dotnet/dotnet.exe' } else { 'dotnet' } }
    & $DotnetPath run --project src/AI.GTX.Harness.Host -c Release --no-build -- --help
    if ($LASTEXITCODE -ne 0) { throw 'Host help failed' }
    if ($RunCodexSmoke) {
        & $DotnetPath run --project src/AI.GTX.Harness.Host -c Release --no-build -- --codex-check
        if ($LASTEXITCODE -ne 0) { throw 'Codex compatibility or ChatGPT authentication unavailable; smoke test not run' }
        & $DotnetPath run --project src/AI.GTX.Harness.Host -c Release --no-build -- --plan 'Plan a tiny greeting formatter library with unit tests. Tasks only; no tools or implementation.'
        if ($LASTEXITCODE -ne 0) { throw 'Live Codex Planner smoke test failed' }
    }
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
