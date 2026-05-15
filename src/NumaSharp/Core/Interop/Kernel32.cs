using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NumaSharp.Core.Interop;

/// <summary>P/Invoke bindings for Windows kernel32.dll threading APIs.</summary>
[SupportedOSPlatform("windows")]
internal static partial class Kernel32
{
    private const string Kernel32Dll = "kernel32.dll";

    /// <summary>GetCurrentThread() — returns a pseudo-handle for the calling thread.</summary>
    [LibraryImport(Kernel32Dll)]
    internal static partial nint GetCurrentThread();

    /// <summary>SetThreadAffinityMask — sets the processor affinity mask for a thread.</summary>
    [LibraryImport(Kernel32Dll, SetLastError = true)]
    internal static partial nuint SetThreadAffinityMask(nint hThread, nuint dwThreadAffinityMask);

    /// <summary>GetCurrentProcessorNumber — returns the index of the current processor.</summary>
    [LibraryImport(Kernel32Dll)]
    internal static partial uint GetCurrentProcessorNumber();
}
