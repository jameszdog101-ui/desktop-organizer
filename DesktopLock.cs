// DesktopLock.exe — 鎖定 / 解鎖桌面圖示排版(每次執行切換一次)
// 解鎖:可自由拖曳圖示定位。鎖定:記下目前每個圖示位置,背景常駐監看,圖示被移動就立刻歸位。
// 鎖定期間 DesktopArrange.exe 會自動略過(回傳 3),不會與本工具衝突。
// 用法: DesktopLock.exe [--quiet]  (切換)    DesktopLock.exe --daemon (內部使用,常駐監看)
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr addr, UIntPtr size, uint type, uint prot);
    [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p, IntPtr addr, UIntPtr size, uint type);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr p, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr p, IntPtr addr, byte[] buf, int size, out IntPtr written);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    struct LVITEM
    {
        public uint mask; public int iItem; public int iSubItem; public uint state; public uint stateMask;
        public IntPtr pszText; public int cchTextMax; public int iImage; public IntPtr lParam;
        public int iIndent; public int iGroupId; public uint cColumns; public IntPtr puColumns;
        public IntPtr piColFmt; public int iGroup;
    }

    const int LVM_GETITEMCOUNT = 0x1004, LVM_GETITEMPOSITION = 0x1010, LVM_SETITEMPOSITION = 0x100F,
              LVM_GETITEMTEXTW = 0x1073, GWL_STYLE = -16, LVS_AUTOARRANGE = 0x100;
    const string StopName = "Local\\DesktopLayoutLock_Stop";   // 存在 = 已鎖定(DesktopArrange 也以此判斷)
    static Dictionary<string, DateTime> Born = new Dictionary<string, DateTime>();   // 鎖定期間新出現圖示的時間(1 小時內可自由移動)
    const int GraceMinutes = 60;
    static List<string> NewNames = new List<string>();   // 本輪掃描新出現的圖示名稱

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

    // 讀取桌面所有圖示的 名稱 -> 位置(x,y);restore 不為 null 時,把位置不同的圖示放回去
    static Dictionary<string, int[]> Scan(Dictionary<string, int[]> restore, bool addNew, bool disableAutoArrange)
    {
        var result = new Dictionary<string, int[]>();
        IntPtr lv = FindDesktopListView();
        if (lv == IntPtr.Zero) return null;
        uint pid; GetWindowThreadProcessId(lv, out pid);
        IntPtr proc = OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);
        if (proc == IntPtr.Zero) return null;
        IntPtr remote = VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)4096, 0x3000, 0x04);
        try
        {
            if (disableAutoArrange)
            {
                int style = GetWindowLong(lv, GWL_STYLE);
                if ((style & LVS_AUTOARRANGE) != 0) SetWindowLong(lv, GWL_STYLE, style & ~LVS_AUTOARRANGE);
            }
            int count = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            int sz = Marshal.SizeOf(typeof(LVITEM));
            IntPtr textPtr = IntPtr.Add(remote, 512), posPtr = IntPtr.Add(remote, 1536);
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

                SendMessage(lv, LVM_GETITEMPOSITION, (IntPtr)i, posPtr);
                byte[] pb = new byte[8];
                ReadProcessMemory(proc, posPtr, pb, 8, out n);
                int[] pos = { BitConverter.ToInt32(pb, 0), BitConverter.ToInt32(pb, 4) };

                int[] want;
                if (restore != null && restore.TryGetValue(name, out want))
                {
                    DateTime born;
                    if (Born.TryGetValue(name, out born) && (DateTime.Now - born).TotalMinutes < GraceMinutes)
                    {   // 豁免期內:使用者可自由移動,並以最新位置為準
                        restore[name] = pos;
                    }
                    else if (want[0] != pos[0] || want[1] != pos[1])
                    {
                        long lp = ((long)(want[1] & 0xFFFF) << 16) | (long)(want[0] & 0xFFFF);
                        SendMessage(lv, LVM_SETITEMPOSITION, (IntPtr)i, (IntPtr)lp);
                        pos = want;
                    }
                }
                else if (restore != null && addNew) { restore[name] = pos; NewNames.Add(name); Born[name] = DateTime.Now; }   // 新出現的圖示
                result[name] = pos;
            }
            return result;
        }
        finally { VirtualFreeEx(proc, remote, UIntPtr.Zero, 0x8000); CloseHandle(proc); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool quiet = Array.IndexOf(args, "--quiet") >= 0;
        SetProcessDPIAware();

        if (Array.IndexOf(args, "--daemon") >= 0)
        {
            bool created;
            using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName, out created))
            {
                if (!created) return 0;                       // 已有常駐程式
                var snap = Scan(null, false, true);
                if (snap == null) snap = new Dictionary<string, int[]>();
                while (!stop.WaitOne(250))
                {
                    try
                    {
                        NewNames.Clear();
                        Scan(snap, true, false);
                        if (NewNames.Count > 0)
                        {   // 鎖定期間出現新圖示:不能讓它卡在左上角 → 稍候讓位置穩定,只替「新圖示」找置中分類的空位,其餘圖示完全不動
                            Thread.Sleep(1500);
                            string arrange = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopArrange.exe");
                            if (System.IO.File.Exists(arrange))
                            {
                                string list = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "desktoplock_new.txt");
                                System.IO.File.WriteAllLines(list, NewNames.ToArray(), new System.Text.UTF8Encoding(false));
                                var pr = Process.Start(new ProcessStartInfo(arrange, "--quiet --force --only=\"" + list + "\"") { UseShellExecute = false, CreateNoWindow = true });
                                pr.WaitForExit(15000);
                            }
                            var fresh = Scan(null, false, false);
                            if (fresh != null) snap = fresh;
                        }
                    }
                    catch { }
                }
            }
            return 0;
        }

        EventWaitHandle running;
        if (EventWaitHandle.TryOpenExisting(StopName, out running))
        {   // 目前已鎖定 → 解鎖:通知常駐程式結束
            running.Set(); running.Close();
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(100);
                EventWaitHandle probe;
                if (!EventWaitHandle.TryOpenExisting(StopName, out probe)) break;
                probe.Close();
            }
            if (!quiet) MessageBox.Show("【 UNLOCK — 已解鎖 】\n\n桌面排版已解鎖，可以自由拖曳圖示位置。\nDesktop layout UNLOCK — you can move icons freely.", "UNLOCK | 解除鎖定", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // 目前未鎖定 → 鎖定:啟動常駐監看
        Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--daemon") { UseShellExecute = false, CreateNoWindow = true });
        bool ok = false;
        for (int i = 0; i < 30 && !ok; i++)
        {
            Thread.Sleep(100);
            EventWaitHandle probe;
            if (EventWaitHandle.TryOpenExisting(StopName, out probe)) { probe.Close(); ok = true; }
        }
        if (!quiet) MessageBox.Show(ok ? "【 LOCKED — 已鎖定 】\n\n桌面排版已鎖定，圖示位置無法更動。\nDesktop layout LOCKED — icons cannot be moved.\n\n再執行一次即可解鎖 (Run again to UNLOCK)."
                                       : "鎖定失敗。 Failed to lock.", ok ? "LOCKED | 鎖定排版" : "Lock Layout", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        return ok ? 0 : 1;
    }
}
