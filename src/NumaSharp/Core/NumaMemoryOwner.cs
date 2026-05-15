using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NumaSharp.Core.Interop;

namespace NumaSharp.Core;

/// <summary>
/// An <see cref="IMemoryOwner{T}"/> backed by cache-line-aligned native memory.
/// Returned to the owning <see cref="NumaMemoryPool"/> on disposal.
/// </summary>
public sealed unsafe class NumaMemoryOwner : IMemoryOwner<byte>
{
    private void* _ptr;
    private readonly int _length;
    private readonly NumaMemoryPool? _pool; // null = standalone (not pooled)
    private bool _disposed;

    /// <inheritdoc />
    public Memory<byte> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new NativeMemoryManager(_ptr, _length).Memory;
        }
    }

    /// <summary>Gets the byte length of this block.</summary>
    public int Length => _length;

    /// <summary>
    /// Returns a <see cref="Span{T}"/> without going through <see cref="Memory"/>.
    /// Only valid while this owner is alive and not disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Span<byte>(_ptr, _length);
    }

    private NumaMemoryOwner(void* ptr, int length, NumaMemoryPool? pool)
    {
        _ptr = ptr;
        _length = length;
        _pool = pool;
    }

    internal static NumaMemoryOwner Allocate(int size, NumaMemoryPool? pool)
    {
        const int alignment = 64; // cache line
        void* ptr = NativeMemory.AlignedAlloc((nuint)size, alignment);
        if (pool is not null && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            MbindToNode(ptr, (nuint)size, pool.NumaNodeId);
        }
        return new NumaMemoryOwner(ptr, size, pool);
    }

    /// <summary>
    /// Calls <c>mbind(2)</c> to bind newly allocated memory to the specified NUMA node.
    /// Silent no-op when <paramref name="numaNodeId"/> is negative or the kernel does not
    /// support NUMA memory policies (<c>ENOSYS</c>).
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static unsafe void MbindToNode(void* ptr, nuint size, int numaNodeId)
    {
        if (numaNodeId < 0)
        {
            return;
        }

        // nodemask: one ulong covers nodes 0–63; bit N set → allow node N.
        ulong nodemask = 1UL << (numaNodeId & 63);
        // maxnode > highest bit set; flags=0 → applies to future page faults only.
        Libc.Mbind(ptr, (ulong)size, Libc.MpolBind, &nodemask, 64, 0);
        // Non-fatal: silently ignore failure (ENOSYS on non-NUMA kernels, EPERM, EINVAL).
    }

    /// <summary>Frees the native memory immediately, bypassing the pool.</summary>
    internal void FreeNative()
    {
        if (_ptr is not null)
        {
            NativeMemory.AlignedFree(_ptr);
            _ptr = null;
        }
    }

    /// <summary>Resets the disposed flag so this owner can be reused from the pool.</summary>
    internal void ResetForReuse() => _disposed = false;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_pool is not null)
        {
            _pool.Return(this);
        }
        else
        {
            FreeNative();
        }
    }

    // MemoryManager that wraps an unmanaged pointer.
    private sealed class NativeMemoryManager(void* ptr, int length) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => new(ptr, length);

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            if ((uint)elementIndex >= (uint)length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            }

            return new MemoryHandle((byte*)ptr + elementIndex);
        }

        public override void Unpin() { }

        protected override void Dispose(bool disposing) { }
    }
}
