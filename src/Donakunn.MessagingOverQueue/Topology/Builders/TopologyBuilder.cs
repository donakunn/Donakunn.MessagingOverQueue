using Donakunn.MessagingOverQueue.Topology.Abstractions;
using Donakunn.MessagingOverQueue.Topology.Conventions;
using System.Reflection;

namespace Donakunn.MessagingOverQueue.Topology.Builders;

/// <summary>
/// Fluent builder for configuring topology with handler-based auto-discovery.
/// </summary>
public sealed class TopologyBuilder
{
    private readonly List<Assembly> _assembliesToScan = [];
    private TopologyNamingOptions _namingOptions = new();

    private bool _autoDiscoverEnabled = true;

    /// <summary>
    /// Sets the service name used in queue naming.
    /// </summary>
    public TopologyBuilder WithServiceName(string serviceName)
    {
        _namingOptions.ServiceName = serviceName;
        return this;
    }

    /// <summary>
    /// Adds an assembly to scan for message handlers.
    /// </summary>
    public TopologyBuilder ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!_assembliesToScan.Contains(assembly))
            _assembliesToScan.Add(assembly);
        return this;
    }

    /// <summary>
    /// Adds assemblies to scan for message handlers.
    /// </summary>
    public TopologyBuilder ScanAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
            ScanAssembly(assembly);
        return this;
    }

    /// <summary>
    /// Scans the assembly containing the specified type for message handlers.
    /// </summary>
    public TopologyBuilder ScanAssemblyContaining<T>()
        => ScanAssembly(typeof(T).Assembly);

    /// <summary>
    /// Disables auto-discovery of message handlers.
    /// </summary>
    public TopologyBuilder DisableAutoDiscovery()
    {
        _autoDiscoverEnabled = false;
        return this;
    }

    public TopologyNamingOptions NamingOptions => _namingOptions;
    public IReadOnlyList<Assembly> AssembliesToScan => _assembliesToScan;
    public bool AutoDiscoverEnabled => _autoDiscoverEnabled;
}

/// <summary>
/// Represents a handler registration with its topology configuration.
/// </summary>
public sealed class HandlerRegistration
{
    public Type HandlerType { get; init; } = null!;
    public Type MessageType { get; init; } = null!;
    public string QueueName { get; init; } = string.Empty;
    public ConsumerQueueInfo? ConsumerConfig { get; init; }
    public TopologyDefinition? TopologyDefinition { get; init; }
}
