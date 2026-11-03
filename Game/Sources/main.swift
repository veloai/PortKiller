import AppKit
import SwiftUI
import GameController

// WoW 등 다른 게임 창이 활성화되어 있을 때도 패드 입력 백그라운드 수신 보장
GCController.shouldMonitorBackgroundEvents = true

final class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // 1. macOS 상태창(메뉴바) 도트 스타일 펫 즉시 기동 및 로밍 시작
        MenuBarPetController.shared.start()

        // 2. 메인 스튜디오 창 준비 (상태창 팝오버에서 '스튜디오 열기'로 표시 가능)
        let contentView = ContentView()

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 1100, height: 720),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.center()
        window.title = "PortKiller Gamepad & Voice Studio (macOS Native)"
        window.contentView = NSHostingView(rootView: contentView)
        self.window = window

        // 화면 스냅샷 자동 생성 (상태창 도트 펫 포함)
        savePreview()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        // 스튜디오 창을 닫아도 상태창 도트 펫은 메뉴바에서 계속 상주
        return false
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.regular)
let delegate = AppDelegate()
app.delegate = delegate
app.run()
