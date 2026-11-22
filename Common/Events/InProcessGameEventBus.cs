using Microsoft.Extensions.DependencyInjection;

namespace LearnBackend.Common.Events;

public sealed class InProcessGameEventBus(IServiceProvider serviceProvider) : IGameEventBus
{
    public async Task PublishAsync<TEvent>(TEvent gameEvent, CancellationToken ct = default)
        where TEvent : IGameEvent
    {
        var handlers = serviceProvider.GetServices<IGameEventHandler<TEvent>>();
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(gameEvent, ct);
        }
    }
}
