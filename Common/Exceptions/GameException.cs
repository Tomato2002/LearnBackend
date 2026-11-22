using System.Net;

namespace LearnBackend.Common.Exceptions;

/// <summary>
/// Exception chuẩn cho các lỗi nghiệp vụ game (không đủ tiền, không đủ thể lực, trùng tên...).
/// Client Unity sẽ dựa vào ErrorCode (VD: "INSUFFICIENT_STAMINA") để hiển thị popup/localization phù hợp.
/// </summary>
public class GameException : Exception
{
    public string ErrorCode { get; }
    public HttpStatusCode StatusCode { get; }

    public GameException(
        string errorCode,
        string message,
        HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}
