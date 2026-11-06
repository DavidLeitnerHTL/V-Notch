# Swaps the WPF notch for the WinUI notch until you right-click the WinUI one, then brings the WPF notch back.
# Installed next to the WinUI build in %LOCALAPPDATA%\V-Notch-WinUI and started from a desktop shortcut.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$wpf = Get-Process V-Notch -ErrorAction SilentlyContinue | Select-Object -First 1
$wpfPath = if ($wpf -and $wpf.Path) { $wpf.Path } else { "$env:LOCALAPPDATA\V-Notch-Release\V-Notch.exe" }
if ($wpf) { $wpf | Stop-Process -Force; Start-Sleep -Milliseconds 600 }

$winui = Start-Process (Join-Path $here 'VNotch.WinUI.exe') -PassThru
$winui.WaitForExit()

$env:VNOTCH_SKIP_INTEGRITY_CHECK = '1'
Start-Process $wpfPath -WorkingDirectory (Split-Path $wpfPath)
