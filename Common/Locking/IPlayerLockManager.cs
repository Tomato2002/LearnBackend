namespace LearnBackend.Common.Locking;

/// <summary>
/// Khóa thao tác theo từng PlayerId để chống race condition / dupe đồ
/// khi client gửi nhiều request thay đổi dữ liệu cùng lúc.
/// Tách thành Interface để sau này scale nhiều server chỉ cần thay bằng Redis Distributed Lock.
/// </summary>
public interface IPlayerLockManager
{
    Task<IDisposable> AcquireAsync(Guid playerId, CancellationToken cancellationToken = default);
}
