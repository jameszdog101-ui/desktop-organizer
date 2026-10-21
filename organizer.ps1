<#
.SYNOPSIS
  桌面自動整理工具 (Desktop Organizer)
.EXAMPLE
  .\organizer.ps1 -Mode Scan -DryRun     # 預覽,不搬移
  .\organizer.ps1 -Mode Scan             # 立即整理一次
  .\organizer.ps1 -Mode Watch            # 常駐,定期整理
  .\organizer.ps1 -Mode Undo             # 還原最近一次整理
#>
param(
    [ValidateSet('Scan', 'Watch', 'Undo')][string]$Mode = 'Scan',
    [switch]$DryRun,
    [switch]$IgnoreAge,                      # 忽略「最短存在時間」限制
    [string]$DesktopPath,                    # 測試用:覆寫桌面路徑
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'config.json')
)

$ErrorActionPreference = 'Stop'
$Config  = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $DesktopPath) { $DesktopPath = [Environment]::GetFolderPath('Desktop') }
$Desktop = (Resolve-Path -LiteralPath $DesktopPath).Path
$Target  = Join-Path $Desktop $Config.targetFolderName
$LogDir  = Join-Path $PSScriptRoot 'logs'
$LogFile = Join-Path $LogDir 'moves.json'
$null = New-Item -ItemType Directory -Force -Path $LogDir

function Write-Log([string]$msg) {
    $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $msg
    Write-Host $line
    Add-Content -LiteralPath (Join-Path $LogDir 'organizer.log') -Value $line -Encoding UTF8
}

function Get-Category([System.IO.FileInfo]$f) {
    $name = $f.BaseName.ToLower()
    foreach ($r in $Config.keywordRules) {
        foreach ($k in $r.keywords) { if ($name.Contains($k.ToLower())) { return $r.category } }
    }
    $ext = $f.Extension.ToLower()
    foreach ($p in $Config.categories.PSObject.Properties) {
        if ($p.Value -contains $ext) { return $p.Name }
    }
    return $Config.defaultCategory
}

# 檔案是否仍被占用 (下載中/編輯中)
function Test-Locked([string]$path) {
    try {
        $fs = [System.IO.File]::Open($path, 'Open', 'ReadWrite', 'None')
        $fs.Close(); return $false
    } catch { return $true }
}

function Get-UniquePath([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $path }
    $dir = Split-Path $path -Parent
    $base = [IO.Path]::GetFileNameWithoutExtension($path)
    $ext = [IO.Path]::GetExtension($path)
    $i = 1
    do { $p = Join-Path $dir ("{0}_{1}{2}" -f $base, $i, $ext); $i++ } while (Test-Path -LiteralPath $p)
    return $p
}

function Get-Batches {
    # 5.1 的 ConvertFrom-Json 不會展開陣列,需再經管線展開
    $x = Get-Content -LiteralPath $LogFile -Raw -Encoding UTF8 | ConvertFrom-Json
    return ,@($x | ForEach-Object { $_ })
}

function Save-Batches($batches) {
    # 逐筆序列化,避免 Windows PowerShell 5.1 對陣列的 value/Count 包裝問題
    $items = @($batches | ForEach-Object { ConvertTo-Json -InputObject $_ -Depth 5 -Compress })
    Set-Content -LiteralPath $LogFile -Value ("[" + ($items -join ",") + "]") -Encoding UTF8
}

