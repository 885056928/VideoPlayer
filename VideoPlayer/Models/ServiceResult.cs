using VideoPlayer.Enums;

namespace VideoPlayer.Models;

public sealed record ServiceResult(
    bool IsSuccess,
    ErrorCode ErrorCode,
    string UserMessage,
    string TechnicalMessage,
    Exception? Exception = null)
{
    public static ServiceResult Success()
    {
        return new ServiceResult(true, ErrorCode.None, string.Empty, string.Empty);
    }

    public static ServiceResult Failure(
        ErrorCode errorCode,
        string userMessage,
        string technicalMessage,
        Exception? exception = null)
    {
        return new ServiceResult(false, errorCode, userMessage, technicalMessage, exception);
    }
}

public sealed record ServiceResult<T>(
    bool IsSuccess,
    T? Value,
    ErrorCode ErrorCode,
    string UserMessage,
    string TechnicalMessage,
    Exception? Exception = null)
{
    public static ServiceResult<T> Success(T value)
    {
        return new ServiceResult<T>(true, value, ErrorCode.None, string.Empty, string.Empty);
    }

    public static ServiceResult<T> Failure(
        ErrorCode errorCode,
        string userMessage,
        string technicalMessage,
        Exception? exception = null)
    {
        return new ServiceResult<T>(false, default, errorCode, userMessage, technicalMessage, exception);
    }
}
