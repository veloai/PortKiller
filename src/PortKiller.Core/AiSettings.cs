namespace PortKiller.Core;

/// <summary>어느 회사 AI 를 쓸지. 열쇠는 각각 따로 등록한다.</summary>
public enum AiProvider
{
    Gemini = 0,
    OpenAi = 1,
}

/// <summary>
/// AI 설정 중 <b>비밀이 아닌 것</b>. 열쇠는 여기 들어오지 않는다 —
/// 이 객체는 평문 JSON 으로 저장되므로, 열쇠가 섞이면 그대로 디스크에 노출된다.
/// 열쇠는 <c>ApiKeyStore</c> 가 따로 암호화해 보관한다.
/// </summary>
public sealed class AiSettings
{
    /// <summary>
    /// 모델 이름을 상수로 박지 않고 설정으로 두는 이유: 모델은 우리 배포 주기와 무관하게
    /// 바뀐다. 박아 두면 이름 하나 때문에 다시 빌드해서 사내에 다시 뿌려야 한다.
    /// 기본값은 사내에서 실제로 쓰고 있는 것과 맞췄다.
    /// </summary>
    public const string DefaultGeminiModel = "gemini-2.5-flash";

    public const string DefaultOpenAiModel = "gpt-5.1";

    /// <summary>
    /// 펫이 어떤 말투로 답할지. 사용자가 고칠 수 있다.
    /// 답이 길면 말풍선이 아니라 창을 채우므로 짧게 답하라고 미리 일러 둔다.
    /// </summary>
    public const string DefaultPersona =
        "너는 사용자의 바탕화면에 사는 작은 동물 캐릭터다. 한국어로, 친근한 반말로 짧게 답한다. " +
        "군더더기 인사말을 붙이지 않고 바로 본론을 말한다. 모르면 모른다고 말한다.";

    public AiProvider Provider { get; set; } = AiProvider.Gemini;

    public string GeminiModel { get; set; } = DefaultGeminiModel;

    public string OpenAiModel { get; set; } = DefaultOpenAiModel;

    public string Persona { get; set; } = DefaultPersona;

    /// <summary>한 번 물어볼 때 기다릴 시간. 넘으면 끊고 사람이 읽을 수 있는 말로 알린다.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>지금 고른 곳의 모델 이름. 비어 있으면 기본값으로 돌아간다.</summary>
    public string ModelFor(AiProvider provider)
    {
        var name = provider == AiProvider.OpenAi ? OpenAiModel : GeminiModel;
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        return provider == AiProvider.OpenAi ? DefaultOpenAiModel : DefaultGeminiModel;
    }

    public static string DisplayName(AiProvider provider)
        => provider == AiProvider.OpenAi ? "OpenAI" : "Gemini";
}
