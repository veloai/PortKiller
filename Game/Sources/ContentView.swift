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
    @State private var isVibrationOn: Bool = true

    public init() {}

    public var body: some View {
        VStack(spacing: 0) {
            // 상단 헤더 & 탭 바
            headerBar

            Divider().background(Color(white: 0.15))

            // 본문 (레퍼런스 이미지 스타일 2-컬럼 레이아웃)
            HStack(alignment: .top, spacing: 20) {
                // 좌측 설정 패널
                leftSettingsPanel
                    .frame(width: 310)

                // 중앙/우측 듀얼센스 벡터 다이어그램 & 콜아웃
                centerControllerDiagram
            }
            .padding(18)

            Divider().background(Color(white: 0.15))

            // 하단 조작 바
            bottomActionBar
        }
        .frame(minWidth: 1100, minHeight: 720)
        .background(Color(red: 0.06, green: 0.08, blue: 0.10))
        .foregroundColor(Color(white: 0.90))
    }

    // MARK: - 상단 헤더 바
    private var headerBar: some View {
        HStack {
            HStack(spacing: 8) {
                Text("⚔️").font(.title3)
                Text("PORTKILLER").font(.headline).bold().foregroundColor(.cyan)
                Text("GAMEPAD & VOICE").font(.subheadline).foregroundColor(.gray)
            }

            Spacer()

            // 탭 메뉴 (L1 / R1)
            HStack(spacing: 16) {
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
                    .font(.caption).bold()
                    .foregroundColor(controller.isConnected ? .green : .orange)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 5)
            .background(Color(white: 0.12))
            .cornerRadius(12)
        }
        .padding(.horizontal, 18)
        .frame(height: 52)
        .background(Color(red: 0.08, green: 0.10, blue: 0.13))
    }

    // MARK: - 좌측 설정 패널 (레퍼런스 이미지 좌측 반영)
    private var leftSettingsPanel: some View {
        VStack(alignment: .leading, spacing: 14) {
            // CONTROL SETTINGS
            Text("CONTROL SETTINGS")
                .font(.caption).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))

            settingRow(title: "X축 시점 반전", value: "Off")
            settingToggleRow(title: "Y축 시점 반전", isOn: $invertY)
            settingRow(title: "좌/우 스틱 스왑", value: "Off")
            settingRow(title: "L2 / R2 스왑", value: "Off")

            Divider().background(Color(white: 0.16)).padding(.vertical, 4)

            // SENSITIVITY
            Text("SENSITIVITY")
                .font(.caption).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))

            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Look Sensitivity").font(.caption).foregroundColor(Color(white: 0.75))
                    Spacer()
                    Text("\(Int(lookSensitivity))").font(.caption).bold().foregroundColor(.cyan)
                }
                Slider(value: $lookSensitivity, in: 1...10, step: 1)
            }

            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Touchpad Multiplier").font(.caption).foregroundColor(Color(white: 0.75))
                    Spacer()
                    Text(String(format: "%.1f", touchSensitivity)).font(.caption).bold().foregroundColor(.cyan)
                }
                Slider(value: $touchSensitivity, in: 1...15)
            }

            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Stick Deadzone").font(.caption).foregroundColor(Color(white: 0.75))
                    Spacer()
                    Text("\(Int(deadzone * 100))%").font(.caption).bold().foregroundColor(.cyan)
                }
                Slider(value: $deadzone, in: 0.05...0.30)
            }

            Divider().background(Color(white: 0.16)).padding(.vertical, 4)

            // OTHER
            Text("OTHER")
                .font(.caption).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))

            settingToggleRow(title: "진동 (Haptics)", isOn: $isVibrationOn)
            settingToggleRow(title: "패드 마이크 PTT", isOn: $isPttEnabled)

            Spacer()
        }
        .padding(16)
        .background(Color(red: 0.08, green: 0.10, blue: 0.13))
        .cornerRadius(8)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color(white: 0.16), lineWidth: 1))
    }

    private func settingRow(title: String, value: String) -> some View {
        HStack {
            Text(title).font(.caption).foregroundColor(Color(white: 0.8))
            Spacer()
            Text(value).font(.caption).bold().foregroundColor(Color(white: 0.5))
        }
    }

    private func settingToggleRow(title: String, isOn: Binding<Bool>) -> some View {
        HStack {
            Text(title).font(.caption).foregroundColor(Color(white: 0.8))
            Spacer()
            Toggle("", isOn: isOn).labelsHidden()
        }
    }

    // MARK: - 중앙/우측 컨트롤러 다이어그램 & 콜아웃
    private var centerControllerDiagram: some View {
        VStack(spacing: 14) {
            // 상단 L1/R1 & L2/R2 콜아웃
            HStack(spacing: 24) {
                calloutCard(title: "L1 / L2 BUTTONS", subtitle: "L2: [Aim / Shift]   L1: [Tab 타겟팅]")
                Spacer()
                calloutCard(title: "R1 / R2 BUTTONS", subtitle: "R2: [Shoot / Ctrl]   R1: [스킬 슬롯 3]")
            }

            // 중앙 컨트롤러 일러스트 & 좌우 버튼 콜아웃
            HStack(alignment: .center, spacing: 16) {
                // 좌측 콜아웃 (D-Pad)
                VStack(alignment: .leading, spacing: 10) {
                    Text("DIRECTIONAL BUTTONS").font(.caption2).bold().foregroundColor(.gray)
                    VStack(alignment: .leading, spacing: 6) {
                        buttonLabelRow(btn: "▲", label: "비상 물약 (9)", active: controller.dpadUp)
                        buttonLabelRow(btn: "◀", label: "가방 열기 (B)", active: controller.dpadLeft)
                        buttonLabelRow(btn: "▼", label: "탈것 탑승 (Z)", active: controller.dpadDown)
                        buttonLabelRow(btn: "▶", label: "세계 지도 (M)", active: controller.dpadRight)
                    }
                    .padding(10)
                    .background(Color(white: 0.10))
                    .cornerRadius(6)

                    Text("TOUCHPAD").font(.caption2).bold().foregroundColor(.gray).padding(.top, 4)
                    HStack {
                        Text("마우스 트랙패드 & 좌클릭")
                            .font(.caption2).foregroundColor(controller.touchpadPressed ? .cyan : .gray)
                    }
                    .padding(8)
                    .background(Color(white: 0.10))
                    .cornerRadius(6)
                }
                .frame(width: 170)

                Spacer()

                // 중앙 PS5 듀얼센스 벡터 그래픽
                ZStack {
                    // 컨트롤러 바디 실루엣
                    DualSenseBodyShape()
                        .fill(Color(red: 0.10, green: 0.13, blue: 0.17))
                        .overlay(DualSenseBodyShape().stroke(Color(red: 0.22, green: 0.28, blue: 0.35), lineWidth: 2))
                        .frame(width: 360, height: 230)

                    // 터치패드 영역
                    RoundedRectangle(cornerRadius: 6)
                        .fill(Color(red: 0.14, green: 0.18, blue: 0.23))
                        .overlay(RoundedRectangle(cornerRadius: 6).stroke(controller.touchpadPressed ? Color.cyan : Color(white: 0.25), lineWidth: 1.5))
                        .frame(width: 130, height: 68)
                        .offset(y: -42)
                        .overlay(
                            Text("TOUCHPAD")
                                .font(.system(size: 9, weight: .bold))
                                .foregroundColor(controller.touchpadPressed ? .cyan : .gray)
                                .offset(y: -42)
                        )

                    // 마이크 음소거 버튼
                    Capsule()
                        .fill(Color.orange)
                        .frame(width: 16, height: 6)
                        .offset(y: 8)

                    // 방향키 (D-Pad)
                    dpadCross
                        .offset(x: -88, y: -18)

                    // 액션 버튼 4개
                    actionButtonsCluster
                        .offset(x: 88, y: -18)

                    // 아날로그 스틱 2개
                    HStack(spacing: 50) {
                        stickVisual(x: controller.leftStickX, y: controller.leftStickY, isClicked: controller.buttonL3)
                        stickVisual(x: controller.rightStickX, y: controller.rightStickY, isClicked: controller.buttonR3)
                    }
                    .offset(y: 38)
                }
                .frame(width: 370, height: 240)

                Spacer()

                // 우측 콜아웃 (Action Buttons)
                VStack(alignment: .leading, spacing: 10) {
                    Text("ACTION BUTTONS").font(.caption2).bold().foregroundColor(.gray)
                    VStack(alignment: .leading, spacing: 6) {
                        buttonLabelRow(btn: "△", label: "스킬 슬롯 2 (2)", active: controller.buttonTriangle, color: .cyan)
                        buttonLabelRow(btn: "○", label: "상호작용 / 룻 (F)", active: controller.buttonCircle, color: .red)
                        buttonLabelRow(btn: "□", label: "스킬 슬롯 1 (1)", active: controller.buttonSquare, color: .purple)
                        buttonLabelRow(btn: "✕", label: "점프 (Space)", active: controller.buttonCross, color: .blue)
                    }
                    .padding(10)
                    .background(Color(white: 0.10))
                    .cornerRadius(6)

                    Text("OPTIONS / MENU").font(.caption2).bold().foregroundColor(.gray).padding(.top, 4)
                    HStack {
                        Text("게임 메뉴 (Escape)")
                            .font(.caption2).foregroundColor(controller.buttonOptions ? .cyan : .gray)
                    }
                    .padding(8)
                    .background(Color(white: 0.10))
                    .cornerRadius(6)
                }
                .frame(width: 170)
            }

            // 하단 스틱 콜아웃
            HStack(spacing: 24) {
                Text("좌측 스틱: 이동 (L3: 자동 달리기)")
                    .font(.caption2).foregroundColor(Color(white: 0.7))
                Divider().frame(height: 12).background(Color.gray)
                Text("우측 스틱: 시점 회전 (R3: 시점 리셋)")
                    .font(.caption2).foregroundColor(Color(white: 0.7))
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 6)
            .background(Color(white: 0.10))
            .cornerRadius(6)

            // 하단 실시간 보이스 명령 박스
            voiceSectionBox
        }
    }

    private var dpadCross: some View {
        ZStack {
            Rectangle().fill(Color(white: 0.18)).frame(width: 14, height: 42).cornerRadius(3)
            Rectangle().fill(Color(white: 0.18)).frame(width: 42, height: 14).cornerRadius(3)
        }
    }

    private var actionButtonsCluster: some View {
        ZStack {
            Circle().fill(controller.buttonTriangle ? Color.cyan : Color(white: 0.22)).frame(width: 16, height: 16).offset(y: -14)
            Circle().fill(controller.buttonCircle ? Color.red : Color(white: 0.22)).frame(width: 16, height: 16).offset(x: 14)
            Circle().fill(controller.buttonSquare ? Color.purple : Color(white: 0.22)).frame(width: 16, height: 16).offset(x: -14)
            Circle().fill(controller.buttonCross ? Color.blue : Color(white: 0.22)).frame(width: 16, height: 16).offset(y: 14)
        }
    }

    private func stickVisual(x: Float, y: Float, isClicked: Bool) -> some View {
        ZStack {
            Circle().fill(Color(white: 0.13)).frame(width: 52, height: 52)
                .overlay(Circle().stroke(Color(white: 0.24), lineWidth: 1.5))
            Circle().fill(isClicked ? Color.cyan : Color(white: 0.85))
                .frame(width: 20, height: 20)
                .offset(x: CGFloat(x) * 16, y: CGFloat(-y) * 16)
        }
    }

    private func buttonLabelRow(btn: String, label: String, active: Bool, color: Color = .cyan) -> some View {
        HStack(spacing: 8) {
            Text(btn)
                .font(.caption2).bold()
                .frame(width: 18, height: 18)
                .background(active ? color : Color(white: 0.18))
                .foregroundColor(active ? .black : .white)
                .cornerRadius(4)
            Text(label)
                .font(.caption2)
                .foregroundColor(active ? color : Color(white: 0.75))
        }
    }

    private func calloutCard(title: String, subtitle: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.caption2).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))
            Text(subtitle).font(.caption).bold().foregroundColor(Color(white: 0.85))
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 8)
        .background(Color(white: 0.10))
        .cornerRadius(6)
        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color(white: 0.16), lineWidth: 1))
    }

    // MARK: - 하단 실시간 보이스 명령 박스
    private var voiceSectionBox: some View {
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

            // 볼륨 VU 미터
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 3).fill(Color(white: 0.14))
                    RoundedRectangle(cornerRadius: 3).fill(Color.green)
                        .frame(width: geo.size.width * CGFloat(voice.inputVolume))
                }
            }
            .frame(height: 5)

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
        .background(Color(red: 0.08, green: 0.10, blue: 0.13))
        .cornerRadius(8)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color(white: 0.16), lineWidth: 1))
    }

    // MARK: - 하단 액션 바
    private var bottomActionBar: some View {
        HStack {
            Text("PORTKILLER macOS NATIVE STUDIO v1.0 (APPLE SILICON ARM64)")
                .font(.caption2).foregroundColor(Color(white: 0.45))

            Spacer()

            Button(action: {
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

// MARK: - PS5 듀얼센스 바디 실루엣 셰이프
struct DualSenseBodyShape: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        let w = rect.width
        let h = rect.height

        // 상단 곡선 및 그립 윤곽
        path.move(to: CGPoint(x: w * 0.22, y: h * 0.15))
        path.addCurve(to: CGPoint(x: w * 0.78, y: h * 0.15),
                      control1: CGPoint(x: w * 0.35, y: h * 0.05),
                      control2: CGPoint(x: w * 0.65, y: h * 0.05))

        // 우측 그립
        path.addCurve(to: CGPoint(x: w * 0.95, y: h * 0.85),
                      control1: CGPoint(x: w * 0.92, y: h * 0.35),
                      control2: CGPoint(x: w * 1.00, y: h * 0.70))
        path.addCurve(to: CGPoint(x: w * 0.70, y: h * 0.75),
                      control1: CGPoint(x: w * 0.88, y: h * 0.98),
                      control2: CGPoint(x: w * 0.75, y: h * 0.90))

        // 하단 중앙 곡선
        path.addCurve(to: CGPoint(x: w * 0.30, y: h * 0.75),
                      control1: CGPoint(x: w * 0.60, y: h * 0.80),
                      control2: CGPoint(x: w * 0.40, y: h * 0.80))

        // 좌측 그립
        path.addCurve(to: CGPoint(x: w * 0.05, y: h * 0.85),
                      control1: CGPoint(x: w * 0.25, y: h * 0.90),
                      control2: CGPoint(x: w * 0.12, y: h * 0.98))
        path.addCurve(to: CGPoint(x: w * 0.22, y: h * 0.15),
                      control1: CGPoint(x: w * 0.00, y: h * 0.70),
                      control2: CGPoint(x: w * 0.08, y: h * 0.35))

        return path
    }
}
