using System.Buffers;

namespace NumaSharp.Core.Tests;

public sealed class NumaMemoryPoolTests
{
    [Test]
    public async Task Rent_ReturnsNonNullOwner()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        using IMemoryOwner<byte> owner = pool.Rent();
        await Assert.That(owner).IsNotNull();
    }

    [Test]
    public async Task Rent_MemoryLengthIsAtLeastRequested()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        using IMemoryOwner<byte> owner = pool.Rent(1024);
        await Assert.That(owner.Memory.Length).IsGreaterThanOrEqualTo(1024);
    }

    [Test]
    public async Task Rent_DefaultReturnsBlockSizeMemory()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        using IMemoryOwner<byte> owner = pool.Rent();
        await Assert.That(owner.Memory.Length).IsEqualTo(4096);
    }

    [Test]
    public async Task Rent_LargerThanBlockSize_IsHonoured()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        using IMemoryOwner<byte> owner = pool.Rent(8192);
        await Assert.That(owner.Memory.Length).IsGreaterThanOrEqualTo(8192);
    }

    [Test]
    public async Task Memory_CanBeWrittenAndRead()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        using IMemoryOwner<byte> owner = pool.Rent();
        owner.Memory.Span[0]  = 0xAB;
        owner.Memory.Span[^1] = 0xCD;
        await Assert.That(owner.Memory.Span[0]).IsEqualTo((byte)0xAB);
        await Assert.That(owner.Memory.Span[^1]).IsEqualTo((byte)0xCD);
    }

    [Test]
    public async Task NumaMemoryOwner_GetSpan_ReturnsCorrectLength()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        NumaMemoryOwner owner = (NumaMemoryOwner)pool.Rent();
        int len = owner.GetSpan().Length;
        owner.Dispose();
        await Assert.That(len).IsEqualTo(4096);
    }

    [Test]
    public async Task Dispose_AllowsMultipleCallsWithoutError()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        IMemoryOwner<byte> owner = pool.Rent();
        owner.Dispose();
        await Assert.That(() => owner.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task Pool_RecyclesBlocks()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        IMemoryOwner<byte> first = pool.Rent();
        first.Dispose();
        using IMemoryOwner<byte> second = pool.Rent();
        await Assert.That(second.Memory.Length).IsEqualTo(4096);
    }

    [Test]
    public async Task BlockSize_MustBePowerOfTwo()
    {
        await Assert.That(() => new NumaMemoryPool(blockSize: 3000))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task BlockSize_MustBeAtLeast64Bytes()
    {
        await Assert.That(() => new NumaMemoryPool(blockSize: 32))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Rent_AfterDispose_Throws()
    {
        NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        pool.Dispose();
        await Assert.That(() => pool.Rent()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task MaxBufferSize_IsIntMaxValue()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0);
        await Assert.That(pool.MaxBufferSize).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task NumaNodeId_ReturnsConfiguredNode()
    {
        using NumaMemoryPool pool = new(numaNodeId: 2, blockSize: 4096);
        await Assert.That(pool.NumaNodeId).IsEqualTo(2);
    }

    [Test]
    public async Task BlockSize_ReturnsConfiguredSize()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 8192);
        await Assert.That(pool.BlockSize).IsEqualTo(8192);
    }

    [Test]
    public async Task NegativeNumaNodeId_ThrowsArgumentOutOfRangeException()
    {
        await Assert.That(() => new NumaMemoryPool(numaNodeId: -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task MultipleRents_FromSameThread_AllSucceed()
    {
        using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
        var owners = new List<IMemoryOwner<byte>>();
        for (int i = 0; i < 64; i++)
        {
            owners.Add(pool.Rent());
        }
        await Assert.That(owners.Count).IsEqualTo(64);
        foreach (IMemoryOwner<byte> owner in owners)
        {
            owner.Dispose();
        }
    }
}
