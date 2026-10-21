# 編譯 DesktopArrange.exe 與 FreeMemory.exe (使用 Windows 內建 .NET Framework 編譯器,免安裝)
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw '找不到 csc.exe' }
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /out:"$PSScriptRoot\DesktopArrange.exe" /r:System.Windows.Forms.dll /r:System.Core.dll "$PSScriptRoot\DesktopArrange.cs"
if ($LASTEXITCODE -ne 0) { throw 'DesktopArrange 編譯失敗' }
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /win32manifest:"$PSScriptRoot\FreeMemory.manifest" /out:"$PSScriptRoot\FreeMemory.exe" /r:System.Windows.Forms.dll "$PSScriptRoot\FreeMemory.cs"
if ($LASTEXITCODE -ne 0) { throw 'FreeMemory 編譯失敗' }
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /out:"$PSScriptRoot\DesktopLock.exe" /r:System.Windows.Forms.dll /r:System.Core.dll "$PSScriptRoot\DesktopLock.cs"
if ($LASTEXITCODE -ne 0) { throw 'DesktopLock 編譯失敗' }
Write-Host '已產生 DesktopArrange.exe、FreeMemory.exe、DesktopLock.exe'
