import Foundation
import GameController

@main
struct TestRunner {
    static func main() {
        print("=== PortKiller Game Logic Verification ===")

        testBatteryInfoFormats()
        testControllerManagerInitialState()
        testVoiceManagerSafePermissionHandling()

        print("=== ALL 3 SUITES PASSED (Verified without hardware/recording) ===")
    }

    static func testBatteryInfoFormats() {
        print("[TEST] 1. Battery Info Formats (가짜 잔량 방지 검증)...")

        // 미연결
        let disconnected = ControllerBatteryInfo.disconnected
        assert(disconnected.displayText == "미연결 (연결 대기)", "미연결 표기 오류")
        assert(disconnected.percentageString == "미연결", "미연결 표기 오류")

        // 미지원
        let unsupported = ControllerBatteryInfo.unsupported
        assert(unsupported.displayText == "배터리 미지원", "미지원 표기 오류")

        // 미확인
        let unknown = ControllerBatteryInfo.unknown
        assert(unknown.displayText == "배터리 정보 없음", "미확인 표기 오류")

        // 실제 배터리 잔량 & 충전 상태
        let discharging = ControllerBatteryInfo.level(percentage: 65, isCharging: false, state: .discharging)
        assert(discharging.displayText == "65% (방전 중)", "방전 중 표기 오류")

        let charging = ControllerBatteryInfo.level(percentage: 80, isCharging: true, state: .charging)
        assert(charging.displayText == "80% ⚡ (충전 중)", "충전 중 표기 오류")

        let full = ControllerBatteryInfo.level(percentage: 100, isCharging: false, state: .full)
        assert(full.displayText == "100% (완충됨)", "완충 표기 오류")

        print("  -> PASSED: 가짜 100% 방지 및 상태별 텍스트 검증 완료.")
    }

    static func testControllerManagerInitialState() {
        print("[TEST] 2. ControllerManager Initial State (패드 미연결 시 입력 상태 검증)...")

        let manager = ControllerManager()
        assert(!manager.isConnected, "초기 연결 상태는 false 여야 함")
        assert(manager.batteryInfo == .disconnected, "미연결 시 배터리는 .disconnected 여야 함")
        assert(manager.leftTrigger == 0.0, "L2 트리거 초기값 0.0")
        assert(manager.rightTrigger == 0.0, "R2 트리거 초기값 0.0")
        assert(!manager.buttonL1, "L1 초기값 false")
        assert(!manager.buttonR1, "R1 초기값 false")
        assert(!manager.dpadUp, "D-Pad Up 초기값 false")
        assert(!manager.dpadDown, "D-Pad Down 초기값 false")
        assert(!manager.dpadLeft, "D-Pad Left 초기값 false")
        assert(!manager.dpadRight, "D-Pad Right 초기값 false")

        print("  -> PASSED: 미연결 시 L1/L2/R1/R2, D-Pad, 배터리 중립 상태 검증 완료.")
    }

    static func testVoiceManagerSafePermissionHandling() {
        print("[TEST] 3. VoiceManager Safe Permission Handling (비녹음 및 안전 검증)...")

        let voice = VoiceManager()
        assert(!voice.isListening, "초기 상태에서 임의 녹음이 시작되지 않아야 함")
        assert(voice.inputVolume == 0.0, "초기 음량 0.0")

        // 권한 일치성 검증
        assert(!voice.canStartListening || (voice.micPermission == .authorized && voice.speechPermission == .authorized), "권한 일치성 검증")

        // 비녹음 안전성 검증: 권한 미승인 상태에서 startListening 직접 호출 시 오디오 엔진이 작동하지 않아야 함
        if !voice.canStartListening {
            voice.startListening()
            assert(!voice.isListening, "권한 미부여 시 녹음이 강제로 시작되어서는 안 됨")
            assert(voice.lastCommandFeedback.contains("권한 미부여"), "권한 미부여 차단 안내 확인")
        }

        // 권한 텍스트 및 안내 메시지 포맷 검증
        let notDeterminedDesc = AudioPermissionStatus.notDetermined.localizedDescription
        let deniedDesc = AudioPermissionStatus.denied.localizedDescription
        let authorizedDesc = AudioPermissionStatus.authorized.localizedDescription
        assert(notDeterminedDesc.contains("권한 요청 대기"), "미결정 상태 설명 검증")
        assert(deniedDesc.contains("설정"), "거부 상태 설명 검증")
        assert(authorizedDesc.contains("허용됨"), "허용 상태 설명 검증")

        print("  -> PASSED: 권한 미부여 시 녹음 차단, 안전 검증 및 권한 인터페이스 검증 완료 (비녹음/팝업 차단 유지).")
    }
}
