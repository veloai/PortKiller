using System.Text.Json;
using System.Text.Json.Nodes;
using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class McpRouterTests
{
    private static McpRouter Router(List<string>? said = null) => new(new[]
    {
        new McpTool("pet_say", "say something",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } } },
            args =>
            {
                var t = McpRouter.Text(args, "text");
                if (t is null) return new McpToolResult("text is required", IsError: true);
                said?.Add(t);
                return new McpToolResult("ok");
            }),
        new McpTool("boom", "always throws", new JsonObject { ["type"] = "object" }, _ => throw new InvalidOperationException("nope")),
    });

    private static JsonElement Parse(string? json) => JsonDocument.Parse(json!).RootElement;

    [Fact]
    public void Initialize_echoes_a_supported_version_and_advertises_tools()
    {
        var r = Parse(Router().Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}"""));

        Assert.Equal(1, r.GetProperty("id").GetInt32());
        Assert.Equal("2025-06-18", r.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.True(r.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));
    }

    [Fact]
    public void Initialize_with_an_unknown_version_answers_the_newest()
    {
        var r = Parse(Router().Handle("""{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"1999-01-01"}}"""));

        Assert.Equal("a", r.GetProperty("id").GetString());
        Assert.Equal(McpRouter.SupportedVersions[0], r.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public void Notifications_get_no_response()
    {
        Assert.Null(Router().Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact]
    public void Tools_list_returns_every_tool_with_its_schema()
    {
        var tools = Parse(Router().Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""")).GetProperty("result").GetProperty("tools");

        Assert.Equal(2, tools.GetArrayLength());
        Assert.Equal("pet_say", tools[0].GetProperty("name").GetString());
        Assert.Equal("object", tools[0].GetProperty("inputSchema").GetProperty("type").GetString());
    }

    [Fact]
    public void Tools_call_runs_the_handler_and_returns_text_content()
    {
        var said = new List<string>();
        var r = Parse(Router(said).Handle("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"pet_say","arguments":{"text":"빌드 끝!"}}}"""));

        Assert.Equal(new[] { "빌드 끝!" }, said);
        Assert.False(r.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal("ok", r.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Tool_failures_are_tool_errors_not_crashes()
    {
        var r = Parse(Router().Handle("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"boom"}}"""));

        Assert.True(r.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("nope", r.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"nope"}}""", -32602)]
    [InlineData("""{"jsonrpc":"2.0","id":6,"method":"resources/list"}""", -32601)]
    [InlineData("""not json""", -32700)]
    [InlineData("""[{"jsonrpc":"2.0","id":7,"method":"ping"}]""", -32600)]
    public void Bad_requests_get_json_rpc_errors(string request, int code)
    {
        Assert.Equal(code, Parse(Router().Handle(request)).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Clean_strips_control_characters_and_caps_length()
    {
        Assert.Equal("ab", McpRouter.Clean("a\u0007b", 10));
        Assert.Equal("abc…", McpRouter.Clean("abcdef", 3));
    }
}
