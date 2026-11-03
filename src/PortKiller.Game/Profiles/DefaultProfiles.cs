using PortKiller.Game.Models;

namespace PortKiller.Game.Profiles;

/// <summary>
/// 와우 클래식(WoW Classic) 및 데스크톱 기본 프로필 프리셋 제공.
/// </summary>
public static class DefaultProfiles
{
    public static GameProfile CreateWoWClassicProfile()
    {
        var profile = new GameProfile
        {
            Name = "World of Warcraft Classic",
            TargetProcessName = "WowClassic",
            LookSensitivity = 6.0,
            TouchpadSensitivity = 8.5,
            StickDeadZone = 0.10,
            IsPushToTalk = true,
            PushToTalkButton = DualSenseButton.Mute,
            EnableTouchpadMouse = true,
            EnableVibration = true,
            EnableInGameOverlay = true
        };

        // 기본 버튼 매핑 (ConsolePort 애드온 호환)
        // VK_SPACE = 0x20, VK_TAB = 0x09, VK_ESCAPE = 0x1B
        // '1'=0x31, '2'=0x32, '3'=0x33, '4'=0x34, '9'=0x39, '0'=0x30
        // 'B'=0x42, 'C'=0x43, 'F'=0x46, 'M'=0x4D, 'Z'=0x5A, 'T'=0x54
        profile.Bindings[DualSenseButton.Cross] = new ButtonActionBinding
        {
            NormalKey = 0x20, // Space (점프)
            Description = "점프"
        };

        profile.Bindings[DualSenseButton.Circle] = new ButtonActionBinding
        {
            NormalKey = 0x46, // F (상호작용 / 루팅)
            Description = "상호작용 / 루팅"
        };

        profile.Bindings[DualSenseButton.Square] = new ButtonActionBinding
        {
            NormalKey = 0x31,      // 1번 슬롯
            L2ModifierKey = 0x35,  // Shift+1 대체 슬롯
            R2ModifierKey = 0x36,  // Ctrl+1 대체 슬롯
            Description = "스킬 슬롯 1"
        };

        profile.Bindings[DualSenseButton.Triangle] = new ButtonActionBinding
        {
            NormalKey = 0x32,      // 2번 슬롯
            L2ModifierKey = 0x37,  // Shift+2 대체
            R2ModifierKey = 0x38,  // Ctrl+2 대체
            Description = "스킬 슬롯 2"
        };

        profile.Bindings[DualSenseButton.R1] = new ButtonActionBinding
        {
            NormalKey = 0x33,      // 3번 슬롯
            L2ModifierKey = 0x34,  // 4번 슬롯
            Description = "스킬 슬롯 3"
        };

        profile.Bindings[DualSenseButton.L1] = new ButtonActionBinding
        {
            NormalKey = 0x09,      // Tab (가장 가까운 적 타겟팅)
            Description = "적 타겟팅 (Tab)"
        };

        profile.Bindings[DualSenseButton.DpadUp] = new ButtonActionBinding
        {
            NormalKey = 0x39,      // 9 (물약/비상 소모품)
            Description = "비상 소모품"
        };

        profile.Bindings[DualSenseButton.DpadDown] = new ButtonActionBinding
        {
            NormalKey = 0x5A,      // Z (탈것 탑승)
            Description = "탈것 탑승"
        };

        profile.Bindings[DualSenseButton.DpadLeft] = new ButtonActionBinding
        {
            NormalKey = 0x42,      // B (가방 열기)
            Description = "가방"
        };

        profile.Bindings[DualSenseButton.DpadRight] = new ButtonActionBinding
        {
            NormalKey = 0x4D,      // M (세계 지도)
            Description = "지도"
        };

        profile.Bindings[DualSenseButton.Options] = new ButtonActionBinding
        {
            NormalKey = 0x1B,      // Esc (게임 메뉴)
            Description = "게임 메뉴 (Esc)"
        };

        profile.Bindings[DualSenseButton.Create] = new ButtonActionBinding
        {
            NormalKey = 0x43,      // C (캐릭터 정보)
            Description = "캐릭터 창"
        };

        profile.Bindings[DualSenseButton.L3] = new ButtonActionBinding
        {
            NormalKey = 0x90,      // NumLock (자동 달리기 토글)
            Description = "자동 달리기"
        };

        profile.Bindings[DualSenseButton.TouchpadClick] = new ButtonActionBinding
        {
            MouseClickType = 1,    // 마우스 좌클릭
            Description = "마우스 좌클릭"
        };

        // 와우 클래식 전용 음성 명령어 프리셋
        // F1=0x70, F2=0x71, F3=0x72
        profile.VoiceCommands = new List<VoiceCommand>
        {
            new() { Phrase = "물약", TargetVirtualKey = 0x39, Category = "회복", Description = "치유 물약 즉시 사용" },
            new() { Phrase = "생석", TargetVirtualKey = 0x30, Category = "회복", Description = "치유 생명석 사용" },
            new() { Phrase = "전투 태세", TargetVirtualKey = 0x70, Category = "태세", Description = "전사 전투 태세 (F1)" },
            new() { Phrase = "방어 태세", TargetVirtualKey = 0x71, Category = "태세", Description = "전사 방어 태세 (F2)" },
            new() { Phrase = "광폭 태세", TargetVirtualKey = 0x72, Category = "태세", Description = "전사 광폭 태세 (F3)" },
            new() { Phrase = "도발", TargetVirtualKey = 0x54, Category = "전투", Description = "도발 스킬 (T)" },
            new() { Phrase = "탈것", TargetVirtualKey = 0x5A, Category = "이동", Description = "탈것 탑승 (Z)" },
            new() { Phrase = "귀환석", TargetVirtualKey = 0x48, Category = "이동", Description = "귀환석 사용 (H)" },
            new() { Phrase = "지도", TargetVirtualKey = 0x4D, Category = "유틸", Description = "지도 열기/닫기 (M)" },
            new() { Phrase = "가방", TargetVirtualKey = 0x42, Category = "유틸", Description = "모든 가방 열기 (B)" }
        };

        return profile;
    }
}
