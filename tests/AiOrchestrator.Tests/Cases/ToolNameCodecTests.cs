using AiOrchestrator.Core.Orchestration;
using AiOrchestrator.Tests.Testing;

namespace AiOrchestrator.Tests.Cases;

public class ToolNameCodecTests
{
    [Fact]
    public void RoundTripsServerAndToolName()
    {
        var encoded = ToolNameCodec.Encode("payee", "get_payee_by_id");
        Assert.Equal("payee__get_payee_by_id", encoded);

        var ok = ToolNameCodec.TryDecode(encoded, out var serverId, out var toolName);
        Assert.True(ok);
        Assert.Equal("payee", serverId);
        Assert.Equal("get_payee_by_id", toolName);
    }

    [Fact]
    public void RejectsNamesWithoutASeparator()
    {
        var ok = ToolNameCodec.TryDecode("not_a_composed_name", out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void HandlesServerIdsContainingHyphens()
    {
        var encoded = ToolNameCodec.Encode("business-security", "check_fraud_flags");
        var ok = ToolNameCodec.TryDecode(encoded, out var serverId, out var toolName);

        Assert.True(ok);
        Assert.Equal("business-security", serverId);
        Assert.Equal("check_fraud_flags", toolName);
    }
}
