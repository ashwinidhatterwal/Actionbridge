$ErrorActionPreference = 'Stop'
$session = (Get-Process -Id $PID).SessionId
Get-Process -Name ActionBridge,ActionBridge.Remote -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session | Stop-Process
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ActionBridge -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ActionBridge.lnk') -ErrorAction SilentlyContinue
$command = "Get-NetFirewallRule -DisplayName 'ActionBridge LAN TCP', 'ActionBridge LAN UDP', 'ActionBridge remote UDP' -ErrorAction SilentlyContinue | Remove-NetFirewallRule"
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
try { Start-Process powershell.exe -ArgumentList '-NoProfile', '-EncodedCommand', $encoded -Verb RunAs -Wait } catch { Write-Warning 'Firewall rules could not be removed.' }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\ActionBridge'
Remove-Item $destination -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'App removed. Your received files and trust/history data were kept.'
Write-Host 'To reset saved identity and history, delete %LOCALAPPDATA%\ActionBridge after quitting the app.'
