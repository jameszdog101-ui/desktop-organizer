// DesktopArrange.exe — 自動排版桌面圖示
// 版面:由左至右為不同類型(系統圖示 → 應用程式 → 資料夾 → 文件 → 圖片 → 影音 → 其他),
// 同類型由上而下依名稱排序;整個區塊水平置中、每欄垂直置中。
// 桌面整理工具的捷徑獨立成一組,固定靠右,且使用相同圖示。用法: DesktopArrange.exe [--quiet] [--dry]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr addr, UIntPtr size, uint type, uint prot);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr addr, UIntPtr size, uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr addr, byte[] buf, int size, out IntPtr written);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    struct LVITEM
    {
        public uint mask; public int iItem; public int iSubItem; public uint state; public uint stateMask;
        public IntPtr pszText; public int cchTextMax; public int iImage; public IntPtr lParam;
        public int iIndent; public int iGroupId; public uint cColumns; public IntPtr puColumns;
        public IntPtr piColFmt; public int iGroup;
    }

    const int LVM_GETITEMCOUNT = 0x1004, LVM_SETITEMPOSITION = 0x100F, LVM_GETITEMSPACING = 0x1033,
              LVM_GETITEMTEXTW = 0x1073, LVM_GETITEMPOSITION = 0x1010, LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036, LVM_GETEXTENDEDLISTVIEWSTYLE = 0x1037;
    const int GWL_STYLE = -16, LVS_AUTOARRANGE = 0x100, LVS_EX_SNAPTOGRID = 0x80000;

    class Item { public int Index; public string Name; public int Kind; public int X, Y; }

    static IntPtr FindDesktopListView()
    {
        IntPtr view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
        {
            IntPtr w = IntPtr.Zero;
            while (view == IntPtr.Zero && (w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                view = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        return view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }

    const int KIND_TOOL = 99;
    const int MarginRows = 3;   // 上下各保留的空白格數
    static readonly string[] ToolNames = { "立即整理桌面 Organize Now", "復原上次整理 Undo Organize", "排版桌面 Arrange", "釋放記憶體 FreeRAM", "鎖定解鎖排版 Lock Layout", "桌面整理工具說明書 Manual" };
    static readonly string[] AppExt = { ".lnk", ".url", ".appref-ms", ".exe" };
    static readonly string[] DocExt = { ".pdf", ".doc", ".docx", ".txt", ".xls", ".xlsx", ".ppt", ".pptx", ".csv", ".md", ".odt", ".rtf", ".hwp" };
    static readonly string[] ImgExt = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".heic", ".tif", ".tiff" };
    static readonly string[] MediaExt = { ".mp4", ".mov", ".avi", ".mkv", ".mp3", ".wav", ".flac", ".m4a", ".wmv" };

    static Dictionary<string, int> BuildKinds()
    {
        // 0=系統圖示 1=應用程式 2=資料夾 3=文件 4=圖片 5=影音 6=其他檔案 99=整理工具
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dirs = new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                           Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) };
        foreach (var d in dirs)
        {
            if (!Directory.Exists(d)) continue;
            foreach (var e in new DirectoryInfo(d).GetFileSystemInfos())
            {
                if ((e.Attributes & FileAttributes.Hidden) != 0) continue;
                bool isDir = (e.Attributes & FileAttributes.Directory) != 0;
                string ext = e.Extension.ToLowerInvariant();
                string bare = Path.GetFileNameWithoutExtension(e.Name);
                int kind;
                if (!isDir && ToolNames.Contains(bare)) kind = KIND_TOOL;
                else if (isDir) kind = 2;
                else if (AppExt.Contains(ext)) kind = 1;
                else if (DocExt.Contains(ext)) kind = 3;
                else if (ImgExt.Contains(ext)) kind = 4;
                else if (MediaExt.Contains(ext)) kind = 5;
                else kind = 6;
                if (!map.ContainsKey(e.Name)) map[e.Name] = kind;
                if (!isDir && (ext == ".lnk" || ext == ".url" || ext == ".appref-ms") && !map.ContainsKey(bare)) map[bare] = kind;
            }
        }
        return map;
    }
    [STAThread]
    static int Main(string[] args)
    {
        bool quiet = args.Contains("--quiet"), dry = args.Contains("--dry");
        // --only=<檔案>:只替檔案內列出的(新增)圖示找位置,其餘圖示完全不動(鎖定期間供 DesktopLock 使用)
        HashSet<string> only = null;
        var onlyArg = args.FirstOrDefault(a => a.StartsWith("--only="));
        if (onlyArg != null) { try { only = new HashSet<string>(File.ReadAllLines(onlyArg.Substring(7), System.Text.Encoding.UTF8)); } catch { only = new HashSet<string>(); } }
        SetProcessDPIAware();
        // 排版已鎖定(DesktopLock.exe 常駐中)時不得更動圖示位置,回傳 3 讓呼叫端知道未排版
        EventWaitHandle locked;
        if (!dry && !args.Contains("--force") && EventWaitHandle.TryOpenExisting("Local\\DesktopLayoutLock_Stop", out locked))
        {
            locked.Close();
            if (!quiet) MessageBox.Show("桌面排版目前已鎖定,請先用「鎖定解鎖排版 Lock Layout」解鎖。", "Desktop Arrange");
            return 3;
        }
        IntPtr lv = FindDesktopListView();
        if (lv == IntPtr.Zero) { if (!quiet) MessageBox.Show("找不到桌面視窗。"); return 1; }

        uint pid; GetWindowThreadProcessId(lv, out pid);
        IntPtr proc = OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);
        if (proc == IntPtr.Zero) { if (!quiet) MessageBox.Show("無法存取桌面程序。"); return 2; }
        IntPtr remote = VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)4096, 0x3000, 0x04);
        try
        {
            var kinds = BuildKinds();
            int count = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            var items = new List<Item>();
            int sz = Marshal.SizeOf(typeof(LVITEM));
            IntPtr textPtr = IntPtr.Add(remote, 512);
            for (int i = 0; i < count; i++)
            {
                var it = new LVITEM { mask = 1, iItem = i, pszText = textPtr, cchTextMax = 260 };
                IntPtr local = Marshal.AllocHGlobal(sz);
                Marshal.StructureToPtr(it, local, false);
                byte[] raw = new byte[sz]; Marshal.Copy(local, raw, 0, sz); Marshal.FreeHGlobal(local);
                IntPtr n;
                WriteProcessMemory(proc, remote, raw, sz, out n);
                SendMessage(lv, LVM_GETITEMTEXTW, (IntPtr)i, remote);
                byte[] buf = new byte[520];
                ReadProcessMemory(proc, textPtr, buf, buf.Length, out n);
                string name = System.Text.Encoding.Unicode.GetString(buf);
                int z = name.IndexOf('\0'); if (z >= 0) name = name.Substring(0, z);
                int kind; if (!kinds.TryGetValue(name, out kind)) kind = 0;
                SendMessage(lv, LVM_GETITEMPOSITION, (IntPtr)i, IntPtr.Add(remote, 1536));
                byte[] pb = new byte[8]; ReadProcessMemory(proc, IntPtr.Add(remote, 1536), pb, 8, out n);
                items.Add(new Item { Index = i, Name = name, Kind = kind, X = BitConverter.ToInt32(pb, 0), Y = BitConverter.ToInt32(pb, 4) });
            }

            int sp = (int)SendMessage(lv, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);
            int cx = sp & 0xFFFF, cy = (sp >> 16) & 0xFFFF;
            RECT rc; GetClientRect(lv, out rc);
            int totalRows = Math.Max(1, (rc.B - rc.T) / Math.Max(1, cy));
            int margin = totalRows > 2 * MarginRows + 1 ? MarginRows : 0;   // 上下各保留 3 格空間(螢幕太矮則不保留)
            int rows = totalRows - 2 * margin;
            int totalCols = Math.Max(1, (rc.R - rc.L) / Math.Max(1, cx));

            if (!dry)
            {   // 關閉「自動排列」才能自行指定位置;保留對齊格線
                int style = GetWindowLong(lv, GWL_STYLE);
                if ((style & LVS_AUTOARRANGE) != 0) SetWindowLong(lv, GWL_STYLE, style & ~LVS_AUTOARRANGE);
                SendMessage(lv, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)LVS_EX_SNAPTOGRID, (IntPtr)LVS_EX_SNAPTOGRID);
            }

            // 把每個類型切成若干「欄」(每欄最多 rows 個,由上而下)
            Func<IEnumerable<Item>, List<List<Item>>> toColumns = list =>
            {
                var l = list.ToList();
                l.Sort((x, y) => StrCmpLogicalW(x.Name, y.Name));
                var cols = new List<List<Item>>();
                for (int i = 0; i < l.Count; i += rows) cols.Add(l.Skip(i).Take(rows).ToList());
                return cols;
            };
            var main = new List<List<List<Item>>>();
            foreach (var g in items.Where(x => x.Kind != KIND_TOOL).GroupBy(x => x.Kind).OrderBy(g => g.Key)) main.Add(toColumns(g));
            var tools = toColumns(items.Where(x => x.Kind == KIND_TOOL));

            int toolCols = tools.Count;
            int reserve = toolCols > 0 ? toolCols + 1 : 0;                 // 右側保留給整理工具(含 1 欄間隔)
            int mainCols = main.Sum(g => g.Count);
            int gaps = Math.Max(0, main.Count - 1);
            int avail = totalCols - reserve;
            if (mainCols + gaps > avail) gaps = 0;                          // 空間不足就不留類型間隔
            int startCol = Math.Max(0, (avail - (mainCols + gaps)) / 2);    // 水平置中

            // 只排新圖示時:已有圖示佔用的格子不可重疊,新圖示取最近的空格
            var occupied = new HashSet<long>();
            if (only != null) foreach (var o in items.Where(x => !only.Contains(x.Name))) occupied.Add(((long)(int)Math.Round((double)o.X / cx) << 32) | (uint)(int)Math.Round((double)o.Y / cy));
            Action<List<Item>, int> placeColumn = (colItems, c) =>
            {
                int startRow = margin + Math.Max(0, (rows - colItems.Count) / 2);    // 每欄垂直置中(扣除上下保留空間)
                for (int i = 0; i < colItems.Count; i++)
                {
                    if (only != null)
                    {
                        if (!only.Contains(colItems[i].Name)) continue;
                        int bc = c, br = startRow + i, bd = int.MaxValue;
                        for (int cc = 0; cc < totalCols; cc++)
                            for (int rr = margin; rr < margin + rows; rr++)
                            {
                                if (occupied.Contains(((long)cc << 32) | (uint)rr)) continue;
                                int dd = Math.Abs(cc - c) * 2 + Math.Abs(rr - br);
                                if (dd < bd) { bd = dd; bc = cc; br = rr; }
                            }
                        occupied.Add(((long)bc << 32) | (uint)br);
                        if (!dry) SendMessage(lv, LVM_SETITEMPOSITION, (IntPtr)colItems[i].Index, (IntPtr)((((long)(br * cy)) << 16) | (long)((bc * cx) & 0xFFFF)));
                    }
                    else if (!dry)
                    {
                        long lp = ((long)((startRow + i) * cy) << 16) | (long)((c * cx) & 0xFFFF);
                        SendMessage(lv, LVM_SETITEMPOSITION, (IntPtr)colItems[i].Index, (IntPtr)lp);
                    }
                }
            };

            int col = startCol, moved = 0;
            for (int gi = 0; gi < main.Count; gi++)
            {
                foreach (var column in main[gi]) { placeColumn(column, col++); moved += column.Count; }
                if (gaps > 0 && gi < main.Count - 1) col++;
            }
            int toolStart = Math.Max(col, totalCols - toolCols);
            for (int i = 0; i < tools.Count; i++) { placeColumn(tools[i], toolStart + i); moved += tools[i].Count; }
            if (!quiet) MessageBox.Show("桌面已自動排版,共 " + moved + " 個圖示。\n左→右:系統 / 應用程式 / 資料夾 / 文件 / 圖片 / 影音 / 其他;整理工具靠右獨立呈現", "Desktop Arrange");
            return 0;
        }
        finally { VirtualFreeEx(proc, remote, UIntPtr.Zero, 0x8000); CloseHandle(proc); }
    }
}
