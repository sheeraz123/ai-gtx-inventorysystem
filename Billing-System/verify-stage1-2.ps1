[CmdletBinding()]
param([switch]$SkipRestore, [switch]$SkipWpfSmoke)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
function Assert-Exit([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)." } }
if (-not $SkipRestore) { dotnet restore GTS.Billing.slnx --configfile NuGet.Config; Assert-Exit 'Restore' }
dotnet build GTS.Billing.slnx -c Release --no-restore; Assert-Exit 'Build'
dotnet test GTS.Billing.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=stage1-2.trx' --results-directory TestResults; Assert-Exit 'Tests'
if (-not $SkipWpfSmoke) {
    $exe = Join-Path $PSScriptRoot 'src/GTS.Billing.Presentation.Wpf/bin/Release/net10.0-windows/GTS.Billing.Presentation.Wpf.exe'
    $report = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'GTS Billing/Logs/smoke.json'
    $started = [DateTime]::UtcNow
    $process = Start-Process -FilePath $exe -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(20000)) { throw 'WPF smoke timeout. The process retains ownership; inspect it before taking further action.' }
    if ($process.ExitCode -ne 0) { throw "WPF smoke failed (exit $($process.ExitCode))." }
    $smoke = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $smoke.windowLoaded -or -not $smoke.windowVisible -or $smoke.modulesNavigated -ne 19 -or -not $smoke.allPlanned -or [DateTime]$smoke.timestampUtc -lt $started) { throw 'WPF smoke evidence is invalid or stale.' }
    Write-Output 'WPF loaded; 19 planned navigation targets exercised. This is not manual visual acceptance.'
}
$databasePaths = git ls-files --cached --others --exclude-standard -- '*.db' '*.db-*' '*.sqlite' '*.sqlite-*'
Assert-Exit 'Git database inclusion check'
if ($databasePaths) { throw 'An unignored database file was found under Billing-System.' }
Write-Output 'Billing verification completed. Plaintext DEVELOPMENT ONLY.'
