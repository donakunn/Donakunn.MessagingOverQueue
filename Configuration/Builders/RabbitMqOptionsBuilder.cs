using AsyncronousComunication.Configuration.Options;

namespace AsyncronousComunication.Configuration.Builders;

/// <summary>
/// Fluent builder for configuring RabbitMQ options.
/// </summary>
public class RabbitMqOptionsBuilder
{
    private readonly RabbitMqOptions _options = new();
    private readonly List<Action<RabbitMqOptions>> _configurations = new();

    /// <summary>
    /// Sets the RabbitMQ host name.
    /// </summary>
    public RabbitMqOptionsBuilder UseHost(string hostName)
    {
        _configurations.Add(o => o.HostName = hostName);
        return this;
    }

    /// <summary>
    /// Sets the RabbitMQ port.
    /// </summary>
    public RabbitMqOptionsBuilder UsePort(int port)
    {
        _configurations.Add(o => o.Port = port);
        return this;
    }

    /// <summary>
    /// Sets the credentials.
    /// </summary>
    public RabbitMqOptionsBuilder WithCredentials(string userName, string password)
    {
        _configurations.Add(o =>
        {
            o.UserName = userName;
            o.Password = password;
        });
        return this;
    }

    /// <summary>
    /// Sets the virtual host.
    /// </summary>
    public RabbitMqOptionsBuilder UseVirtualHost(string virtualHost)
    {
        _configurations.Add(o => o.VirtualHost = virtualHost);
        return this;
    }

    /// <summary>
    /// Enables SSL.
    /// </summary>
    public RabbitMqOptionsBuilder UseSsl(string? serverName = null)
    {
        _configurations.Add(o =>
        {
            o.UseSsl = true;
            o.SslServerName = serverName;
        });
        return this;
    }

    /// <summary>
    /// Sets the client-provided connection name.
    /// </summary>
    public RabbitMqOptionsBuilder WithConnectionName(string name)
    {
        _configurations.Add(o => o.ClientProvidedName = name);
        return this;
    }

    /// <summary>
    /// Sets the connection timeout.
    /// </summary>
    public RabbitMqOptionsBuilder WithConnectionTimeout(TimeSpan timeout)
    {
        _configurations.Add(o => o.ConnectionTimeout = timeout);
        return this;
    }

    /// <summary>
    /// Sets the heartbeat interval.
    /// </summary>
    public RabbitMqOptionsBuilder WithHeartbeat(TimeSpan interval)
    {
        _configurations.Add(o => o.RequestedHeartbeat = interval);
        return this;
    }

    /// <summary>
    /// Sets the channel pool size.
    /// </summary>
    public RabbitMqOptionsBuilder WithChannelPoolSize(int size)
    {
        _configurations.Add(o => o.ChannelPoolSize = size);
        return this;
    }

    /// <summary>
    /// Configures automatic recovery.
    /// </summary>
    public RabbitMqOptionsBuilder WithAutomaticRecovery(bool enabled = true, TimeSpan? interval = null)
    {
        _configurations.Add(o =>
        {
            o.AutomaticRecoveryEnabled = enabled;
            if (interval.HasValue)
                o.NetworkRecoveryInterval = interval.Value;
        });
        return this;
    }

    /// <summary>
    /// Adds an exchange configuration.
    /// </summary>
    public RabbitMqOptionsBuilder AddExchange(Action<ExchangeBuilder> configure)
    {
        var builder = new ExchangeBuilder();
        configure(builder);
        _configurations.Add(o => o.Exchanges.Add(builder.Build()));
        return this;
    }

    /// <summary>
    /// Adds a queue configuration.
    /// </summary>
    public RabbitMqOptionsBuilder AddQueue(Action<QueueBuilder> configure)
    {
        var builder = new QueueBuilder();
        configure(builder);
        _configurations.Add(o => o.Queues.Add(builder.Build()));
        return this;
    }

    /// <summary>
    /// Adds a binding configuration.
    /// </summary>
    public RabbitMqOptionsBuilder AddBinding(Action<BindingBuilder> configure)
    {
        var builder = new BindingBuilder();
        configure(builder);
        _configurations.Add(o => o.Bindings.Add(builder.Build()));
        return this;
    }

    /// <summary>
    /// Builds the options.
    /// </summary>
    public RabbitMqOptions Build()
    {
        foreach (var configuration in _configurations)
        {
            configuration(_options);
        }
        return _options;
    }
}

