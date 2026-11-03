namespace PortKiller.Game.Models;

/// <summary>
/// 보이스 명령과 가상 키 매핑 정의
/// </summary>
public sealed class VoiceCommand
{
    /// <summary>유저가 발화할 한글/영문 단어 (예: "물약", "전투 태세")</summary>
    public string Phrase { get; set; } = string.Empty;

    /// <summary>발동시킬 대상 가상 키 코드 (예: 0x39는 숫자 '9')</summary>
    public ushort TargetVirtualKey { get; set; }

    /// <summary>함께 누를 조합키 (VK_SHIFT = 0x10, VK_CONTROL = 0x11, 0이면 없음)</summary>
    public ushort ModifierVirtualKey { get; set; }

    /// <summary>스킬 카테고리 (회복, 태세, 유틸리티, 타겟팅)</summary>
    public string Category { get; set; } = "유틸리티";

    /// <summary>사용자 설명</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>마지막 발동 시각 (쿨다운 및 피드백 렌더용)</summary>
    public DateTimeOffset LastTriggered { get; set; } = DateTimeOffset.MinValue;
}
