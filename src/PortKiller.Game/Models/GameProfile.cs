namespace PortKiller.Game.Models;

/// <summary>
/// 각 버튼에 할당된 동작 (기본 키 및 조합키 상태)
/// </summary>
public sealed class ButtonActionBinding
{
    /// <summary>기본 입력 시 전송할 가상 키 코드 (0이면 없음)</summary>
    public ushort NormalKey { get; set; }

    /// <summary>L2 트리거를 누른 채 입력 시 전송할 가상 키 코드 (Shift 조합)</summary>
    public ushort L2ModifierKey { get; set; }

    /// <summary>R2 트리거를 누른 채 입력 시 전송할 가상 키 코드 (Ctrl 조합)</summary>
    public ushort R2ModifierKey { get; set; }

    /// <summary>마우스 클릭 여부 (0: 없음, 1: 좌클릭, 2: 우클릭)</summary>
    public int MouseClickType { get; set; }

    /// <summary>버튼 라벨 설명 (예: "점프", "스킬 1", "가방 열기")</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 특정 게임을 위한 통합 컨트롤러 및 보이스 프로필
/// </summary>
public sealed class GameProfile
{
    public string Name { get; set; } = "World of Warcraft Classic";

    /// <summary>감지할 대상 프로세스 이름 (예: "WowClassic", "Wow")</summary>
    public string TargetProcessName { get; set; } = "WowClassic";

    /// <summary>버튼별 매핑 설정</summary>
    public Dictionary<DualSenseButton, ButtonActionBinding> Bindings { get; set; } = new();

    /// <summary>보이스 명령 목록</summary>
    public List<VoiceCommand> VoiceCommands { get; set; } = new();

    // 조작 감도 설정
    public double LookSensitivity { get; set; } = 6.0;
    public double TouchpadSensitivity { get; set; } = 8.0;
    public double StickDeadZone { get; set; } = 0.10;
    public bool InvertYAxis { get; set; } = false;
    public bool SwapSticks { get; set; } = false;

    // 보이스 및 오디오 설정
    public bool IsPushToTalk { get; set; } = true;
    public DualSenseButton PushToTalkButton { get; set; } = DualSenseButton.Mute;
    public string PreferredAudioDeviceName { get; set; } = "Wireless Controller";

    // 기능 토글
    public bool EnableTouchpadMouse { get; set; } = true;
    public bool EnableVibration { get; set; } = true;
    public bool EnableInGameOverlay { get; set; } = true;
}
