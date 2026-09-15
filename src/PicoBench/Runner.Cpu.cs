namespace PicoBench;

public static partial class Runner
{
    internal const ulong PerfAttrInherit = 0x2;
    internal const ulong PerfAttrExcludeKernel = 0x20;

    private static readonly object LinuxPerfGate = new();
    private static bool _linuxPerfEnabled;
    private static int _linuxPerfFd = -1;
    private static int _linuxPerfOwnerThreadId = -1;
    private static int _linuxPerfProcessFd = -1;

    internal static CpuCycleMeasurementKind GetCpuCycleMeasurementKind()
    {
        EnsurePlatformInitialized();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CpuCycleMeasurementKind.ThreadCycles;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return _linuxPerfEnabled
                ? CpuCycleMeasurementKind.PerfEventCpuCycles
                : CpuCycleMeasurementKind.Unsupported;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return CpuCycleMeasurementKind.MonotonicClockProxy;

        return CpuCycleMeasurementKind.Unsupported;
    }

    internal static bool AreCpuCyclesAvailable() =>
        GetCpuCycleMeasurementKind() != CpuCycleMeasurementKind.Unsupported;

    internal static bool HasMeaningfulCpuCycles()
    {
        return GetCpuCycleMeasurementKind()
            is CpuCycleMeasurementKind.ThreadCycles
                or CpuCycleMeasurementKind.PerfEventCpuCycles;
    }

    /// <summary>
    /// Reads a cycle counter that is valid across thread hops. Used by async
    /// timing, where the measured work may resume on a different thread
    /// between the start and end reads.
    /// </summary>
    private static ulong GetProcessCpuCycles()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ulong cycleCount = 0;
            if (QueryProcessCycleTime(GetCurrentProcess(), ref cycleCount))
                return cycleCount;

