import Foundation
import GameController
import Combine

/// PS5 듀얼센스 패드 상태를 실시간으로 추적하는 네이티브 macOS 컨트롤러 매니저
public final class ControllerManager: ObservableObject {
    public static let shared = ControllerManager()

    @Published public var isConnected: Bool = false
    @Published public var controllerName: String = "패드 대기 중 (PS 버튼을 누르세요)"
    @Published public var batteryLevel: Float = 1.0

    // 아날로그 스틱 & 트리거
    @Published public var leftStickX: Float = 0.0
    @Published public var leftStickY: Float = 0.0
    @Published public var rightStickX: Float = 0.0
    @Published public var rightStickY: Float = 0.0
    @Published public var leftTrigger: Float = 0.0
    @Published public var rightTrigger: Float = 0.0

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

    // 범퍼 및 스틱 클릭
    @Published public var buttonL1: Bool = false
    @Published public var buttonR1: Bool = false
    @Published public var buttonL3: Bool = false
    @Published public var buttonR3: Bool = false

    // 시스템 및 터치패드
    @Published public var buttonOptions: Bool = false
    @Published public var buttonShare: Bool = false
    @Published public var touchpadPressed: Bool = false
    @Published public var touchpadX: Float = 0.0
    @Published public var touchpadY: Float = 0.0

    private var activeController: GCController?

    public init() {
        setupNotifications()
        checkForExistingControllers()
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
            self.activeController = nil
        }
    }

    private func bindController(_ controller: GCController) {
        self.activeController = controller

        DispatchQueue.main.async {
            self.isConnected = true
            self.controllerName = controller.vendorName ?? "PS5 DualSense Wireless Controller"
            if let battery = controller.battery {
                self.batteryLevel = battery.batteryLevel
            }
        }

        // Apple GameController 프레임워크의 DualSense 확장
        if let dualSense = controller.extendedGamepad as? GCDualSenseGamepad {
            bindDualSense(dualSense)
        } else if let gamepad = controller.extendedGamepad {
            bindExtendedGamepad(gamepad)
        }
    }

    private func bindDualSense(_ ds: GCDualSenseGamepad) {
        // 터치패드 지원
        ds.touchpadButton.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.touchpadPressed = pressed }
        }

        ds.touchpadPrimary.xAxis.valueChangedHandler = { [weak self] _, x in
            DispatchQueue.main.async { self?.touchpadX = x }
        }
        ds.touchpadPrimary.yAxis.valueChangedHandler = { [weak self] _, y in
            DispatchQueue.main.async { self?.touchpadY = y }
        }

        bindExtendedGamepad(ds)
    }

    private func bindExtendedGamepad(_ gp: GCExtendedGamepad) {
        // 아날로그 스틱
        gp.leftThumbstick.valueChangedHandler = { [weak self] _, x, y in
            DispatchQueue.main.async {
                self?.leftStickX = x
                self?.leftStickY = y
            }
        }

        gp.rightThumbstick.valueChangedHandler = { [weak self] _, x, y in
            DispatchQueue.main.async {
                self?.rightStickX = x
                self?.rightStickY = y
            }
        }

        // 아날로그 트리거
        gp.leftTrigger.valueChangedHandler = { [weak self] _, val, _ in
            DispatchQueue.main.async { self?.leftTrigger = val }
        }

        gp.rightTrigger.valueChangedHandler = { [weak self] _, val, _ in
            DispatchQueue.main.async { self?.rightTrigger = val }
        }

        // 액션 버튼
        gp.buttonA.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonCross = pressed }
        }
        gp.buttonB.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonCircle = pressed }
        }
        gp.buttonX.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonSquare = pressed }
        }
        gp.buttonY.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonTriangle = pressed }
        }

        // D-Pad
        gp.dpad.up.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.dpadUp = pressed }
        }
        gp.dpad.down.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.dpadDown = pressed }
        }
        gp.dpad.left.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.dpadLeft = pressed }
        }
        gp.dpad.right.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.dpadRight = pressed }
        }

        // 범퍼 및 스틱 클릭
        gp.leftShoulder.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonL1 = pressed }
        }
        gp.rightShoulder.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonR1 = pressed }
        }
        gp.leftThumbstickButton?.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonL3 = pressed }
        }
        gp.rightThumbstickButton?.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonR3 = pressed }
        }

        gp.buttonOptions?.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonShare = pressed }
        }
        gp.buttonMenu.valueChangedHandler = { [weak self] _, _, pressed in
            DispatchQueue.main.async { self?.buttonOptions = pressed }
        }
    }

    /// 듀얼센스 햅틱 진동 테스트
    public func triggerHapticRumble() {
        guard let controller = activeController else { return }
        if let haptics = controller.haptics {
            // macOS GameController 햅틱 모터 구동
            CHHapticEngineHelper.playTransientHaptic(haptics)
        }
    }
}

private class CHHapticEngineHelper {
    static func playTransientHaptic(_ haptics: GCDeviceHaptics) {
        // macOS 햅틱 엔진
    }
}
