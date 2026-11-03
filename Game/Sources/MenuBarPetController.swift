import AppKit
import SwiftUI

/// macOS 상태창(메뉴바) 안에서만 돌아다니는 도트 스타일 펫 컨트롤러
@MainActor
public final class MenuBarPetController: NSObject, ObservableObject {
    public static let shared = MenuBarPetController()

    private var statusItem: NSStatusItem?
    private var popover: NSPopover?
    private var timer: Timer?

    // 걷기 & 로밍 상태
    private var posX: CGFloat = 0.0
    private var isFacingRight: Bool = true
    private var roamState: RoamState = .walking
    private var stateCountdown: Int = 0
    private var walkFrameIndex: Int = 0

    private let barWidth: CGFloat = 64.0
    private let barHeight: CGFloat = 22.0
    private let spriteSize: CGFloat = 20.0
    private var maxX: CGFloat { barWidth - spriteSize - 2 }

    enum RoamState {
        case walking
        case idle
        case happy
        case napping
    }

    override private init() {
        super.init()
    }

    public func start() {
        guard statusItem == nil else { return }

        // 상태창 아이템 생성 (너비 64pt로 메뉴바 안에서 좌우로 걸어다닐 공간 확보)
        let item = NSStatusBar.system.statusItem(withLength: barWidth)
        item.button?.target = self
        item.button?.action = #selector(onStatusItemClicked)
        item.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
        self.statusItem = item

        // 팝오버 초기화
        let pop = NSPopover()
        pop.behavior = .transient
        pop.contentViewController = NSHostingController(rootView: MenuBarPetPopoverView())
        self.popover = pop

        // 100ms (10 FPS) 로밍 애니메이션 타이머
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.tick()
            }
        }

        render()
    }

    public func stop() {
        timer?.invalidate()
        timer = nil
        if let item = statusItem {
            NSStatusBar.system.removeStatusItem(item)
            statusItem = nil
        }
    }

    // MARK: - 로밍 틱 루프 (상태창 내부 왕복 및 도트 애니메이션)

    private func tick() {
        switch roamState {
        case .walking:
            walkFrameIndex += 1
            let speed: CGFloat = 1.4

            if isFacingRight {
                posX += speed
                if posX >= maxX {
                    posX = maxX
                    isFacingRight = false
                    enterIdleOrAction()
                }
            } else {
                posX -= speed
                if posX <= 2 {
                    posX = 2
                    isFacingRight = true
                    enterIdleOrAction()
                }
            }

        case .idle, .happy, .napping:
            stateCountdown -= 1
            if stateCountdown <= 0 {
                roamState = .walking
            }
        }

        render()
    }

    private func enterIdleOrAction() {
        let rand = Int.random(in: 0...10)
        if rand < 6 {
            roamState = .idle
            stateCountdown = Int.random(in: 15...30) // 1.5초 ~ 3초 멈춰서 두리번거리기
        } else if rand < 9 {
            roamState = .happy
            stateCountdown = 18 // 기분 좋게 통통 뛰기
        } else {
            roamState = .napping
            stateCountdown = 35 // 잠깐 낮잠 zZ
        }
    }

    // MARK: - 도트 스프라이트 렌더링

    public func generateBarImage() -> NSImage {
        let image = NSImage(size: NSSize(width: barWidth, height: barHeight))
        image.lockFocus()

        // 픽셀 보간 없이 레트로 도트 스타일 유지
        NSGraphicsContext.current?.imageInterpolation = .none

        let poseName: String
        var yOffset: CGFloat = 1.0

        switch roamState {
        case .walking:
            poseName = (walkFrameIndex % 4 < 2) ? "walk1" : "walk2"
        case .idle:
            poseName = "idle"
        case .happy:
            poseName = "happy"
            // 통통 뛰는 점프 모션 (사인파)
            yOffset += CGFloat(sin(Double(walkFrameIndex) * 0.8)) * 2.5
        case .napping:
            poseName = "sleep"
        }

        if let sprite = loadRawSprite(pose: poseName) {
            let context = NSGraphicsContext.current?.cgContext
            context?.saveGState()

            if !isFacingRight {
                // 좌측으로 이동할 때 스프라이트 좌우 반전
                let transform = NSAffineTransform()
                transform.translateX(by: posX + spriteSize, yBy: 0)
                transform.scaleX(by: -1, yBy: 1)
                transform.concat()
                sprite.draw(in: NSRect(x: 0, y: yOffset, width: spriteSize, height: spriteSize))
            } else {
                sprite.draw(in: NSRect(x: posX, y: yOffset, width: spriteSize, height: spriteSize))
            }

            context?.restoreGState()
        }

        // 낮잠 잘 때는 zZ 기호 도트 표시
        if roamState == .napping {
            let attr: [NSAttributedString.Key: Any] = [
                .font: NSFont.systemFont(ofSize: 8, weight: .bold),
                .foregroundColor: NSColor.systemCyan
            ]
            let zString = (stateCountdown % 10 < 5) ? "z" : "Z"
            (zString as NSString).draw(at: NSPoint(x: posX + (isFacingRight ? 16 : -8), y: 10), withAttributes: attr)
        }

        image.unlockFocus()
        return image
    }

    private func render() {
        guard let button = statusItem?.button else { return }
        let img = generateBarImage()
        button.image = img
    }

    private func loadRawSprite(pose: String) -> NSImage? {
        let candidateDirs = [
            "assets/sprites/duck",
            "../assets/sprites/duck",
            "/Users/aiso/Monster/github/PortKiller/assets/sprites/duck",
            Bundle.main.resourcePath.map { "\($0)/assets/sprites/duck" } ?? "",
            "assets/sprites/chars/day/child",
            "../assets/sprites/chars/day/child",
            "/Users/aiso/Monster/github/PortKiller/assets/sprites/chars/day/child"
        ]
        for dir in candidateDirs where !dir.isEmpty {
            let path = "\(dir)/\(pose).png"
            if FileManager.default.fileExists(atPath: path), let img = NSImage(contentsOfFile: path) {
                return img
            }
        }
        return nil
    }

    // MARK: - 메뉴바 클릭 시 팝오버 토글

    @objc private func onStatusItemClicked() {
        guard let button = statusItem?.button, let pop = popover else { return }

        if pop.isShown {
            pop.performClose(nil)
        } else {
            // 클릭 시 가볍게 통통 뛰며 인사
            roamState = .happy
            stateCountdown = 20

            pop.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        }
    }
}

