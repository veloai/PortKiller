import AppKit
import SwiftUI
import GameController

// WoW 등 다른 게임 창이 활성화되어 있을 때도 패드 입력 백그라운드 수신 보장
GCController.shouldMonitorBackgroundEvents = true

final class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
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
        window.orderFrontRegardless()
        window.makeKeyAndOrderFront(nil)
        self.window = window

        NSApp.activate(ignoringOtherApps: true)

        // 화면 스냅샷 자동 생성 (사용자 채팅창 미리보기용)
        savePreview()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        return true
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.regular)
let delegate = AppDelegate()
app.delegate = delegate
app.run()