            return 0;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxPerfProcessFd >= 0)
            return ReadLinuxPerfCounter(_linuxPerfProcessFd);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // mach_absolute_time is process-wide and monotonic, so it stays
            // valid across thread hops.
            return GetMacOsMonotonicTime();
        }

        return 0;
    }

    private static ulong GetCpuCycles()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ulong cycleCount = 0;
            if (QueryThreadCycleTime(GetCurrentThread(), ref cycleCount))
                return cycleCount;

            return 0;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!_linuxPerfEnabled)
                return 0;

            EnsureThreadPerfCounter();
            return _linuxPerfFd >= 0 ? ReadLinuxPerfCounter(_linuxPerfFd) : 0;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: mach_absolute_time is a monotonic clock, not real CPU cycles.
            // CpuCyclesPerOp will not be meaningful on this platform.
            return GetMacOsMonotonicTime();
        }

        return 0;
    }

    /// <summary>
    /// Opens (or re-opens) the per-thread cycle counter for the calling thread.
    /// The perf event is task-bound, so a counter opened on one thread does not
    /// advance when read from another thread. Platform initialization must not
    /// open it eagerly (environment metadata may be created on any thread).
    /// </summary>
    private static void EnsureThreadPerfCounter()
    {
        var threadId = Environment.CurrentManagedThreadId;
        if (_linuxPerfFd >= 0 && _linuxPerfOwnerThreadId == threadId)
            return;

        lock (LinuxPerfGate)
        {
            if (_linuxPerfFd >= 0 && _linuxPerfOwnerThreadId == threadId)
                return;

            var syscallNumber = GetPerfEventOpenSyscallNumber();
            if (syscallNumber < 0)
                return;

            var attr = CreateCyclesPerfEventAttr();
            var fd = SyscallPerfEventOpen(syscallNumber, ref attr, 0, -1, -1, 0);
            if (fd < 0)
                return;

            var previous = _linuxPerfFd;
            _linuxPerfFd = fd;
            _linuxPerfOwnerThreadId = threadId;
            if (previous >= 0)
                CloseLinuxPerfFd(previous);
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr threadHandle, ref ulong cycleTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryProcessCycleTime(IntPtr processHandle, ref ulong cycleTime);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private const int PerfTypeHardware = 0;
    private const int PerfCountHwCpuCycles = 0;

    private static int GetPerfEventOpenSyscallNumber()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        return architecture switch
        {
            Architecture.X64 => 298,
            Architecture.X86 => 336,
            Architecture.Arm64 => 241,
            Architecture.Arm => 364,
            _ => -1,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PerfEventAttr
    {
        public uint Type;
        public uint Size;
        public ulong Config;
        public ulong SamplePeriod;
        public ulong SampleType;
        public ulong ReadFormat;
        public ulong Flags;
        public uint WakeupEvents;
        public uint BpType;
        public ulong BpAddr;
        public ulong BpLen;
        public ulong BranchSampleType;
        public ulong SampleRegsUser;
        public ulong SampleStackUser;
        public int ClockId;
        public ulong SampleRegsIntr;
        public uint AuxWatermark;
        public ushort SampleMaxStack;
        public ushort Reserved2;
        public uint AuxSampleSize;
        public uint Reserved3;
    }

    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static extern int SyscallPerfEventOpen(
        int syscallNumber,
        ref PerfEventAttr attr,
        int pid,
        int cpu,
        int groupFd,
        ulong flags
    );

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern unsafe IntPtr LinuxRead(int fd, byte* buf, IntPtr count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int LinuxClose(int fd);

    private static void CleanupLinuxPerf()
    {
        _linuxPerfOwnerThreadId = -1;
        CloseLinuxPerfFd(Interlocked.Exchange(ref _linuxPerfFd, -1));
        CloseLinuxPerfFd(Interlocked.Exchange(ref _linuxPerfProcessFd, -1));
    }

    private static void CloseLinuxPerfFd(int fd)
    {
        if (fd < 0)
            return;

        try
        {
            LinuxClose(fd);
        }
        catch
        {
            // Ignore errors during cleanup.
        }
    }

    /// <summary>
    /// Decides whether unprivileged hardware cycle counting is allowed.
    /// <c>perf_event_paranoid</c> above 2 denies unprivileged perf events
    /// entirely; at 2 user-space counting is still allowed because the event
    /// excludes kernel cycles. A null value means the setting was unreadable
    /// and the syscall is attempted.
    /// </summary>
    internal static bool ShouldEnableLinuxPerf(int? paranoidValue)
    {
        return paranoidValue is null || paranoidValue.Value <= 2;
    }

    internal static PerfEventAttr CreateCyclesPerfEventAttr()
    {
        return new PerfEventAttr
        {
            Type = PerfTypeHardware,
            Size = (uint)Marshal.SizeOf<PerfEventAttr>(),
            Config = PerfCountHwCpuCycles,
            Flags = PerfAttrExcludeKernel,
        };
    }

    internal static PerfEventAttr CreateProcessCyclesPerfEventAttr()
    {
        var attr = CreateCyclesPerfEventAttr();
        attr.Flags |= PerfAttrInherit;
        return attr;
    }

    private static int? ReadPerfEventParanoid()
    {
        try
        {
            if (!File.Exists("/proc/sys/kernel/perf_event_paranoid"))
                return null;

            var raw = File.ReadAllText("/proc/sys/kernel/perf_event_paranoid").Trim();
            return int.TryParse(raw, out var value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static void InitializeLinuxPerf()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupLinuxPerf();
        AppDomain.CurrentDomain.DomainUnload += (_, _) => CleanupLinuxPerf();

        try
        {
            if (!ShouldEnableLinuxPerf(ReadPerfEventParanoid()))
                return;

            var syscallNumber = GetPerfEventOpenSyscallNumber();
            if (syscallNumber < 0)
                return;

            // Process-wide companion for async timing: the measured work may
            // resume on a different thread between the start and end reads, so
            // per-thread counters cannot be subtracted safely. The per-thread
            // counter is opened lazily by EnsureThreadPerfCounter so it is
            // bound to the thread that actually runs the benchmark.
            var processAttr = CreateProcessCyclesPerfEventAttr();
            var processId = Process.GetCurrentProcess().Id;
            _linuxPerfProcessFd = SyscallPerfEventOpen(
                syscallNumber,
                ref processAttr,
                processId,
                -1,
                -1,
                0
            );
            if (_linuxPerfProcessFd < 0)
                _linuxPerfProcessFd = -1;

            _linuxPerfEnabled = true;
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException or DllNotFoundException)
        {
            _linuxPerfEnabled = false;
            _linuxPerfFd = -1;
            _linuxPerfProcessFd = -1;
        }
        catch
        {
            _linuxPerfEnabled = false;
            _linuxPerfFd = -1;
            _linuxPerfProcessFd = -1;
        }
    }

    private static unsafe ulong ReadLinuxPerfCounter(int fd)
    {
        if (fd < 0)
            return 0;

        try
        {
            byte* buffer = stackalloc byte[sizeof(ulong)];
            var bytesRead = LinuxRead(fd, buffer, (IntPtr)sizeof(ulong));
            if (bytesRead == (IntPtr)sizeof(ulong))
            {
                ulong value = 0;
                for (var i = 0; i < sizeof(ulong); i++)
                    value |= ((ulong)buffer[i]) << (i * 8);

                return value;
            }

            CleanupLinuxPerf();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            CleanupLinuxPerf();
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern ulong mach_absolute_time();

    private static ulong GetMacOsMonotonicTime()
    {
        try
        {
            return mach_absolute_time();
        }
        catch
        {
            return 0;
        }
    }
}
