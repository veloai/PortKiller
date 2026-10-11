import AppKit
import SwiftUI

/// macOS 최상위 플로팅 반투명 HUD 창 매니저
public final class OverlayWindowManager: ObservableObject {
    public static let shared = OverlayWindowManager()

    private var overlayPanel: NSPanel?

    public func toggleOverlay() {
        if let panel = overlayPanel, panel.isVisible {
            panel.close()
            overlayPanel = nil
        } else {
            showOverlay()
        }
    }

    public func showOverlay() {
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 380, height: 75),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered,
            defer: false
        )

        panel.isFloatingPanel = true
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.isMovableByWindowBackground = true

        // 화면 상단 중앙 배치
        if let screen = NSScreen.main {
            let screenRect = screen.visibleFrame
            let x = screenRect.midX - 190
            let y = screenRect.maxY - 95
            panel.setFrameOrigin(NSPoint(x: x, y: y))
        }

        panel.contentView = NSHostingView(rootView: OverlayHudView())
        panel.orderFrontRegardless()
        self.overlayPanel = panel
    }
}

public struct OverlayHudView: View {
    @ObservedObject var controller = ControllerManager.shared
    @ObservedObject var voice = VoiceManager.shared

    public var body: some View {
        HStack(spacing: 12) {
            // 패드 연결 & 배터리
            HStack(spacing: 6) {
                Text("🎮").font(.body)
                Text(controller.isConnected ? "PS5 듀얼센스" : "패드 대기")
                    .font(.caption).bold().foregroundColor(.white)
                Text("(\(Int(controller.batteryLevel * 100))%)")
                    .font(.caption2).foregroundColor(.green)
            }

            Divider().frame(height: 18).background(Color.gray)

            // 보이스 피드백 토스트
            Text(voice.lastCommandFeedback)
                .font(.caption).bold()
                .foregroundColor(.cyan)
                .lineLimit(1)

            Spacer()

            Button(action: { OverlayWindowManager.shared.toggleOverlay() }) {
                Text("✕").font(.caption2).foregroundColor(.gray)
            }.buttonStyle(.plain)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(
            RoundedRectangle(cornerRadius: 18)
                .fill(Color(red: 0.07, green: 0.09, blue: 0.12).opacity(0.92))
                .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.cyan, lineWidth: 1.5))
        )
    }
}
