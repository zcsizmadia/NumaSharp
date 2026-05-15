using System.Runtime.CompilerServices;

namespace NumaSharp.Core;

/// <summary>
/// Holds one independent <typeparamref name="T"/> instance per NUMA node, each allocated
/// (and ideally touched) on its owning node so the OS first-touch policy keeps the data
/// in local memory.
/// </summary>
/// <typeparam name="T">The value type stored per node. Must not be <see langword="null"/>.</typeparam>
/// <remarks>
/// <para>
/// <b>Motivation — Seastar's <c>distributed&lt;&gt;</c></b><br/>
/// Seastar (the C++ async framework behind ScyllaDB / Redpanda) solves the same problem
/// with its <c>seastar::distributed&lt;Service&gt;</c> container: one independent service
/// instance lives on each shard, reads are always local, and cross-shard calls are
/// explicit. <see cref="NumaLocal{T}"/> is the .NET equivalent at NUMA-node granularity:
/// one value per node, zero-cost local reads, optional aggregation across nodes.
/// </para>
/// <para>
/// <b>Usage pattern</b>
/// <code>
/// // Counters — each node increments its own slot, no cross-node traffic.
/// using NumaLocal&lt;long[]&gt; counters = new(_ => new long[64]);
/// counters.Value[requestSlot]++;
///
/// // Read-local config snapshot — replicated, never contended.
/// using NumaLocal&lt;ConfigSnapshot&gt; config = new(_ => ConfigSnapshot.Load());
/// ConfigSnapshot local = config.Value;
///
/// // Aggregate across nodes.
/// long total = counters.Aggregate(static (a, b) => a + b[0]);
/// </code>
/// </para>
/// <para>
/// <b>Thread safety</b><br/>
/// <see cref="Value"/> performs a simple indexed array read — it is always safe to call
/// from any thread. Mutations to the returned object are the caller's responsibility.
/// </para>
/// </remarks>
public sealed class NumaLocal<T> : IDisposable where T : notnull
{
    // Cached once at class-load time — avoids a Lazy<T> volatile read on every Value access.
    private static readonly NumaTopology s_topology = NumaTopology.Instance;

    private readonly T[] _values;
    private readonly Action<T>? _dispose;
    private int _disposed;

    /// <summary>
    /// Initialises a new <see cref="NumaLocal{T}"/> by calling <paramref name="factory"/>
    /// once per NUMA node. The factory receives the <see cref="NumaNode"/> so the value can
    /// be pinned or sized per node.
    /// </summary>
    /// <param name="factory">
    /// Called once per node in node-id order. Must not return <see langword="null"/>.
    /// </param>
    /// <param name="dispose">
    /// Optional per-value cleanup called from <see cref="Dispose"/> when
    /// <typeparamref name="T"/> does not implement <see cref="IDisposable"/>.
    /// </param>
    public NumaLocal(Func<NumaNode, T> factory, Action<T>? dispose = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        IReadOnlyList<NumaNode> nodes = s_topology.Nodes;
        _values = new T[nodes.Count];
        _dispose = dispose;

        for (int i = 0; i < nodes.Count; i++)
        {
            T value = factory(nodes[i]);
            ArgumentNullException.ThrowIfNull(value, nameof(factory));
            _values[i] = value;
        }
    }

    /// <summary>
    /// Returns the value for the NUMA node that owns the calling thread's current CPU.
    /// This is an O(1) array index — no locking, no cross-node traffic.
    /// </summary>
    /// <remarks>
    /// The node is determined by querying the OS for the current CPU and mapping it to its
    /// NUMA node via <see cref="NumaTopology"/>. On platforms with a single node the result
    /// is always <c>_values[0]</c>.
    /// </remarks>
    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _values[s_topology.GetCurrentNodeIndex()];
    }

    /// <summary>Returns the value for the specified NUMA node index.</summary>
    /// <param name="nodeId">Zero-based NUMA node index.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="nodeId"/> is negative or &gt;= <see cref="NodeCount"/>.
    /// </exception>
    public T GetForNode(int nodeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(nodeId, _values.Length);
        return _values[nodeId];
    }

    /// <summary>Gets the number of NUMA nodes (and therefore the number of values).</summary>
    public int NodeCount => _values.Length;

    /// <summary>
    /// Aggregates all per-node values by applying <paramref name="selector"/> to each and
    /// folding the results with <paramref name="accumulator"/>.
    /// </summary>
    /// <typeparam name="TResult">The aggregate result type.</typeparam>
    /// <param name="selector">Extracts a scalar from each per-node value.</param>
    /// <param name="accumulator">Combines two scalars into one.</param>
    /// <param name="seed">Starting value for the fold.</param>
    public TResult Aggregate<TResult>(
        Func<T, TResult> selector,
        Func<TResult, TResult, TResult> accumulator,
        TResult seed = default!)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(accumulator);

        TResult result = seed;
        foreach (T value in _values)
        {
            result = accumulator(result, selector(value));
        }

        return result;
    }

    /// <summary>
    /// Aggregates all per-node values using <paramref name="accumulator"/> directly
    /// when <typeparamref name="T"/> is itself the scalar (e.g. <c>long</c>, <c>int</c>).
    /// </summary>
    public T Aggregate(Func<T, T, T> accumulator, T seed = default!)
    {
        ArgumentNullException.ThrowIfNull(accumulator);

        T result = seed;
        foreach (T value in _values)
        {
            result = accumulator(result, value);
        }

        return result;
    }

    /// <summary>
    /// Applies <paramref name="action"/> to every per-node value, in node-id order.
    /// </summary>
    public void ForEach(Action<int, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        for (int i = 0; i < _values.Length; i++)
        {
            action(i, _values[i]);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (T value in _values)
        {
            if (_dispose is not null)
            {
                _dispose(value);
            }
            else if (value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
