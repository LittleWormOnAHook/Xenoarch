# Xenoarch tooling: make the open Unity Editor compile, then report errors.
# 1) Reads Logs/xenoarch_editor_state.txt (heartbeat from Editor/XenoarchAutoCompile.cs) for the real Play/compile state.
# 2) Drops Temp/xenoarch_compile.trigger (the poller refreshes + requests a compile; works while Unity is in the background).
# 3) If the trigger is not picked up in 10 s, toggles focus (taskbar -> Unity) so Unity's auto-refresh fires.
# -StopPlay leaves Play mode first (only when Anthony has OK'd it). Never saves scenes.
param([string]$Project = "A:\Xenoarch", [int]$TimeoutSec = 180, [switch]$StopPlay)
$ErrorActionPreference = "Stop"
Set-Location $Project
$log = "Logs\xenoarch_compile.txt"; $stateFile = "Logs\xenoarch_editor_state.txt"

function Get-EditorState {
  if (-not (Test-Path $stateFile)) { return "unknown (no heartbeat yet; poller not compiled)" }
  $line = (Get-Content $stateFile -Raw).Trim()
  $age = ((Get-Date) - (Get-Item $stateFile).LastWriteTime).TotalSeconds
  if ($age -gt 30) { return "stale heartbeat (${age}s old; Editor busy, hung or throttled): $line" }
  return $line
}
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class XaWin {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@
function Focus-Toggle {
  $p = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like "*Xenoarch*" } | Select-Object -First 1
  if (-not $p) { Write-Output "Unity window not found"; return }
  $h = $p.MainWindowHandle
  if ([XaWin]::IsIconic($h)) { [XaWin]::ShowWindowAsync($h, 9) | Out-Null }
  # Unity only auto-refreshes when it GAINS focus, so focus the taskbar first, then Unity.
  [XaWin]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [XaWin]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [XaWin]::SetForegroundWindow([XaWin]::FindWindow("Shell_TrayWnd", $null)) | Out-Null; Start-Sleep -Milliseconds 600
  [XaWin]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [XaWin]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [XaWin]::SetForegroundWindow($h) | Out-Null
  Write-Output "Focus toggled to Unity"
}

Write-Output "Editor state: $(Get-EditorState)"
if ($StopPlay) { New-Item -ItemType File -Force "Temp\xenoarch_stopplay.trigger" | Out-Null; Start-Sleep 4; Write-Output "After stop-Play: $(Get-EditorState)" }
$before = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
New-Item -ItemType File -Force "Temp\xenoarch_compile.trigger" | Out-Null
$start = Get-Date; $toggled = $false
while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSec) {
  Start-Sleep 2
  $new = if (Test-Path $log) { Get-Content $log | Select-Object -Skip $before } else { @() }
  if (-not $toggled -and (Test-Path "Temp\xenoarch_compile.trigger") -and ((Get-Date) - $start).TotalSeconds -gt 10) { Focus-Toggle; $toggled = $true }
  if ($new -match "compile finished") { break }
  if (($new -match "compile requested") -and ((Get-Date) - $start).TotalSeconds -gt 25 -and -not ($new -match "compile started")) { Write-Output "Nothing to recompile (scripts already current)"; break }
}
$new = if (Test-Path $log) { Get-Content $log | Select-Object -Skip $before } else { @() }
if (Test-Path "Temp\xenoarch_compile.trigger") { Write-Output "NOT PICKED UP. Editor state: $(Get-EditorState)" }
$new
$ed = Get-Content "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Tail 3000 | Select-String "error CS" | Select-Object -Last 20
if ($ed) { "Editor.log errors:"; $ed | ForEach-Object { $_.Line } } else { "Editor.log: no error CS lines in the tail" }
Write-Output "DLLs: $((Get-ChildItem Library\ScriptAssemblies\DMI.Xenoarch.*.dll | ForEach-Object { $_.Name + ' ' + $_.LastWriteTime.ToString('HH:mm:ss') }) -join ', ')"
