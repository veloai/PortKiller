import SwiftUI
import GameController

public struct ContentView: View {
    @ObservedObject var controller = ControllerManager.shared
    @ObservedObject var voice = VoiceManager.shared

    @State private var selectedTab = 0 // 0: Controls, 1: Voice, 2: HUD
    @State private var lookSensitivity: Double = 6.0
    @State private var touchSensitivity: Double = 8.5
    @State private var deadzone: Double = 0.10
    @State private var invertY: Bool = false
    @State private var isPttEnabled: Bool = true
    @State private var isOverlayOpen: Bool = false

    public init() {}

    public var body: some View {
        VStack(spacing: 0) {
            // 상단 헤더 & 탭 바
            headerBar

            Divider().background(Color(white: 0.15))

            // 본문 콘텐츠
            HStack(spacing: 16) {
                // 좌측 설정 패널 (레퍼런스 이미지 좌측 반영)
                leftSettingsPanel
                    .frame(width: 320)

                // 중앙 컨트롤러 & 실시간 진단 뷰
                centerDiagnosticView
            }
            .padding(16)

            Divider().background(Color(white: 0.15))

            // 하단 조작 바
            bottomActionBar
        }
        .frame(minWidth: 1050, minHeight: 680)
        .background(Color(red: 0.06, green: 0.08, blue: 0.10))
        .foregroundColor(Color(white: 0.90))
    }

