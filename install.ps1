# 安裝:登入時自動背景執行 + 桌面建立「立即整理」「復原整理」捷徑
$ErrorActionPreference = 'Stop'
$script  = Join-Path $PSScriptRoot 'organizer.ps1'
$desktop = [Environment]::GetFolderPath('Desktop')
$ps      = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"

$action  = New-ScheduledTaskAction -Execute $ps -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`" -Mode Watch"
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$set     = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'DesktopOrganizer' -Action $action -Trigger $trigger -Settings $set -RunLevel Highest -Force | Out-Null
Start-ScheduledTask -TaskName 'DesktopOrganizer'

& (Join-Path $PSScriptRoot 'make-icon.ps1')
$ico = Join-Path $PSScriptRoot 'organizer.ico'
$sh = New-Object -ComObject WScript.Shell
foreach ($s in @(@{n='立即整理桌面 Organize Now'; a='-Mode Scan -IgnoreAge'}, @{n='復原上次整理 Undo Organize'; a='-Mode Undo'})) {
    $lnk = $sh.CreateShortcut((Join-Path $desktop ($s.n + '.lnk')))
    $lnk.TargetPath = $ps
    $lnk.IconLocation = $ico
    $lnk.Arguments  = "-NoProfile -ExecutionPolicy Bypass -File `"$script`" $($s.a)"
    $lnk.Save()
}
$fm = $sh.CreateShortcut((Join-Path $desktop '釋放記憶體 FreeRAM.lnk')); $fm.TargetPath = Join-Path $PSScriptRoot 'FreeMemory.exe'; $fm.IconLocation = $ico; $fm.Save()
$lk = $sh.CreateShortcut((Join-Path $desktop '鎖定解鎖排版 Lock Layout.lnk')); $lk.TargetPath = Join-Path $PSScriptRoot 'DesktopLock.exe'; $lk.IconLocation = $ico; $lk.Save()
Write-Host '安裝完成。建議先執行:organizer.ps1 -Mode Scan -DryRun -IgnoreAge 預覽'
