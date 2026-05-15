namespace NumaSharp.Scheduling;

/// <summary>
/// Extension methods that provide ergonomic access to NUMA-aware scheduling
/// without needing to hold a direct reference to a scheduler instance.
/// </summary>
public static class NumaTaskExtensions
{
    /// <summary>
    /// Runs <paramref name="action"/> on the specified NUMA node using the
    /// <see cref="NumaTaskScheduler.Shared"/> scheduler.
    /// </summary>
    public static Task RunOnNumaNode(
        this Action       action,
        int               nodeIndex,
        CancellationToken cancellationToken = default) =>
        NumaTaskScheduler.Shared.RunOnNode(nodeIndex, action, cancellationToken);

    /// <summary>
    /// Runs <paramref name="func"/> on the specified NUMA node using the
    /// <see cref="NumaTaskScheduler.Shared"/> scheduler.
    /// </summary>
    public static Task<TResult> RunOnNumaNode<TResult>(
        this Func<TResult> func,
        int                nodeIndex,
        CancellationToken  cancellationToken = default) =>
        NumaTaskScheduler.Shared.RunOnNode(nodeIndex, func, cancellationToken);

    /// <summary>
    /// Schedules <paramref name="action"/> using the
    /// <see cref="NumaTaskScheduler.Shared"/> scheduler with the
    /// <see cref="NumaSchedulingPolicy.LocalityFirst"/> policy automatically applied.
    /// </summary>
    public static Task RunNumaLocal(
        this Action       action,
        CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(action, cancellationToken,
            TaskCreationOptions.PreferFairness, NumaTaskScheduler.Shared);
}
