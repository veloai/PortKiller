import Foundation
import ApplicationServices
import AppKit

/// macOS 저수준 가상 키보드 및 마우스 이벤트 주입기
/// PS5 듀얼센스 패드 입력을 macOS 가상 키보드(Space, Tab, WASD, 1~9, Shift 등) 및 마우스 이벤트로 실시간 변환
@MainActor
public final class InputSimulator: ObservableObject {
    public static let shared = InputSimulator()

    @Published public var isEnabled: Bool = true
    @Published public var targetOnlyWoW: Bool = false
    @Published public var enableStickWASD: Bool = true
    @Published public var stickDeadzone: Float = 0.22
    @Published public var mouseSpeed: Float = 14.0
    @Published public var touchpadMultiplier: Float = 450.0

    private let eventSource = CGEventSource(stateID: .hidSystemState)

    // 현재 눌려 있는 키 및 모디파이어 플래그 추적 (중복 발송 방지 및 완전한 릴리즈 보장)
    private var heldKeys = Set<CGKeyCode>()
    private var activeModifiers: CGEventFlags = []
    private var isLeftMouseDown: Bool = false
    private var isRightMouseDown: Bool = false

    // 아날로그 스틱 WASD 상태
    private var isWDown = false
    private var isSDown = false
    private var isADown = false
    private var isDDown = false

    // 우측 스틱 마우스 연속 이동 타이머
    private var rightStickX: Float = 0.0
    private var rightStickY: Float = 0.0
    private var stickMouseTimer: Timer?

    // 터치패드 상태
    private var lastTouchX: Float?
    private var lastTouchY: Float?
    private var lastTouchTimestamp: TimeInterval = 0
    private var smoothedDx: CGFloat = 0.0
    private var smoothedDy: CGFloat = 0.0

    // MARK: - Virtual Key Codes (macOS Carbon HIToolbox)
    public struct Keys {
        public static let w: CGKeyCode = 13
        public static let a: CGKeyCode = 0
        public static let s: CGKeyCode = 1
        public static let d: CGKeyCode = 2

        public static let space: CGKeyCode = 49
        public static let tab: CGKeyCode = 48
        public static let escape: CGKeyCode = 53
        public static let numLock: CGKeyCode = 71 // Clear / NumLock (WoW 기본 자동 달리기)

        public static let f: CGKeyCode = 3  // 상호작용 / 룻
        public static let b: CGKeyCode = 11 // 모든 가방 열기
        public static let m: CGKeyCode = 46 // 세계 지도
        public static let z: CGKeyCode = 6  // 탈것 탑승
        public static let c: CGKeyCode = 8  // 캐릭터 정보
        public static let t: CGKeyCode = 17

        public static let num1: CGKeyCode = 18 // 스킬 1
        public static let num2: CGKeyCode = 19 // 스킬 2
        public static let num3: CGKeyCode = 20 // 스킬 3
        public static let num4: CGKeyCode = 21 // 스킬 4
        public static let num5: CGKeyCode = 23
        public static let num6: CGKeyCode = 22
        public static let num7: CGKeyCode = 26
        public static let num8: CGKeyCode = 28
        public static let num9: CGKeyCode = 25 // 비상 물약
        public static let num0: CGKeyCode = 29

        public static let shift: CGKeyCode = 56   // L2 보조 모디파이어
        public static let control: CGKeyCode = 59 // R2 보조 모디파이어
    }

    @Published public var isAccessibilityTrusted: Bool = AXIsProcessTrusted()

