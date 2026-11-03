import Foundation
import GameController
import Combine
import IOKit
import IOKit.hid

/// 패드 배터리 상태 정의 (가짜 잔량 방지)
public enum ControllerBatteryInfo: Equatable {
    case disconnected
    case unsupported
    case unknown
    case level(percentage: Int, isCharging: Bool, state: GCDeviceBattery.State)

    public var displayText: String {
        switch self {
        case .disconnected:
            return "미연결 (연결 대기)"
        case .unsupported:
            return "배터리 미지원"
        case .unknown:
            return "배터리 정보 없음"
        case .level(let pct, let isCharging, let state):
            if isCharging || state == .charging {
                return "\(pct)% ⚡ (충전 중)"
            } else if state == .full {
                return "\(pct)% (완충됨)"
            } else {
                return "\(pct)% (방전 중)"
            }
        }
    }

    public var percentageString: String {
        switch self {
        case .disconnected:
            return "미연결"
        case .unsupported:
            return "미지원"
        case .unknown:
            return "미확인"
        case .level(let pct, _, _):
            return "\(pct)%"
        }
    }
}

/// PS5 듀얼센스 패드 상태를 실시간으로 추적하는 네이티브 macOS 컨트롤러 매니저
@MainActor
public final class ControllerManager: ObservableObject {
    public static let shared = ControllerManager()

    @Published public var isConnected: Bool = false
    @Published public var controllerName: String = "패드 대기 중 (PS 버튼을 누르세요)"
    
    // 배터리 상태 (가짜 100% 잔량 방지, 미연결 시 .disconnected)
    @Published public var batteryInfo: ControllerBatteryInfo = .disconnected

    // 아날로그 스틱 & 트리거 (L2/R2)
    @Published public var leftStickX: Float = 0.0
    @Published public var leftStickY: Float = 0.0
    @Published public var rightStickX: Float = 0.0
    @Published public var rightStickY: Float = 0.0
    @Published public var leftTrigger: Float = 0.0
    @Published public var rightTrigger: Float = 0.0

    // 범퍼 버튼 (L1/R1)
    @Published public var buttonL1: Bool = false
    @Published public var buttonR1: Bool = false

    // 액션 버튼 (PS 기호: ✕, ○, □, △)
    @Published public var buttonCross: Bool = false     // ✕
    @Published public var buttonCircle: Bool = false    // ○
    @Published public var buttonSquare: Bool = false    // □
    @Published public var buttonTriangle: Bool = false  // △

    // 방향키 (D-Pad)
    @Published public var dpadUp: Bool = false
    @Published public var dpadDown: Bool = false
    @Published public var dpadLeft: Bool = false
    @Published public var dpadRight: Bool = false

    // 스틱 클릭 (L3/R3)
    @Published public var buttonL3: Bool = false
    @Published public var buttonR3: Bool = false

    // 시스템 및 터치패드
    @Published public var buttonOptions: Bool = false
    @Published public var buttonShare: Bool = false
    @Published public var touchpadPressed: Bool = false
    @Published public var touchpadX: Float = 0.0
    @Published public var touchpadY: Float = 0.0

    // 마이크 음소거 / PTT 버튼
    @Published public var isPttEnabled: Bool = true
    @Published public var buttonMute: Bool = false

    private var activeController: GCController?
    private var batteryTimer: Timer?
    private var hidManager: IOHIDManager?

    public init() {
        GCController.shouldMonitorBackgroundEvents = true
        setupNotifications()
        checkForExistingControllers()
        setupIOHID()

        // 시스템 컨트롤러 열거 지연 대비 추가 재검색
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in
            if self?.activeController == nil {
                self?.checkForExistingControllers()
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in
            if self?.activeController == nil {
                self?.checkForExistingControllers()
            }
        }
    }

    private func setupIOHID() {
        let manager = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
        let match: NSDictionary = [kIOHIDVendorIDKey: 0x054C]
        IOHIDManagerSetDeviceMatching(manager, match as CFDictionary)
        IOHIDManagerScheduleWithRunLoop(manager, CFRunLoopGetMain(), CFRunLoopMode.defaultMode.rawValue)
        let ret = IOHIDManagerOpen(manager, IOOptionBits(kIOHIDOptionsTypeNone))
        if ret == kIOReturnSuccess {
            self.hidManager = manager
            IOHIDManagerRegisterInputValueCallback(manager, { context, result, sender, value in
                let elem = IOHIDValueGetElement(value)
                let page = IOHIDElementGetUsagePage(elem)
                let usage = IOHIDElementGetUsage(elem)
                let intVal = IOHIDValueGetIntegerValue(value)

                // UsagePage 0x09 (Button), Usage 0x0F (DualSense 물리 마이크 음소거 버튼)
                if page == 0x09 && usage == 0x0F {
                    let isPressed = (intVal != 0)
                    Task { @MainActor in
                        ControllerManager.shared.handleMicButton(pressed: isPressed)
                    }
                }
            }, nil)
        }
    }

