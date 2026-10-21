# Desktop Organizer & Layout Lock (`desktop-organizer`) — AI / Developer Handoff

> **Purpose of this file**: Provide complete technical context, architecture, configuration, and recent changelog so any AI agent (Antigravity, Codex, Claude, Cursor, etc.) or developer taking over after a logout/reboot can immediately understand and maintain this system.

---

## 1. Environment & Paths

| Item | Path / Value |
|---|---|
| **Project Directory** | `C:\Users\User\.gemini\antigravity\scratch\desktop-organizer` |
| **User Desktop Path** | `D:\Users\User\Desktop` (`[Environment]::GetFolderPath('Desktop')`) |
| **Organized Target Folder** | `D:\Users\User\Desktop\整理 Organized` |
| **User Manual on Desktop** | `D:\Users\User\Desktop\桌面整理工具說明書 Manual.txt` |
| **Scheduled Task Name** | `DesktopOrganizer` (Trigger: `AtLogOn`, `RunLevel: Highest`) |
| **Watch Mutex Name** | `Global\DesktopOrganizerWatch` |
| **Layout Lock IPC Event** | `Local\DesktopLayoutLock_Stop` (`ManualReset` `EventWaitHandle`) |

---

## 2. Source Files & Executables

All C# tools are compiled with the built-in .NET Framework 4.0 x64 compiler (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) via `.\build.ps1`.

| File | Role |
|---|---|
| `config.json` | Central JSON configuration for file organization rules, intervals, exemptions, and auto-RAM release threshold. |
| `organizer.ps1` | Main PowerShell script supporting `-Mode Scan`, `-Mode Watch`, `-Mode Undo`, `-DryRun`, and `-IgnoreAge`. |
| `DesktopArrange.cs` / `.exe` | Win32 `SysListView32` desktop icon arranger. Supports `--quiet`, `--dry`, `--force`, and `--only=<filepath>`. |
| `DesktopLock.cs` / `.exe` | Desktop icon position lock/unlock toggle (`--daemon` runs background watcher every 250ms). |
| `FreeMemory.cs` / `.exe` | Working set (`EmptyWorkingSet`) + Standby List (`NtSetSystemInformation`) RAM cleaner. Supports `--quiet`. |
| `FreeMemory.manifest` | UAC manifest (`requireAdministrator`) for `FreeMemory.exe`. |
| `build.ps1` | Compiles `DesktopArrange.exe`, `FreeMemory.exe`, and `DesktopLock.exe`. |
| `install.ps1` / `uninstall.ps1` | Registers/unregisters the `DesktopOrganizer` scheduled task and creates/removes desktop `.lnk` shortcuts. |
| `logs/organizer.log` | Timestamped execution log. |
| `logs/moves.json` | Batch history of moved files used by `-Mode Undo`. |
| `logs/seen.clixml` | Tracks first-seen desktop arrival timestamps for the 60-minute new-file grace period. |
| `logs/layout.sig` | Desktop item signature used to detect changes before auto-arranging. |

---

## 3. Core Specifications & Rules

### A. File Organization (`organizer.ps1` + `config.json`)
1. **15-Minute Auto-Scan**: `watchIntervalSeconds: 900`. Runs in background via Scheduled Task `DesktopOrganizer` (`RunLevel: Highest`).
2. **1-Hour New-File Exemption**: `minAgeMinutes: 60`. New files arriving on the desktop (tracked via `logs/seen.clixml` or `LastWriteTime`, whichever is later) are not moved for 60 minutes unless manually triggered via `立即整理桌面 Organize Now` (`-IgnoreAge`).
3. **Single-Layer `YYYY-MM-DD` Folder Structure**:
   - `groupByCategory: false`, `groupByMonth: false`, `groupByDay: true`.
   - Files are moved directly into `D:\Users\User\Desktop\整理 Organized\<YYYY-MM-DD>\<filename>` based on their desktop arrival date (`$arrived`).
   - Exact SHA-256 duplicates go to `整理 Organized\重複檔 Duplicates`; same-name different-content files get `_1`, `_2` suffixes.
4. **Files Kept on Desktop Permanently**:
   - All directories and shortcuts (`.lnk`, `.url`)
   - Executables (`.exe`, `.appref-ms`)
   - Files starting with `★`, `!`, `！` (`keepPrefixes`)
   - Files containing `重要`, `重點`, `important`, `urgent`, `緊急`, `合約`, `contract`, `待辦`, `todo` (`keepKeywords`)
   - Temporary/system files (`desktop.ini`, `thumbs.db`, `~$*`, `.crdownload`, `.tmp`, `.part`, etc.)
   - `桌面整理工具說明書 Manual.txt` (in `ignoreNames`)

