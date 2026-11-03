using System.Text.Json;
using System.Text.Json.Nodes;

namespace PortKiller.Core;

/// <summary>MCP 로 내보내는 도구 하나. 입력 형식(JSON Schema)과, 입력을 받아 글로 답하는 처리기.</summary>
public sealed record McpTool(string Name, string Description, JsonObject InputSchema, Func<JsonElement, McpToolResult> Handle);

/// <summary>도구 실행 결과. <paramref name="IsError"/> 가 참이면 도구가 실패한 것으로 알린다(요청 자체는 성공).</summary>
public sealed record McpToolResult(string Text, bool IsError = false);

/// <summary>
/// MCP(Model Context Protocol) 요청을 처리하는 순수 규칙. 전송(HTTP)을 모르고 JSON 글 → JSON 글만 다룬다.
///
/// <para>지원하는 것: <c>initialize</c>, <c>notifications/initialized</c>(등 알림), <c>ping</c>, <c>tools/list</c>, <c>tools/call</c>.
/// 도구만 내보내는 작은 서버라 리소스·프롬프트는 없다.</para>
/// </summary>
public sealed class McpRouter
{
    /// <summary>알아듣는 프로토콜 버전. 클라이언트가 이 중 하나를 원하면 그대로, 아니면 가장 새것으로 답한다.</summary>
    public static readonly IReadOnlyList<string> SupportedVersions = new[] { "2025-11-25", "2025-06-18", "2025-03-26" };

    private readonly Dictionary<string, McpTool> _tools;
    private readonly string _serverName;
    private readonly string _serverVersion;

    public McpRouter(IEnumerable<McpTool> tools, string serverName = "portkiller-pet", string serverVersion = "1.0.0")
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _serverName = serverName;
        _serverVersion = serverVersion;
    }

    /// <summary>
    /// 요청 하나를 처리한다. 응답이 없는 알림(id 없음)이면 null 을 돌려준다(HTTP 로는 202).
    /// 처리기에서 난 예외는 도구 오류로 감싸 돌려준다 - 펫 쪽 사정으로 서버가 죽으면 안 된다.
    /// </summary>
    public string? Handle(string requestJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(requestJson);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error(null, -32600, "Invalid Request (batch not supported)");

            var hasId = root.TryGetProperty("id", out var idEl);
            JsonNode? id = hasId ? JsonNode.Parse(idEl.GetRawText()) : null;

            if (!root.TryGetProperty("method", out var methodEl) || methodEl.ValueKind != JsonValueKind.String)
                return hasId ? Error(id, -32600, "Invalid Request") : null;

            var method = methodEl.GetString()!;
            var @params = root.TryGetProperty("params", out var p) ? p : default;

            // 알림(id 없음)은 답하지 않는다.
            if (!hasId) return null;

            try
            {
                return method switch
                {
                    "initialize" => Result(id, Initialize(@params)),
                    "ping" => Result(id, new JsonObject()),
                    "tools/list" => Result(id, ListTools()),
                    "tools/call" => CallTool(id, @params),
                    _ => Error(id, -32601, $"Method not found: {method}"),
                };
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Error(id, -32603, "Internal error: " + ex.Message);
            }
        }
    }

    private JsonObject Initialize(JsonElement @params)
    {
        var requested = @params.ValueKind == JsonValueKind.Object && @params.TryGetProperty("protocolVersion", out var v)
            ? v.GetString()
            : null;
        var version = requested is not null && SupportedVersions.Contains(requested) ? requested : SupportedVersions[0];

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = _serverName, ["version"] = _serverVersion },
            ["instructions"] = "Desktop pet on the user's PC. Use pet_say to tell the user something short (task done, build failed), " +
                               "pet_status / dev_status to read the pet's and the dev environment's state.",
        };
    }

    private JsonObject ListTools()
    {
        var list = new JsonArray();
        foreach (var t in _tools.Values)
        {
            list.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema.DeepClone(),
            });
        }

        return new JsonObject { ["tools"] = list };
    }

    private string CallTool(JsonNode? id, JsonElement @params)
    {
        if (@params.ValueKind != JsonValueKind.Object || !@params.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return Error(id, -32602, "Invalid params: name is required");

        if (!_tools.TryGetValue(nameEl.GetString()!, out var tool))
            return Error(id, -32602, $"Unknown tool: {nameEl.GetString()}");

        var args = @params.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
            ? a
            : JsonDocument.Parse("{}").RootElement;

        McpToolResult result;
        try
        {
            result = tool.Handle(args);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result = new McpToolResult("tool failed: " + ex.Message, IsError: true);
        }

        return Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = result.Text }),
            ["isError"] = result.IsError,
        });
    }

    private static string Result(JsonNode? id, JsonNode result) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result,
    }.ToJsonString();

    private static string Error(JsonNode? id, int code, string message) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    }.ToJsonString();

    /// <summary>도구 입력에서 글 하나를 꺼낸다. 없거나 글이 아니면 null.</summary>
    public static string? Text(JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>화면에 띄울 글을 안전하게 다듬는다: 제어 문자 제거, 길이 제한.</summary>
    public static string Clean(string text, int maxLength)
    {
        var chars = text.Where(c => !char.IsControl(c) || c == '\n').ToArray();
        var s = new string(chars).Trim();
        return s.Length > maxLength ? s[..maxLength] + "…" : s;
    }
}
