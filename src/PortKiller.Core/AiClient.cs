using System.Net;

namespace PortKiller.Core;

/// <summary>
/// 사용자에게 그대로 보여 줄 수 있는 실패. 예외 메시지를 날것으로 띄우면
/// 영어 스택과 URL 이 그대로 나와서, 사용자는 무엇을 해야 할지 알 수 없다.
/// </summary>
public sealed class AiException : Exception
{
    public AiException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// 한 번 묻고 한 번 답받는다. 대화 이력도, 스트리밍도 없다.
///
/// <para>왜 <b>Core</b> 에 두나: 이 계층은 "윈도우 코드가 없는 곳"이지 "통신이 없는 곳"이 아니다.
/// HttpClient 는 어느 OS 에서나 같고, 여기 둬야 검사가 닿는다 —
/// 검사 프로젝트는 Core 만 참조한다. 열쇠 보관만 윈도우 것이라 Platform 에 남는다.</para>
/// </summary>
public interface IAiClient
{
    AiProvider Provider { get; }

    /// <summary>물어본다. 실패는 전부 <see cref="AiException"/> 으로 나온다.</summary>
    Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default);
}

/// <summary>두 회사 클라이언트가 함께 쓰는 실패 해석. 한 곳에 모아 둬야 말이 갈리지 않는다.</summary>
internal static class AiFailure
{
    /// <summary>
    /// HTTP 상태를 사람 말로 바꾼다. <b>응답 본문을 그대로 붙이지 않는다</b> —
    /// 401 응답에 보낸 열쇠가 되비쳐 오는 서비스가 있고, 그러면 화면과 로그에 열쇠가 남는다.
    /// </summary>
    public static AiException FromStatus(HttpStatusCode status, string providerName) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new AiException($"{providerName} 열쇠가 거절당했어요. 설정에서 키를 다시 등록해 주세요."),
        HttpStatusCode.NotFound =>
            new AiException("모델 이름을 찾지 못했어요. 설정에서 모델 이름을 확인해 주세요."),
        HttpStatusCode.TooManyRequests =>
            new AiException("요청이 너무 잦아요. 잠시 뒤에 다시 시도해 주세요."),
        HttpStatusCode.RequestEntityTooLarge =>
            new AiException("질문이 너무 길어요. 조금 줄여서 다시 보내 주세요."),
        >= HttpStatusCode.InternalServerError =>
            new AiException($"{providerName} 서버가 지금 응답하지 못해요. 잠시 뒤에 다시 시도해 주세요."),
        _ => new AiException($"{providerName} 요청이 실패했어요 (HTTP {(int)status})."),
    };

    /// <summary>
    /// 통신 자체가 안 된 경우. 취소 토큰이 눌린 것과 시간이 넘은 것은 <b>다른 일</b>이라
    /// 호출한 쪽이 구분할 수 있게 갈라 준다 — 사용자가 직접 끈 것을 오류로 알리면 안 된다.
    /// </summary>
    public static Exception FromTransport(Exception ex, CancellationToken token)
    {
        if (ex is OperationCanceledException && token.IsCancellationRequested) return ex;
        if (ex is TaskCanceledException or TimeoutException)
            return new AiException("응답이 너무 오래 걸려서 그만뒀어요. 다시 시도하거나 질문을 줄여 보세요.");
        if (ex is HttpRequestException)
            return new AiException("인터넷에 연결하지 못했어요. 네트워크나 사내 프록시 설정을 확인해 주세요.");
        return new AiException("요청을 보내지 못했어요.", ex);
    }

    public static AiException EmptyAnswer(string providerName)
        => new($"{providerName} 가 답을 돌려주지 않았어요. 질문을 바꿔서 다시 시도해 주세요.");
}
