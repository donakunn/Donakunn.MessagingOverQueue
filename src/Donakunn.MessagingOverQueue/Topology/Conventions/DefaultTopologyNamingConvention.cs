using Donakunn.MessagingOverQueue.Topology.Abstractions;
using Donakunn.MessagingOverQueue.Topology.Attributes;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Donakunn.MessagingOverQueue.Topology.Conventions;

/// <summary>
/// Default Redis-Streams-native naming convention.
/// Stream key = {category}.{name}[.{version}]
/// Consumer group = {service}.{name}
/// </summary>
public sealed partial class DefaultTopologyNamingConvention(TopologyNamingOptions options)
    : ITopologyNamingConvention
{
    private readonly TopologyNamingOptions _options = options
        ?? throw new ArgumentNullException(nameof(options));

    public DefaultTopologyNamingConvention() : this(new TopologyNamingOptions()) { }

    /// <inheritdoc />
    public string GetStreamKey(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        var attr     = messageType.GetCustomAttribute<EventTopologyAttribute>();
        var name     = FormatName(attr?.Name ?? GetBaseName(messageType));
        var category = FormatName(attr?.Category ?? ExtractCategory(messageType));
        var version  = attr?.Version;

        return version is null
            ? $"{category}.{name}"
            : $"{category}.{name}.{version}";
    }

    /// <inheritdoc />
    public ConsumerTopologyNames GetConsumerNames(Type handlerType, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(messageType);

        var eventAttr    = messageType.GetCustomAttribute<EventTopologyAttribute>();
        var consumerAttr = handlerType.GetCustomAttribute<ConsumerTopologyAttribute>();

        // Stream key: category and name always from event; version from consumer first
        var eventName     = FormatName(eventAttr?.Name ?? GetBaseName(messageType));
        var eventCategory = FormatName(eventAttr?.Category ?? ExtractCategory(messageType));
        var version       = eventAttr?.Version;

        var streamKey = version is null
            ? $"{eventCategory}.{eventName}"
            : $"{eventCategory}.{eventName}.{version}";

        // Consumer group: service from consumer/options/namespace; name from consumer/event
        var groupName   = FormatName(consumerAttr?.Name ?? eventAttr?.Name ?? GetBaseName(messageType));
        var serviceName = FormatName(
            consumerAttr?.Category
            ?? _options.ServiceName
            ?? GetServiceNameFromHandler(handlerType));

        return new ConsumerTopologyNames(streamKey, $"{serviceName}.{groupName}");
    }

    /// <inheritdoc />
    public string GetDeadLetterStreamKey(string streamKey, string consumerGroupName)
        => $"{streamKey}:{consumerGroupName}:dlq";

    // ── Private helpers ─────────────────────────────────────────────────

    private string GetBaseName(Type messageType)
    {
        var name = messageType.Name;
        foreach (var suffix in _options.SuffixesToRemove)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }
        return name;
    }

    private static string GetServiceNameFromHandler(Type handlerType)
    {
        var ns = handlerType.Namespace;
        if (string.IsNullOrEmpty(ns))
            return "Default";

        foreach (var part in ns.Split('.'))
        {
            if (!part.Equals("Handlers", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("Handler",  StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("Services", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("src",      StringComparison.OrdinalIgnoreCase))
            {
                return part; // original casing — FormatName kebab-cases it
            }
        }

        return "Default";
    }

    private static string ExtractCategory(Type messageType)
    {
        var ns = messageType.Namespace;
        if (string.IsNullOrEmpty(ns))
            return "general";

        foreach (var part in ns.Split('.').Reverse())
        {
            if (!part.Equals("Messages", StringComparison.OrdinalIgnoreCase))
            {
                return part.ToLowerInvariant();
            }
        }

        return "general";
    }

    private static string FormatName(string name) => ToKebabCase(name);

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = KebabCaseRegex().Replace(value, "-$1");
        return result.TrimStart('-').ToLowerInvariant();
    }

    [GeneratedRegex(@"([A-Z])")]
    private static partial Regex KebabCaseRegex();
}

/// <summary>
/// Configuration options for topology naming.
/// </summary>
public sealed class TopologyNamingOptions
{
    /// <summary>
    /// Global service name used as consumer group prefix.
    /// When set, overrides namespace-based service name extraction.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Type name suffixes stripped before name formatting.
    /// </summary>
    public string[] SuffixesToRemove { get; set; } = ["Message"];
}
