using System.Text.Json;
using System.Text.Json.Nodes;
using PortKiller.Core;

namespace PortKiller.App;

/// <summary>MCP 도구가 펫에게 부탁할 수 있는 일. 펫 창이 구현한다(화면 스레드로 넘기는 것도 펫 창 몫).</summary>
internal interface IPetMcpHost
{
    void Say(string text, PetPose? reaction);
    JsonObject Status();
    JsonObject Dev();
    void Briefing(bool show);
}

/// <summary>
/// 펫이 MCP 로 내보내는 도구들. Claude Code·ALFA 같은 에이전트가 "작업 끝났다고 알려 줘", "개발 환경 어때?"를 펫에게 시킬 수 있다.
///
/// <para>일부러 좁게 둔다: 말하기·표정·상태 읽기·브리핑 켜기뿐이다. 파일·명령 실행·입력 조작 같은 도구는 없다.</para>
/// </summary>
internal static class PetMcpTools
{
    private const int MaxSayLength = 140;

    public static IReadOnlyList<McpTool> Build(IPetMcpHost host) => new[]
    {
        new McpTool(
            "pet_say",
            "Show a short speech bubble from the desktop pet (Korean is fine). Use it to tell the user something at a glance: " +
            "a task finished, a build failed, attention needed. Optional mood sets the pet's reaction.",
            Schema(new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string", ["description"] = $"What the pet says (max {MaxSayLength} chars)." },
                ["mood"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("none", "happy", "sulk", "eat", "sick"),
                    ["description"] = "happy = good news, sulk = something failed or needs attention.",
                },
            }, "text"),
            args =>
            {
                var text = McpRouter.Text(args, "text");
                if (string.IsNullOrWhiteSpace(text)) return new McpToolResult("text is required", IsError: true);

                PetPose? pose = McpRouter.Text(args, "mood") switch
                {
                    "happy" => PetPose.Happy,
                    "sulk" => PetPose.Sulk,
                    "eat" => PetPose.Eat,
                    "sick" => PetPose.Sick,
                    _ => null,
                };
                host.Say(McpRouter.Clean(text, MaxSayLength), pose);
                return new McpToolResult("said");
            }),

        new McpTool(
            "pet_status",
            "Read the desktop pet's current state: species, life stage, stats (0-100), condition, poops on the ground.",
            Schema(new JsonObject()),
            _ => new McpToolResult(host.Status().ToJsonString(Pretty))),

        new McpTool(
            "dev_status",
            "Read the user's dev environment as last checked by the pet (Docker Desktop, Docker engine, containers, local server ports), " +
            "with a one-line summary. Checked about once a minute.",
            Schema(new JsonObject()),
            _ => new McpToolResult(host.Dev().ToJsonString(Pretty))),

        new McpTool(
            "pet_briefing",
            "Show or hide the pet's briefing: the pet stands up with glasses next to a status dashboard card.",
            Schema(new JsonObject { ["show"] = new JsonObject { ["type"] = "boolean" } }, "show"),
            args =>
            {
                if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("show", out var s)
                    || s.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return new McpToolResult("show (boolean) is required", IsError: true);

                host.Briefing(s.GetBoolean());
                return new McpToolResult(s.GetBoolean() ? "briefing shown" : "briefing hidden");
            }),
    };

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonObject Schema(JsonObject properties, params string[] required)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (required.Length > 0) schema["required"] = new JsonArray(required.Select(r => (JsonNode)r!).ToArray());
        return schema;
    }
}
