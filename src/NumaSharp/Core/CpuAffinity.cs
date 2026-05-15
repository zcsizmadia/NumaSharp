using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NumaSharp.Core.Interop;

namespace NumaSharp.Core;

/// <summary>
/// Provides CPU affinity control for the current thread.
/// On Linux this uses <c>sched_setaffinity</c>; on Windows <c>SetThreadAffinityMask</c>.
/// On unsupported platforms the call is silently ignored.
/// </summary>
public static class CpuAffinity
{
    /// <summary>
    /// Pins the calling thread to a single logical CPU.
    /// </summary>
    /// <param name="cpuId">0-based logical CPU index.</param>
    public static void SetCurrentThreadAffinity(int cpuId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cpuId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cpuId, Environment.ProcessorCount);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            SetLinuxAffinity(cpuId);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetWindowsAffinity((ulong)(1L << cpuId));
        }
        // macOS: unsupported, silently ignore
    }

    /// <summary>
    /// Sets the CPU affinity mask for the calling thread.
    /// Each set bit corresponds to a logical CPU the thread may run on.
    /// </summary>
    /// <param name="affinityMask">Bitmask of allowed CPUs.</param>
    public static void SetCurrentThreadAffinityMask(ulong affinityMask)
    {
        ArgumentOutOfRangeException.ThrowIfZero(affinityMask);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            SetLinuxAffinityMask(affinityMask);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetWindowsAffinity(affinityMask);
        }
    }

    /// <summary>
    /// Pins the calling thread to the CPUs in <paramref name="cpuIds"/>.
    /// Unlike <see cref="SetCurrentThreadAffinityMask(ulong)"/>, this correctly handles
    /// systems with more than 64 logical CPUs by setting the appropriate word of the
    /// 1024-bit Linux <c>cpu_set_t</c>.
    /// On Windows, only CPUs 0–63 are reachable via <c>SetThreadAffinityMask</c>;
    /// higher CPUs require processor-group APIs (not implemented).
    /// </summary>
    public static void SetCurrentThreadAffinityForCpus(IReadOnlyList<int> cpuIds)
    {
        ArgumentNullException.ThrowIfNull(cpuIds);
        if (cpuIds.Count == 0)
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            SetLinuxAffinityForCpus(cpuIds);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ulong mask = 0;
            foreach (int cpu in cpuIds)
            {
                if ((uint)cpu < 64u)
                {
                    mask |= 1UL << cpu;
                }
            }

            if (mask != 0)
            {
                SetWindowsAffinity(mask);
            }
        }
        // macOS: unsupported, silently ignore
    }

    /// <summary>
    /// Returns the affinity mask that covers all CPUs of the given NUMA node.
    /// </summary>
    /// <remarks>
    /// The returned <c>ulong</c> represents at most 64 CPUs (bits 0–63).
    /// Use <see cref="SetCurrentThreadAffinityForCpus"/> on systems with more than 64 CPUs.
    /// </remarks>
    public static ulong BuildNodeAffinityMask(NumaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ulong mask = 0;
        foreach (int cpu in node.CpuIds)
        {
            if (cpu < 64)
            {
                mask |= (ulong)(1L << cpu);
            }
        }

        return mask;
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinuxAffinity(int cpuId)
    {
        // cpu_set_t is 128 bytes (1024 bits) on Linux
        ulong[] cpuSet = new ulong[16];
        cpuSet[cpuId / 64] = (ulong)(1L << (cpuId % 64));

        int result = Libc.SchedSetaffinity(0, (nuint)(cpuSet.Length * sizeof(ulong)), cpuSet);
        if (result != 0)
        {
            ThrowForErrno(Marshal.GetLastPInvokeError(), cpuId);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinuxAffinityMask(ulong affinityMask)
    {
        // cpu_set_t is 128 bytes (1024 bits) on Linux.
        // A ulong covers only CPUs 0-63; cpuSet[0] is the correct word for those bits.
        ulong[] cpuSet = new ulong[16];
        cpuSet[0] = affinityMask;

        int result = Libc.SchedSetaffinity(0, (nuint)(cpuSet.Length * sizeof(ulong)), cpuSet);
        if (result != 0)
        {
            ThrowForErrno(Marshal.GetLastPInvokeError(), -1);
        }
    }

    [SupportedOSPlatform("linux")]
    private static void SetLinuxAffinityForCpus(IReadOnlyList<int> cpuIds)
    {
        // cpu_set_t is 128 bytes (1024 bits) on Linux — handles up to 1024 logical CPUs.
        ulong[] cpuSet = new ulong[16];
        foreach (int cpu in cpuIds)
        {
            // Silently ignore CPUs beyond the kernel's cpu_set_t capacity.
            if ((uint)cpu < 1024u)
            {
                cpuSet[cpu >> 6] |= 1UL << (cpu & 63);
            }
        }

        int result = Libc.SchedSetaffinity(0, (nuint)(cpuSet.Length * sizeof(ulong)), cpuSet);
        if (result != 0)
        {
            ThrowForErrno(Marshal.GetLastPInvokeError(), -1);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsAffinity(ulong affinityMask)
    {
        nint threadHandle = Kernel32.GetCurrentThread();
        nuint result = Kernel32.SetThreadAffinityMask(threadHandle, (nuint)affinityMask);
        if (result == 0)
        {
            throw new InvalidOperationException(
                $"SetThreadAffinityMask failed with error {Marshal.GetLastPInvokeError()}.");
        }
    }

    private static void ThrowForErrno(int errno, int cpuId)
    {
        string message = errno switch
        {
            1 => "Operation not permitted (EPERM). This process may lack CAP_SYS_NICE.",
            22 => $"Invalid argument (EINVAL). CPU {cpuId} may exceed the kernel's cpu_set_t size.",
            _ => $"sched_setaffinity failed with errno {errno}."
        };

        throw new InvalidOperationException(message);
    }
}
