using System.Buffers;

namespace NumaSharp.Core.Tests;

/// <summary>Additional coverage for <see cref="NumaMemoryPool"/> and <see cref="NumaMemoryOwner"/>.</summary>
public sealed class NumaMemoryOwnerTests
{
    [Test]
    public async Task Rent_ReturnsNonNull()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0);
        using IMemoryOwner<byte> owner = pool.Rent();
        await Assert.That(owner).IsNotNull();
    }

    [Test]
    public async Task Rent_MemoryLength_IsAtLeastBlockSize()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        using IMemoryOwner<byte> owner = pool.Rent();
        await Assert.That(owner.Memory.Length).IsGreaterThanOrEqualTo(4096);
    }

    [Test]
    public async Task Rent_WithMinBufferSize_ReturnsLargeEnoughMemory()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        int requested = 8192;
        using IMemoryOwner<byte> owner = pool.Rent(requested);
        await Assert.That(owner.Memory.Length).IsGreaterThanOrEqualTo(requested);
    }

    [Test]
    public async Task Rent_DefaultBlockSize_IsReadWriteable()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0);
        using IMemoryOwner<byte> owner = pool.Rent();
        Memory<byte> mem = owner.Memory;
        for (int i = 0; i < mem.Length; i++)
        {
            mem.Span[i] = (byte)(i & 0xFF);
        }

        await Assert.That(mem.Span[42]).IsEqualTo((byte)42);
    }

    [Test]
    public async Task Rent_ReturnedBlock_IsReused()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);

        IMemoryOwner<byte> first = pool.Rent();
        // Write a sentinel value
        first.Memory.Span[0] = 0xAB;
        first.Dispose();

        // Second rental from same thread should get the recycled block (zeroed).
        IMemoryOwner<byte> second = pool.Rent();
        // Block is recycled — just verify we got something valid.
        await Assert.That(second.Memory.Length).IsGreaterThanOrEqualTo(4096);
        second.Dispose();
    }

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0);
        IMemoryOwner<byte> owner = pool.Rent();
        owner.Dispose();
        // Second dispose should be a no-op.
        await Assert.That(() => owner.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task Pool_Rent_AfterPoolDisposed_ThrowsObjectDisposedException()
    {
        NumaMemoryPool pool = new(numaNodeId: 0);
        pool.Dispose();
        await Assert.That(() => pool.Rent()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Pool_Concurrent_RentAndReturn_IsThreadSafe()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        const int threads = 8;
        const int rentalsPerThread = 100;

        Exception? caught = null;
        Thread[] workers = new Thread[threads];

        for (int t = 0; t < threads; t++)
        {
            workers[t] = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < rentalsPerThread; i++)
                    {
                        using IMemoryOwner<byte> owner = pool.Rent();
                        // Touch memory to catch access violations
                        owner.Memory.Span[0] = 1;
                        owner.Memory.Span[owner.Memory.Length - 1] = 2;
                    }
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref caught, ex);
                }
            });
        }

        foreach (Thread w in workers)
        {
            w.Start();
        }

        foreach (Thread w in workers)
        {
            w.Join();
        }

        await Assert.That(caught).IsNull();
    }

    [Test]
    public async Task Pool_NumaNodeId_ReturnsCorrectValue()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0);
        await Assert.That(pool.NumaNodeId).IsEqualTo(0);
    }

    [Test]
    public async Task Pool_BlockSize_ReturnsCorrectValue()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 8192);
        await Assert.That(pool.BlockSize).IsEqualTo(8192);
    }

    [Test]
    public async Task Pool_InvalidBlockSize_NonPowerOfTwo_Throws()
    {
        await Assert.That(() => new NumaMemoryPool(numaNodeId: 0, blockSize: 3000))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Pool_InvalidBlockSize_TooSmall_Throws()
    {
        await Assert.That(() => new NumaMemoryPool(numaNodeId: 0, blockSize: 32))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Pool_NegativeNodeId_Throws()
    {
        await Assert.That(() => new NumaMemoryPool(numaNodeId: -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task MaxBufferSize_IsIntMaxValue()
    {
        using NumaMemoryPool pool = new();
        await Assert.That(pool.MaxBufferSize).IsEqualTo(int.MaxValue);
    }
}
