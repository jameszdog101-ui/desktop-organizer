Stop-ScheduledTask -TaskName 'DesktopOrganizer' -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName 'DesktopOrganizer' -Confirm:$false -ErrorAction SilentlyContinue
$desktop = [Environment]::GetFolderPath('Desktop')
foreach ($n in '立即整理桌面 Organize Now.lnk', '復原上次整理 Undo Organize.lnk', '立即整理桌面.lnk', '復原上次整理.lnk', '排版桌面 Arrange.lnk', '釋放記憶體 FreeRAM.lnk', '鎖定解鎖排版 Lock Layout.lnk') { Remove-Item -LiteralPath (Join-Path $desktop $n) -ErrorAction SilentlyContinue }
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*organizer.ps1*-Mode Watch*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Write-Host '已解除安裝(已整理的檔案不會被移動)。'

Get-Process DesktopLock -ErrorAction SilentlyContinue | Stop-Process -Force
