using System;

namespace LearnBackend.Shared.Contracts
{
    /// <summary>
    /// Common Response Wrapper thống nhất cho toàn bộ Game API (tương thích Unity .NET Standard 2.1).
    /// Mọi response đều mang ServerTimeUtc giúp Unity đồng bộ đồng hồ chống chỉnh giờ điện thoại.
    /// </summary>
    public sealed class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? Message { get; set; }
        public DateTime ServerTimeUtc { get; set; }
        public T? Data { get; set; }

        public ApiResponse() { }

        public ApiResponse(bool success, string? errorCode, string? message, DateTime serverTimeUtc, T? data)
        {
            Success = success;
            ErrorCode = errorCode;
            Message = message;
            ServerTimeUtc = serverTimeUtc;
            Data = data;
        }

        public static ApiResponse<T> Ok(T data, string? message = null)
            => new ApiResponse<T>(true, null, message, DateTime.UtcNow, data);

        public static ApiResponse<T> Fail(string errorCode, string message)
            => new ApiResponse<T>(false, errorCode, message, DateTime.UtcNow, default);
    }

    public static class ApiResponse
    {
        public static ApiResponse<T> Ok<T>(T data, string? message = null)
            => ApiResponse<T>.Ok(data, message);

        public static ApiResponse<object?> OkMessage(string message)
            => new ApiResponse<object?>(true, null, message, DateTime.UtcNow, null);

        public static ApiResponse<object?> Fail(string errorCode, string message)
            => new ApiResponse<object?>(false, errorCode, message, DateTime.UtcNow, null);
    }
}
