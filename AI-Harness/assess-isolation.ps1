#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

# Read-only host inventory. Does not enable features, start services/VMs, install, or elevate itself.
# The only writes are a new report under the harness's ignored workspace directory.
function Read-Capability([scriptblock]$Query) {
    try { [pscustomobject]@{ Known = $true; Value = (& $Query); ErrorType = $null } }
    catch { [pscustomobject]@{ Known = $false; Value = $null; ErrorType = $_.Exception.GetType().Name } }
}
function Invoke-WslQuery([string[]]$QueryArguments) {
    $command = Get-Command wsl.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (!$command) { return [pscustomobject]@{ Started = $false; ExitCode = $null; TimedOut = $false; ListedDistributionCount = $null } }
    $start = [Diagnostics.ProcessStartInfo]::new($command.Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $QueryArguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(10000)) {
            $process.Kill($true)
            $process.WaitForExit()
            return [pscustomobject]@{ Started = $true; ExitCode = $null; TimedOut = $true; ListedDistributionCount = $null }
        }
        $text = $stdout.GetAwaiter().GetResult().Replace([string][char]0, '')
        [void]$stderr.GetAwaiter().GetResult()
        # Do not publish distribution names, help dumps, environment variables or unrelated system data.
        $count = if ($process.ExitCode -eq 0 -and $QueryArguments -contains '--quiet') { @($text.Split("`n") | Where-Object { $_.Trim() }).Count } else { $null }
        return [pscustomobject]@{ Started = $true; ExitCode = $process.ExitCode; TimedOut = $false; ListedDistributionCount = $count }
    } catch {
        return [pscustomobject]@{ Started = $false; ExitCode = $null; TimedOut = $false; ListedDistributionCount = $null; ErrorType = $_.Exception.GetType().Name }
    } finally { $process.Dispose() }
}

$windows = Read-Capability {
    Get-ItemProperty 'HKLM:/SOFTWARE/Microsoft/Windows NT/CurrentVersion' |
        Select-Object ProductName, EditionID, DisplayVersion, CurrentBuildNumber, UBR
}
$administrator = Read-Capability {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
$computer = Read-Capability {
    Get-CimInstance Win32_ComputerSystem -Property HypervisorPresent, TotalPhysicalMemory -ErrorAction Stop |
        Select-Object HypervisorPresent, TotalPhysicalMemory
}
$processor = Read-Capability {
    @(Get-CimInstance Win32_Processor -Property VirtualizationFirmwareEnabled, VMMonitorModeExtensions, SecondLevelAddressTranslationExtensions -ErrorAction Stop |
        Select-Object VirtualizationFirmwareEnabled, VMMonitorModeExtensions, SecondLevelAddressTranslationExtensions)
}
$features = foreach ($featureName in @('Containers-DisposableClientVM', 'Microsoft-Hyper-V-All', 'VirtualMachinePlatform', 'Microsoft-Windows-Subsystem-Linux')) {
    try {
        $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction Stop
        [pscustomobject]@{ Name = $featureName; State = [string]$feature.State; QuerySucceeded = $true; ErrorType = $null }
    } catch {
        [pscustomobject]@{ Name = $featureName; State = 'Unknown'; QuerySucceeded = $false; ErrorType = $_.Exception.GetType().Name }
    }
}
$services = foreach ($serviceName in @('vmms', 'vmcompute', 'hns', 'LxssManager', 'WslService')) {
    try {
        $entry = Get-Service -Name $serviceName -ErrorAction Stop
        [pscustomobject]@{ Name = $serviceName; Found = $true; Status = [string]$entry.Status; QuerySucceeded = $true }
    } catch {
        $missing = $_.CategoryInfo.Category -eq [Management.Automation.ErrorCategory]::ObjectNotFound
        [pscustomobject]@{ Name = $serviceName; Found = if ($missing) { $false } else { $null }; Status = if ($missing) { 'NotFound' } else { 'Unknown' }; QuerySucceeded = $missing }
    }
}
$tools = foreach ($toolName in @('WindowsSandbox.exe', 'vmconnect.exe', 'docker.exe', 'podman.exe', 'wsl.exe', 'wslc.exe', 'hcsdiag.exe', 'runhcs.exe')) {
    [pscustomobject]@{ Name = $toolName; FoundOnPath = [bool](Get-Command $toolName -CommandType Application -ErrorAction SilentlyContinue) }
}
$report = [ordered]@{
    SchemaVersion = 1
    CollectedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Windows = $windows
    AdministratorProcess = $administrator
    Computer = $computer
    Processor = $processor
    OptionalFeatures = @($features)
    Services = @($services)
    Tools = @($tools)
    WindowsSandboxBinaryPresent = Test-Path -LiteralPath "$env:SystemRoot/System32/WindowsSandbox.exe"
    HyperVModulePresent = @(Get-Module -ListAvailable Hyper-V).Count -gt 0
    WslStatus = Invoke-WslQuery @('--status')
    WslDistributions = Invoke-WslQuery @('--list', '--quiet')
    IsolationVerified = $false
    NegativeOsTests = 'NotRun: no verified isolated runtime was selected or launched'
    GeneratedCodeExecuted = $false
    SystemChangesPerformed = $false
    Warning = 'Inventory is not a confinement attestation and cannot enable the harness execution gate.'
}
$outputRoot = Join-Path $PSScriptRoot 'workspace/isolation-assessment'
[void][IO.Directory]::CreateDirectory($outputRoot)
$outputPath = Join-Path $outputRoot (([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) + '-' + [Guid]::NewGuid().ToString('N') + '.json')
$json = $report | ConvertTo-Json -Depth 12
$file = [IO.FileStream]::new($outputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $file.Write($bytes)
} finally { $file.Dispose() }
Write-Output $json
Write-Output "Assessment report: $outputPath"