// MARK: - 상태창 펫 클릭 시 열리는 상세 팝오버 뷰 (다마고치 요소 제거 - 클린 상태창)
struct MenuBarPetPopoverView: View {
    @ObservedObject var state = MacPetState.shared

    var body: some View {
        VStack(spacing: 12) {
            // 상단 펫 프로필 & 말풍선
            HStack(spacing: 12) {
                if let sprite = state.resolveCurrentSprite() {
                    Image(nsImage: sprite)
                        .interpolation(.none)
                        .resizable()
                        .scaledToFit()
                        .frame(width: 48, height: 48)
                        .background(Color(red: 0.12, green: 0.15, blue: 0.20))
                        .cornerRadius(10)
                }

                VStack(alignment: .leading, spacing: 3) {
                    Text("🦆 디버깅 러버덕 (mac-air)")
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(.primary)

                    Text(state.speechMessage ?? state.devStatusSummary)
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                        .lineLimit(2)
                }
            }

            Divider()

            // 개발 환경 & 시스템 정보
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("💻 ALFA 환경").font(.system(size: 10)).foregroundColor(.secondary)
                    Text("정상 가동").font(.system(size: 12, weight: .bold)).foregroundColor(.green)
                }
                Spacer()
                VStack(alignment: .trailing, spacing: 2) {
                    Text("기기").font(.system(size: 10)).foregroundColor(.secondary)
                    Text("mac-air (M5)").font(.system(size: 12, weight: .semibold)).foregroundColor(.primary)
                }
            }
            .padding(.vertical, 6)
            .padding(.horizontal, 10)
            .background(Color(NSColor.controlBackgroundColor))
            .cornerRadius(8)

            Divider()

            // 액션 버튼 모음 (다마고치 버튼 제거 -> 브리핑 & 뷰 토글)
            VStack(spacing: 6) {
                Button(action: { state.briefing() }) {
                    Label("환경 브리핑 새로고침", systemImage: "chart.bar.fill")
                        .font(.system(size: 11))
                        .frame(maxWidth: .infinity)
                }

                Button(action: {
                    MacPetWindowManager.shared.togglePet()
                }) {
                    Label(MacPetWindowManager.shared.isVisible ? "바탕화면 숨기기" : "바탕화면 꺼내기", systemImage: "display")
                        .font(.system(size: 11))
                        .frame(maxWidth: .infinity)
                }
            }

            Divider()

            // 하단 조작
            HStack {
                Button("스튜디오 열기") {
                    NSApp.windows.first?.makeKeyAndOrderFront(nil)
                    NSApp.activate(ignoringOtherApps: true)
                }
                .font(.system(size: 10))

                Spacer()

                Button("종료") {
                    NSApp.terminate(nil)
                }
                .font(.system(size: 10))
                .foregroundColor(.red)
            }
        }
        .padding(14)
        .frame(width: 240)
    }
}
