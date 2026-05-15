using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NumaSharp.Core;

/// <summary>
/// Windows kernel32 API surface needed for NUMA topology discovery and thread affinity.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    // ── Structures ────────────────────────────────────────────────────────────

    /// <summary>
    /// Describes the processor-group affinity of a NUMA node or a thread.
    /// Maps to the Windows GROUP_AFFINITY structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GROUP_AFFINITY
    {
        public nuint  Mask;
        public ushort Group;
        public ushort Reserved0;
        public ushort Reserved1;
        public ushort Reserved2;
    }

    /// <summary>
    /// Identifies a specific logical processor by group and number.
    /// Maps to the Windows PROCESSOR_NUMBER structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESSOR_NUMBER
    {
        public ushort Group;
        public byte   Number;
        public byte   Reserved;
    }

    // ── Imports ───────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumaHighestNodeNumber(out uint HighestNodeNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumaNodeProcessorMaskEx(
        ushort          Node,
        out GROUP_AFFINITY ProcessorMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetThreadGroupAffinity(
        nint            hThread,
        ref GROUP_AFFINITY GroupAffinity,
        out GROUP_AFFINITY PreviousGroupAffinity);

    [DllImport("kernel32.dll")]
    internal static extern nint GetCurrentThread();

    [DllImport("kernel32.dll")]
    internal static extern void GetCurrentProcessorNumberEx(out PROCESSOR_NUMBER ProcNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumaProcessorNodeEx(
        ref PROCESSOR_NUMBER Processor,
        out ushort           NodeNumber);
}
