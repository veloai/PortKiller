namespace PortKiller.Core;

/// <summary>ALFA 연동이 지금 어느 단계인지(§14, §11.4).</summary>
public enum PetLinkPhase
{
    /// <summary>연동 안 됨. 키가 없다.</summary>
    Off,

    /// <summary>요청을 보냈고 주인 승인을 기다린다. 펫 본체가 5초마다 받아 가기를 한다.</summary>
    Waiting,

    /// <summary>키가 있고 마지막 확인이 성공했다(또는 아직 확인 전).</summary>
    Linked,

    /// <summary>승인되지 않은 채 10분이 지났다.</summary>
    Expired,

    /// <summary>주인이 거절했거나, 이 요청으로는 키를 받을 수 없다.</summary>
    Rejected,

    /// <summary>키가 있었는데 ALFA 가 401 을 줬다(해제됨·같은 기기 새 승인·키 틀림).</summary>
    Revoked,

    /// <summary>키는 있는데 ALFA 에 닿지 않는다. 펫은 말하지 않는다.</summary>
    Unreachable,

    /// <summary>
    /// 승인은 됐는데 키를 받는 응답이 도중에 끊겨, 서버는 "이미 받아 감"(409)이라 한다. 키는 잃었다 — 다시 요청해야 한다.
    /// 전에는 이것을 '거절'로 보여 ALFA 화면(키 받아 감)과 펫 말이 서로 달랐다(코드 리뷰 3번).
    /// </summary>
    KeyLost,
}

/// <summary>상태가 바뀔 때 펫이 할 말과 표정. 둘 다 없으면 조용히.</summary>
public sealed record PetLinkSpeech(string? Line, PetLinkMood Mood);

public enum PetLinkMood { None, Happy, Sad }

/// <summary>
/// ALFA 연동 상태 기계(순수). 시계는 바깥에서 받는다. 화면·네트워크·키를 모른다 —
/// 받아 가기 비밀값과 키는 이 객체에 절대 넣지 않는다(글자로 찍혀 기록에 남을 수 있다).
/// </summary>
public sealed record PetLinkState
{
    public PetLinkPhase Phase { get; init; }

