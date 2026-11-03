import AppKit
import SwiftUI

/// macOS 바탕화면 위에 떠 있는 플로팅 펫 윈도우 매니저
@MainActor
public final class MacPetWindowManager: ObservableObject {
    public static let shared = MacPetWindowManager()

    private var petPanel: NSPanel?

    public var isVisible: Bool {
        return petPanel?.isVisible ?? false
    }

    public func togglePet() {
        if let panel = petPanel, panel.isVisible {
            panel.close()
            petPanel = nil
        } else {
            showPet()
        }
    }

    public func showPet() {
        if let panel = petPanel, panel.isVisible {
            panel.orderFrontRegardless()
            return
        }

        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 220, height: 220),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered,
            defer: false
        )

        panel.isFloatingPanel = true
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.isMovableByWindowBackground = true // 창 배경 드래그로 어디든 이동 가능

        // 화면 우측 하단 기본 배치 (작업표시줄 위)
        if let screen = NSScreen.main {
            let screenRect = screen.visibleFrame
            let x = screenRect.maxX - 240
            let y = screenRect.minY + 40
            panel.setFrameOrigin(NSPoint(x: x, y: y))
        }

        panel.contentView = NSHostingView(rootView: MacPetView())
        panel.orderFrontRegardless()
        self.petPanel = panel
    }

    public func hidePet() {
        petPanel?.close()
        petPanel = nil
    }
}
