using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NumaSharp.Core.Interop;

/// <summary>P/Invoke bindings for Linux libc scheduling and memory syscalls.</summary>
[SupportedOSPlatform("linux")]
internal static partial class Libc
{
    private const string LibcName = "libc";

    /// <summary>sched_setaffinity(2) — set thread CPU affinity.</summary>
    [LibraryImport(LibcName, EntryPoint = "sched_setaffinity", SetLastError = true)]
    internal static partial int SchedSetaffinity(int pid, nuint cpuSetSize, ulong[] cpuSet);

    /// <summary>sched_getaffinity(2) — get thread CPU affinity.</summary>
    [LibraryImport(LibcName, EntryPoint = "sched_getaffinity", SetLastError = true)]
    internal static partial int SchedGetaffinity(int pid, nuint cpuSetSize, ulong[] cpuSet);

    /// <summary>
    /// Raw syscall(2) entry point — used to invoke mbind(2) without depending on libnuma.
    /// The signature is fixed (non-variadic) for the exact argument count mbind needs.
    /// </summary>
    [LibraryImport(LibcName, EntryPoint = "syscall", SetLastError = true)]
    private static unsafe partial int SyscallMbind(
        long nr, void* addr, ulong len, int mode, ulong* nodemask, ulong maxnode, uint flags);

    // mbind(2) syscall numbers — Linux only, architecture-specific.
    private static readonly long s_sysNrMbind = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64   => 237,
        Architecture.Arm64 => 235,
        _                  => -1,
    };

    /// <summary>
    /// mbind(2) — bind memory policy to a NUMA node.
    /// Invoked via <c>syscall(2)</c> so no dependency on libnuma is required.
    /// Returns -1 on unsupported architectures without touching errno.
    /// </summary>
    internal static unsafe int Mbind(void* addr, ulong len, int mode, ulong* nodemask, ulong maxnode, uint flags)
    {
        if (s_sysNrMbind < 0)
        {
            return -1;
        }

        return SyscallMbind(s_sysNrMbind, addr, len, mode, nodemask, maxnode, flags);
    }

    /// <summary>MPOL_BIND — strict NUMA node binding policy.</summary>
    internal const int MpolBind = 2;

    /// <summary>MPOL_DEFAULT — default memory policy.</summary>
    internal const int MpolDefault = 0;
}
