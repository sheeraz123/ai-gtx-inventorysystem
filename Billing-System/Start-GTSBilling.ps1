param([ValidateSet('Release','Debug')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$project='src/GTS.Billing.Presentation.Wpf/GTS.Billing.Presentation.Wpf.csproj'
Push-Location -LiteralPath $PSScriptRoot
try {
    # Build and launch the same project in one command so a failed build cannot start an older binary.
    dotnet run --configuration $Configuration --project $project
    if ($LASTEXITCODE -ne 0) {
        throw "GTS Billing build or launch exited with code $LASTEXITCODE. See the build output or startup dialog for details."
    }
} finally {
    Pop-Location
}
