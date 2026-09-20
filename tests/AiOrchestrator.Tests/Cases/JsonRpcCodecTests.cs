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

    [Fact]
    public void DecodesAResponseWithAStringId()
    {
        // JSON-RPC 2.0 permits string ids; reading such an id as a number used to throw and take
        // the transport's whole read loop down with it.
        var decoded = JsonRpcCodec.TryDecodeLine("{\"jsonrpc\":\"2.0\",\"id\":\"req-1\",\"result\":{\"ok\":true}}");

        Assert.NotNull(decoded);
        Assert.True(decoded!.IsResponse);
        Assert.Equal("\"req-1\"", decoded.IdKey);
    }

    [Fact]
    public void IdKeyKeepsNumberAndStringIdsDistinct()
    {
        var numeric = JsonRpcCodec.TryDecodeLine("{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{}}");
        var textual = JsonRpcCodec.TryDecodeLine("{\"jsonrpc\":\"2.0\",\"id\":\"7\",\"result\":{}}");

        Assert.Equal("7", numeric!.IdKey);
        Assert.Equal("\"7\"", textual!.IdKey);
        Assert.False(numeric.IdKey == textual.IdKey, "A numeric id must not correlate to a string id.");
    }

    [Fact]
    public void IdKeyIsNullWhenThereIsNoUsableId()
    {
        var noId = JsonRpcCodec.TryDecodeLine("{\"jsonrpc\":\"2.0\",\"result\":{}}");
        var nullId = JsonRpcCodec.TryDecodeLine("{\"jsonrpc\":\"2.0\",\"id\":null,\"result\":{}}");

        Assert.True(noId!.IdKey is null);
        Assert.True(nullId!.IdKey is null);
    }
}
