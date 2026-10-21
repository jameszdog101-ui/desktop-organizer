param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'PerformanceGuardian\PerformanceGuardian.csproj'
$publish = Join-Path $root 'publish'
$compilerCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)

dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

$iscc = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) { throw '找不到 Inno Setup 6。请安装后重新执行 build-installer.ps1。' }
& $iscc (Join-Path $root 'installer\SmartDesktopGuardian.iss')
if ($LASTEXITCODE -ne 0) { throw '安装包编译失败' }
Write-Host "安装包已产生：$(Join-Path $root 'artifacts\SmartDesktopGuardian-Setup-x64.exe')"
