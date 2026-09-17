using System.Text.Json;
using AiOrchestrator.Mcp.Protocol;
using AiOrchestrator.Tests.Testing;

namespace AiOrchestrator.Tests.Cases;

public class JsonRpcCodecTests
{
    [Fact]
    public void EncodesAndDecodesARequest()
    {
        var request = JsonRpcMessage.Request(42, "tools/list", JsonSerializer.SerializeToElement(new { }));
        var line = JsonRpcCodec.EncodeLine(request);
        Assert.False(line.Contains('\n'), "Encoded line must not contain embedded newlines.");

        var decoded = JsonRpcCodec.TryDecodeLine(line);
        Assert.NotNull(decoded);
        Assert.True(decoded!.IsRequest);
        Assert.Equal("tools/list", decoded.Method);
        Assert.Equal(42L, decoded.Id!.Value.GetInt64());
    }

    [Fact]
    public void EncodesAndDecodesANotification()
    {
        var notification = JsonRpcMessage.Notification(McpMethods.InitializedNotification, null);
        var decoded = JsonRpcCodec.TryDecodeLine(JsonRpcCodec.EncodeLine(notification));

        Assert.NotNull(decoded);
        Assert.True(decoded!.IsNotification);
        Assert.False(decoded.IsRequest);
    }

    [Fact]
    public void EncodesAndDecodesASuccessResponse()
    {
        var idElement = JsonSerializer.SerializeToElement(7L);
        var result = JsonSerializer.SerializeToElement(new { ok = true });
        var response = JsonRpcMessage.SuccessResponse(idElement, result);

        var decoded = JsonRpcCodec.TryDecodeLine(JsonRpcCodec.EncodeLine(response));

        Assert.NotNull(decoded);
        Assert.True(decoded!.IsResponse);
        Assert.Equal(7L, decoded.Id!.Value.GetInt64());
        Assert.True(decoded.Result!.Value.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void EncodesAndDecodesAnErrorResponse()
    {
        var idElement = JsonSerializer.SerializeToElement(3L);
        var response = JsonRpcMessage.ErrorResponse(idElement, JsonRpcErrorCodes.MethodNotFound, "nope");

        var decoded = JsonRpcCodec.TryDecodeLine(JsonRpcCodec.EncodeLine(response));

        Assert.NotNull(decoded);
        Assert.True(decoded!.IsResponse);
        Assert.NotNull(decoded.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, decoded.Error!.Code);
        Assert.Equal("nope", decoded.Error.Message);
    }
}
