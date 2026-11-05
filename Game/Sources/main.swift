import AppKit
import SwiftUI
import RecoveryShared

final class AppDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // 1. macOS 상태창(메뉴바) 도트 스타일 펫 즉시 기동 및 로밍 시작
        MenuBarPetController.shared.start()

        // Introduce the installed recovery feature once; later launches stay quiet.
        // Never capture the user's desktop automatically.
        let introductionKey = "pkp.recovery.introduction.v1"
        if FileManager.default.fileExists(atPath: RecoveryWire.directory.appendingPathComponent("policy.json").path),
           !UserDefaults.standard.bool(forKey: introductionKey) {
            RecoveryClient.shared.open()
            UserDefaults.standard.set(true, forKey: introductionKey)
        }
    }

    // Optional game input and voice managers are created only after this user action.
    // Building an invisible ContentView at startup used to request Mac control access.
    @MainActor func showStudio() {
        if window == nil {
            let window = NSWindow(
                contentRect: NSRect(x: 100, y: 100, width: 1100, height: 720),
                styleMask: [.titled, .closable, .miniaturizable, .resizable],
                backing: .buffered, defer: false
            )
            window.center()
            window.title = "PortKiller Gamepad & Voice Studio (macOS Native)"
            window.isReleasedWhenClosed = false
            window.contentView = NSHostingView(rootView: ContentView())
            self.window = window
        }
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        RecoveryClient.shared.open()
        return true
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
