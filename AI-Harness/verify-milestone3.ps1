param([string]$DotnetPath = '')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & ./verify-milestone2.ps1 -DotnetPath $DotnetPath
    if ($LASTEXITCODE -ne 0) { throw 'Previous milestone regression checks failed' }
    if (!$DotnetPath) { $DotnetPath = if (Test-Path '.dotnet/dotnet.exe') { Join-Path $PSScriptRoot '.dotnet/dotnet.exe' } else { 'dotnet' } }
    & $DotnetPath run --project src/AI.GTX.Harness.Host -c Release --no-build -- --m3-check
    if ($LASTEXITCODE -ne 4) { throw 'Expected the current unverified isolation backend to fail closed. Review backend verification before changing this check.' }
    Write-Output 'Regression verification passed. Live Developer/test demo is blocked by unverified OS isolation; no human approvals were granted.'
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