    private init() {
        promptAccessibilityIfNeeded()
        Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in
                guard let self = self else { return }
                let current = AXIsProcessTrusted()
                if self.isAccessibilityTrusted != current {
                    self.isAccessibilityTrusted = current
                }
            }
        }
    }

    public func promptAccessibilityIfNeeded() {
        let promptKey = "AXTrustedCheckOptionPrompt" as CFString
        let options = [promptKey: true] as CFDictionary
        isAccessibilityTrusted = AXIsProcessTrustedWithOptions(options)
    }

    // MARK: - 게임 프로세스 활성화 검증
    public func canSendInput() -> Bool {
        guard isEnabled else { return false }
        if !targetOnlyWoW { return true }

        if let frontApp = NSWorkspace.shared.frontmostApplication {
            let bundleId = frontApp.bundleIdentifier?.lowercased() ?? ""
            let name = frontApp.localizedName?.lowercased() ?? ""
            if bundleId.contains("warcraft") || name.contains("warcraft") || name.contains("wow") {
                return true
            }
        }
        return false
    }

    // MARK: - 키보드 이벤트 주입
    public func sendKey(keyCode: CGKeyCode, isPressed: Bool) {
        guard canSendInput() else {
            if !isPressed && heldKeys.contains(keyCode) {
                forceKeyUp(keyCode: keyCode)
            }
            return
        }

        if isPressed {
            if !heldKeys.contains(keyCode) {
                heldKeys.insert(keyCode)
                postKeyEvent(keyCode: keyCode, keyDown: true)
            }
        } else {
            if heldKeys.contains(keyCode) {
                heldKeys.remove(keyCode)
                postKeyEvent(keyCode: keyCode, keyDown: false)
            }
        }
    }

    private func forceKeyUp(keyCode: CGKeyCode) {
        heldKeys.remove(keyCode)
        postKeyEvent(keyCode: keyCode, keyDown: false)
    }

    private func postKeyEvent(keyCode: CGKeyCode, keyDown: Bool) {
        guard let event = CGEvent(keyboardEventSource: eventSource, virtualKey: keyCode, keyDown: keyDown) else { return }
        if !activeModifiers.isEmpty {
            event.flags.formUnion(activeModifiers)
        }
        event.post(tap: .cghidEventTap)
    }

    // MARK: - 모디파이어 키 (Shift / Ctrl) 주입
    public func sendModifier(keyCode: CGKeyCode, mask: CGEventFlags, isPressed: Bool) {
        guard canSendInput() else {
            if !isPressed && activeModifiers.contains(mask) {
                activeModifiers.remove(mask)
                heldKeys.remove(keyCode)
                postModifierEvent(keyCode: keyCode, keyDown: false)
            }
            return
        }

        if isPressed {
            if !activeModifiers.contains(mask) {
                activeModifiers.insert(mask)
                heldKeys.insert(keyCode)
                postModifierEvent(keyCode: keyCode, keyDown: true)
            }
        } else {
            if activeModifiers.contains(mask) {
                activeModifiers.remove(mask)
                heldKeys.remove(keyCode)
                postModifierEvent(keyCode: keyCode, keyDown: false)
            }
        }
    }

    private func postModifierEvent(keyCode: CGKeyCode, keyDown: Bool) {
        guard let event = CGEvent(keyboardEventSource: eventSource, virtualKey: keyCode, keyDown: keyDown) else { return }
        event.flags = activeModifiers
        event.post(tap: .cghidEventTap)
    }

    // MARK: - 스틱 이동 -> WASD 변환 (대각선 동시 입력 지원)
    public func handleLeftStick(x: Float, y: Float) {
        guard canSendInput() && enableStickWASD else {
            releaseWASD()
            return
        }

        let dz = stickDeadzone

        // 전진/후진 (Y축)
        if y > dz {
            if !isWDown { sendKey(keyCode: Keys.w, isPressed: true); isWDown = true }
            if isSDown { sendKey(keyCode: Keys.s, isPressed: false); isSDown = false }
        } else if y < -dz {
            if !isSDown { sendKey(keyCode: Keys.s, isPressed: true); isSDown = true }
            if isWDown { sendKey(keyCode: Keys.w, isPressed: false); isWDown = false }
        } else {
            if isWDown { sendKey(keyCode: Keys.w, isPressed: false); isWDown = false }
            if isSDown { sendKey(keyCode: Keys.s, isPressed: false); isSDown = false }
        }

        // 좌/우 이동 (X축)
        if x < -dz {
            if !isADown { sendKey(keyCode: Keys.a, isPressed: true); isADown = true }
            if isDDown { sendKey(keyCode: Keys.d, isPressed: false); isDDown = false }
        } else if x > dz {
            if !isDDown { sendKey(keyCode: Keys.d, isPressed: true); isDDown = true }
            if isADown { sendKey(keyCode: Keys.a, isPressed: false); isADown = false }
        } else {
            if isADown { sendKey(keyCode: Keys.a, isPressed: false); isADown = false }
            if isDDown { sendKey(keyCode: Keys.d, isPressed: false); isDDown = false }
        }
    }

    public func releaseWASD() {
        if isWDown { sendKey(keyCode: Keys.w, isPressed: false); isWDown = false }
        if isSDown { sendKey(keyCode: Keys.s, isPressed: false); isSDown = false }
        if isADown { sendKey(keyCode: Keys.a, isPressed: false); isADown = false }
        if isDDown { sendKey(keyCode: Keys.d, isPressed: false); isDDown = false }
    }

    // MARK: - 우측 스틱 -> 마우스 부드러운 연속 이동 (시점 제어)
    public func handleRightStick(x: Float, y: Float) {
        self.rightStickX = x
        self.rightStickY = y

        let dz: Float = 0.12
        let isOutside = abs(x) > dz || abs(y) > dz

        if isOutside {
            if stickMouseTimer == nil {
                startStickMouseTimer()
            }
        } else {
            stopStickMouseTimer()
        }
    }

    private func startStickMouseTimer() {
        stickMouseTimer?.invalidate()
        stickMouseTimer = Timer.scheduledTimer(withTimeInterval: 0.016, repeats: true) { [weak self] _ in
            Task { @MainActor [weak self] in
                self?.tickStickMouse()
            }
        }
    }

    private func stopStickMouseTimer() {
        stickMouseTimer?.invalidate()
        stickMouseTimer = nil
    }

    private func tickStickMouse() {
        guard canSendInput() else { return }
        let dz: Float = 0.12
        var vx = rightStickX
        var vy = rightStickY

        if abs(vx) <= dz { vx = 0 }
        if abs(vy) <= dz { vy = 0 }

        guard vx != 0 || vy != 0 else {
            stopStickMouseTimer()
            return
        }

        // 2차 가속 커브 (미세 조준 + 빠른 회전 지원)
        let signX: Float = vx >= 0 ? 1 : -1
        let signY: Float = vy >= 0 ? 1 : -1
        let magX = (abs(vx) - dz) / (1.0 - dz)
        let magY = (abs(vy) - dz) / (1.0 - dz)
        let speedX = signX * (magX * magX) * self.mouseSpeed
        let speedY = signY * (magY * magY) * self.mouseSpeed

        let dx = CGFloat(speedX)
        let dy = CGFloat(-speedY) // 화면 좌표계 Y 반전

        self.moveMouseRelative(dx: dx, dy: dy)
    }

    // MARK: - 터치패드 마우스 이동 및 클릭 (순수 1:1 x, y 추적)
    public func updateTouchpad(x: Float, y: Float) {
        guard canSendInput() else {
            lastTouchX = nil
            lastTouchY = nil
            return
        }

        // 손가락을 뗐을 때 앵커 초기화
        if abs(x) < 0.0001 && abs(y) < 0.0001 {
            if let px = lastTouchX, let py = lastTouchY, hypot(px, py) > 0.05 {
                lastTouchX = nil
                lastTouchY = nil
            }
            return
        }

        let now = ProcessInfo.processInfo.systemUptime

        // 첫 터치 시작이거나 0.25초 이상 입력이 없었던 경우 새 기준점 등록
        guard let prevX = lastTouchX, let prevY = lastTouchY, (now - lastTouchTimestamp) < 0.25 else {
            lastTouchX = x
            lastTouchY = y
            lastTouchTimestamp = now
            return
        }

        let rawDx = x - prevX
        let rawDy = y - prevY
        let dist = hypot(rawDx, rawDy)

        // 정지 상태 미세 센서 노이즈 무시
        if dist < 0.0006 {
            lastTouchTimestamp = now
            return
        }

        // 손가락을 뗐다가 다른 곳을 터치했을 때의 순간 점프 방지
        if abs(rawDx) > 0.12 || abs(rawDy) > 0.12 || dist > 0.15 {
            lastTouchX = x
            lastTouchY = y
            lastTouchTimestamp = now
            return
        }

        // 순수 1:1 x, y 좌표 이동 (왜곡 없이 손가락 방향 그대로)
        let dx = CGFloat(rawDx * touchpadMultiplier)
        let dy = CGFloat(-rawDy * touchpadMultiplier) // Y축 반전

        moveMouseRelative(dx: dx, dy: dy)

        lastTouchX = x
        lastTouchY = y
        lastTouchTimestamp = now
    }

    public func resetTouchpadAnchor() {
        lastTouchX = nil
        lastTouchY = nil
        smoothedDx = 0
        smoothedDy = 0
    }

    // 직전에 주입한 커서 위치 (X/Y 축 덮어쓰기 방지 및 정지 후 재출발 시 점프 방지)
    private var lastPostedPos: CGPoint?
    private var lastPostTime: TimeInterval = 0

    private func desktopBounds() -> CGRect {
        var count: UInt32 = 0
        CGGetActiveDisplayList(0, nil, &count)
        var ids = [CGDirectDisplayID](repeating: 0, count: Int(max(count, 1)))
        CGGetActiveDisplayList(count, &ids, &count)
        var rect = CGRect.null
        for id in ids.prefix(Int(count)) { rect = rect.union(CGDisplayBounds(id)) }
        return rect.isNull ? CGDisplayBounds(CGMainDisplayID()) : rect
    }

    public func moveMouseRelative(dx: CGFloat, dy: CGFloat) {
        guard abs(dx) > 0.01 || abs(dy) > 0.01 else { return }

        let now = ProcessInfo.processInfo.systemUptime
        let sysPos = CGEvent(source: nil)?.location ?? .zero

        // 대각선 이동 완벽 지원:
        // DualSense는 대각선 조작 시 X축과 Y축 변화를 아주 짧은 간격(마이크로초 단위)으로 연속 전달합니다.
        // WindowServer의 실제 커서 좌표 갱신은 비동기이므로, 연속된 이벤트 사이에서 sysPos를 매번 읽으면
        // 방금 반영된 직전 축(X 또는 Y)의 이동량이 상쇄되어 대각선 이동이 불가능해집니다.
        // 따라서 이전 주입 후 0.25초 이내이거나, 외부 마우스로 15px 이상 크게 이동하지 않은 경우에는
        // 직전에 주입한 위치(lastPostedPos)를 안정된 기준점으로 유지합니다.
        let base: CGPoint
        if let last = lastPostedPos {
            let externalMovedDist = hypot(sysPos.x - last.x, sysPos.y - last.y)
            if (now - lastPostTime) > 0.25 && externalMovedDist > 15.0 {
                // 사용자가 패드 외의 실제 물리 마우스로 커서를 옮긴 경우
                base = sysPos
            } else {
                base = last
            }
        } else {
            base = sysPos
        }

        let bounds = desktopBounds()
        let newPos = CGPoint(
            x: min(max(base.x + dx, bounds.minX), bounds.maxX - 1),
            y: min(max(base.y + dy, bounds.minY), bounds.maxY - 1)
        )
        lastPostedPos = newPos
        lastPostTime = now

        let eventType: CGEventType
        let mouseButton: CGMouseButton
        if isLeftMouseDown {
            eventType = .leftMouseDragged
            mouseButton = .left
        } else if isRightMouseDown {
            eventType = .rightMouseDragged
            mouseButton = .right
        } else {
            eventType = .mouseMoved
            mouseButton = .left
        }

        // WindowServer에 하드웨어 HID 마우스 이벤트 주입 (호버 상태, 툴팁, 커서 이동 완전 지원)
        if let moveEv = CGEvent(mouseEventSource: eventSource, mouseType: eventType, mouseCursorPosition: newPos, mouseButton: mouseButton) {
            moveEv.setDoubleValueField(.mouseEventDeltaX, value: Double(dx))
            moveEv.setDoubleValueField(.mouseEventDeltaY, value: Double(dy))
            moveEv.post(tap: .cghidEventTap)
        }
    }

    public func setLeftClick(isPressed: Bool) {
        guard canSendInput() else { return }
        if isPressed != isLeftMouseDown {
            isLeftMouseDown = isPressed
            let pos = lastPostedPos ?? CGEvent(source: nil)?.location ?? .zero
            let type: CGEventType = isPressed ? .leftMouseDown : .leftMouseUp
            if let ev = CGEvent(mouseEventSource: eventSource, mouseType: type, mouseCursorPosition: pos, mouseButton: .left) {
                ev.setIntegerValueField(.mouseEventClickState, value: 1)
                ev.post(tap: .cghidEventTap)
            }
        }
    }

    public func setRightClick(isPressed: Bool) {
        guard canSendInput() else { return }
        if isPressed != isRightMouseDown {
            isRightMouseDown = isPressed
            let pos = lastPostedPos ?? CGEvent(source: nil)?.location ?? .zero
            let type: CGEventType = isPressed ? .rightMouseDown : .rightMouseUp
            if let ev = CGEvent(mouseEventSource: eventSource, mouseType: type, mouseCursorPosition: pos, mouseButton: .right) {
                ev.setIntegerValueField(.mouseEventClickState, value: 1)
                ev.post(tap: .cghidEventTap)
            }
        }
    }

    // MARK: - 안전 초기화 (패드 분리나 포커스 전환 시 잔류 키 해제)
    public func releaseAllHeldKeys() {
        stopStickMouseTimer()
        releaseWASD()
        for key in heldKeys {
            postKeyEvent(keyCode: key, keyDown: false)
        }
        heldKeys.removeAll()
        activeModifiers = []

        if isLeftMouseDown {
            setLeftClick(isPressed: false)
        }
        if isRightMouseDown {
            setRightClick(isPressed: false)
        }
        resetTouchpadAnchor()
    }
}
