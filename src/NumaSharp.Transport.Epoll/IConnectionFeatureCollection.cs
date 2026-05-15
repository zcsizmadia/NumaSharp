namespace NumaSharp.Transport;

/// <summary>
/// A typed feature bag for attaching extensible data to a connection.
/// Provides O(1) get/set for the common case of a small number of features.
/// </summary>
public interface IConnectionFeatures
{
    /// <summary>Gets a feature by type; returns <c>null</c> when not present.</summary>
    TFeature? GetFeature<TFeature>() where TFeature : class;

    /// <summary>Sets a feature. Pass <c>null</c> to remove it.</summary>
    void SetFeature<TFeature>(TFeature? instance) where TFeature : class;
}

/// <summary>Default implementation backed by a small dictionary.</summary>
public sealed class ConnectionFeatures : IConnectionFeatures
{
    private Dictionary<Type, object>? _features;

    /// <inheritdoc />
    public TFeature? GetFeature<TFeature>() where TFeature : class
    {
        if (_features is null)
        {
            return null;
        }

        _features.TryGetValue(typeof(TFeature), out object? value);
        return value as TFeature;
    }

    /// <inheritdoc />
    public void SetFeature<TFeature>(TFeature? instance) where TFeature : class
    {
        if (instance is null)
        {
            _features?.Remove(typeof(TFeature));
            return;
        }

        _features ??= new Dictionary<Type, object>(4);
        _features[typeof(TFeature)] = instance;
    }
}
