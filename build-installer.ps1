param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'PerformanceGuardian\PerformanceGuardian.csproj'
$publish = Join-Path $root 'publish'
$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)

$localDotnet = Join-Path $root 'work\.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$iscc = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it and run this script again.' }
& $iscc (Join-Path $root 'installer\SmartDesktopGuardian.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
Write-Host "Installer created: $(Join-Path $root 'artifacts\SmartDesktopGuardian-Setup-x64.exe')"
