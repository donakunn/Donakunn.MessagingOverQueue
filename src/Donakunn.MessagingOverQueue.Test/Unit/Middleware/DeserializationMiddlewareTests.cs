using System.Collections.Concurrent;
using System.Reflection;
using Donakunn.MessagingOverQueue.Abstractions.Messages;
using Donakunn.MessagingOverQueue.Abstractions.Serialization;
using Donakunn.MessagingOverQueue.Consuming.Middleware;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Donakunn.MessagingOverQueue.Test.Unit.Middleware;

public class DeserializationMiddlewareTests
{
    [Fact]
    public async Task TypeCache_DoesNotGrowBeyondLimit()
    {
        // Arrange
        var serializer = new Mock<IMessageSerializer>();
        var typeResolver = new Mock<IMessageTypeResolver>();
        typeResolver.Setup(r => r.ResolveType(It.IsAny<string>())).Returns((Type?)null);

        var middleware = new DeserializationMiddleware(
            serializer.Object, typeResolver.Object, NullLogger<DeserializationMiddleware>.Instance);

        // Act - send 2000 unique message types
        for (int i = 0; i < 2000; i++)
        {
            var context = new ConsumeContext
            {
                DeliveryTag = (ulong)i,
                Body = [],
                ContentType = "application/json",
                Data = { ["message-type"] = $"UnknownType{i}" }
            };

            await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.CompletedTask, CancellationToken.None);
        }

        // Assert - cache should be bounded
        var cache = GetPrivateField<ConcurrentDictionary<string, Type?>>(middleware, "_resolvedTypeCache");
        Assert.True(cache.Count <= 1024, $"Cache grew to {cache.Count}, expected <= 1024");
    }

    private static T GetPrivateField<T>(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (T)field.GetValue(obj)!;
    }
}
