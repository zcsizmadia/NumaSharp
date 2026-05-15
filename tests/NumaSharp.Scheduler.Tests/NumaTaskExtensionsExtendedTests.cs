using NumaSharp.Scheduling;

namespace NumaSharp.Scheduler.Tests;

/// <summary>Additional coverage for <see cref="NumaTaskExtensions"/>.</summary>
public sealed class NumaTaskExtensionsExtendedTests
{
    [Test]
    public async Task RunOnNumaNode_Action_ExecutesOnCorrectNode()
    {
        bool ran = false;
        await ((Action)(() => { ran = true; })).RunOnNumaNode(0);
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task RunOnNumaNode_Func_ReturnsResult()
    {
        int result = await ((Func<int>)(() => 77)).RunOnNumaNode(0);
        await Assert.That(result).IsEqualTo(77);
    }

    [Test]
    public async Task RunOnNumaNode_Func_PropagatesException()
    {
        Func<int> func = () => throw new InvalidOperationException("ext-test");
        await Assert.That(() => func.RunOnNumaNode(0)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RunNumaLocal_Action_Executes()
    {
        bool ran = false;
        await ((Action)(() => { ran = true; })).RunNumaLocal();
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task RunOnNumaNode_WithCancellation_AlreadyCancelled_Throws()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.That(async () =>
        {
            await ((Action)(() => { })).RunOnNumaNode(0, cts.Token);
        }).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunNumaLocal_Action_PropagatesException()
    {
        Action action = () => throw new InvalidOperationException("local-test");
        await Assert.That(() => action.RunNumaLocal()).Throws<InvalidOperationException>();
    }
}