    private func setupNotifications() {
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(controllerDidConnect),
            name: .GCControllerDidConnect,
            object: nil
        )

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(controllerDidDisconnect),
            name: .GCControllerDidDisconnect,
            object: nil
        )
    }

    private func checkForExistingControllers() {
        if let first = GCController.controllers().first {
            bindController(first)
        } else {
            DispatchQueue.main.async {
                self.isConnected = false
                self.batteryInfo = .disconnected
            }
        }
    }

    @objc private func controllerDidConnect(notification: Notification) {
        if let controller = notification.object as? GCController {
            bindController(controller)
        }
    }

    @objc private func controllerDidDisconnect(notification: Notification) {
        DispatchQueue.main.async {
            self.isConnected = false
            self.controllerName = "패드 연결 해제됨"
            self.batteryInfo = .disconnected
            self.activeController = nil
            self.resetInputStates()
            InputSimulator.shared.releaseAllHeldKeys()
        }
        batteryTimer?.invalidate()
        batteryTimer = nil
    }

    private func bindController(_ controller: GCController) {
        self.activeController = controller

        DispatchQueue.main.async {
            self.isConnected = true
            self.controllerName = controller.vendorName ?? "PS5 DualSense Wireless Controller"
            self.updateBatteryStatus()
        }

        // 주기적 배터리 상태 갱신
        batteryTimer?.invalidate()
        batteryTimer = Timer.scheduledTimer(withTimeInterval: 5.0, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in
                self?.updateBatteryStatus()
            }
        }

        // Apple GameController 프레임워크의 DualSense 확장
        if let dualSense = controller.extendedGamepad as? GCDualSenseGamepad {
            bindDualSense(dualSense)
        } else if let gamepad = controller.extendedGamepad {
            bindExtendedGamepad(gamepad)
        }
    }

    private func updateBatteryStatus() {
        guard isConnected, let controller = activeController else {
            self.batteryInfo = .disconnected
            return
        }

        guard let battery = controller.battery else {
            self.batteryInfo = .unsupported
            return
        }

        let level = battery.batteryLevel
        if level < 0 || battery.batteryState == .unknown {
            self.batteryInfo = .unknown
        } else {
            let pct = Int(round(level * 100))
            let isCharging = (battery.batteryState == .charging)
            self.batteryInfo = .level(percentage: pct, isCharging: isCharging, state: battery.batteryState)
        }
    }

    private func resetInputStates() {
        leftStickX = 0
        leftStickY = 0
        rightStickX = 0
        rightStickY = 0
        leftTrigger = 0
        rightTrigger = 0
        buttonL1 = false
        buttonR1 = false
        buttonL3 = false
        buttonR3 = false
        buttonCross = false
        buttonCircle = false
        buttonSquare = false
        buttonTriangle = false
        dpadUp = false
        dpadDown = false
        dpadLeft = false
        dpadRight = false
        touchpadPressed = false
        touchpadX = 0
        touchpadY = 0
        buttonOptions = false
        buttonShare = false
        buttonMute = false
    }

    private func bindDualSense(_ ds: GCDualSenseGamepad) {
        // 터치패드 물리 클릭 (마우스 좌클릭)
        ds.touchpadButton.pressedChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.touchpadPressed = pressed
                InputSimulator.shared.setLeftClick(isPressed: pressed)
            }
        }

        // 터치패드 주 손가락 이동 -> 마우스 커서 조작
        ds.touchpadPrimary.valueChangedHandler = { [weak self] _, x, y in
            DispatchQueue.main.async {
                self?.touchpadX = x
                self?.touchpadY = y
                InputSimulator.shared.updateTouchpad(x: x, y: y)
            }
        }

        bindExtendedGamepad(ds)
    }

    private func bindExtendedGamepad(_ gp: GCExtendedGamepad) {
        // 초기값 동기화
        DispatchQueue.main.async {
            self.leftStickX = gp.leftThumbstick.xAxis.value
            self.leftStickY = gp.leftThumbstick.yAxis.value
            self.rightStickX = gp.rightThumbstick.xAxis.value
            self.rightStickY = gp.rightThumbstick.yAxis.value
            self.leftTrigger = gp.leftTrigger.value
            self.rightTrigger = gp.rightTrigger.value
            self.buttonL1 = gp.leftShoulder.isPressed
            self.buttonR1 = gp.rightShoulder.isPressed
            self.buttonCross = gp.buttonA.isPressed
            self.buttonCircle = gp.buttonB.isPressed
            self.buttonSquare = gp.buttonX.isPressed
            self.buttonTriangle = gp.buttonY.isPressed
            self.dpadUp = gp.dpad.up.isPressed
            self.dpadDown = gp.dpad.down.isPressed
            self.dpadLeft = gp.dpad.left.isPressed
            self.dpadRight = gp.dpad.right.isPressed
        }

        // 아날로그 스틱 (좌측: WASD 이동)
        gp.leftThumbstick.valueChangedHandler = { [weak self] _, x, y in
            DispatchQueue.main.async {
                self?.leftStickX = x
                self?.leftStickY = y
                InputSimulator.shared.handleLeftStick(x: x, y: y)
            }
        }

        // 아날로그 스틱 (우측: 마우스 시점 연속 이동)
        gp.rightThumbstick.valueChangedHandler = { [weak self] _, x, y in
            DispatchQueue.main.async {
                self?.rightStickX = x
                self?.rightStickY = y
                InputSimulator.shared.handleRightStick(x: x, y: y)
            }
        }

        // 아날로그 트리거 (L2: Shift 모디파이어 / R2: Ctrl 모디파이어)
        gp.leftTrigger.valueChangedHandler = { [weak self] _, val, _ in
            DispatchQueue.main.async {
                self?.leftTrigger = val
                InputSimulator.shared.sendModifier(keyCode: InputSimulator.Keys.shift, mask: .maskShift, isPressed: val > 0.35)
            }
        }

        gp.rightTrigger.valueChangedHandler = { [weak self] _, val, _ in
            DispatchQueue.main.async {
                self?.rightTrigger = val
                InputSimulator.shared.sendModifier(keyCode: InputSimulator.Keys.control, mask: .maskControl, isPressed: val > 0.35)
            }
        }

        // 범퍼 버튼 (L1: Tab 타겟팅 / R1: 3번 스킬 슬롯)
        gp.leftShoulder.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonL1 = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.tab, isPressed: pressed)
            }
        }
        gp.rightShoulder.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonR1 = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.num3, isPressed: pressed)
            }
        }

        // 액션 버튼 (✕: Space 점프, ○: F 상호작용/룻, □: 1번 스킬, △: 2번 스킬)
        gp.buttonA.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonCross = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.space, isPressed: pressed)
            }
        }
        gp.buttonB.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonCircle = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.f, isPressed: pressed)
            }
        }
        gp.buttonX.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonSquare = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.num1, isPressed: pressed)
            }
        }
        gp.buttonY.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonTriangle = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.num2, isPressed: pressed)
            }
        }

        // 방향키 D-Pad (▲: 9번 비상물약, ▼: Z 탈것탑승, ◀: B 가방열기, ▶: M 세계지도)
        gp.dpad.up.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.dpadUp = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.num9, isPressed: pressed)
            }
        }
        gp.dpad.down.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.dpadDown = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.z, isPressed: pressed)
            }
        }
        gp.dpad.left.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.dpadLeft = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.b, isPressed: pressed)
            }
        }
        gp.dpad.right.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.dpadRight = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.m, isPressed: pressed)
            }
        }

        // 스틱 클릭 (L3: NumLock 자동 달리기 / R3: 마우스 우클릭)
        gp.leftThumbstickButton?.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonL3 = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.numLock, isPressed: pressed)
            }
        }
        gp.rightThumbstickButton?.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonR3 = pressed
                InputSimulator.shared.setRightClick(isPressed: pressed)
            }
        }

        // 시스템 버튼 (Share/Options: C 캐릭터 창, Menu/Options: Escape 게임 메뉴)
        gp.buttonOptions?.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonShare = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.c, isPressed: pressed)
            }
        }
        gp.buttonMenu.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.buttonOptions = pressed
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.escape, isPressed: pressed)
            }
        }

        // PS 홈 버튼 (PS 로고 버튼) - 마이크 온오프 / PTT
        gp.buttonHome?.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async {
                self?.handleMicButton(pressed: pressed)
            }
        }
    }

    /// DualSense 마이크 버튼 및 PS 버튼 입력 처리 (PTT 및 토글 지원)
    public func handleMicButton(pressed: Bool) {
        self.buttonMute = pressed
        if isPttEnabled {
            // PTT (Push-To-Talk) 모드:
            // 누르고 있는 동안 마이크 ON (음소거 해제), 손을 떼면 마이크 OFF (음소거)
            VoiceManager.shared.setSystemMicMute(!pressed)
            if pressed {
                if !VoiceManager.shared.isListening && VoiceManager.shared.canStartListening {
                    VoiceManager.shared.startListening()
                }
            } else {
                if VoiceManager.shared.isListening {
                    VoiceManager.shared.stopListening()
                }
            }
        } else {
            // 토글 모드: 버튼을 누르는 순간(down)마다 음소거 ON/OFF 전환
            if pressed {
                VoiceManager.shared.toggleMicMute()
            }
        }
    }

    /// 듀얼센스 햅틱 진동 테스트
    public func triggerHapticRumble() {
        guard let controller = activeController else { return }
        if let haptics = controller.haptics {
            CHHapticEngineHelper.playTransientHaptic(haptics)
        }
    }
}

private class CHHapticEngineHelper {
    static func playTransientHaptic(_ haptics: GCDeviceHaptics) {
        // macOS 햅틱 엔진
    }
}