# ---- 桌面圖示自動排版:呼叫 DesktopArrange.exe(桌面項目有變動時才執行)----
function Set-DesktopLayout {
    if (-not $Config.autoArrangeDesktop) { return }
    $exe = Join-Path $PSScriptRoot 'DesktopArrange.exe'
    if (-not (Test-Path -LiteralPath $exe)) { return }
    try {
        $names = @(Get-ChildItem -LiteralPath $Desktop -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) +
                 @(Get-ChildItem -LiteralPath ([Environment]::GetFolderPath('CommonDesktopDirectory')) -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
        $sig = ($names | Sort-Object) -join '|'
        $sigFile = Join-Path $LogDir 'layout.sig'
        if (-not $IgnoreAge -and (Test-Path -LiteralPath $sigFile) -and ([IO.File]::ReadAllText($sigFile) -eq $sig)) { return }   # 手動「立即整理」(-IgnoreAge) 一律重新排版
        $pr = Start-Process -FilePath $exe -ArgumentList '--quiet' -Wait -PassThru
        if ($pr.ExitCode -eq 0) { [IO.File]::WriteAllText($sigFile, $sig); Write-Log '已自動排版桌面圖示' }
        else { Write-Log "排版程式回傳代碼 $($pr.ExitCode)" }
    } catch { Write-Log "排版失敗: $($_.Exception.Message)" }
}
# ---- 記憶體智慧自動釋放:超過設定百分比(預設 85%)時背景呼叫 FreeMemory.exe --quiet ----
function Invoke-AutoFreeMemory {
    $th = $Config.autoFreeMemoryThresholdPercent
    if (-not $th -or $th -le 0) { return }
    $exe = Join-Path $PSScriptRoot 'FreeMemory.exe'
    if (-not (Test-Path -LiteralPath $exe)) { return }
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $usedPct = [math]::Round((1.0 - ($os.FreePhysicalMemory / $os.TotalVisibleMemorySize)) * 100, 1)
        if ($usedPct -ge $th) {
            $pr = Start-Process -FilePath $exe -ArgumentList '--quiet' -Wait -PassThru
            $osAfter = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
            $afterPct = if ($osAfter) { [math]::Round((1.0 - ($osAfter.FreePhysicalMemory / $osAfter.TotalVisibleMemorySize)) * 100, 1) } else { $usedPct }
            Write-Log "記憶體使用率達 $usedPct% (>= $th%),已自動釋放記憶體 (釋放後: $afterPct%)"
        }
    } catch { Write-Log "自動釋放記憶體失敗: $($_.Exception.Message)" }
}

# ---- 記錄檔案「首次出現」時間:以出現在桌面起算,而非檔案修改時間 ----
$SeenFile = Join-Path $LogDir 'seen.clixml'
function Get-Seen {
    if (Test-Path -LiteralPath $SeenFile) { try { return Import-Clixml -LiteralPath $SeenFile } catch {} }
    return @{}
}

