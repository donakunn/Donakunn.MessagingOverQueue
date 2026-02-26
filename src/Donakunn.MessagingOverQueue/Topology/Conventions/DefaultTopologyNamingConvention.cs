using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Topology.Abstractions;
using Donakunn.MessagingOverQueue.Topology.Attributes;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Donakunn.MessagingOverQueue.Topology.Conventions;

/// <summary>
/// Default naming convention for messaging topology.
/// Uses message type names with consistent formatting.
/// </summary>
/// <remarks>
/// Creates a new instance with the specified options.
/// </remarks>
/// <param name="options">The naming options.</param>
public sealed partial class DefaultTopologyNamingConvention(TopologyNamingOptions options) : ITopologyNamingConvention
{
    private readonly TopologyNamingOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Creates a new instance with default options.
    /// </summary>
    public DefaultTopologyNamingConvention()
        : this(new TopologyNamingOptions())
    {
    }

    /// <inheritdoc />
    public string GetExchangeName(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        var name = FormatName(ResolveEventName(messageType));

        if (typeof(IEvent).IsAssignableFrom(messageType))
            return $"events.{name}";

        if (typeof(ICommand).IsAssignableFrom(messageType))
            return $"commands.{name}";

        return name;
    }

    /// <inheritdoc />
    public string GetQueueName(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        var name = FormatName(ResolveEventName(messageType));

        if (typeof(IEvent).IsAssignableFrom(messageType))
        {
            var serviceName = FormatName(_options.ServiceName ?? "default");
            return $"{serviceName}.{name}";
        }

        if (typeof(ICommand).IsAssignableFrom(messageType))
            return name;

        return name;
    }

    /// <summary>
    /// Gets the consumer queue name for a handler type.
    /// Uses handler-specific naming when service name is provided.
    /// </summary>
    /// <param name="handlerType">The handler type.</param>
    /// <param name="messageType">The message type being handled.</param>
    /// <returns>The consumer queue name.</returns>
    public string GetConsumerQueueName(Type handlerType, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(messageType);

        var eventAttr    = messageType.GetCustomAttribute<EventTopologyAttribute>();
        var consumerAttr = handlerType.GetCustomAttribute<ConsumerTopologyAttribute>();

        // Message name segment: consumer.Name ?? event.Name ?? convention
        var baseName = consumerAttr?.Name
            ?? eventAttr?.Name
            ?? GetBaseName(messageType);

        // Version: consumer.Version ?? event.Version (consumer wins)
        var version = consumerAttr?.Version ?? eventAttr?.Version;

        var nameWithVersion = version is null ? baseName : $"{baseName}.{version}";

        // Service prefix: consumer.Category ?? global ServiceName ?? namespace extraction
        var serviceName = consumerAttr?.Category
            ?? _options.ServiceName
            ?? GetServiceNameFromHandler(handlerType);

        return $"{FormatName(serviceName)}.{FormatName(nameWithVersion)}";
    }

    /// <inheritdoc />
    public string GetRoutingKey(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        var attr = messageType.GetCustomAttribute<EventTopologyAttribute>();

        if (typeof(IEvent).IsAssignableFrom(messageType))
        {
            var baseName = attr?.Name ?? GetBaseName(messageType);
            var version = attr?.Version;
            var nameSegment = ConvertToRoutingKeySegment(version is null ? baseName : $"{baseName}.{version}");
            var category = attr?.Category ?? ExtractCategory(messageType);
            return $"{category}.{nameSegment}";
        }

        var cmdBaseName = attr?.Name ?? GetBaseName(messageType);
        return ConvertToRoutingKeySegment(cmdBaseName);
    }

    /// <inheritdoc />
    public string GetExchangeType(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        if (typeof(ICommand).IsAssignableFrom(messageType))
            return "direct";

        return "topic";
    }

    /// <inheritdoc />
    public string GetDeadLetterExchangeName(string sourceQueueName)
        => $"dlx.{sourceQueueName}";

    /// <inheritdoc />
    public string GetDeadLetterQueueName(string sourceQueueName)
        => $"{sourceQueueName}.dlq";

    private string ResolveEventName(Type messageType)
    {
        var attr = messageType.GetCustomAttribute<EventTopologyAttribute>();
        var baseName = attr?.Name ?? GetBaseName(messageType);
        return attr?.Version is null ? baseName : $"{baseName}.{attr.Version}";
    }

    private string GetBaseName(Type messageType)
    {
        var name = messageType.Name;

        // Remove common suffixes
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

        var parts = ns.Split('.');

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (!part.Equals("Handlers", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("Handler", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("Services", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                return part; // original casing — FormatName will kebab-case it
            }
        }

        return "Default";
    }

    private static string FormatName(string name)
    {
        return ToKebabCase(name);
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Insert hyphens before uppercase letters and convert
        var result = KebabCaseRegex().Replace(value, "-$1");

        // Remove leading hyphen if any
        return result.TrimStart('-').ToLowerInvariant();
    }

    private static string ConvertToRoutingKeySegment(string value)
    {
        // Convert to dot notation for routing keys
        var result = KebabCaseRegex().Replace(value, ".$1");
        return result.TrimStart('.').ToLowerInvariant();
    }

    private static string ExtractCategory(Type messageType)
    {
        // Try to extract category from namespace
        var ns = messageType.Namespace;
        if (string.IsNullOrEmpty(ns))
            return "general";

        var parts = ns.Split('.');

        // Look for common domain patterns like "Events", "Commands", "Messages"
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var part = parts[i];
            if (!part.Equals("Events", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("Commands", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("Messages", StringComparison.OrdinalIgnoreCase))
            {
                return part.ToLowerInvariant();
            }
        }

        return "general";
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
    /// Global service name used as queue prefix for events.
    /// When set, overrides namespace-based service name extraction.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Type name suffixes stripped before name formatting.
    /// Defaults to Command, Event, Message, Query.
    /// </summary>
    public string[] SuffixesToRemove { get; set; } = ["Command", "Event", "Message", "Query"];
}
