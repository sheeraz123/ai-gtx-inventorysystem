param([ValidateSet('Release','Debug')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$application=Join-Path $PSScriptRoot "src/GTS.Billing.Presentation.Wpf/bin/$Configuration/net10.0-windows/GTS.Billing.Presentation.Wpf.exe"
if(-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "The $Configuration application has not been built. Build GTS.Billing.slnx with configuration $Configuration first."
}
# Stable entry point; configuration, ownership, identity and upgrade approval remain inside the application.
Start-Process -FilePath $application -WorkingDirectory (Split-Path -Parent $application) -WindowStyle Normal
