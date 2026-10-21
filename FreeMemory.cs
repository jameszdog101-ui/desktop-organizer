// FreeMemory.exe — 手動釋放記憶體(電腦卡頓時使用)
// 1) 縮減所有程序的工作集 (EmptyWorkingSet)  2) 以系統管理員執行時,另外清除待命清單/已修改頁面清單
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attr; }

    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr t, bool disable, ref TOKEN_PRIVILEGES n, int len, IntPtr prev, IntPtr ret);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref int info, int len);

    static ulong Avail() { var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) }; GlobalMemoryStatusEx(ref m); return m.ullAvailPhys; }
    static ulong Total() { var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) }; GlobalMemoryStatusEx(ref m); return m.ullTotalPhys; }

    static bool EnableProfilePrivilege()
    {
        IntPtr tok; long luid;
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out tok)) return false;   // ADJUST_PRIVILEGES | QUERY
        if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out luid)) return false;
        var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attr = 2 };
        AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        bool ok = Marshal.GetLastWin32Error() == 0;
        CloseHandle(tok);
        return ok;
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool quiet = Array.IndexOf(args, "--quiet") >= 0;
        ulong before = Avail();
        int trimmed = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                IntPtr h = OpenProcess(0x0100 | 0x0400, false, p.Id);   // SET_QUOTA | QUERY_INFORMATION
                if (h == IntPtr.Zero) continue;
                if (EmptyWorkingSet(h)) trimmed++;
                CloseHandle(h);
            }
            catch { }
        }
        bool deep = false;
        if (EnableProfilePrivilege())
        {
            int cmd = 3; int r1 = NtSetSystemInformation(80, ref cmd, 4);   // 清除已修改頁面清單
            cmd = 4;     int r2 = NtSetSystemInformation(80, ref cmd, 4);   // 清除待命清單
            deep = (r1 == 0 && r2 == 0);
        }
        System.Threading.Thread.Sleep(800);
        ulong after = Avail();
        double gb = 1024.0 * 1024 * 1024;
        double freed = ((double)after - (double)before) / (1024.0 * 1024);
        if (!quiet)
            MessageBox.Show(
                string.Format("釋放完成 / Done\n\n處理程序數:{0}\n可用記憶體:{1:F2} GB → {2:F2} GB(共 {3:F1} GB)\n變化:{4}{5:F0} MB\n深度清理(待命清單):{6}",
                    trimmed, before / gb, after / gb, Total() / gb, freed >= 0 ? "+" : "", freed, deep ? "已執行" : "未執行(需以系統管理員執行)"),
                "Free Memory 釋放記憶體");
        return 0;
    }
}
