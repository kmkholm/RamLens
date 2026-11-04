// RamLens - accurate per-process and kernel RAM accounting for Windows (x64).
// Reads SystemProcessInformation directly from the kernel (ntdll), so protected and
// hidden processes (Memory Compression, Registry, vmmem, services) are included.
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RamLens
{
    static class L
    {
        public static bool En;
        static readonly string Cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RamLens", "lang.txt");
        public static string T(string ar, string en) { return En ? en : ar; }
        public static RightToLeft Rtl { get { return En ? RightToLeft.No : RightToLeft.Yes; } }
        public static MessageBoxOptions MbOpt { get { return En ? 0 : MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign; } }
        public static void Load() { try { En = File.Exists(Cfg) && File.ReadAllText(Cfg).Trim() == "en"; } catch { } }
        public static void Save(bool en) { try { Directory.CreateDirectory(Path.GetDirectoryName(Cfg)); File.WriteAllText(Cfg, en ? "en" : "ar"); } catch { } }
    }

    static class Native
    {
        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int retLen);

        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                         ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] public static extern bool GetPhysicallyInstalledSystemMemory(out ulong kb);

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFINFO
        {
            public uint cb;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable,
                           SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("psapi.dll")] public static extern bool GetPerformanceInfo(out PERFINFO p, uint size);

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    }

    class ProcInfo
    {
        public int Pid, Ppid, Handles, Threads, Session;
        public string Name = "", Path = "", Services = "", Kind = "";
        public long PrivWS, WS, PeakWS, Commit, PagedQ, NonPagedQ;
    }

    class PoolTag
    {
        public string Tag;
        public long Paged, NonPaged, Allocs;
    }

    class Snapshot
    {
        public List<ProcInfo> Procs = new List<ProcInfo>();
        public List<PoolTag> Pools = new List<PoolTag>();
        public long Installed, Total, HwReserved, InUse, Modified, Standby, Free, Commit, CommitLimit;
        public long NonPaged, PagedRes, PagedVirt, SysCache, Driver, Code, Kernel;
        public long ProcPrivate, Compression, VM, SharedMax, Unattributed;
        public bool CountersOk = true;
        public DateTime Time = DateTime.Now;
    }

    static class Collector
    {
        const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
        static readonly Dictionary<string, PerformanceCounter> ctrs = new Dictionary<string, PerformanceCounter>();
        static Dictionary<int, string> svcCache = new Dictionary<int, string>();

        static readonly HashSet<string> SystemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "System", "Registry", "Memory Compression", "Secure System", "smss.exe", "csrss.exe",
            "wininit.exe", "services.exe", "lsass.exe", "winlogon.exe", "fontdrvhost.exe", "dwm.exe",
            "LsaIso.exe", "Interrupts" };

        static IntPtr Query(int cls)
        {
            int size = 1 << 20;
            while (true)
            {
                IntPtr buf = Marshal.AllocHGlobal(size);
                int ret;
                int st = Native.NtQuerySystemInformation(cls, buf, size, out ret);
                if (st == 0) return buf;
                Marshal.FreeHGlobal(buf);
                if (st != STATUS_INFO_LENGTH_MISMATCH || size > (256 << 20)) return IntPtr.Zero;
                size = Math.Max(size * 2, ret + 65536);
            }
        }

        static long Ctr(string name)
        {
            try
            {
                PerformanceCounter pc;
                if (!ctrs.TryGetValue(name, out pc))
                {
                    pc = new PerformanceCounter("Memory", name, true);
                    ctrs[name] = pc;
                }
                return pc.RawValue;
            }
            catch { return -1; }
        }

        static HashSet<int> WindowPids()
        {
            var set = new HashSet<int>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (Native.IsWindowVisible(h) && Native.GetWindow(h, 4) == IntPtr.Zero && Native.GetWindowTextLength(h) > 0)
                {
                    int pid;
                    Native.GetWindowThreadProcessId(h, out pid);
                    set.Add(pid);
                }
                return true;
            }, IntPtr.Zero);
            return set;
        }

        static Dictionary<int, string> Services()
        {
            var d = new Dictionary<int, List<string>>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, ProcessId FROM Win32_Service WHERE ProcessId <> 0"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        List<string> l;
                        if (!d.TryGetValue(pid, out l)) d[pid] = l = new List<string>();
                        l.Add(Convert.ToString(mo["Name"]));
                    }
            }
            catch { }
            return d.ToDictionary(k => k.Key, v => string.Join(", ", v.Value.OrderBy(x => x)));
        }

        static string ImagePath(int pid)
        {
            IntPtr h = Native.OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : "";
            }
            finally { Native.CloseHandle(h); }
        }

        static void ReadProcesses(Snapshot s)
        {
            IntPtr buf = Query(5); // SystemProcessInformation
            if (buf == IntPtr.Zero) return;
            try
            {
                long off = 0;
                while (true)
                {
                    IntPtr p = new IntPtr(buf.ToInt64() + off);
                    int next = Marshal.ReadInt32(p, 0);
                    var pi = new ProcInfo();
                    pi.Threads = Marshal.ReadInt32(p, 4);
                    pi.PrivWS = Marshal.ReadInt64(p, 8);
                    int nameLen = (ushort)Marshal.ReadInt16(p, 56);
                    IntPtr namePtr = Marshal.ReadIntPtr(p, 64);
                    pi.Pid = (int)Marshal.ReadInt64(p, 80);
                    pi.Ppid = (int)Marshal.ReadInt64(p, 88);
                    pi.Handles = Marshal.ReadInt32(p, 96);
                    pi.Session = Marshal.ReadInt32(p, 100);
                    pi.PeakWS = Marshal.ReadInt64(p, 136);
                    pi.WS = Marshal.ReadInt64(p, 144);
                    pi.PagedQ = Marshal.ReadInt64(p, 160);
                    pi.NonPagedQ = Marshal.ReadInt64(p, 176);
                    pi.Commit = Marshal.ReadInt64(p, 184);
                    pi.Name = namePtr == IntPtr.Zero ? "" : Marshal.PtrToStringUni(namePtr, nameLen / 2);
                    if (pi.Pid != 0) s.Procs.Add(pi);
                    if (next == 0) break;
                    off += next;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static void ReadPools(Snapshot s)
        {
            IntPtr buf = Query(22); // SystemPoolTagInformation
            if (buf == IntPtr.Zero) return;
            try
            {
                int count = Marshal.ReadInt32(buf, 0);
                for (int i = 0; i < count; i++)
                {
                    IntPtr e = new IntPtr(buf.ToInt64() + 8 + (long)i * 40);
                    var b = new byte[4];
                    Marshal.Copy(e, b, 0, 4);
                    var t = new StringBuilder();
                    foreach (byte c in b) t.Append(c >= 32 && c < 127 ? (char)c : '?');
                    var pt = new PoolTag();
                    pt.Tag = t.ToString();
                    pt.Paged = Marshal.ReadInt64(e, 16);
                    pt.NonPaged = Marshal.ReadInt64(e, 32);
                    pt.Allocs = (long)(uint)Marshal.ReadInt32(e, 4) - (uint)Marshal.ReadInt32(e, 8)
                              + (long)(uint)Marshal.ReadInt32(e, 24) - (uint)Marshal.ReadInt32(e, 28);
                    if (pt.Paged + pt.NonPaged > 0) s.Pools.Add(pt);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static Snapshot Take(bool refreshServices)
        {
            var s = new Snapshot();
            var ms = new Native.MEMORYSTATUSEX();
            Native.GlobalMemoryStatusEx(ms);
            s.Total = (long)ms.ullTotalPhys;
            ulong kb;
            s.Installed = Native.GetPhysicallyInstalledSystemMemory(out kb) ? (long)kb * 1024 : s.Total;
            s.HwReserved = Math.Max(0, s.Installed - s.Total);

            Native.PERFINFO pf;
            Native.GetPerformanceInfo(out pf, (uint)Marshal.SizeOf(typeof(Native.PERFINFO)));
            long page = (long)pf.PageSize.ToUInt64();

            s.Standby = Ctr("Standby Cache Normal Priority Bytes") + Ctr("Standby Cache Reserve Bytes") + Ctr("Standby Cache Core Bytes");
            s.Free = Ctr("Free & Zero Page List Bytes");
            s.Modified = Ctr("Modified Page List Bytes");
            s.NonPaged = Ctr("Pool Nonpaged Bytes");
            s.PagedRes = Ctr("Pool Paged Resident Bytes");
            s.PagedVirt = Ctr("Pool Paged Bytes");
            s.SysCache = Ctr("System Cache Resident Bytes");
            s.Driver = Ctr("System Driver Resident Bytes");
            s.Code = Ctr("System Code Resident Bytes");
            s.Commit = Ctr("Committed Bytes");
            s.CommitLimit = Ctr("Commit Limit");
            if (s.Standby < 0 || s.Free < 0 || s.Modified < 0 || s.NonPaged < 0 || s.PagedRes < 0 || s.SysCache < 0 || s.Commit < 0)
            {
                // Localized or broken performance counters: fall back to GetPerformanceInfo
                s.CountersOk = false;
                s.Standby = (long)ms.ullAvailPhys; s.Free = 0; s.Modified = 0;
                s.NonPaged = (long)pf.KernelNonpaged.ToUInt64() * page;
                s.PagedRes = s.PagedVirt = (long)pf.KernelPaged.ToUInt64() * page;
                s.SysCache = (long)pf.SystemCache.ToUInt64() * page;
                s.Driver = s.Code = 0;
                s.Commit = (long)pf.CommitTotal.ToUInt64() * page;
                s.CommitLimit = (long)pf.CommitLimit.ToUInt64() * page;
            }
            s.Driver = Math.Max(0, s.Driver); s.Code = Math.Max(0, s.Code); s.PagedVirt = Math.Max(0, s.PagedVirt);
            s.InUse = s.Total - s.Standby - s.Free - s.Modified;

            ReadProcesses(s);
            ReadPools(s);

            if (refreshServices || svcCache.Count == 0) svcCache = Services();
            var wins = WindowPids();
            foreach (var p in s.Procs)
            {
                string svc;
                if (svcCache.TryGetValue(p.Pid, out svc)) p.Services = svc;
                p.Path = ImagePath(p.Pid);
                p.Kind = Classify(p, wins);
                if (p.Name.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase)) s.Compression += p.PrivWS;
                else if (p.Kind == Kinds.VM) s.VM += p.PrivWS;
                else s.ProcPrivate += p.PrivWS;
                s.SharedMax += Math.Max(0, p.WS - p.PrivWS);
            }
            s.Kernel = s.NonPaged + s.PagedRes + s.SysCache + s.Driver + s.Code;
            s.Unattributed = Math.Max(0, s.InUse - s.ProcPrivate - s.Compression - s.VM - s.Kernel);
            return s;
        }

        static string Classify(ProcInfo p, HashSet<int> wins)
        {
            if (p.Name.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("vmwp.exe", StringComparison.OrdinalIgnoreCase)
                || p.Name.Equals("VBoxHeadless.exe", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("vmware-vmx.exe", StringComparison.OrdinalIgnoreCase))
                return Kinds.VM;
            if (SystemNames.Contains(p.Name) || p.Pid == 4) return Kinds.System;
            if (p.Services.Length > 0) return Kinds.Service;
            if (wins.Contains(p.Pid)) return Kinds.App;
            if (p.Session == 0) return Kinds.Session0;
            return Kinds.Hidden;
        }
    }

    static class Kinds
    {
        public static readonly string App = L.T("تطبيق (نافذة ظاهرة)", "App (visible window)");
        public static readonly string Hidden = L.T("خلفية مخفية", "Hidden background");
        public static readonly string Service = L.T("خدمة", "Service");
        public static readonly string Session0 = L.T("خلفية (جلسة 0)", "Background (session 0)");
        public static readonly string System = L.T("نظام ويندوز", "Windows system");
        public static readonly string VM = L.T("آلة افتراضية", "Virtual machine");
    }

    static class Fmt
    {
        public static string GB(long b) { return (b / 1073741824.0).ToString("N2") + " GB"; }
        public static string MB(long b) { return (b / 1048576.0).ToString("N0") + " MB"; }
        public static double MBd(long b) { return Math.Round(b / 1048576.0, 1); }
    }

    static class PoolInfo
    {
        static readonly Dictionary<string, string> Known = new Dictionary<string, string> {
            { "MmSt", L.T("مدير الذاكرة: جداول أقسام الملفات المفتوحة/المخطّطة", "Memory manager: section tables of open/mapped files") },
            { "MmCa", L.T("مدير الذاكرة: مناطق التحكم للملفات المخطّطة", "Memory manager: control areas of mapped files") },
            { "MmSm", L.T("مدير الذاكرة: مقاطع الملفات المخطّطة", "Memory manager: segments of mapped files") },
            { "MmRe", L.T("مدير الذاكرة: إعادة تموضع ASLR", "Memory manager: ASLR relocation") },
            { "Cont", L.T("ذاكرة فيزيائية متصلة تحجزها التعريفات (كرت الشاشة غالباً)", "Contiguous physical memory reserved by drivers (usually the GPU)") },
            { "File", L.T("كائنات الملفات المفتوحة", "Open file objects") },
            { "Toke", L.T("رموز الأمان (Tokens)", "Security tokens") },
            { "Proc", L.T("كائنات العمليات", "Process objects") },
            { "Thre", L.T("كائنات الخيوط", "Thread objects") },
            { "Irp ", L.T("طلبات الإدخال/الإخراج (IRP)", "I/O request packets (IRP)") },
            { "Even", L.T("كائنات الأحداث", "Event objects") },
            { "AfdB", L.T("مخازن الشبكة (afd.sys)", "Network buffers (afd.sys)") },
            { "Pool", L.T("جداول الـ Pool نفسها", "Pool tracking tables") },
            { "NVRM", L.T("تعريف كرت NVIDIA (nvlddmkm.sys)", "NVIDIA GPU driver (nvlddmkm.sys)") },
            { "MPsc", L.T("مضاد الفيروسات Microsoft Defender", "Microsoft Defender antivirus") },
            { "smNp", L.T("مخزن ضغط الذاكرة (Store Manager)", "Memory compression store (Store Manager)") },
            { "NtxF", L.T("نظام الملفات NTFS", "NTFS file system") },
        };
        public static string Describe(string tag)
        {
            string d;
            if (Known.TryGetValue(tag, out d)) return d;
            if (tag.StartsWith("CM")) return L.T("سجل ويندوز (Registry)", "Windows Registry");
            if (tag.StartsWith("Ntf") || tag.StartsWith("NtF")) return L.T("نظام الملفات NTFS", "NTFS file system");
            if (tag.StartsWith("FM")) return L.T("مدير المرشّحات (مضاد الفيروسات/التشفير غالباً)", "Filter Manager (usually antivirus/encryption)");
            if (tag.StartsWith("Etw")) return L.T("تتبّع أحداث ويندوز ETW", "Event Tracing for Windows (ETW)");
            if (tag.StartsWith("Ob")) return L.T("مدير الكائنات", "Object manager");
            if (tag.StartsWith("Dxg") || tag.StartsWith("DxgK")) return L.T("نواة رسوميات DirectX", "DirectX graphics kernel");
            if (tag.StartsWith("Mm")) return L.T("مدير الذاكرة", "Memory manager");
            return L.T("انقر مرتين للبحث عن التعريف المسؤول", "Double-click to find the owning driver");
        }
    }

    static class Report
    {
        public class Group { public string Name, Kind, Path; public int Count; public long Priv, WS, Commit; public List<int> Pids = new List<int>(); }

        public static List<Group> Groups(Snapshot s)
        {
            return s.Procs.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g =>
            {
                var r = new Group();
                r.Name = g.First().Name; r.Count = g.Count();
                r.Priv = g.Sum(p => p.PrivWS); r.WS = g.Sum(p => p.WS); r.Commit = g.Sum(p => p.Commit);
                r.Kind = g.Any(p => p.Kind == Kinds.App) ? Kinds.App : g.First().Kind;
                r.Path = g.Select(p => p.Path).FirstOrDefault(x => x.Length > 0) ?? "";
                r.Pids = g.Select(p => p.Pid).ToList();
                return r;
            }).OrderByDescending(g => g.Priv).ToList();
        }

        public static List<string> Findings(Snapshot s)
        {
            var f = new List<string>();
            f.Add(string.Format(L.T("الذاكرة المركّبة {0}، المتاحة لويندوز {1}، المحجوزة للعتاد (كرت شاشة مدمج/BIOS) {2}.", "Installed RAM {0}, usable by Windows {1}, reserved by hardware (iGPU/BIOS) {2}."),
                Fmt.GB(s.Installed), Fmt.GB(s.Total), Fmt.GB(s.HwReserved)));
            f.Add(string.Format(L.T("المستخدمة فعلياً (In use) = {0}، وتتوزّع هكذا: ذاكرة خاصة للعمليات {1}، ضغط الذاكرة {2}، آلات افتراضية {3}، نواة ويندوز (Pools + Cache + تعريفات) {4}، غير منسوبة لعملية بعينها {5}.", "In use = {0}, split as: process private memory {1}, memory compression {2}, virtual machines {3}, Windows kernel (pools + cache + drivers) {4}, not attributed to any process {5}."),
                Fmt.GB(s.InUse), Fmt.GB(s.ProcPrivate), Fmt.GB(s.Compression), Fmt.GB(s.VM), Fmt.GB(s.Kernel), Fmt.GB(s.Unattributed)));
            f.Add(string.Format(L.T("ذاكرة Standby = {0}: ليست مستهلكة، بل ملفات قرأها ويندوز سابقاً ويحتفظ بها لتسريع فتحها، ويحرّرها فوراً لأي برنامج يطلب ذاكرة. المتاح الحقيقي = Standby + الحرّة = {1}.", "Standby = {0}: this is not consumed memory. It holds files Windows read earlier, kept to open them faster, and is released instantly to any program that needs memory. Truly available = Standby + Free = {1}."),
                Fmt.GB(s.Standby), Fmt.GB(s.Standby + s.Free)));
            f.Add(string.Format(L.T("الالتزام (Commit) = {0} من حدّ {1}: هذا ما حجزته البرامج ووعدها به ويندوز (في الرام أو ملف الترحيل)، ولهذا يكون أكبر من المستخدم فعلياً.", "Commit = {0} of a {1} limit: memory programs have reserved and Windows has promised them (in RAM or the page file), which is why it is larger than In use."),
                Fmt.GB(s.Commit), Fmt.GB(s.CommitLimit)));

            var groups = Groups(s);
            f.Add(L.T("أكبر البرامج استهلاكاً (مجموع كل عملياتها): ", "Largest programs (all their processes combined): ") + string.Join(L.T("، ", ", "),
                groups.Take(8).Select(g => string.Format("{0} ×{1} = {2}", g.Name, g.Count, Fmt.MB(g.Priv)))));

            var hidden = s.Procs.Where(p => p.Kind != Kinds.App).ToList();
            f.Add(string.Format(L.T("العمليات غير الظاهرة (خدمات، خلفية، نظام): {0} عملية تستهلك {1} من الذاكرة الخاصة؛ والتطبيقات ذات النوافذ: {2}.", "Non-visible processes (services, background, system): {0} processes using {1} of private memory; apps with windows: {2}."),
                hidden.Count, Fmt.GB(hidden.Sum(p => p.PrivWS)), Fmt.GB(s.Procs.Where(p => p.Kind == Kinds.App).Sum(p => p.PrivWS))));

            if (s.NonPaged > (1536L << 20))
                f.Add(string.Format(L.T("تحذير: الـ Non-paged pool = {0} (الطبيعي أقل من 1 GB). هذه ذاكرة نواة لا تُرحَّل أبداً، والتضخم عادةً تسرّب من تعريف (شبكة/كرت شاشة/مضاد فيروسات/VPN). أكبر الوسوم: {1}. افتح تبويب «ذاكرة النواة» وانقر مرتين على الوسم لمعرفة ملف التعريف.", "Warning: Non-paged pool = {0} (normal is under 1 GB). This kernel memory is never paged out, and growth usually means a driver leak (network/GPU/antivirus/VPN). Top tags: {1}. Open the Kernel memory tab and double-click a tag to find the driver file."),
                    Fmt.GB(s.NonPaged), TopTags(s, false)));
            if (s.PagedVirt > (3L << 30))
                f.Add(string.Format(L.T("تحذير: الـ Paged pool = {0} (المقيم منه في الرام {1}). أكبر الوسوم: {2}.", "Warning: Paged pool = {0} ({1} resident in RAM). Top tags: {2}."),
                    Fmt.GB(s.PagedVirt), Fmt.GB(s.PagedRes), TopTags(s, true)));
            if (s.Compression > (1L << 30))
                f.Add(string.Format(L.T("عملية Memory Compression تحمل {0}: هي صفحات برامج أخرى مضغوطة داخل الرام بدل كتابتها للقرص. مدير المهام يخفيها، وحجمها يعكس ضغطاً حقيقياً على الذاكرة.", "The Memory Compression process holds {0}: pages of other programs compressed inside RAM instead of written to disk. Task Manager hides it, and its size reflects real memory pressure."),
                    Fmt.GB(s.Compression)));
            if (s.VM > (512L << 20))
                f.Add(string.Format(L.T("آلات افتراضية (WSL2/Docker/Hyper-V) تستهلك {0}. لتحديد حدّ لـ WSL ضع في ‎%UserProfile%\\.wslconfig: [wsl2] memory=8GB ثم wsl --shutdown.", "Virtual machines (WSL2/Docker/Hyper-V) use {0}. To cap WSL put in %UserProfile%\\.wslconfig: [wsl2] memory=8GB then run wsl --shutdown."),
                    Fmt.GB(s.VM)));
            if (s.Unattributed > (3L << 30))
            {
                long locked = s.Unattributed - Math.Min(s.SharedMax, s.Unattributed);
                string msg = string.Format(L.T("الذاكرة غير المنسوبة {0}. جزء منها صفحات مشتركة (مكتبات DLL وملفات مخطّطة، بحدّ أقصى {1}) وجداول صفحات ومكدّسات النواة. ما يزيد على ذلك (≈ {2}) غالباً ذاكرة مقفلة بواسطة تعريف: كرت الشاشة، Hyper-V/حماية VBS، VMware/VirtualBox، أو قرص RAM — وهذه لا تظهر في مدير المهام إطلاقاً.", "Unattributed memory {0}. Part of it is shared pages (DLLs and mapped files, at most {1}), page tables and kernel stacks. Anything beyond that (about {2}) is usually memory locked by a driver: GPU, Hyper-V/VBS security, VMware/VirtualBox or a RAM disk. Task Manager never shows it."),
                    Fmt.GB(s.Unattributed), Fmt.GB(s.SharedMax), Fmt.GB(locked));
                if (locked < (1L << 30)) msg = msg.Substring(0, msg.IndexOf(L.T("ما يزيد", "Anything beyond"))).TrimEnd() + L.T(" لا يوجد مؤشر على ذاكرة مقفلة بواسطة التعريفات.", " No sign of driver-locked memory.");
                f.Add(msg);
            }
            f.Add(L.T("لماذا يختلف مدير المهام؟ عمود «الذاكرة» فيه هو الذاكرة الخاصة لكل عملية فقط، ولا يعرض ذاكرة النواة ولا الصفحات المشتركة ولا الذاكرة المقفلة بالتعريفات، ويخفي Memory Compression، لذلك مجموع العمود أقل بكثير من «قيد الاستخدام».", "Why does Task Manager differ? Its Memory column is only each process's private memory. It omits kernel memory, shared pages and driver-locked memory, and it hides Memory Compression, so the column sum is far below In use."));
            if (!s.CountersOk)
                f.Add(L.T("ملاحظة: عدّادات الأداء غير متاحة على هذا النظام، فاستُخدمت قيم GetPerformanceInfo التقريبية.", "Note: performance counters are unavailable on this system, so approximate GetPerformanceInfo values were used."));
            return f;
        }

        static string TopTags(Snapshot s, bool paged)
        {
            return string.Join(L.T("، ", ", "), s.Pools.OrderByDescending(p => paged ? p.Paged : p.NonPaged).Take(5)
                .Select(p => string.Format("'{0}' {1}", p.Tag, Fmt.MB(paged ? p.Paged : p.NonPaged))));
        }

        public static string Build(Snapshot s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RamLens report  " + s.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(new string('=', 70));
            foreach (var l in Findings(s)) { sb.AppendLine("- " + l); }
            sb.AppendLine();
            sb.AppendLine(string.Format("Installed {0} | Usable {1} | InUse {2} | Modified {3} | Standby {4} | Free {5}",
                Fmt.GB(s.Installed), Fmt.GB(s.Total), Fmt.GB(s.InUse), Fmt.GB(s.Modified), Fmt.GB(s.Standby), Fmt.GB(s.Free)));
            sb.AppendLine(string.Format("NonPaged {0} | PagedRes {1} (virt {2}) | SysCache {3} | Driver {4} | Code {5}",
                Fmt.GB(s.NonPaged), Fmt.GB(s.PagedRes), Fmt.GB(s.PagedVirt), Fmt.GB(s.SysCache), Fmt.GB(s.Driver), Fmt.GB(s.Code)));
            sb.AppendLine(string.Format("ProcPrivate {0} | Compression {1} | VM {2} | SharedMax {3} | Unattributed {4} | Commit {5}/{6}",
                Fmt.GB(s.ProcPrivate), Fmt.GB(s.Compression), Fmt.GB(s.VM), Fmt.GB(s.SharedMax), Fmt.GB(s.Unattributed), Fmt.GB(s.Commit), Fmt.GB(s.CommitLimit)));
            sb.AppendLine();
            sb.AppendLine("TOP PROGRAMS (grouped)  private | workingset | commit | count | kind");
            foreach (var g in Groups(s).Take(25))
                sb.AppendLine(string.Format("{0,-34} {1,10} {2,10} {3,10} {4,4}  {5}", g.Name, Fmt.MB(g.Priv), Fmt.MB(g.WS), Fmt.MB(g.Commit), g.Count, g.Kind));
            sb.AppendLine();
            sb.AppendLine("TOP PROCESSES  pid | private | workingset | commit | kind | services");
            foreach (var p in s.Procs.OrderByDescending(p => p.PrivWS).Take(30))
                sb.AppendLine(string.Format("{0,-30} {1,6} {2,10} {3,10} {4,10}  {5}  {6}", p.Name, p.Pid, Fmt.MB(p.PrivWS), Fmt.MB(p.WS), Fmt.MB(p.Commit), p.Kind, p.Services));
            sb.AppendLine();
            sb.AppendLine("TOP POOL TAGS  paged | nonpaged | outstanding allocs");
            foreach (var t in s.Pools.OrderByDescending(t => t.Paged + t.NonPaged).Take(20))
                sb.AppendLine(string.Format("'{0}' {1,10} {2,10} {3,10}  {4}", t.Tag, Fmt.MB(t.Paged), Fmt.MB(t.NonPaged), t.Allocs, PoolInfo.Describe(t.Tag)));
            return sb.ToString();
        }
    }

    class BarPanel : Panel
    {
        public List<Tuple<string, long, Color>> Segs = new List<Tuple<string, long, Color>>();
        public BarPanel() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            long total = Segs.Sum(x => x.Item2);
            if (total <= 0) return;
            float x0 = 0, w = Width - 1;
            foreach (var s in Segs)
            {
                float sw = (float)(w * s.Item2 / (double)total);
                using (var br = new SolidBrush(s.Item3)) g.FillRectangle(br, x0, 0, sw, Height - 1);
                x0 += sw;
            }
            g.DrawRectangle(Pens.Gray, 0, 0, Width - 1, Height - 1);
        }
    }

    class MainForm : Form
    {
        DataGridView gridProc, gridPool;
        TextBox txtSearch;
        CheckBox chkGroup, chkAuto;
        RichTextBox rtbDiag;
        BarPanel bar;
        FlowLayoutPanel cards, legend;
        ToolStripStatusLabel lblStatus;
        System.Windows.Forms.Timer timer;
        Snapshot last;
        bool busy;
        int tick;
        float sc = 1f;
        string procSort = "Private DESC", poolSort = "Total DESC";
        readonly Dictionary<string, Label> cardLabels = new Dictionary<string, Label>();
        readonly bool isAdmin;

        static readonly string[,] ProcCols = {
            { "Name", L.T("البرنامج", "Program") }, { "Pid", "PID" }, { "Count", L.T("عدد العمليات", "Processes") }, { "Kind", L.T("النوع", "Type") },
            { "Private", L.T("الذاكرة الخاصة (MB)", "Private memory (MB)") }, { "Shared", L.T("المشتركة (MB)", "Shared (MB)") }, { "WS", L.T("مجموعة العمل (MB)", "Working set (MB)") },
            { "Commit", L.T("الالتزام الخاص (MB)", "Private commit (MB)") }, { "Peak", L.T("الذروة (MB)", "Peak (MB)") }, { "PagedQ", "Paged pool (KB)" },
            { "NonPagedQ", "NonPaged pool (KB)" }, { "Handles", L.T("المقابض", "Handles") }, { "Threads", L.T("الخيوط", "Threads") },
            { "Session", L.T("الجلسة", "Session") }, { "Services", L.T("الخدمات المستضافة", "Hosted services") }, { "Path", L.T("المسار", "Path") } };

        public MainForm()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;
            isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            Text = L.T("عدسة الذاكرة RamLens — من يستهلك الرام فعلاً", "RamLens - who really uses your RAM") + (isAdmin ? L.T("  [مسؤول]", "  [Administrator]") : "");
            Font = new Font("Segoe UI", 9.5f);
            RightToLeft = L.Rtl;
            RightToLeftLayout = !L.En;
            Size = new Size((int)(1300 * sc), (int)(860 * sc));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 247, 250);

            // ---------- top summary ----------
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8, 6, 8, 2) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cards = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            string[] cardNames = { L.T("المركّبة", "Installed"), L.T("المحجوزة للعتاد", "Hardware reserved"), L.T("المستخدمة فعلياً", "In use"), L.T("Standby (قابلة للتحرير)", "Standby (releasable)"), L.T("حرّة", "Free"),
                                   "Modified", L.T("الالتزام Commit", "Commit"), L.T("خاص العمليات", "Process private"), L.T("نواة ويندوز", "Windows kernel"), L.T("غير منسوبة", "Unattributed") };
            foreach (var n in cardNames)
            {
                var lb = new Label
                {
                    AutoSize = false, Size = new Size((int)(150 * sc), (int)(50 * sc)), TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3), Text = n
                };
                cardLabels[n] = lb;
                cards.Controls.Add(lb);
            }
            bar = new BarPanel { Height = (int)(24 * sc), Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(3, 6, 3, 2) };
            legend = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            top.Controls.Add(cards, 0, 0);
            top.Controls.Add(bar, 0, 1);
            top.Controls.Add(legend, 0, 2);

            // ---------- tabs ----------
            var tabs = new TabControl { Dock = DockStyle.Fill, RightToLeftLayout = !L.En };
            var tpProc = new TabPage(L.T("العمليات (الكل، بما فيها المخفية)", "Processes (all, including hidden)"));
            var tpPool = new TabPage(L.T("ذاكرة النواة (Pool tags)", "Kernel memory (Pool tags)"));
            var tpDiag = new TabPage(L.T("التشخيص والشرح", "Diagnosis & explanation"));
            tabs.TabPages.AddRange(new[] { tpProc, tpPool, tpDiag });

            gridProc = MakeGrid();
            gridProc.CellFormatting += GridProc_CellFormatting;
            gridProc.ContextMenuStrip = MakeMenu();
            var tb = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
            tb.Controls.Add(new Label { Text = L.T("بحث:", "Search:"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            txtSearch = new TextBox { Width = (int)(220 * sc) };
            txtSearch.TextChanged += delegate { ApplyFilter(); };
            tb.Controls.Add(txtSearch);
            chkGroup = new CheckBox { Text = L.T("تجميع حسب البرنامج", "Group by program"), AutoSize = true, Checked = true, Margin = new Padding(12, 6, 3, 3) };
            chkGroup.CheckedChanged += delegate { BindProcs(); };
            tb.Controls.Add(chkGroup);
            chkAuto = new CheckBox { Text = L.T("تحديث تلقائي كل 5 ثوانٍ", "Auto-refresh every 5 s"), AutoSize = true, Checked = true, Margin = new Padding(12, 6, 3, 3) };
            tb.Controls.Add(chkAuto);
            tb.Controls.Add(MakeButton(L.T("تحديث الآن", "Refresh now"), delegate { Collect(true); }));
            tb.Controls.Add(MakeButton(L.T("تصدير CSV", "Export CSV"), delegate { ExportCsv(); }));
            tb.Controls.Add(MakeButton(L.T("حفظ تقرير نصّي", "Save text report"), delegate { SaveReport(); }));
            tb.Controls.Add(MakeButton(L.En ? "عربي" : "English", delegate { SwitchLanguage(); }));
            if (!isAdmin) tb.Controls.Add(MakeButton(L.T("تشغيل كمسؤول (مسارات كاملة)", "Run as administrator (full paths)"), delegate { RunAsAdmin(); }));
            tpProc.Controls.Add(gridProc);
            tpProc.Controls.Add(tb);

            gridPool = MakeGrid();
            gridPool.CellDoubleClick += delegate(object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) FindDriver(Convert.ToString(gridPool.Rows[e.RowIndex].Cells["Tag"].Value));
            };
            var poolHint = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = (int)(44 * sc), Padding = new Padding(6),
                Text = L.T("هذه ذاكرة يحجزها ويندوز والتعريفات داخل النواة ولا تظهر لأي عملية في مدير المهام. الوسم (Tag) يدل على المكوّن. انقر مرتين على أي وسم ليبحث البرنامج في ملفات التعريفات (.sys) عن المسؤول عنه.", "Memory reserved by Windows and drivers inside the kernel. No process shows it in Task Manager. The tag identifies the component; double-click a tag to search driver files (.sys) for its owner.")
            };
            tpPool.Controls.Add(gridPool);
            tpPool.Controls.Add(poolHint);

            rtbDiag = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 11f), RightToLeft = L.Rtl };
            tpDiag.Controls.Add(rtbDiag);

            var status = new StatusStrip();
            lblStatus = new ToolStripStatusLabel(L.T("جارٍ القراءة...", "Reading..."));
            status.Items.Add(lblStatus);

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(status);

            timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += delegate { if (chkAuto.Checked) { tick++; Collect(tick % 6 == 0); } };
            Shown += delegate { Collect(true); timer.Start(); };
        }

        Button MakeButton(string text, EventHandler h)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(6, 3, 3, 3) };
            b.Click += h;
            return b;
        }

        DataGridView MakeGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                RightToLeft = L.Rtl, AllowUserToResizeRows = false
            };
            g.RowTemplate.Height = (int)(24 * sc);
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 252);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 236, 244);
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = (int)(46 * sc);
            typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(g, true, null);
            return g;
        }

        ContextMenuStrip MakeMenu()
        {
            var m = new ContextMenuStrip { RightToLeft = L.Rtl };
            m.Items.Add(L.T("فتح موقع الملف", "Open file location"), null, delegate
            {
                string p = SelectedValue("Path");
                if (p.Length > 0) Process.Start("explorer.exe", "/select,\"" + p + "\"");
            });
            m.Items.Add(L.T("نسخ المسار", "Copy path"), null, delegate { string p = SelectedValue("Path"); if (p.Length > 0) Clipboard.SetText(p); });
            m.Items.Add(L.T("بحث في الإنترنت عن الاسم", "Search the web for this name"), null, delegate
            {
                string n = SelectedValue("Name");
                if (n.Length > 0) Process.Start("https://www.google.com/search?q=" + Uri.EscapeDataString(n + " process"));
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("إنهاء العملية...", "End process..."), null, delegate { KillSelected(); });
            return m;
        }

        string SelectedValue(string col)
        {
            if (gridProc.CurrentRow == null || !gridProc.Columns.Contains(col)) return "";
            return Convert.ToString(gridProc.CurrentRow.Cells[col].Value);
        }

        void KillSelected()
        {
            if (last == null || gridProc.CurrentRow == null) return;
            List<int> pids;
            string name = SelectedValue("Name");
            if (chkGroup.Checked) pids = last.Procs.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Pid).ToList();
            else pids = new List<int> { Convert.ToInt32(gridProc.CurrentRow.Cells["Pid"].Value) };
            var kind = SelectedValue("Kind");
            string warn = (kind == Kinds.System || kind == Kinds.Service) ? L.T("\n\nتنبيه: هذه عملية نظام/خدمة، وإنهاؤها قد يسبّب عدم استقرار ويندوز.", "\n\nWarning: this is a system process/service; ending it may make Windows unstable.") : "";
            if (MessageBox.Show(string.Format(L.T("إنهاء {0} ({1} عملية)؟ ستفقد أي عمل غير محفوظ فيها.{2}", "End {0} ({1} processes)? Any unsaved work in it will be lost.{2}"), name, pids.Count, warn),
                L.T("تأكيد", "Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2,
                L.MbOpt) != DialogResult.Yes) return;
            int ok = 0;
            foreach (int pid in pids) { try { Process.GetProcessById(pid).Kill(); ok++; } catch { } }
            lblStatus.Text = string.Format(L.T("أُنهيت {0} من {1} عملية.", "Ended {0} of {1} processes."), ok, pids.Count);
            Collect(false);
        }

        void Collect(bool services)
        {
            if (busy) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Snapshot s = null; string err = null;
                try { s = Collector.Take(services); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        if (s == null) { lblStatus.Text = L.T("خطأ: ", "Error: ") + err; return; }
                        last = s;
                        ShowSnapshot();
                    });
                }
                catch { busy = false; }
            });
        }

        void SetCard(string key, long bytes, Color c)
        {
            var lb = cardLabels[key];
            lb.Text = key + "\n" + Fmt.GB(bytes);
            lb.BackColor = c;
        }

        void ShowSnapshot()
        {
            var s = last;
            Color w = Color.White;
            SetCard(L.T("المركّبة", "Installed"), s.Installed, w);
            SetCard(L.T("المحجوزة للعتاد", "Hardware reserved"), s.HwReserved, w);
            SetCard(L.T("المستخدمة فعلياً", "In use"), s.InUse, Color.FromArgb(255, 226, 214));
            SetCard(L.T("Standby (قابلة للتحرير)", "Standby (releasable)"), s.Standby, Color.FromArgb(214, 234, 255));
            SetCard(L.T("حرّة", "Free"), s.Free, Color.FromArgb(222, 245, 225));
            SetCard("Modified", s.Modified, w);
            SetCard(L.T("الالتزام Commit", "Commit"), s.Commit, w);
            SetCard(L.T("خاص العمليات", "Process private"), s.ProcPrivate + s.Compression + s.VM, w);
            SetCard(L.T("نواة ويندوز", "Windows kernel"), s.Kernel, w);
            SetCard(L.T("غير منسوبة", "Unattributed"), s.Unattributed, s.Unattributed > (3L << 30) ? Color.FromArgb(255, 243, 200) : w);

            bar.Segs = new List<Tuple<string, long, Color>> {
                Tuple.Create(L.T("العمليات (خاص)", "Processes (private)"), s.ProcPrivate, Color.FromArgb(214, 69, 65)),
                Tuple.Create(L.T("ضغط الذاكرة", "Memory compression"), s.Compression, Color.FromArgb(142, 68, 173)),
                Tuple.Create(L.T("آلات افتراضية", "Virtual machines"), s.VM, Color.FromArgb(230, 126, 34)),
                Tuple.Create(L.T("نواة ويندوز", "Windows kernel"), s.Kernel, Color.FromArgb(52, 73, 94)),
                Tuple.Create(L.T("غير منسوبة (مشتركة/مقفلة)", "Unattributed (shared/locked)"), s.Unattributed, Color.FromArgb(241, 196, 15)),
                Tuple.Create("Modified", s.Modified, Color.FromArgb(149, 165, 166)),
                Tuple.Create(L.T("Standby (مؤقتة)", "Standby (cache)"), s.Standby, Color.FromArgb(93, 173, 226)),
                Tuple.Create(L.T("حرّة", "Free"), s.Free, Color.FromArgb(88, 214, 141)) };
            bar.Invalidate();
            legend.SuspendLayout();
            while (legend.Controls.Count > 0) legend.Controls[0].Dispose();
            foreach (var seg in bar.Segs)
            {
                legend.Controls.Add(new Panel { Size = new Size((int)(12 * sc), (int)(12 * sc)), BackColor = seg.Item3, Margin = new Padding(8, 6, 2, 2) });
                legend.Controls.Add(new Label { AutoSize = true, Text = seg.Item1 + " " + Fmt.GB(seg.Item2), Margin = new Padding(0, 3, 6, 2) });
            }
            legend.ResumeLayout();

            BindProcs();
            BindPools();

            rtbDiag.Clear();
            foreach (var line in Report.Findings(s))
            {
                rtbDiag.SelectionBullet = true;
                rtbDiag.AppendText(line + "\n\n");
            }
            lblStatus.Text = string.Format(L.T("آخر تحديث {0}  •  {1} عملية  •  {2}", "Last refresh {0}  -  {1} processes  -  {2}"), s.Time.ToString("HH:mm:ss"), s.Procs.Count,
                isAdmin ? L.T("صلاحيات مسؤول", "Administrator rights") : L.T("بدون صلاحيات مسؤول: بعض المسارات قد تكون فارغة، لكن أرقام الذاكرة كاملة ودقيقة", "Not elevated: some paths may be empty, but all memory figures are complete and accurate"));
        }

        void BindProcs()
        {
            if (last == null) return;
            var dvOld = gridProc.DataSource as DataView;
            if (dvOld != null && !string.IsNullOrEmpty(dvOld.Sort)) procSort = dvOld.Sort;
            string selName = SelectedValue("Name");
            string selPid = SelectedValue("Pid");
            int firstRow = gridProc.FirstDisplayedScrollingRowIndex;

            var t = new DataTable();
            bool grp = chkGroup.Checked;
            t.Columns.Add("Name", typeof(string));
            if (grp) t.Columns.Add("Count", typeof(int)); else t.Columns.Add("Pid", typeof(int));
            t.Columns.Add("Kind", typeof(string));
            t.Columns.Add("Private", typeof(double));
            t.Columns.Add("Shared", typeof(double));
            t.Columns.Add("WS", typeof(double));
            t.Columns.Add("Commit", typeof(double));
            if (!grp)
            {
                t.Columns.Add("Peak", typeof(double));
                t.Columns.Add("PagedQ", typeof(double));
                t.Columns.Add("NonPagedQ", typeof(double));
                t.Columns.Add("Handles", typeof(int));
                t.Columns.Add("Threads", typeof(int));
                t.Columns.Add("Session", typeof(int));
                t.Columns.Add("Services", typeof(string));
            }
            t.Columns.Add("Path", typeof(string));
            if (grp)
                foreach (var g in Report.Groups(last))
                    t.Rows.Add(g.Name, g.Count, g.Kind, Fmt.MBd(g.Priv), Fmt.MBd(Math.Max(0, g.WS - g.Priv)), Fmt.MBd(g.WS), Fmt.MBd(g.Commit), g.Path);
            else
                foreach (var p in last.Procs)
                    t.Rows.Add(p.Name, p.Pid, p.Kind, Fmt.MBd(p.PrivWS), Fmt.MBd(Math.Max(0, p.WS - p.PrivWS)), Fmt.MBd(p.WS), Fmt.MBd(p.Commit),
                        Fmt.MBd(p.PeakWS), Math.Round(p.PagedQ / 1024.0), Math.Round(p.NonPagedQ / 1024.0), p.Handles, p.Threads, p.Session, p.Services, p.Path);

            var dv = new DataView(t);
            try { dv.Sort = procSort; } catch { dv.Sort = procSort = "Private DESC"; }
            gridProc.DataSource = dv;
            for (int i = 0; i < ProcCols.GetLength(0); i++)
                if (gridProc.Columns.Contains(ProcCols[i, 0])) gridProc.Columns[ProcCols[i, 0]].HeaderText = ProcCols[i, 1];
            foreach (DataGridViewColumn c in gridProc.Columns)
            {
                if (c.ValueType == typeof(double)) { c.DefaultCellStyle.Format = "N0"; c.Width = (int)(110 * sc); }
                else if (c.ValueType == typeof(int)) c.Width = (int)(70 * sc);
            }
            gridProc.Columns["Name"].Width = (int)(200 * sc);
            gridProc.Columns["Kind"].Width = (int)(140 * sc);
            if (grp) gridProc.Columns["Count"].Width = (int)(95 * sc);
            gridProc.Columns["Path"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            gridProc.Columns["Path"].MinimumWidth = (int)(200 * sc);
            if (!grp) gridProc.Columns["Services"].Width = (int)(220 * sc);
            ApplyFilter();

            foreach (DataGridViewRow r in gridProc.Rows)
            {
                bool match = grp ? Convert.ToString(r.Cells["Name"].Value) == selName : Convert.ToString(r.Cells["Pid"].Value) == selPid;
                if (match && (selName.Length > 0)) { gridProc.CurrentCell = r.Cells["Name"]; break; }
            }
            if (firstRow >= 0 && firstRow < gridProc.RowCount) try { gridProc.FirstDisplayedScrollingRowIndex = firstRow; } catch { }
        }

        void ApplyFilter()
        {
            var dv = gridProc.DataSource as DataView;
            if (dv == null) return;
            string q = txtSearch.Text.Trim();
            if (q.Length == 0) { dv.RowFilter = ""; return; }
            var e = new StringBuilder();
            foreach (char c in q)
            {
                if (c == '*' || c == '%' || c == '[' || c == ']') e.Append('[').Append(c).Append(']');
                else if (c == '\'') e.Append("''");
                else e.Append(c);
            }
            string like = "LIKE '%" + e + "%'";
            string f = "Name " + like + " OR Path " + like + " OR Kind " + like;
            if (dv.Table.Columns.Contains("Services")) f += " OR Services " + like;
            dv.RowFilter = f;
        }

        void GridProc_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || gridProc.Columns[e.ColumnIndex].Name != "Private" || e.Value == null || e.Value == DBNull.Value) return;
            double mb = Convert.ToDouble(e.Value);
            if (mb >= 1024) { e.CellStyle.BackColor = Color.FromArgb(250, 200, 195); e.CellStyle.Font = new Font(gridProc.Font, FontStyle.Bold); }
            else if (mb >= 300) e.CellStyle.BackColor = Color.FromArgb(253, 229, 200);
        }

        void BindPools()
        {
            var dvOld = gridPool.DataSource as DataView;
            if (dvOld != null && !string.IsNullOrEmpty(dvOld.Sort)) poolSort = dvOld.Sort;
            int firstRow = gridPool.FirstDisplayedScrollingRowIndex;
            var t = new DataTable();
            t.Columns.Add("Tag", typeof(string));
            t.Columns.Add("NonPaged", typeof(double));
            t.Columns.Add("Paged", typeof(double));
            t.Columns.Add("Total", typeof(double));
            t.Columns.Add("Allocs", typeof(long));
            t.Columns.Add("Desc", typeof(string));
            foreach (var p in last.Pools)
                t.Rows.Add(p.Tag, Math.Round(p.NonPaged / 1048576.0, 2), Math.Round(p.Paged / 1048576.0, 2),
                    Math.Round((p.Paged + p.NonPaged) / 1048576.0, 2), p.Allocs, PoolInfo.Describe(p.Tag));
            var dv = new DataView(t);
            try { dv.Sort = poolSort; } catch { dv.Sort = poolSort = "Total DESC"; }
            gridPool.DataSource = dv;
            string[,] hdr = { { "Tag", L.T("الوسم", "Tag") }, { "NonPaged", L.T("NonPaged (MB) — لا تُرحَّل", "NonPaged (MB) - never paged") }, { "Paged", "Paged (MB)" },
                              { "Total", L.T("المجموع (MB)", "Total (MB)") }, { "Allocs", L.T("تخصيصات قائمة", "Live allocations") }, { "Desc", L.T("الوصف", "Description") } };
            for (int i = 0; i < hdr.GetLength(0); i++) gridPool.Columns[hdr[i, 0]].HeaderText = hdr[i, 1];
            foreach (DataGridViewColumn c in gridPool.Columns) { c.Width = (int)(150 * sc); if (c.ValueType == typeof(double)) c.DefaultCellStyle.Format = "N1"; }
            gridPool.Columns["Desc"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            if (firstRow >= 0 && firstRow < gridPool.RowCount) try { gridPool.FirstDisplayedScrollingRowIndex = firstRow; } catch { }
        }

        void FindDriver(string tag)
        {
            lblStatus.Text = L.T("جارٍ البحث عن الوسم '", "Searching driver files for tag '") + tag + L.T("' في ملفات التعريفات...", "'...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                var hits = new List<string>();
                byte[] pat = Encoding.ASCII.GetBytes(tag);
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
                foreach (var f in Directory.GetFiles(dir, "*.sys"))
                {
                    try { if (IndexOf(File.ReadAllBytes(f), pat) >= 0) hits.Add(Path.GetFileName(f)); } catch { }
                }
                BeginInvoke((MethodInvoker)delegate
                {
                    lblStatus.Text = L.T("انتهى البحث.", "Search finished.");
                    string msg = hits.Count == 0
                        ? L.T("لم يُعثر على الوسم في ملفات التعريفات. قد يكون من نواة ويندوز (ntoskrnl.exe) أو من تعريف في مجلد آخر.", "Tag not found in driver files. It may belong to the Windows kernel (ntoskrnl.exe) or to a driver in another folder.")
                        : L.T("الوسم '", "Tag '") + tag + L.T("' موجود في التعريفات التالية (أحدها المسؤول؛ الوسوم القصيرة الشائعة تعطي نتائج إضافية):\n\n", "' appears in these drivers (one of them is the owner; short common tags give extra hits):\n\n") + string.Join("\n", hits.Take(40));
                    MessageBox.Show(msg, L.T("البحث عن التعريف", "Find driver"), MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, L.MbOpt);
                });
            });
        }

        static int IndexOf(byte[] b, byte[] p)
        {
            for (int i = 0; i <= b.Length - p.Length; i++)
            {
                if (b[i] != p[0]) continue;
                int j = 1;
                while (j < p.Length && b[i + j] == p[j]) j++;
                if (j == p.Length) return i;
            }
            return -1;
        }

        void ExportCsv()
        {
            var dv = gridProc.DataSource as DataView;
            if (dv == null) return;
            using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "RamLens_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                var cols = gridProc.Columns.Cast<DataGridViewColumn>().ToList();
                sb.AppendLine(string.Join(",", cols.Select(c => Csv(c.HeaderText))));
                foreach (DataRowView r in dv) sb.AppendLine(string.Join(",", cols.Select(c => Csv(Convert.ToString(r[c.DataPropertyName])))));
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                lblStatus.Text = L.T("حُفظ: ", "Saved: ") + d.FileName;
            }
        }

        static string Csv(string v) { return "\"" + (v ?? "").Replace("\"", "\"\"") + "\""; }

        void SaveReport()
        {
            if (last == null) return;
            using (var d = new SaveFileDialog { Filter = "Text|*.txt", FileName = "RamLens_report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(d.FileName, Report.Build(last), new UTF8Encoding(true));
                lblStatus.Text = L.T("حُفظ: ", "Saved: ") + d.FileName;
            }
        }

        void SwitchLanguage()
        {
            L.Save(!L.En);
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true }); Close(); } catch { }
        }

        void RunAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch { }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            L.Load();
            if (args.Any(a => a.Equals("/en", StringComparison.OrdinalIgnoreCase))) L.En = true;
            if (args.Any(a => a.Equals("/ar", StringComparison.OrdinalIgnoreCase))) L.En = false;
            if (args.Length >= 2 && args[0].Equals("/report", StringComparison.OrdinalIgnoreCase))
            {
                string outPath = Path.GetFullPath(args[1]);
                File.WriteAllText(outPath, Report.Build(Collector.Take(true)), new UTF8Encoding(true));
                if (!args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase))) try { Process.Start("notepad.exe", "\"" + outPath + "\""); } catch { }
                return;
            }
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
