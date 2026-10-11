namespace PortKiller.Game.Models;

/// <summary>
/// PS5 듀얼센스 컨트롤러의 물리 버튼 정의
/// </summary>
public enum DualSenseButton
{
    None = 0,

    // 방향키 (D-Pad)
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,

    // 액션 버튼
    Square,     // □ (네모)
    Cross,      // ✕ (엑스)
    Circle,     // ○ (동그라미)
    Triangle,   // △ (세모)

    // 범퍼 및 트리거
    L1,
    R1,
    L2,
    R2,

    // 스틱 클릭
    L3,
    R3,

    // 시스템 및 유틸리티
    Create,         // 공유(Share) 버튼
    Options,        // 옵션 버튼
    PlayStation,    // PS 홈 버튼
    TouchpadClick,  // 중앙 터치패드 물리 클릭
    Mute            // 마이크 음소거 버튼 (PTT 트리거로 주로 활용)
}
