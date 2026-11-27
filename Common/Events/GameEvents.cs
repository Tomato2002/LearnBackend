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
/// - Auth chỉ bắn PlayerRegisteredEvent -> từng game tự cắm Handler tặng quà tân thủ (RPG tặng Hero, Card tặng Bộ bài...).
/// - Battle/Gacha/Shop chỉ bắn sự kiện -> Quest/Achievement/BattlePass tự lắng nghe xử lý.
/// </summary>
public interface IGameEventBus
{
    Task PublishAsync<TEvent>(TEvent gameEvent, CancellationToken ct = default) where TEvent : IGameEvent;
}

public sealed record PlayerRegisteredEvent(Guid PlayerId, DateTime OccurredAtUtc) : IGameEvent;
public sealed record StageClearedEvent(Guid PlayerId, string StageId, DateTime OccurredAtUtc) : IGameEvent;
public sealed record GachaSummonedEvent(Guid PlayerId, string BannerId, int PullCount, DateTime OccurredAtUtc) : IGameEvent;
public sealed record ShopPurchasedEvent(Guid PlayerId, string ShopItemId, int Quantity, DateTime OccurredAtUtc) : IGameEvent;
