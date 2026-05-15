using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NumaSharp.Transport.Epoll.Interop;

[SupportedOSPlatform("linux")]
internal static partial class EpollInterop
{
    private const string LibcName = "libc";

    // epoll_create1 flags
    internal const int EpollCloexec = 0x80000;

    // epoll event flags
    internal const uint EpollIn = 0x001;
    internal const uint EpollOut = 0x004;
    internal const uint EpollErr = 0x008;
    internal const uint EpollHup = 0x010;
    internal const uint EpollRdhup = 0x2000;
    internal const uint EpollEdgeTriggered = 1u << 31;
    internal const uint EpollOneShot      = 1u << 30;
    internal const uint EpollExclusive    = 1u << 28;  // EPOLLEXCLUSIVE (kernel 4.5+): one waiter notified per event

    // epoll_ctl ops
    internal const int EpollCtlAdd = 1;
    internal const int EpollCtlDel = 2;
    internal const int EpollCtlMod = 3;

    // eventfd flags (for wakeup fd)
    internal const int EfdNonblock = 0x800;    // O_NONBLOCK
    internal const int EfdCloexec  = 0x80000;  // O_CLOEXEC

    [LibraryImport(LibcName, EntryPoint = "epoll_create1", SetLastError = true)]
    internal static partial int EpollCreate1(int flags);

    [LibraryImport(LibcName, EntryPoint = "epoll_ctl", SetLastError = true)]
    internal static partial int EpollCtl(int epfd, int op, int fd, ref EpollEvent @event);

    [LibraryImport(LibcName, EntryPoint = "epoll_wait", SetLastError = true)]
    internal static unsafe partial int EpollWait(int epfd, EpollEvent* events, int maxEvents, int timeout);

    [LibraryImport(LibcName, EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int fd);

    /// <summary>Creates an eventfd(2) for inter-thread wakeup signalling.</summary>
    [LibraryImport(LibcName, EntryPoint = "eventfd", SetLastError = true)]
    internal static partial int EventFd(uint initVal, int flags);

    /// <summary>write(2) — used to increment the eventfd counter by <c>count</c> bytes.</summary>
    [LibraryImport(LibcName, EntryPoint = "write", SetLastError = true)]
    internal static unsafe partial nint Write(int fd, void* buf, nuint count);

    /// <summary>read(2) — used to drain the eventfd counter (reads 8 bytes).</summary>
    [LibraryImport(LibcName, EntryPoint = "read", SetLastError = true)]
    internal static unsafe partial nint Read(int fd, void* buf, nuint count);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct EpollEvent
{
    public uint Events;
    public EpollData Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct EpollData
{
    [FieldOffset(0)] public nint Ptr;
    [FieldOffset(0)] public int Fd;
    [FieldOffset(0)] public uint U32;
    [FieldOffset(0)] public ulong U64;
}
