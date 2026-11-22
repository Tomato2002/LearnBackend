namespace LearnBackend.Common.DTOs;

/// <summary>
/// Common Response Wrapper thống nhất cho toàn bộ Game API (thành công lẫn thất bại).
/// Unity Client chỉ cần 1 lớp ApiClient để deserialize ApiResponse&lt;T&gt; và tự động đồng bộ ServerTimeUtc.
/// </summary>
public sealed record ApiResponse<T>(
    bool Success,
    string? ErrorCode,
    string? Message,
    DateTime ServerTimeUtc,
    T? Data)
{
    public static ApiResponse<T> Ok(T data, string? message = null)
        => new(true, null, message, DateTime.UtcNow, data);

    public static ApiResponse<T> Fail(string errorCode, string message)
        => new(false, errorCode, message, DateTime.UtcNow, default);
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null)
        => ApiResponse<T>.Ok(data, message);

    public static ApiResponse<object?> OkMessage(string message)
        => new(true, null, message, DateTime.UtcNow, null);

    public static ApiResponse<object?> Fail(string errorCode, string message)
        => new(false, errorCode, message, DateTime.UtcNow, null);
}
