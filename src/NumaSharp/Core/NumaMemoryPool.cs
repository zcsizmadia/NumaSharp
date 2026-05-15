using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NumaSharp.Core;

/// <summary>
/// A NUMA-aware <see cref="MemoryPool{T}"/> that allocates cache-line-aligned native
/// memory blocks and recycles them via a lock-free stack per thread.
/// </summary>
/// <remarks>
/// Blocks are allocated with <see cref="NativeMemory.AlignedAlloc"/> and are therefore
/// zero-copy compatible — they can be pinned, used in scatter/gather I/O rings, and
/// handed directly to the kernel.
/// </remarks>
public sealed class NumaMemoryPool : MemoryPool<byte>
{
    private const int CacheLineSize = 64;
    private const int DefaultBlockSize = 4096;      // 4 KB — one OS page
    private const int MaxPooledBlocksPerThread = 32;

    private readonly int _blockSize;
    private readonly int _numaNodeId;
    private readonly ThreadLocal<Stack<NumaMemoryOwner>> _threadLocalPool;
    private bool _disposed;

    /// <inheritdoc />
    public override int MaxBufferSize => int.MaxValue;

    /// <summary>Gets the NUMA node ID this pool is associated with.</summary>
    public int NumaNodeId => _numaNodeId;

    /// <summary>Gets the block size in bytes used by this pool.</summary>
    public int BlockSize => _blockSize;

    /// <summary>
    /// Creates a new <see cref="NumaMemoryPool"/> for the specified NUMA node.
    /// </summary>
    /// <param name="numaNodeId">The NUMA node affinity for allocations.</param>
    /// <param name="blockSize">
    /// Size in bytes of each pooled block. Defaults to 4096 (one OS page).
    /// Must be a power of two and at least 64 bytes.
    /// </param>
    public NumaMemoryPool(int numaNodeId = 0, int blockSize = DefaultBlockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numaNodeId);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, CacheLineSize);

        if (!IsPowerOfTwo(blockSize))
        {
            throw new ArgumentException("blockSize must be a power of two.", nameof(blockSize));
        }

        _blockSize = blockSize;
        _numaNodeId = numaNodeId;
        _threadLocalPool = new ThreadLocal<Stack<NumaMemoryOwner>>(
            () => new Stack<NumaMemoryOwner>(MaxPooledBlocksPerThread), trackAllValues: true);
    }

    /// <inheritdoc />
    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (minBufferSize == -1 || minBufferSize <= _blockSize)
        {
            Stack<NumaMemoryOwner> pool = _threadLocalPool.Value!;
            if (pool.TryPop(out NumaMemoryOwner? owner))
            {
                owner.ResetForReuse();
                return owner;
            }

            return NumaMemoryOwner.Allocate(_blockSize, this);
        }

        // Requested size exceeds the pool block size — allocate directly.
        int aligned = AlignUp(minBufferSize, CacheLineSize);
        return NumaMemoryOwner.Allocate(aligned, this);
    }

    /// <summary>Returns a block to the thread-local pool, or frees it if the pool is full.</summary>
    internal void Return(NumaMemoryOwner owner)
    {
        if (_disposed)
        {
            owner.FreeNative();
            return;
        }

        Stack<NumaMemoryOwner> pool = _threadLocalPool.Value!;
        if (pool.Count < MaxPooledBlocksPerThread && owner.Length == _blockSize)
        {
            pool.Push(owner);
        }
        else
        {
            owner.FreeNative();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing && _threadLocalPool.Values is { } allStacks)
        {
            foreach (Stack<NumaMemoryOwner> stack in allStacks)
            {
                while (stack.TryPop(out NumaMemoryOwner? owner))
                {
                    owner.FreeNative();
                }
            }
        }

        _threadLocalPool.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);
}
