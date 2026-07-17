using System.Text.Json;
using WpfPilot.Server;
using Xunit;

namespace WpfPilot.Tests;

public class JsonRpcTests
{
    [Fact]
    public void TryParse_ValidRequest_ExtractsFields()
    {
        const string line = """
        {"jsonrpc":"2.0","id":7,"method":"find_elements","token":"secret","params":{"query":"Greet","limit":20}}
        """;

        var ok = RpcRequest.TryParse(line, out var request, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("find_elements", request.Method);
        Assert.Equal("secret", request.Token);
        Assert.Equal("Greet", request.Params.GetProperty("query").GetString());
        Assert.Equal(20, request.Params.GetProperty("limit").GetInt32());
        Assert.NotNull(request.Id);
        Assert.Equal(7, request.Id!.Value.GetInt32());
    }

    [Fact]
    public void TryParse_MissingMethod_Fails()
    {
        var ok = RpcRequest.TryParse("""{"jsonrpc":"2.0","id":1}""", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_InvalidJson_Fails()
    {
        var ok = RpcRequest.TryParse("{ not json", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Result_ProducesJsonRpcEnvelope()
    {
        RpcRequest.TryParse("""{"jsonrpc":"2.0","id":3,"method":"ping"}""", out var request, out _);

        var json = Rpc.Result(request.Id, new { pong = true });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(3, root.GetProperty("id").GetInt32());
        Assert.True(root.GetProperty("result").GetProperty("pong").GetBoolean());
        Assert.False(root.TryGetProperty("error", out _));
    }

    [Fact]
    public void Error_ProducesJsonRpcErrorEnvelope()
    {
        var json = Rpc.Error(null, RpcCodes.Unauthorized, "Invalid or missing token.");

        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(RpcCodes.Unauthorized, error.GetProperty("code").GetInt32());
        Assert.Equal("Invalid or missing token.", error.GetProperty("message").GetString());
    }
}
