import SwiftUI
import AppKit

/// macOS 바탕화면 위에 직접 떠 있는 펫 뷰
public struct MacPetView: View {
    @ObservedObject var state = MacPetState.shared
    @State private var isHovered = false
    @State private var showHeart = false

    public init() {}

    public var body: some View {
        VStack(spacing: 4) {
            // 1. 말풍선
            if let message = state.speechMessage {
                SpeechBubbleView(text: message)
                    .transition(.asymmetric(
                        insertion: .scale(scale: 0.85).combined(with: .opacity),
                        removal: .opacity
                    ))
                    .animation(.spring(response: 0.35, dampingFraction: 0.7), value: state.speechMessage)
            }

            // 2. 펫 캐릭터 & 똥 영역
            HStack(alignment: .bottom, spacing: 6) {
                // 펫 본체
                ZStack {
                    if let image = state.resolveCurrentSprite() {
                        Image(nsImage: image)
                            .interpolation(.none) // 레트로 도트 픽셀 유지
                            .resizable()
                            .scaledToFit()
                            .frame(width: 130, height: 130)
                            .shadow(color: Color.black.opacity(0.35), radius: 6, x: 0, y: 3)
                    } else {
                        // 이미지 누락 시 대체 뷰
                        VStack {
                            Text("🐾").font(.system(size: 60))
                            Text("PKP Pet").font(.caption).foregroundColor(.white)
                        }
                        .frame(width: 120, height: 120)
                    }

                    // 쓰다듬었을 때 하트 이펙트
                    if showHeart {
                        Text("❤️")
                            .font(.system(size: 32))
                            .offset(y: -50)
                            .transition(.move(edge: .top).combined(with: .opacity))
                    }
                }
                .scaleEffect(isHovered ? 1.05 : 1.0)
                .animation(.spring(response: 0.2, dampingFraction: 0.6), value: isHovered)
                .onHover { hovering in
                    isHovered = hovering
                }
                .onTapGesture {
                    triggerHeartAnimation()
                    state.pet()
                }

                // 똥
                if state.isPoopPresent, let poopImg = state.resolvePoopSprite() {
                    Image(nsImage: poopImg)
                        .interpolation(.none)
                        .resizable()
                        .scaledToFit()
                        .frame(width: 36, height: 36)
                        .onTapGesture {
                            state.cleanPoop()
                        }
                        .help("클릭해서 똥 치우기")
                }
            }
        }
        .padding(10)
        // 우클릭 컨텍스트 메뉴
        .contextMenu {
            Button("🍎 간식 주기 (밥먹기)") {
                state.feed()
            }
            Button("🧹 똥 치우기 (청소)") {
                state.cleanPoop()
            }
            Button("💖 쓰다듬기") {
                triggerHeartAnimation()
                state.pet()
            }
            Button("📊 펫 브리핑") {
                state.briefing()
            }
            Divider()
            Button("🎮 게임패드 HUD 열기/닫기") {
                OverlayWindowManager.shared.toggleOverlay()
            }
            Divider()
            Button("❌ 펫 숨기기") {
                MacPetWindowManager.shared.togglePet()
            }
        }
    }

    private func triggerHeartAnimation() {
        withAnimation(.easeOut(duration: 0.2)) {
            showHeart = true
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
            withAnimation(.easeIn(duration: 0.3)) {
                showHeart = false
            }
        }
    }
}

/// 말풍선 뷰 (꼬리 포함)
struct SpeechBubbleView: View {
    let text: String

    var body: some View {
        VStack(spacing: 0) {
            Text(text)
                .font(.system(size: 12, weight: .semibold, design: .rounded))
                .foregroundColor(Color(red: 0.1, green: 0.12, blue: 0.15))
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .background(
                    RoundedRectangle(cornerRadius: 14)
                        .fill(Color(red: 0.98, green: 0.98, blue: 0.96))
                        .shadow(color: Color.black.opacity(0.25), radius: 4, x: 0, y: 2)
                        .overlay(
                            RoundedRectangle(cornerRadius: 14)
                                .stroke(Color(red: 0.85, green: 0.85, blue: 0.80), lineWidth: 1)
                        )
                )

            // 말풍선 아래쪽 꼬리 삼각형
            Path { path in
                path.move(to: CGPoint(x: 0, y: 0))
                path.addLine(to: CGPoint(x: 10, y: 0))
                path.addLine(to: CGPoint(x: 5, y: 6))
                path.closeSubpath()
            }
            .fill(Color(red: 0.98, green: 0.98, blue: 0.96))
            .frame(width: 10, height: 6)
            .offset(y: -0.5)
        }
    }
}
