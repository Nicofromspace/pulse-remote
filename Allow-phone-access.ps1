$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"')) -Wait
    exit
}
try {
    $appPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'app\Pulse.Remote.exe')).Path
    $name = 'PulseRemote-Local-48261'
    Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -Name $name -DisplayName 'Pulse Remote - local phone dashboard' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 48261 -Program $appPath -RemoteAddress LocalSubnet -Profile Private,Public | Out-Null
    Get-NetFirewallRule -Name 'PulseRemote-mDNS-5353' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -Name 'PulseRemote-mDNS-5353' -DisplayName 'Pulse Remote - permanent local address' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Program $appPath -RemoteAddress LocalSubnet -Profile Private,Public | Out-Null
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show('Phone access is ready. Scan the QR code in Pulse Remote. Only your local subnet can connect.','Pulse Remote')
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'Pulse Remote setup error')
}
