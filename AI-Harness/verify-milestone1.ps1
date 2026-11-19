param([string]$DotnetPath = '')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.cli-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    if (!$DotnetPath) {
        $DotnetPath = if (Test-Path '.dotnet/dotnet.exe') { Join-Path $PSScriptRoot '.dotnet/dotnet.exe' } else { 'dotnet' }
    }
    & $DotnetPath --version
    if ($LASTEXITCODE -ne 0) { throw 'Install the stable SDK required by global.json. This script never installs SDKs.' }
    & $DotnetPath build AI.GTX.Harness.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $DotnetPath test AI.GTX.Harness.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=milestone1.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & $DotnetPath run --project src/AI.GTX.Harness.Host -c Release --no-build -- --self-check
    if ($LASTEXITCODE -ne 0) { throw 'Startup self-check failed' }
    & $DotnetPath run --project src/AI.GTX.Harness.Host -c Release --no-build -- --run 'Validate placeholder workflow'
    if ($LASTEXITCODE -ne 4) { throw 'Expected NotConfigured (exit 4); placeholder must not claim successful engineering work' }
} finally { Pop-Location }
