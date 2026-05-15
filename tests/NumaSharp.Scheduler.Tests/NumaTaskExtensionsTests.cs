using NumaSharp.Scheduling;

namespace NumaSharp.Scheduler.Tests;

public sealed class NumaTaskExtensionsTests
{
    [Test]
    public async Task RunOnNumaNode_Action_Executes()
    {
        bool ran = false;
        Action action = () => { ran = true; };
        await action.RunOnNumaNode(0);
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task RunOnNumaNode_Func_ReturnsResult()
    {
        Func<int> func = () => 99;
        int result = await func.RunOnNumaNode(0);
        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task RunNumaLocal_Action_Executes()
    {
        bool ran = false;
        Action action = () => { ran = true; };
        await action.RunNumaLocal();
        await Assert.That(ran).IsTrue();
    }
}
