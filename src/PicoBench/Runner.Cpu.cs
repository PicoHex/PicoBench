namespace PicoBench;

public static partial class Runner
{
    private static volatile int _linuxPerfFd = -1;

    internal static CpuCycleMeasurementKind GetCpuCycleMeasurementKind()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CpuCycleMeasurementKind.ThreadCycles;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return _linuxPerfFd >= 0
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
        return GetCpuCycleMeasurementKind() is CpuCycleMeasurementKind.ThreadCycles
            or CpuCycleMeasurementKind.PerfEventCpuCycles;
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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxPerfFd >= 0)
            return ReadLinuxPerfCounter();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: mach_absolute_time is a monotonic clock, not real CPU cycles.
            // CpuCyclesPerOp will not be meaningful on this platform.
            return GetMacOsMonotonicTime();
        }

        return 0;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr threadHandle, ref ulong cycleTime);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

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
            _ => -1
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerfEventAttr
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
        if (_linuxPerfFd < 0)
            return;

        try
        {
            LinuxClose(_linuxPerfFd);
        }
        catch
        {
            // Ignore errors during cleanup.
        }
        finally
        {
            _linuxPerfFd = -1;
        }
    }

    private static void InitializeLinuxPerf()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupLinuxPerf();
        AppDomain.CurrentDomain.DomainUnload += (_, _) => CleanupLinuxPerf();

        try
        {
            if (File.Exists("/proc/sys/kernel/perf_event_paranoid"))
            {
                var paranoidValue = File.ReadAllText("/proc/sys/kernel/perf_event_paranoid").Trim();
                if (int.TryParse(paranoidValue, out var value) && value > 1)
                {
                    _linuxPerfFd = -1;
                    return;
                }
            }

            var attr = new PerfEventAttr
            {
                Type = PerfTypeHardware,
                Size = (uint)Marshal.SizeOf<PerfEventAttr>(),
                Config = PerfCountHwCpuCycles,
                Flags = 0
            };

            var syscallNumber = GetPerfEventOpenSyscallNumber();
            if (syscallNumber < 0)
            {
                _linuxPerfFd = -1;
                return;
            }

            _linuxPerfFd = SyscallPerfEventOpen(syscallNumber, ref attr, 0, -1, -1, 0);
            if (_linuxPerfFd < 0)
                _linuxPerfFd = -1;
        }
        catch (Exception ex)
            when (ex is UnauthorizedAccessException or IOException or DllNotFoundException)
        {
            _linuxPerfFd = -1;
        }
        catch
        {
            _linuxPerfFd = -1;
        }
    }

    private static unsafe ulong ReadLinuxPerfCounter()
    {
        if (_linuxPerfFd < 0)
            return 0;

        try
        {
            byte* buffer = stackalloc byte[sizeof(ulong)];
            var bytesRead = LinuxRead(_linuxPerfFd, buffer, (IntPtr)sizeof(ulong));
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

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int mach_timebase_info(out MachTimebaseInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MachTimebaseInfo
    {
        public uint Numer;
        public uint Denom;
    }

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
