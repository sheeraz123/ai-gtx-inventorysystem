param([string]$DotnetPath = '')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & ./verify-milestone3.ps1 -DotnetPath $DotnetPath
    if ($LASTEXITCODE -ne 0) { throw 'Existing milestone verification failed' }
    & ./assess-isolation.ps1
    Write-Output 'Assessment and fail-closed regressions completed. OS confinement tests were NOT RUN; no generated workload or backend was enabled.'
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
