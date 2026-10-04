$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'ActionBridge.exe'
if (-not (Test-Path $source)) { throw 'Keep Install-Windows.ps1 next to ActionBridge.exe.' }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\ActionBridge'
New-Item -ItemType Directory -Force $destination | Out-Null
$session = (Get-Process -Id $PID).SessionId
Get-Process -Name ActionBridge,ActionBridge.Remote -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session | Stop-Process
Copy-Item $source (Join-Path $destination 'ActionBridge.exe') -Force
$remoteExe = Join-Path $PSScriptRoot 'ActionBridge.Remote.exe'
if (Test-Path $remoteExe) { Copy-Item $remoteExe $destination -Force }
Copy-Item (Join-Path $PSScriptRoot 'Uninstall-Windows.ps1') $destination -Force
$exe = Join-Path $destination 'ActionBridge.exe'
$escaped = $exe.Replace("'", "''")
$remoteEscaped = (Join-Path $destination 'ActionBridge.Remote.exe').Replace("'", "''")
$firewall = @"
`$ErrorActionPreference = 'Stop'
Get-NetFirewallRule -DisplayName 'ActionBridge LAN TCP', 'ActionBridge LAN UDP', 'ActionBridge remote UDP' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName 'ActionBridge LAN TCP' -Direction Inbound -Action Allow -Program '$escaped' -Protocol TCP -LocalPort 45833 -Profile Private -RemoteAddress LocalSubnet | Out-Null
New-NetFirewallRule -DisplayName 'ActionBridge LAN UDP' -Direction Inbound -Action Allow -Program '$escaped' -Protocol UDP -LocalPort 45832 -Profile Private -RemoteAddress LocalSubnet | Out-Null
New-NetFirewallRule -DisplayName 'ActionBridge remote UDP' -Direction Inbound -Action Allow -Program '$remoteEscaped' -Protocol UDP -LocalPort 45840-45860 -Profile Private,Public | Out-Null
"@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($firewall))
try {
    $elevated = Start-Process powershell.exe -ArgumentList '-NoProfile', '-EncodedCommand', $encoded -Verb RunAs -PassThru -Wait
    if ($elevated.ExitCode -ne 0) { Write-Warning 'Firewall setup failed. The companion may be blocked until allowed on a private network.' }
} catch { Write-Warning 'Firewall approval was declined. Allow ActionBridge through Windows Firewall on your private network.' }
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item $run -Force | Out-Null
New-ItemProperty $run -Name ActionBridge -Value ('"' + $exe + '" --tray') -PropertyType String -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'ActionBridge.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $destination
$shortcut.Save()
Start-Process $exe
Write-Host 'ActionBridge is ready. Install the Android APK, share a file to ActionBridge, and tap Allow on this PC once.'
Write-Host 'Received files: Downloads\ActionBridge'
