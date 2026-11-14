using System.Runtime.InteropServices;

namespace Isla.Services;

internal readonly record struct SystemStats(double CpuPercent, double RamPercent, double RamUsedGb, double RamTotalGb);

/// <summary>
/// CPU y RAM sin dependencias externas: GetSystemTimes para la CPU (diferencia
/// entre dos muestras) y GlobalMemoryStatusEx para la memoria.
/// </summary>
internal sealed class StatsService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _primed;

    internal SystemStats Sample()
    {
        var cpu = SampleCpu();
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };

        if (!GlobalMemoryStatusEx(ref memory) || memory.TotalPhys == 0)
        {
            return new SystemStats(cpu, 0, 0, 0);
        }

        const double gb = 1024d * 1024d * 1024d;
        var used = memory.TotalPhys - memory.AvailPhys;
        return new SystemStats(cpu, memory.MemoryLoad, used / gb, memory.TotalPhys / gb);
    }

    private double SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleNow = idle.ToUInt64();
        var kernelNow = kernel.ToUInt64();
        var userNow = user.ToUInt64();

        if (!_primed)
        {
            // La primera muestra solo sirve como referencia: el porcentaje real
            // necesita dos lecturas separadas en el tiempo.
            _primed = true;
            _lastIdle = idleNow;
            _lastKernel = kernelNow;
            _lastUser = userNow;
            return 0;
        }

        var idleDelta = idleNow - _lastIdle;
        var totalDelta = (kernelNow - _lastKernel) + (userNow - _lastUser);

        _lastIdle = idleNow;
        _lastKernel = kernelNow;
        _lastUser = userNow;

        if (totalDelta == 0) return 0;

        var busy = 100d * (totalDelta - idleDelta) / totalDelta;
        return Math.Clamp(busy, 0, 100);
    }
}
