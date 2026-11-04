namespace PortKiller.Core;

/// <summary>어느 회사 AI 를 쓸지. 열쇠는 각각 따로 등록한다.</summary>
public enum AiProvider
{
    Gemini = 0,
    OpenAi = 1,

    /// <summary>ALFA 비서(Alorys). ALFA 가 승인해 준 펫 전용 키로 부른다.</summary>
    Alfa = 2,
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

    /// <summary>ALFA 주소. 기본은 세 기기가 함께 쓰는 허브. 허브(https uziseignk.com)나 이 PC 만 받는다.</summary>
    public string AlfaBaseUrl { get; set; } = AlfaChatClient.DefaultBaseUrl;

    /// <summary>ALFA 설정 화면에 이 펫이 어느 기기 것인지 보이는 이름(예: windows-office).</summary>
    public string AlfaDeviceName { get; set; } = "";

    /// <summary>한 번 물어볼 때 기다릴 시간. 넘으면 끊고 사람이 읽을 수 있는 말로 알린다.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>지금 고른 곳의 모델 이름. 비어 있으면 기본값으로 돌아간다.</summary>
    public string ModelFor(AiProvider provider)
    {
        if (provider == AiProvider.Alfa) return "Alorys";
        var name = provider == AiProvider.OpenAi ? OpenAiModel : GeminiModel;
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        return provider == AiProvider.OpenAi ? DefaultOpenAiModel : DefaultGeminiModel;
    }

    public static string DisplayName(AiProvider provider) => provider switch
    {
        AiProvider.OpenAi => "OpenAI",
        AiProvider.Alfa => "ALFA",
        _ => "Gemini",
    };

    /// <summary>
    /// 기다릴 시간. ALFA 는 서버가 90초까지 생각하므로 그보다 조금 더 기다린다 —
    /// 먼저 끊으면 답이 오는데도 "시간 초과"로 보인다.
    /// </summary>
    public int TimeoutFor(AiProvider provider)
        => provider == AiProvider.Alfa ? Math.Max(TimeoutSeconds, 100) : TimeoutSeconds;
}
