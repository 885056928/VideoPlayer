using VideoPlayer.Enums;
using VideoPlayer.Models;
using Xunit;

namespace VideoPlayer.Tests.Unit.Models;

public sealed class ServiceResultTests
{
    [Fact]
    public void Failure_CarriesErrorCodeAndMessages()
    {
        var result = ServiceResult.Failure(
            ErrorCode.FileNotFound,
            "文件不存在。",
            "Missing file");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.FileNotFound, result.ErrorCode);
        Assert.Equal("文件不存在。", result.UserMessage);
    }

    [Fact]
    public void GenericSuccess_CarriesValue()
    {
        var result = ServiceResult<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(ErrorCode.None, result.ErrorCode);
    }
}
