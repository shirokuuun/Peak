param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\Peak'))
$ErrorActionPreference = 'Stop'
$notchRoot = Split-Path -Parent $PSScriptRoot
$notchSource = Join-Path $notchRoot 'dist\Peak'
if (-not (Test-Path -LiteralPath (Join-Path $notchSource 'Peak.exe'))) { throw 'Run scripts/publish.ps1 first.' }
if ((Get-Process -Name Peak,UsageNotch -ErrorAction SilentlyContinue)) { throw 'Exit Peak from its tray menu before installing.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -Path (Join-Path $notchSource '*') -Destination $Destination -Recurse -Force
$notchShell = New-Object -ComObject WScript.Shell
$notchShortcut = $notchShell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Peak.lnk'))
$notchShortcut.TargetPath = Join-Path $Destination 'Peak.exe'
$notchShortcut.IconLocation = (Join-Path $Destination 'Peak.exe') + ',0'
$notchShortcut.WorkingDirectory = $Destination
$notchShortcut.Description = 'Peak - AI usage and task activity'
$notchShortcut.Save()

# Keep an existing UsageNotch entry useful after the product rename.
$notchLegacyShortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'UsageNotch.lnk'
if (Test-Path -LiteralPath $notchLegacyShortcutPath -PathType Leaf) {
    $notchLegacyShortcut = $notchShell.CreateShortcut($notchLegacyShortcutPath)
    if ([IO.Path]::GetFileName($notchLegacyShortcut.TargetPath) -in 'UsageNotch.exe', 'Peak.exe') {
        $notchLegacyShortcut.TargetPath = Join-Path $Destination 'Peak.exe'
        $notchLegacyShortcut.IconLocation = (Join-Path $Destination 'Peak.exe') + ',0'
        $notchLegacyShortcut.WorkingDirectory = $Destination
        $notchLegacyShortcut.Arguments = ''
        $notchLegacyShortcut.Description = 'Peak - formerly UsageNotch'
        $notchLegacyShortcut.Save()
    }
}
Write-Output "Installed for this user: $Destination. Open Peak from Start."
