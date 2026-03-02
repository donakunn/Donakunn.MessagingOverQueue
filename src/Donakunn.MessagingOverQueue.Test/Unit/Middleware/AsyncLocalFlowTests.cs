using Donakunn.MessagingOverQueue.Consuming.Middleware;

namespace Donakunn.MessagingOverQueue.Test.Unit.Middleware;

public class AsyncLocalFlowTests
{
    private static readonly AsyncLocal<string> TestAsyncLocal = new();

    [Fact]
    public async Task ConsumePipeline_PreservesAsyncLocalContext()
    {
        // Arrange
        var capturedValue = "";
        var middleware = new PassthroughMiddleware();

        var pipeline = ConsumePipeline.Build(
            [middleware],
            (ctx, ct) =>
            {
                capturedValue = TestAsyncLocal.Value!;
                return Task.CompletedTask;
            });

        TestAsyncLocal.Value = "test-correlation-id";

        // Act
        var context = new ConsumeContext
        {
            DeliveryTag = 1,
            Body = [],
            ContentType = "application/json"
        };
        await pipeline(context, CancellationToken.None);

        // Assert
        Assert.Equal("test-correlation-id", capturedValue);
    }

    private class PassthroughMiddleware : IConsumeMiddleware
    {
        public ValueTask InvokeAsync(
            ConsumeContext context,
            Func<ConsumeContext, CancellationToken, ValueTask> next,
            CancellationToken cancellationToken)
        {
            return next(context, cancellationToken);
        }
    }
}