    // MARK: - 상단 헤더 바
    private var headerBar: some View {
        HStack {
            HStack(spacing: 8) {
                Text("⚔️").font(.title2)
                Text("PORTKILLER").font(.headline).bold().foregroundColor(.cyan)
                Text("GAMEPAD & VOICE").font(.subheadline).foregroundColor(.gray)
            }

            Spacer()

            // 탭 메뉴 (L1 / R1)
            HStack(spacing: 12) {
                Text("L1").font(.caption).bold().padding(.horizontal, 6).padding(.vertical, 2).background(Color(white: 0.15)).cornerRadius(4)

                Button(action: { selectedTab = 0 }) {
                    Text("CONTROLS (패드)").font(.system(size: 13, weight: selectedTab == 0 ? .bold : .regular))
                        .foregroundColor(selectedTab == 0 ? .cyan : .gray)
                }.buttonStyle(.plain)

                Button(action: { selectedTab = 1 }) {
                    Text("VOICE (보이스 명령)").font(.system(size: 13, weight: selectedTab == 1 ? .bold : .regular))
                        .foregroundColor(selectedTab == 1 ? .cyan : .gray)
                }.buttonStyle(.plain)

                Button(action: { selectedTab = 2 }) {
                    Text("HUD & OVERLAY").font(.system(size: 13, weight: selectedTab == 2 ? .bold : .regular))
                        .foregroundColor(selectedTab == 2 ? .cyan : .gray)
                }.buttonStyle(.plain)

                Text("R1").font(.caption).bold().padding(.horizontal, 6).padding(.vertical, 2).background(Color(white: 0.15)).cornerRadius(4)
            }

            Spacer()

            // 연결 상태 배지
            HStack(spacing: 6) {
                Circle()
                    .fill(controller.isConnected ? Color.green : Color.orange)
                    .frame(width: 8, height: 8)
                Text(controller.isConnected ? "PS5 듀얼센스 연결됨" : "패드 대기 중 (PS버튼 클릭)")
                    .font(.caption)
                    .foregroundColor(controller.isConnected ? .green : .orange)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background(Color(white: 0.12))
            .cornerRadius(12)
        }
        .padding(.horizontal, 18)
        .frame(height: 52)
        .background(Color(red: 0.08, green: 0.10, blue: 0.13))
    }

    // MARK: - 좌측 설정 패널
    private var leftSettingsPanel: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                Text("CONTROL SETTINGS")
                    .font(.caption).bold().foregroundColor(Color(white: 0.5))

                Toggle("Y축 시점 반전", isOn: $invertY)
                Toggle("터치패드 마우스 모드", isOn: .constant(true))
                Toggle("패드 마이크 PTT 모드", isOn: $isPttEnabled)

                Divider().background(Color(white: 0.15))

                Text("SENSITIVITY & DEADZONE")
                    .font(.caption).bold().foregroundColor(Color(white: 0.5))

                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text("시점 회전 감도").font(.caption).foregroundColor(.gray)
                        Spacer()
                        Text("\(Int(lookSensitivity))").font(.caption).bold().foregroundColor(.cyan)
                    }
                    Slider(value: $lookSensitivity, in: 1...10, step: 1)
                }

                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text("터치패드 마우스 감도").font(.caption).foregroundColor(.gray)
                        Spacer()
                        Text(String(format: "%.1f", touchSensitivity)).font(.caption).bold().foregroundColor(.cyan)
                    }
                    Slider(value: $touchSensitivity, in: 1...15)
                }

                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text("스틱 데드존").font(.caption).foregroundColor(.gray)
                        Spacer()
                        Text("\(Int(deadzone * 100))%").font(.caption).bold().foregroundColor(.cyan)
                    }
                    Slider(value: $deadzone, in: 0.05...0.30)
                }

                Divider().background(Color(white: 0.15))

                Text("DEVICE INFO")
                    .font(.caption).bold().foregroundColor(Color(white: 0.5))

                VStack(alignment: .leading, spacing: 4) {
                    Text(controller.controllerName).font(.caption).bold()
                    Text("배터리: \(Int(controller.batteryLevel * 100))%").font(.caption).foregroundColor(.green)
                }
            }
            .padding(14)
        }
        .background(Color(red: 0.09, green: 0.11, blue: 0.14))
        .cornerRadius(10)
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color(white: 0.16), lineWidth: 1))
    }

    // MARK: - 중앙 실시간 진단 뷰
    private var centerDiagnosticView: some View {
        VStack(spacing: 14) {
            // 상단 L1/R1 & L2/R2
            HStack {
                HStack(spacing: 12) {
                    triggerPill(label: "L1 (Tab 타겟팅)", active: controller.buttonL1)
                    triggerPill(label: "L2: \(Int(controller.leftTrigger * 100))% (Shift)", active: controller.leftTrigger > 0.1)
                }
                Spacer()
                HStack(spacing: 12) {
                    triggerPill(label: "R2: \(Int(controller.rightTrigger * 100))% (Ctrl)", active: controller.rightTrigger > 0.1)
                    triggerPill(label: "R1 (스킬 3)", active: controller.buttonR1)
                }
            }

            // 중앙 스틱 & 액션 버튼 시각화
            HStack(spacing: 20) {
                // D-Pad
                VStack(spacing: 4) {
                    Text("D-PAD").font(.caption2).bold().foregroundColor(.gray)
                    buttonPill(label: "▲ 물약", active: controller.dpadUp)
                    HStack(spacing: 4) {
                        buttonPill(label: "◀ 가방", active: controller.dpadLeft)
                        buttonPill(label: "▼ 탈것", active: controller.dpadDown)
                        buttonPill(label: "▶ 지도", active: controller.dpadRight)
                    }
                }
                .frame(width: 170)

                Spacer()

                // 좌측 & 우측 스틱
                HStack(spacing: 24) {
                    VStack {
                        stickCircle(x: controller.leftStickX, y: controller.leftStickY, isClicked: controller.buttonL3)
                        Text("L-STICK (이동/L3)").font(.caption2).foregroundColor(controller.buttonL3 ? .cyan : .gray)
                    }
                    VStack {
                        stickCircle(x: controller.rightStickX, y: controller.rightStickY, isClicked: controller.buttonR3)
                        Text("R-STICK (시점/R3)").font(.caption2).foregroundColor(controller.buttonR3 ? .cyan : .gray)
                    }
                }

                Spacer()

                // 액션 버튼 (△, ○, □, ✕)
                VStack(spacing: 4) {
                    Text("ACTION BUTTONS").font(.caption2).bold().foregroundColor(.gray)
                    buttonPill(label: "△ 스킬 2", active: controller.buttonTriangle, color: .cyan)
                    HStack(spacing: 4) {
                        buttonPill(label: "□ 스킬 1", active: controller.buttonSquare, color: .purple)
                        buttonPill(label: "✕ 점프", active: controller.buttonCross, color: .blue)
                        buttonPill(label: "○ 상호작용", active: controller.buttonCircle, color: .red)
                    }
                }
                .frame(width: 170)
            }
            .padding(.vertical, 14)

            // 터치패드 상태
            HStack {
                Text("TOUCHPAD").font(.caption).bold().foregroundColor(.cyan)
                Spacer()
                Text("X: \(String(format: "%.2f", controller.touchpadX))  Y: \(String(format: "%.2f", controller.touchpadY))  클릭: \(controller.touchpadPressed ? "YES" : "NO")")
                    .font(.caption).fontDesign(.monospaced).foregroundColor(.gray)
            }
            .padding(10)
            .background(Color(white: 0.12))
            .cornerRadius(8)

            // 보이스 진단 섹션
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("🎙️ 실시간 보이스 명령").font(.caption).bold()
                    Spacer()
                    Button(action: { voice.toggleListening() }) {
                        Text(voice.isListening ? "마이크 끄기" : "마이크 듣기 시작")
                            .font(.caption).bold()
                            .padding(.horizontal, 10).padding(.vertical, 4)
                            .background(voice.isListening ? Color.red : Color.cyan)
                            .foregroundColor(voice.isListening ? .white : .black)
                            .cornerRadius(6)
                    }.buttonStyle(.plain)
                }

                // 음량 VU 미터
                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        RoundedRectangle(cornerRadius: 4).fill(Color(white: 0.15))
                        RoundedRectangle(cornerRadius: 4).fill(Color.green)
                            .frame(width: geo.size.width * CGFloat(voice.inputVolume))
                    }
                }
                .frame(height: 6)

                // 피드백 텍스트
                Text(voice.lastCommandFeedback)
                    .font(.caption).bold()
                    .foregroundColor(.cyan)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(8)
                    .background(Color(white: 0.08))
                    .cornerRadius(6)
            }
            .padding(12)
            .background(Color(red: 0.09, green: 0.11, blue: 0.14))
            .cornerRadius(10)
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color(white: 0.16), lineWidth: 1))
        }
    }

    // MARK: - 스틱 조이스틱 원형 UI
    private func stickCircle(x: Float, y: Float, isClicked: Bool) -> some View {
        ZStack {
            Circle().fill(Color(white: 0.12)).frame(width: 80, height: 80)
                .overlay(Circle().stroke(Color(white: 0.22), lineWidth: 2))
            Circle().fill(isClicked ? Color.cyan : Color.white)
                .frame(width: 28, height: 28)
                .offset(x: CGFloat(x) * 24, y: CGFloat(-y) * 24)
                .shadow(radius: 4)
        }
    }

    private func buttonPill(label: String, active: Bool, color: Color = .cyan) -> some View {
        Text(label)
            .font(.caption2).bold()
            .frame(width: 52, height: 28)
            .background(active ? color : Color(white: 0.15))
            .foregroundColor(active ? .black : .white)
            .cornerRadius(6)
            .scaleEffect(active ? 1.08 : 1.0)
            .animation(.easeInOut(duration: 0.1), value: active)
    }

    private func triggerPill(label: String, active: Bool) -> some View {
        Text(label)
            .font(.caption).bold()
            .padding(.horizontal, 10).padding(.vertical, 6)
            .background(active ? Color.cyan : Color(white: 0.14))
            .foregroundColor(active ? .black : Color(white: 0.8))
            .cornerRadius(6)
    }

    // MARK: - 하단 액션 바
    private var bottomActionBar: some View {
        HStack {
            Text("PORTKILLER macOS NATIVE STUDIO v1.0 (APPLE SILICON ARM64)")
                .font(.caption2).foregroundColor(Color(white: 0.45))

            Spacer()

            Button(action: {
                // HUD 오버레이 토글
                OverlayWindowManager.shared.toggleOverlay()
            }) {
                Text("🎮 실시간 HUD 오버레이 띄우기")
                    .font(.caption).bold()
                    .padding(.horizontal, 14).padding(.vertical, 6)
                    .background(Color.cyan)
                    .foregroundColor(.black)
                    .cornerRadius(6)
            }.buttonStyle(.plain)
        }
        .padding(.horizontal, 18)
        .frame(height: 44)
        .background(Color(red: 0.08, green: 0.10, blue: 0.13))
    }
}
