#requires -Version 7.0
[CmdletBinding()]
param([Guid]$RunId = [Guid]::NewGuid())
$ErrorActionPreference = 'Stop'
$outputRoot = Join-Path $PSScriptRoot 'workspace/windows-readiness'
[void][IO.Directory]::CreateDirectory($outputRoot)
$outputPath = Join-Path $outputRoot ($RunId.ToString('N') + '.json')
if (Test-Path -LiteralPath $outputPath) { throw 'Readiness report already exists; choose a new RunId.' }
$report = [ordered]@{ SchemaVersion = 1; CollectedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); RunId = $RunId; AssessmentComplete = $false; SystemChangesPerformed = $false; IsolationVerified = $false }
function Read-Fact([scriptblock]$Query) {
    try { [pscustomobject]@{ Known = $true; Value = (& $Query); ErrorType = $null } }
    catch { [pscustomobject]@{ Known = $false; Value = $null; ErrorType = $_.Exception.GetType().Name } }
}
try {
    $administrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $report.AdministratorProcess = $administrator
    if (!$administrator) { throw [UnauthorizedAccessException]::new('Administrator PowerShell 7 is required; no auto-elevation is performed by this script.') }
    # Invoke the original read-only assessment without replacing it or changing any feature.
    $baselineOutput = & (Join-Path $PSScriptRoot 'assess-isolation.ps1')
    $report.BaselineReport = ($baselineOutput | Where-Object { $_ -is [string] -and $_.StartsWith('Assessment report: ') } | Select-Object -Last 1) -replace '^Assessment report: ', ''
    $report.Windows = Read-Fact {
        $version = Get-ItemProperty 'HKLM:/SOFTWARE/Microsoft/Windows NT/CurrentVersion'
        $os = Get-CimInstance Win32_OperatingSystem -Property OSArchitecture, Version, BuildNumber, DataExecutionPrevention_Available, TotalVisibleMemorySize, FreePhysicalMemory
        [pscustomobject]@{ ProductName = $version.ProductName; EditionID = $version.EditionID; DisplayVersion = $version.DisplayVersion;
            Build = "$($version.CurrentBuildNumber).$($version.UBR)"; Architecture = $os.OSArchitecture; Version = $os.Version;
            DEPAvailable = $os.DataExecutionPrevention_Available; TotalMemoryMiB = [math]::Round($os.TotalVisibleMemorySize / 1024);
            FreeMemoryMiB = [math]::Round($os.FreePhysicalMemory / 1024) }
    }
    $report.Virtualization = Read-Fact {
        [pscustomobject]@{
            Computer = Get-CimInstance Win32_ComputerSystem -Property HypervisorPresent | Select-Object HypervisorPresent
            Processor = @(Get-CimInstance Win32_Processor -Property AddressWidth, DataWidth, NumberOfCores, NumberOfLogicalProcessors, VirtualizationFirmwareEnabled, VMMonitorModeExtensions, SecondLevelAddressTranslationExtensions |
                Select-Object AddressWidth, DataWidth, NumberOfCores, NumberOfLogicalProcessors, VirtualizationFirmwareEnabled, VMMonitorModeExtensions, SecondLevelAddressTranslationExtensions)
        }
    }
    $report.Features = @(foreach ($featureName in @('Microsoft-Hyper-V-All', 'Microsoft-Hyper-V-Hypervisor', 'Microsoft-Hyper-V-Services', 'Microsoft-Hyper-V-Management-PowerShell', 'Containers-DisposableClientVM', 'VirtualMachinePlatform', 'Microsoft-Windows-Subsystem-Linux')) {
        try {
            $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction Stop
            [pscustomobject]@{ Name = $featureName; QuerySucceeded = $true; State = [string]$feature.State; ErrorType = $null }
        } catch { [pscustomobject]@{ Name = $featureName; QuerySucceeded = $false; State = 'Unknown'; ErrorType = $_.Exception.GetType().Name } }
    })
    $report.Storage = Read-Fact {
        $relevantDrives = @($env:SystemDrive, [IO.Path]::GetPathRoot($PSScriptRoot).TrimEnd('\')) | Select-Object -Unique
        @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Where-Object { $_.DeviceID -in $relevantDrives } |
            Select-Object DeviceID, FileSystem, @{Name='SizeGiB';Expression={[math]::Round($_.Size / 1GB, 1)}}, @{Name='FreeGiB';Expression={[math]::Round($_.FreeSpace / 1GB, 1)}})
    }
    $report.RecentHotfixes = Read-Fact {
        @(Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 5 HotFixID, @{Name='InstalledOn';Expression={if ($_.InstalledOn) { $_.InstalledOn.ToString('yyyy-MM-dd') } else { $null }}})
    }
    $report.EsuLicenseIndicators = Read-Fact {
        # Project only non-secret fields. Never request product keys, activation IDs or tokens.
        @(Get-CimInstance -Query "SELECT Name, LicenseStatus FROM SoftwareLicensingProduct WHERE Name LIKE '%ESU%'" -ErrorAction Stop |
            Select-Object Name, LicenseStatus)
    }
    $report.PendingReboot = Read-Fact {
        [pscustomobject]@{
            ComponentServicing = Test-Path 'HKLM:/SOFTWARE/Microsoft/Windows/CurrentVersion/Component Based Servicing/RebootPending'
            WindowsUpdate = Test-Path 'HKLM:/SOFTWARE/Microsoft/Windows/CurrentVersion/WindowsUpdate/Auto Update/RebootRequired'
        }
    }
    $report.BootPrerequisites = Read-Fact {
        # Read only. Publish only prerequisite flags, never the complete boot configuration.
        $bootLines = & "$env:SystemRoot/System32/bcdedit.exe" /enum '{current}' 2>&1
        $exitCode = $LASTEXITCODE
        $flags = @($bootLines | ForEach-Object { if ($_ -match '^\s*(hypervisorlaunchtype|nx)\s+(\S+)') { [pscustomobject]@{ Name = $Matches[1]; Value = $Matches[2] } } })
        [pscustomobject]@{ ExitCode = $exitCode; ExplicitFlags = $flags; UnlistedFlags = 'Not explicitly reported; no boot-setting change attempted' }
    }
    $report.ServicingAssessment = 'Installed build/update observations only. ESU enrollment and update-policy compliance are not proven; no licensing keys or authentication data queried.'
    $report.AssessmentComplete = $true
} catch {
    $report.ErrorType = $_.Exception.GetType().Name
    $report.Error = if ($_.Exception -is [UnauthorizedAccessException]) { 'Administrator session unavailable; assessment not completed' } else { 'Read-only assessment failed; sensitive exception details omitted' }
} finally {
    $json = $report | ConvertTo-Json -Depth 16
    $file = [IO.FileStream]::new($outputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $file.Write([Text.Encoding]::UTF8.GetBytes($json)) } finally { $file.Dispose() }
    Write-Output "Readiness report: $outputPath"
}
if (!$report.AssessmentComplete) { exit 2 }
