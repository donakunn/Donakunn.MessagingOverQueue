using System.Text.Json.Serialization;

namespace Donakunn.MessagingOverQueue.RedisStreams.Serialization;

/// <summary>
/// Internal JSON source-generated context for header serialization.
/// Avoids runtime reflection for Dictionary&lt;string, string&gt; (de)serialization.
/// </summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class InternalJsonContext : JsonSerializerContext;