### B. Desktop Icon Arrangement (`DesktopArrange.cs`)
1. **Top & Bottom Margins**: `MarginRows = 3` (leaves 3 empty grid rows at top and 3 at bottom when screen height permits).
2. **Centering & Grouping**:
   - Icons are grouped left-to-right by type: `0=System` → `1=Apps` → `2=Folders` → `3=Documents` → `4=Images` → `5=Media` → `6=Others`.
   - Within each type, sorted logically (`StrCmpLogicalW`) top-to-bottom.
   - Main block is horizontally centered; each column is vertically centered within the available rows.
3. **Right-Pinned Tool Shortcuts (`KIND_TOOL = 99`)**:
   - `立即整理桌面 Organize Now`
   - `復原上次整理 Undo Organize`
   - `排版桌面 Arrange`
   - `釋放記憶體 FreeRAM`
   - `鎖定解鎖排版 Lock Layout`
   - `桌面整理工具說明書 Manual`
4. **Lock Awareness**:
   - Checks `EventWaitHandle.TryOpenExisting("Local\\DesktopLayoutLock_Stop")`.
   - If locked and `--force` is NOT passed, exits with code `3` without moving any icons.
   - Supports `--only=<file>` (used by `DesktopLock.exe` when locked): calculates ideal grid positions for newly added desktop items and places **only** those new items into the nearest unoccupied grid cell without moving any existing locked icons.

### C. Desktop Layout Lock (`DesktopLock.cs`)
1. **Toggle Behavior**:
   - Running `DesktopLock.exe` when unlocked launches `DesktopLock.exe --daemon` and shows a popup titled `LOCKED | 鎖定排版` with header `【 LOCKED — 已鎖定 】`.
   - Running `DesktopLock.exe` when locked signals `Local\DesktopLayoutLock_Stop`, terminates the daemon, and shows a popup titled `UNLOCK | 解除鎖定` with header `【 UNLOCK — 已解鎖 】`.
2. **Daemon Loop (250ms)**:
   - Snapshots icon coordinates `(X, Y)` via `LVM_GETITEMPOSITION` and restores any moved icon via `LVM_SETITEMPOSITION`.
   - **New Icons While Locked**:
     - Calls `DesktopArrange.exe --quiet --force --only=<temp_list>` after 1.5s so new shortcuts/files are placed in their proper centered category column instead of getting stuck at top-left `(0,0)`.
     - Records arrival in `Born[name]` and grants a **60-minute grace period (`GraceMinutes = 60`)** during which the user can freely drag the newly added icon to a custom spot (updating its locked position).

### D. Smart Memory Release (`FreeMemory.cs` + `organizer.ps1`)
1. **Auto-Trigger at 85%**: `autoFreeMemoryThresholdPercent: 85` in `config.json`. Every 15 minutes during `Invoke-Scan`, `Invoke-AutoFreeMemory` checks `Win32_OperatingSystem` memory load. If `>= 85%`, runs `FreeMemory.exe --quiet` silently (no UAC prompt because `DesktopOrganizer` task runs with `RunLevel: Highest`).
2. **Manual Shortcut**: `釋放記憶體 FreeRAM.lnk` remains on the desktop for on-demand cleanup and displays a before/after GB & MB summary dialog.

---

## 4. Other OS / System Changes Made in This Session (2026-10-20)

1. **Windows Input Methods (IME)**:
   - Cleaned up user language list (`Set-WinUserLanguageList`) to keep only 3 input methods:
     - `en-CA`: US Keyboard (`1009:00000409`)
     - `zh-Hant-TW`: Microsoft Bopomofo (`0404:{B115690A-EA02-48D5-A231-E3578D2FDF80}{B2F9C502-1742-11D4-9790-0080C882687E}`)
     - `zh-Hans-CN`: Microsoft Pinyin (`0804:{E7EA138E-69F8-11D7-A6EA-00065B844310}{E7EA138F-69F8-11D7-A6EA-00065B844311}`)
   - Reset Bopomofo keyboard layout from Pinyin back to Standard Bopomofo by removing `Keyboard Layout Setting.New Phonetic` & `Keyboard Layout Setting.Phonetic` under `HKCU:\Software\Microsoft\IME\15.0\IMETC`.
   - Enabled single-press Left/Right `Shift` to toggle Chinese/English mode (`Left Shift Usage = 1`, `Right Shift Usage = 1` under `HKCU:\Software\Microsoft\IME\15.0\IMETC`).
2. **Browser Shortcuts Added to Desktop**:
   - `D:\Users\User\Desktop\Internet Explorer.lnk`
   - `D:\Users\User\Desktop\Microsoft Edge.lnk`