function Invoke-Scan {
    $seen = Get-Seen
    $present = @{}
    $now = Get-Date
    $moves = @()
    # 只處理桌面第一層「檔案」;資料夾、隱藏/系統檔一律不動
    $files = Get-ChildItem -LiteralPath $Desktop -File -Force -ErrorAction SilentlyContinue |
        Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::Hidden) -and -not ($_.Attributes -band [IO.FileAttributes]::System) }

    foreach ($f in $files) {
        if ($Config.ignoreExtensions -contains $f.Extension.ToLower()) { continue }
        if ($Config.ignoreNames -contains $f.Name.ToLower()) { continue }
        if ($f.Name.StartsWith('~$')) { continue }   # Office 暫存檔
        $present[$f.FullName] = $true
        # 保留在桌面:應用程式、重點文書、以 ★ 開頭等
        if ($Config.keepExtensions -contains $f.Extension.ToLower()) { continue }
        $lname = $f.Name.ToLower()
        if (@($Config.keepPrefixes | Where-Object { $f.Name.StartsWith($_) }).Count -gt 0) { continue }
        if (@($Config.keepKeywords | Where-Object { $lname.Contains($_.ToLower()) }).Count -gt 0) { continue }
        # 新檔等待 minAgeMinutes(從首次出現在桌面算起,或修改時間較晚者)
        if (-not $seen.ContainsKey($f.FullName)) { $seen[$f.FullName] = $now }
        $arrived = $seen[$f.FullName]; if ($f.LastWriteTime -gt $arrived) { $arrived = $f.LastWriteTime }
        if (-not $IgnoreAge -and ($now - $arrived).TotalMinutes -lt $Config.minAgeMinutes) { continue }
        if (Test-Locked $f.FullName) { Write-Log "略過(使用中): $($f.Name)"; continue }

        $dir = $Target
        if ($Config.groupByCategory) { $dir = Join-Path $dir (Get-Category $f) }
        # 依「到達桌面的日期」(下載/拖入時間)分資料夾:yyyy-MM-dd
        if ($Config.groupByMonth) { $dir = Join-Path $dir $arrived.ToString('yyyy-MM') }
        if ($Config.groupByDay) { $dir = Join-Path $dir $arrived.ToString('yyyy-MM-dd') }
        $dest = Join-Path $dir $f.Name

        # 同名:內容相同 => _Duplicates;內容不同 => 加流水號
        if (Test-Path -LiteralPath $dest) {
            $h1 = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
            $h2 = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
            if ($h1 -eq $h2) { $dest = Get-UniquePath (Join-Path (Join-Path $Target $Config.duplicatesFolderName) $f.Name) }
            else { $dest = Get-UniquePath $dest }
        }

        if ($DryRun) { Write-Log "[預覽] $($f.Name) -> $($dest.Substring($Desktop.Length + 1))"; continue }
        try {
            $null = New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent)
            Move-Item -LiteralPath $f.FullName -Destination $dest
            $moves += [pscustomobject]@{ from = $f.FullName; to = $dest }
            Write-Log "已整理: $($f.Name) -> $($dest.Substring($Desktop.Length + 1))"
        } catch { Write-Log "失敗: $($f.Name): $($_.Exception.Message)" }
    }

    if ($moves.Count -gt 0) {
        $batches = @()
        if (Test-Path -LiteralPath $LogFile) {
            $batches = Get-Batches
        }
        $batches += [pscustomobject]@{ time = $now.ToString('s'); moves = $moves }
        Save-Batches $batches
    }
    if (-not $DryRun) {
        $keep = @{}; foreach ($k in $seen.Keys) { if ($present.ContainsKey($k)) { $keep[$k] = $seen[$k] } }
        Export-Clixml -InputObject $keep -LiteralPath $SeenFile
        Set-DesktopLayout
        Invoke-AutoFreeMemory
    }
    Write-Log ("掃描完成,{0} {1} 個檔案" -f $(if ($DryRun) { '預計搬移' } else { '已搬移' }), $(if ($DryRun) { '-' } else { $moves.Count }))
}

function Invoke-Undo {
    if (-not (Test-Path -LiteralPath $LogFile)) { Write-Log '沒有可還原的紀錄'; return }
    $batches = Get-Batches
    if ($batches.Count -eq 0) { Write-Log '沒有可還原的紀錄'; return }
    $last = $batches[-1]
    foreach ($m in $last.moves) {
        if (-not (Test-Path -LiteralPath $m.to)) { Write-Log "找不到(已被移動?): $($m.to)"; continue }
        $back = Get-UniquePath $m.from
        Move-Item -LiteralPath $m.to -Destination $back
        Write-Log "已還原: $(Split-Path $back -Leaf)"
    }
    $rest = @($batches | Select-Object -SkipLast 1)
    Save-Batches $rest
}

# 單一實例保護 (Watch)
switch ($Mode) {
    'Scan'  { Invoke-Scan }
    'Undo'  { Invoke-Undo }
    'Watch' {
        $mutex = New-Object System.Threading.Mutex($false, 'Global\DesktopOrganizerWatch')
        if (-not $mutex.WaitOne(0)) { Write-Log '已有另一個監控程序在執行'; return }
        Write-Log "開始監控: $Desktop (每 $($Config.watchIntervalSeconds) 秒)"
        while ($true) {
            try { Invoke-Scan } catch { Write-Log "錯誤: $($_.Exception.Message)" }
            Start-Sleep -Seconds $Config.watchIntervalSeconds
        }
    }
}