    /// <summary>대기 중일 때 화면에 보일 확인 코드(비밀 아님 — ALFA 화면에도 보인다).</summary>
    public string? Code { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>이 연동을 받은 시각. 모르면 null.</summary>
    public DateTimeOffset? LinkedAt { get; init; }

    /// <summary>ALFA 가 마지막으로 "살아 있다"고 답한 시각.</summary>
    public DateTimeOffset? LastSeen { get; init; }

    /// <summary>대기 전 단계. 취소하면 그 단계로 돌아간다(키가 있었으면 연결, 끊긴 키였으면 끊김).</summary>
    public PetLinkPhase BeforeWaiting { get; init; }

    /// <summary>방금 CANCEL 로 대기를 그만뒀다. 이 전이에는 말하지 않는다("연결됐어요"가 거짓말이 된다).</summary>
    public bool JustCancelled { get; init; }

    public static readonly PetLinkState Off = new() { Phase = PetLinkPhase.Off };

    /// <summary>앱을 켰을 때: 키가 있으면 일단 연결로 보고 첫 확인을 기다린다.</summary>
    public static PetLinkState Initial(bool hasKey, DateTimeOffset? linkedAt = null)
        => hasKey ? new() { Phase = PetLinkPhase.Linked, LinkedAt = linkedAt } : Off;

    public bool IsWaiting => Phase == PetLinkPhase.Waiting;

    /// <summary>키를 쓸 수 있다고 보는 상태(연결 확인이 잠깐 안 될 때 포함).</summary>
    public bool IsLinked => Phase is PetLinkPhase.Linked or PetLinkPhase.Unreachable;

    // ---------- 전이 ----------

    public PetLinkState Requested(string code, DateTimeOffset expiresAt)
        => new()
        {
            Phase = PetLinkPhase.Waiting, Code = code, ExpiresAt = expiresAt,
            BeforeWaiting = IsWaiting ? BeforeWaiting : Phase,
            LinkedAt = LinkedAt, LastSeen = LastSeen,
        };

    /// <summary>
    /// 받아 가기 결과. 대기·네트워크 오류는 그대로 대기(만료 시각이 지나도 — C1).
    /// 410 은 거절·만료를 가르지 않으므로 시계로 가른다.
    /// </summary>
    public PetLinkState Pickup(AlfaChatClient.PickupState result, DateTimeOffset now)
    {
        if (!IsWaiting) return this;
        return result switch
        {
            AlfaChatClient.PickupState.Pending or AlfaChatClient.PickupState.Retry => this,
            AlfaChatClient.PickupState.Issued => new() { Phase = PetLinkPhase.Linked, LinkedAt = now, LastSeen = now },
            AlfaChatClient.PickupState.Closed when ExpiresAt is { } e && now >= e => Closed(PetLinkPhase.Expired),
            AlfaChatClient.PickupState.AlreadyIssued => Closed(PetLinkPhase.KeyLost),
            _ => Closed(PetLinkPhase.Rejected),
        };
    }

    private PetLinkState Closed(PetLinkPhase phase) => new() { Phase = phase, ExpiresAt = ExpiresAt };

    /// <summary>CANCEL: 받아 가기를 멈추고 요청 전으로. 예전 키가 있었으면 연결 상태로 돌아간다.</summary>
    public PetLinkState Cancelled()
        => !IsWaiting ? this
            : BeforeWaiting is PetLinkPhase.Linked or PetLinkPhase.Unreachable
                ? new() { Phase = PetLinkPhase.Linked, LinkedAt = LinkedAt, LastSeen = LastSeen, JustCancelled = true }
            : BeforeWaiting == PetLinkPhase.Revoked ? new() { Phase = PetLinkPhase.Revoked, LastSeen = LastSeen, JustCancelled = true }
            : Off;

    /// <summary>
    /// 5분마다 A7. 대기 중에는 받아 가기가 우선이라 무시한다.
    /// 401 은 키가 없는 상태(Off·만료·거절)면 바꾸지 않는다 — 원래 연결이 없었다.
    /// </summary>
    public PetLinkState Checked(AlfaChatClient.LinkState result, DateTimeOffset now)
    {
        if (IsWaiting) return this;
        return result switch
        {
            AlfaChatClient.LinkState.Connected => this with { Phase = PetLinkPhase.Linked, LastSeen = now, Code = null, ExpiresAt = null, JustCancelled = false },
            AlfaChatClient.LinkState.Unlinked when IsLinked => new() { Phase = PetLinkPhase.Revoked, LastSeen = LastSeen },
            AlfaChatClient.LinkState.Unreachable when IsLinked => this with { Phase = PetLinkPhase.Unreachable, JustCancelled = false },
            _ => this,
        };
    }

    /// <summary>묻기가 401 을 받았다(C9). 확인 401 과 같다.</summary>
    public PetLinkState Unauthorized(DateTimeOffset now) => Checked(AlfaChatClient.LinkState.Unlinked, now);

    /// <summary>A8 로 끊었거나 키를 지웠다.</summary>
    public PetLinkState Unlinked() => Off;

    // ---------- 보이기 ----------

    /// <summary>메뉴에 쓰는 세 낱말: LINKED / WAITING / OFF.</summary>
    public string MenuWord => Phase switch
    {
        PetLinkPhase.Linked => "LINKED",
        PetLinkPhase.Waiting => "WAITING",
        _ => "OFF",
    };

    /// <summary>dot 상태 도장: ▦ 연결 / ▒ 대기 / ▧ 거절·해제 / ░ 그 밖.</summary>
    public string Glyph => Phase switch
    {
        PetLinkPhase.Linked => "▦",
        PetLinkPhase.Waiting => "▒",
        PetLinkPhase.Rejected or PetLinkPhase.Revoked or PetLinkPhase.KeyLost => "▧",
        _ => "░",
    };

    /// <summary>창의 STATUS 칸 글자.</summary>
    public string StatusWord => Phase switch
    {
        PetLinkPhase.Linked => "LINKED",
        PetLinkPhase.Waiting => "WAITING",
        PetLinkPhase.Expired => "OFF · 만료",
        PetLinkPhase.Rejected => "OFF · 거절",
        PetLinkPhase.KeyLost => "OFF · 키 받기 실패",
        PetLinkPhase.Revoked => "OFF · 연결이 끊겼어요",
        PetLinkPhase.Unreachable => "OFF · 연결 불가",
        _ => "OFF",
    };

    public string MenuLabel => $"🔗 ALFA 연동 · {MenuWord}";

    /// <summary>승인 대기 남은 시간. 0 아래로 내려가지 않는다(그래도 받아 가기는 계속).</summary>
    public TimeSpan Remaining(DateTimeOffset now)
        => ExpiresAt is { } e && e > now ? e - now : TimeSpan.Zero;

    public static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    public int? LastSeenMinutes(DateTimeOffset now)
        => LastSeen is { } s ? (int)Math.Max(0, Math.Floor((now - s).TotalMinutes)) : null;

    /// <summary>상태 카드 한 줄: <c>ALFA ▦ LINKED · 2m</c>.</summary>
    public string StatusLine(DateTimeOffset now)
    {
        var line = $"ALFA {Glyph} {MenuWord}";
        if (Phase == PetLinkPhase.Waiting) return $"{line} · {Clock(Remaining(now))}";
        if (Phase == PetLinkPhase.Unreachable) return $"{line} · 연결 불가";
        if (IsLinked && LastSeenMinutes(now) is { } m) return $"{line} · {Ago(m)}";
        return line;
    }

    public static string Ago(int minutes) => minutes switch
    {
        < 60 => $"{minutes}m",
        < 60 * 48 => $"{minutes / 60}h",
        _ => $"{minutes / (60 * 24)}d",
    };

    // ---------- 바뀔 때 ----------

    /// <summary>
    /// 기록(startup.log)할 만큼 바뀌었는지(C8). 단계가 바뀔 때만 — 확인할 때마다 바뀌는 마지막 확인 시각은 셈하지 않는다.
    /// </summary>
    public static bool ShouldLog(PetLinkState before, PetLinkState after) => before.Phase != after.Phase;

    /// <summary>기록 한 줄. 코드·시각만, 비밀값 없음.</summary>
    public static string LogLine(PetLinkState before, PetLinkState after) => $"alfa-link {before.Phase} -> {after.Phase}";

    /// <summary>§14 말풍선·표정. 연결 불가는 말하지 않는다. 같은 단계로는 말하지 않으므로 "끊겼어요"는 한 번만.</summary>
    public static PetLinkSpeech Speech(PetLinkState before, PetLinkState after)
    {
        if (after.JustCancelled) return new(null, PetLinkMood.None);
        if (before.Phase == after.Phase)
            return before.IsWaiting && after.IsWaiting && before.Code != after.Code
                ? new(RequestedLine, PetLinkMood.None)
                : new(null, PetLinkMood.None);
        return after.Phase switch
        {
            PetLinkPhase.Waiting => new(RequestedLine, PetLinkMood.None),
            PetLinkPhase.Linked when before.IsWaiting => new("ALFA 와 연결됐어요!", PetLinkMood.Happy),
            PetLinkPhase.Expired or PetLinkPhase.Rejected => new("연동이 안 됐어요. 다시 요청할까요?", PetLinkMood.Sad),
            PetLinkPhase.Revoked => new("ALFA 연결이 끊겼어요", PetLinkMood.Sad),
            PetLinkPhase.KeyLost => new("키를 받다가 끊겼어요. 다시 요청해 주세요", PetLinkMood.Sad),
            _ => new(null, PetLinkMood.None),
        };
    }

    public const string RequestedLine = "ALFA 에 확인 코드로 승인 부탁했어요!";
}
