namespace LearnBackend.Common.Events;

/// <summary>
/// Marker interface cho mọi sự kiện nghiệp vụ nội bộ (Domain Events).
/// </summary>
public interface IGameEvent
{
    Guid PlayerId { get; }
    DateTime OccurredAtUtc { get; }
}

public interface IGameEventHandler<in TEvent> where TEvent : IGameEvent
{
    Task HandleAsync(TEvent gameEvent, CancellationToken ct);
}

/// <summary>
/// Event Bus nội bộ giúp các module giao tiếp lỏng lẻo (Decoupled):
/// Battle/Gacha/Shop chỉ việc Publish event, Quest/Achievement/BattlePass tự lắng nghe xử lý.
/// </summary>
public interface IGameEventBus
{
    Task PublishAsync<TEvent>(TEvent gameEvent, CancellationToken ct = default) where TEvent : IGameEvent;
}

public sealed record StageClearedEvent(Guid PlayerId, string StageId, DateTime OccurredAtUtc) : IGameEvent;
public sealed record GachaSummonedEvent(Guid PlayerId, string BannerId, int PullCount, DateTime OccurredAtUtc) : IGameEvent;
public sealed record ShopPurchasedEvent(Guid PlayerId, string ShopItemId, int Quantity, DateTime OccurredAtUtc) : IGameEvent;
