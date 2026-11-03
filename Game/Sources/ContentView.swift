import SwiftUI
import GameController

public struct ContentView: View {
    @ObservedObject var controller = ControllerManager.shared
    @ObservedObject var voice = VoiceManager.shared
    @ObservedObject var simulator = InputSimulator.shared

    @State private var selectedTab = 0 // 0: Controls, 1: Voice, 2: HUD
    @State private var lookSensitivity: Double = 14.0
    @State private var touchSensitivity: Double = 16.0
    @State private var deadzone: Double = 0.22
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
        .frame(minWidth: 1100, minHeight: 740)
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

            // 연결 상태 및 실제 배터리 상태 표시 (가짜 잔량 방지)
            HStack(spacing: 8) {
                HStack(spacing: 6) {
                    Circle()
                        .fill(controller.isConnected ? Color.green : Color.orange)
                        .frame(width: 8, height: 8)
                    Text(controller.isConnected ? "PS5 듀얼센스 연결됨" : "패드 대기 중 (PS버튼 클릭)")
                        .font(.caption).bold()
                        .foregroundColor(controller.isConnected ? .green : .orange)
                }

                Divider().frame(height: 14).background(Color.gray)

                // 실제 배터리 잔량
                HStack(spacing: 4) {
                    Text(controller.isConnected ? "🔋" : "🔌")
                        .font(.caption2)
                    Text(controller.batteryInfo.displayText)
                        .font(.caption).bold()
                        .foregroundColor(controller.isConnected ? .cyan : .gray)
                }

                Divider().frame(height: 14).background(Color.gray)

                // 가상 키보드/마우스 입력 변환 상태
                HStack(spacing: 4) {
                    Circle()
                        .fill(simulator.isEnabled ? Color.cyan : Color.gray)
                        .frame(width: 6, height: 6)
                    Text(simulator.isEnabled ? "⌨️🖱️ 가상 키마 연동" : "가상 키마 꺼짐")
                        .font(.caption2).bold()
                        .foregroundColor(simulator.isEnabled ? .cyan : .gray)
                }
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

    // MARK: - 좌측 설정 패널
    private var leftSettingsPanel: some View {
        VStack(alignment: .leading, spacing: 14) {
            // HYBRID INPUT EMULATOR
            Text("HYBRID INPUT EMULATOR")
                .font(.caption).bold().foregroundColor(.cyan)

            if !simulator.isAccessibilityTrusted {
                Button(action: {
                    if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
                        NSWorkspace.shared.open(url)
                    }
                    simulator.promptAccessibilityIfNeeded()
                }) {
                    HStack {
                        Text("⚠️ 손쉬운 사용 권한 필요 (클릭)")
                            .font(.caption2).bold()
                            .foregroundColor(.yellow)
                    }
                    .padding(6)
                    .frame(maxWidth: .infinity)
                    .background(Color.yellow.opacity(0.18))
                    .cornerRadius(6)
                    .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.yellow, lineWidth: 1))
                }
                .buttonStyle(.plain)
            }

            settingToggleRow(title: "가상 키보드/마우스 활성", isOn: $simulator.isEnabled)
            settingToggleRow(title: "좌측 스틱 WASD 이동", isOn: $simulator.enableStickWASD)
            settingToggleRow(title: "WoW 전용 입력 제한", isOn: $simulator.targetOnlyWoW)

            Divider().background(Color(white: 0.16)).padding(.vertical, 4)

            // CONTROL SETTINGS
            Text("CONTROL SETTINGS")
                .font(.caption).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))

            settingRow(title: "X축 시점 반전", value: "Off")
            settingToggleRow(title: "Y축 시점 반전", isOn: $invertY)
            settingRow(title: "좌/우 스틱 스왑", value: "Off")
            settingRow(title: "L2 / R2 스왑", value: "Off")

            Divider().background(Color(white: 0.16)).padding(.vertical, 4)

            // SENSITIVITY
            Text("SENSITIVITY & DEADZONE")
                .font(.caption).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))

            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Mouse Look Speed (스틱)").font(.caption).foregroundColor(Color(white: 0.75))
                    Spacer()
                    Text("\(Int(simulator.mouseSpeed))").font(.caption).bold().foregroundColor(.cyan)
                }
                Slider(value: Binding(get: { Double(simulator.mouseSpeed) }, set: { simulator.mouseSpeed = Float($0) }), in: 4...30, step: 1)
            }

            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Touchpad Sensitivity (터치패드)").font(.caption).foregroundColor(Color(white: 0.75))
                    Spacer()
                    Text("\(Int(simulator.touchpadMultiplier))").font(.caption).bold().foregroundColor(.cyan)
                }
                Slider(value: Binding(get: { Double(simulator.touchpadMultiplier) }, set: { simulator.touchpadMultiplier = Float($0) }), in: 200...1500, step: 25)
            }

            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Stick Deadzone").font(.caption).foregroundColor(Color(white: 0.75))
                    Spacer()
                    Text("\(Int(simulator.stickDeadzone * 100))%").font(.caption).bold().foregroundColor(.cyan)
                }
                Slider(value: Binding(get: { Double(simulator.stickDeadzone) }, set: { simulator.stickDeadzone = Float($0) }), in: 0.05...0.40)
            }

            Divider().background(Color(white: 0.16)).padding(.vertical, 4)

            // OTHER
            Text("OTHER")
                .font(.caption).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))

            settingToggleRow(title: "진동 (Haptics)", isOn: $isVibrationOn)
            settingToggleRow(title: "패드 마이크 PTT", isOn: $controller.isPttEnabled)

            Divider().background(Color(white: 0.16)).padding(.vertical, 4)

            // DEVICE & BATTERY INFO (가짜 잔량 방지)
            Text("DEVICE & BATTERY")
                .font(.caption).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))

            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("컨트롤러:").font(.caption2).foregroundColor(.gray)
                    Text(controller.controllerName).font(.caption2).bold().lineLimit(1)
                }
                HStack {
                    Text("배터리 상태:").font(.caption2).foregroundColor(.gray)
                    Text(controller.batteryInfo.displayText)
                        .font(.caption2).bold()
                        .foregroundColor(controller.isConnected ? .green : .orange)
                }
            }

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
        VStack(spacing: 12) {
            // 상단 L1/R1 & L2/R2 동적 콜아웃 (실제 입력 연동)
            HStack(spacing: 20) {
                // L1 & L2 상태
                VStack(alignment: .leading, spacing: 4) {
                    Text("L1 / L2 BUTTONS & TRIGGER")
                        .font(.caption2).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))
                    HStack(spacing: 12) {
                        HStack(spacing: 6) {
                            Text("L1")
                                .font(.caption2).bold()
                                .padding(.horizontal, 6).padding(.vertical, 2)
                                .background(controller.buttonL1 ? Color.cyan : Color(white: 0.2))
                                .foregroundColor(controller.buttonL1 ? .black : .white)
                                .cornerRadius(4)
                            Text("Tab 타겟팅")
                                .font(.caption2)
                                .foregroundColor(controller.buttonL1 ? .cyan : Color(white: 0.7))
                        }
                        Divider().frame(height: 14).background(Color.gray)
                        HStack(spacing: 6) {
                            Text("L2")
                                .font(.caption2).bold()
                                .padding(.horizontal, 6).padding(.vertical, 2)
                                .background(controller.leftTrigger > 0.1 ? Color.cyan : Color(white: 0.2))
                                .foregroundColor(controller.leftTrigger > 0.1 ? .black : .white)
                                .cornerRadius(4)
                            Text("Aim / Shift: \(Int(controller.leftTrigger * 100))%")
                                .font(.caption2).bold()
                                .foregroundColor(controller.leftTrigger > 0.1 ? .cyan : Color(white: 0.7))
                        }
                    }
                }
                .padding(.horizontal, 12).padding(.vertical, 8)
                .background(Color(white: 0.10)).cornerRadius(6)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color(white: 0.16), lineWidth: 1))

                Spacer()

                // R1 & R2 상태
                VStack(alignment: .leading, spacing: 4) {
                    Text("R1 / R2 BUTTONS & TRIGGER")
                        .font(.caption2).bold().foregroundColor(Color(red: 0.45, green: 0.55, blue: 0.65))
                    HStack(spacing: 12) {
                        HStack(spacing: 6) {
                            Text("R1")
                                .font(.caption2).bold()
                                .padding(.horizontal, 6).padding(.vertical, 2)
                                .background(controller.buttonR1 ? Color.cyan : Color(white: 0.2))
                                .foregroundColor(controller.buttonR1 ? .black : .white)
                                .cornerRadius(4)
                            Text("스킬 슬롯 3")
                                .font(.caption2)
                                .foregroundColor(controller.buttonR1 ? .cyan : Color(white: 0.7))
                        }
                        Divider().frame(height: 14).background(Color.gray)
                        HStack(spacing: 6) {
                            Text("R2")
                                .font(.caption2).bold()
                                .padding(.horizontal, 6).padding(.vertical, 2)
                                .background(controller.rightTrigger > 0.1 ? Color.cyan : Color(white: 0.2))
                                .foregroundColor(controller.rightTrigger > 0.1 ? .black : .white)
                                .cornerRadius(4)
                            Text("Shoot / Ctrl: \(Int(controller.rightTrigger * 100))%")
                                .font(.caption2).bold()
                                .foregroundColor(controller.rightTrigger > 0.1 ? .cyan : Color(white: 0.7))
                        }
                    }
                }
                .padding(.horizontal, 12).padding(.vertical, 8)
                .background(Color(white: 0.10)).cornerRadius(6)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color(white: 0.16), lineWidth: 1))
            }

            // 중앙 컨트롤러 일러스트 & 좌우 버튼 콜아웃
            HStack(alignment: .center, spacing: 16) {
                // 좌측 콜아웃 (D-Pad)
                VStack(alignment: .leading, spacing: 8) {
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
                    VStack(alignment: .leading, spacing: 3) {
                        Text("트랙패드 & 좌클릭")
                            .font(.caption2).bold().foregroundColor(controller.touchpadPressed ? .cyan : .white)
                        HStack {
                            Text(String(format: "X:%.2f Y:%.2f", controller.touchpadX, controller.touchpadY))
                                .font(.system(size: 9, design: .monospaced))
                                .foregroundColor(.cyan)
                            Spacer()
                            Text(controller.touchpadPressed ? "클릭" : "대기")
                                .font(.system(size: 8, weight: .bold))
                                .padding(.horizontal, 4)
                                .padding(.vertical, 1)
                                .background(controller.touchpadPressed ? Color.green : Color(white: 0.2))
                                .foregroundColor(controller.touchpadPressed ? .black : .gray)
                                .cornerRadius(3)
                        }
                    }
                    .padding(8)
                    .background(Color(white: 0.10))
                    .cornerRadius(6)
                }
                .frame(width: 170)

                Spacer()

                // 중앙 PS5 듀얼센스 벡터 그래픽 (L1/L2/R1/R2, D-Pad, 액션 버튼, 스틱 완전 반영)
                ZStack {
                    // 1. 트리거 (L2 / R2) - 패드 본체 뒤쪽 상단에 배치
                    HStack(spacing: 210) {
                        // L2 트리거 시각화
                        triggerVisual(name: "L2", value: controller.leftTrigger)
                        // R2 트리거 시각화
                        triggerVisual(name: "R2", value: controller.rightTrigger)
                    }
                    .offset(y: -110)

                    // 2. 범퍼 (L1 / R1) - 패드 어깨 부위에 배치
                    HStack(spacing: 210) {
                        bumperVisual(name: "L1", isPressed: controller.buttonL1)
                        bumperVisual(name: "R1", isPressed: controller.buttonR1)
                    }
                    .offset(y: -80)

                    // 3. 컨트롤러 바디 실루엣
                    DualSenseBodyShape()
                        .fill(Color(red: 0.10, green: 0.13, blue: 0.17))
                        .overlay(DualSenseBodyShape().stroke(Color(red: 0.22, green: 0.28, blue: 0.35), lineWidth: 2))
                        .frame(width: 360, height: 230)

                    // 4. 터치패드 영역
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

                    // 5. 마이크 음소거 버튼
                    Capsule()
                        .fill(voice.isMicMuted ? Color.orange : Color(red: 0.2, green: 0.8, blue: 0.4))
                        .frame(width: 16, height: 6)
                        .offset(y: 8)
                        .overlay(
                            Capsule()
                                .stroke(controller.buttonMute ? Color.white : Color.clear, lineWidth: 1.5)
                                .offset(y: 8)
                        )

                    // 6. 방향키 (D-Pad) - 4방향 키 입력 실시간 반영
                    dpadInteractiveCluster
                        .offset(x: -88, y: -18)

                    // 7. 액션 버튼 4개 (△, ○, □, ✕) - 실시간 입력 반영
                    actionButtonsCluster
                        .offset(x: 88, y: -18)

                    // 8. 아날로그 스틱 2개 (L3/R3)
                    HStack(spacing: 50) {
                        stickVisual(x: controller.leftStickX, y: controller.leftStickY, isClicked: controller.buttonL3)
                        stickVisual(x: controller.rightStickX, y: controller.rightStickY, isClicked: controller.buttonR3)
                    }
                    .offset(y: 38)
                }
                .frame(width: 380, height: 260)

                Spacer()

                // 우측 콜아웃 (Action Buttons)
                VStack(alignment: .leading, spacing: 8) {
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
                Text("좌측 스틱: 이동 (L3 클릭: 자동 달리기)")
                    .font(.caption2).foregroundColor(controller.buttonL3 ? .cyan : Color(white: 0.7))
                Divider().frame(height: 12).background(Color.gray)
                Text("우측 스틱: 시점 회전 (R3 클릭: 시점 리셋)")
                    .font(.caption2).foregroundColor(controller.buttonR3 ? .cyan : Color(white: 0.7))
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 5)
            .background(Color(white: 0.10))
            .cornerRadius(6)

            // 하단 실시간 보이스 명령 및 권한 가이드 박스
            voiceSectionBox
        }
    }

    // MARK: - 트리거 (L2/R2) 시각화 위젯
    private func triggerVisual(name: String, value: Float) -> some View {
        VStack(spacing: 2) {
            Text("\(name) \(Int(value * 100))%")
                .font(.system(size: 8, weight: .bold))
                .foregroundColor(value > 0.05 ? .cyan : .gray)
            ZStack(alignment: .bottom) {
                RoundedRectangle(cornerRadius: 3)
                    .fill(Color(white: 0.15))
                    .frame(width: 50, height: 20)
                    .overlay(RoundedRectangle(cornerRadius: 3).stroke(value > 0.05 ? Color.cyan : Color(white: 0.25), lineWidth: 1))
                RoundedRectangle(cornerRadius: 3)
                    .fill(Color.cyan)
                    .frame(width: 50, height: max(0, CGFloat(value) * 20))
            }
        }
    }

    // MARK: - 범퍼 (L1/R1) 시각화 위젯
    private func bumperVisual(name: String, isPressed: Bool) -> some View {
        Text(name)
            .font(.system(size: 10, weight: .bold))
            .frame(width: 54, height: 16)
            .background(isPressed ? Color.cyan : Color(white: 0.22))
            .foregroundColor(isPressed ? .black : .white)
            .cornerRadius(3)
            .overlay(RoundedRectangle(cornerRadius: 3).stroke(isPressed ? Color.cyan : Color(white: 0.40), lineWidth: 1))
            .shadow(color: isPressed ? Color.cyan.opacity(0.8) : Color.clear, radius: 4)
    }

    // MARK: - 방향키 (D-Pad) 4방향 독립 시각화 클러스터
    private var dpadInteractiveCluster: some View {
        ZStack {
            // 중앙 허브
            Circle().fill(Color(white: 0.14)).frame(width: 14, height: 14)

            // Up (▲)
            dpadArm(icon: "▲", isPressed: controller.dpadUp)
                .offset(y: -16)

            // Down (▼)
            dpadArm(icon: "▼", isPressed: controller.dpadDown)
                .offset(y: 16)

            // Left (◀)
            dpadArm(icon: "◀", isPressed: controller.dpadLeft)
                .offset(x: -16)

            // Right (▶)
            dpadArm(icon: "▶", isPressed: controller.dpadRight)
                .offset(x: 16)
        }
    }

    private func dpadArm(icon: String, isPressed: Bool) -> some View {
        Text(icon)
            .font(.system(size: 11, weight: .bold))
            .frame(width: 18, height: 18)
            .background(isPressed ? Color.cyan : Color(white: 0.25))
            .foregroundColor(isPressed ? .black : .white)
            .cornerRadius(3)
            .overlay(RoundedRectangle(cornerRadius: 3).stroke(isPressed ? Color.cyan : Color(white: 0.45), lineWidth: 1))
            .shadow(color: isPressed ? Color.cyan.opacity(0.8) : Color.clear, radius: 4)
    }

    // MARK: - 액션 버튼 클러스터
    private var actionButtonsCluster: some View {
        ZStack {
            actionBtn(label: "△", isPressed: controller.buttonTriangle, color: .cyan)
                .offset(y: -16)
            actionBtn(label: "○", isPressed: controller.buttonCircle, color: .red)
                .offset(x: 16)
            actionBtn(label: "□", isPressed: controller.buttonSquare, color: .purple)
                .offset(x: -16)
            actionBtn(label: "✕", isPressed: controller.buttonCross, color: .blue)
                .offset(y: 16)
        }
    }

    private func actionBtn(label: String, isPressed: Bool, color: Color) -> some View {
        Text(label)
            .font(.system(size: 9, weight: .bold))
            .frame(width: 18, height: 18)
            .background(isPressed ? color : Color(white: 0.22))
            .foregroundColor(isPressed ? .black : color)
            .cornerRadius(9)
            .overlay(Circle().stroke(color, lineWidth: 1))
            .shadow(color: isPressed ? color.opacity(0.8) : Color.clear, radius: 4)
    }

    // MARK: - 아날로그 스틱 시각화
    private func stickVisual(x: Float, y: Float, isClicked: Bool) -> some View {
        ZStack {
            Circle().fill(Color(white: 0.13)).frame(width: 52, height: 52)
                .overlay(Circle().stroke(Color(white: 0.24), lineWidth: 1.5))
            Circle().fill(isClicked ? Color.cyan : Color(white: 0.85))
                .frame(width: 20, height: 20)
                .offset(x: CGFloat(x) * 16, y: CGFloat(-y) * 16)
                .shadow(color: isClicked ? Color.cyan : Color.clear, radius: 4)
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

    // MARK: - 하단 실시간 보이스 명령 및 권한 가이드 박스
    private var voiceSectionBox: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                HStack(spacing: 6) {
                    Text("🎙️").font(.body)
                    Text("실시간 보이스 명령").font(.caption).bold()
                }

                Spacer()

                // 권한 배지 상태
                HStack(spacing: 8) {
                    Text("마이크: \(voice.micPermission.localizedDescription)")
                        .font(.system(size: 10, weight: .semibold))
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(voice.micPermission == .authorized ? Color.green.opacity(0.2) : (voice.micPermission == .notDetermined ? Color.blue.opacity(0.2) : Color.orange.opacity(0.2)))
                        .foregroundColor(voice.micPermission == .authorized ? .green : (voice.micPermission == .notDetermined ? .cyan : .orange))
                        .cornerRadius(4)

                    Text("음성인식: \(voice.speechPermission.localizedDescription)")
                        .font(.system(size: 10, weight: .semibold))
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(voice.speechPermission == .authorized ? Color.green.opacity(0.2) : (voice.speechPermission == .notDetermined ? Color.blue.opacity(0.2) : Color.orange.opacity(0.2)))
                        .foregroundColor(voice.speechPermission == .authorized ? .green : (voice.speechPermission == .notDetermined ? .cyan : .orange))
                        .cornerRadius(4)
                }

                Spacer()

                // 마이크 음소거 / 활성화 토글 버튼
                Button(action: {
                    voice.toggleMicMute()
                }) {
                    HStack(spacing: 6) {
                        Text(voice.isMicMuted ? "🔇 마이크 켜기 (음소거 해제)" : "🎙️ 마이크 끄기 (음소거)")
                    }
                    .font(.caption).bold()
                    .padding(.horizontal, 12).padding(.vertical, 5)
                    .background(voice.isMicMuted ? Color.orange : Color.green)
                    .foregroundColor(.white)
                    .cornerRadius(6)
                }
                .buttonStyle(.plain)
            }

            // 권한 안내 및 피드백 텍스트
            HStack(spacing: 8) {
                Text(voice.lastCommandFeedback)
                    .font(.caption).bold()
                    .foregroundColor(.cyan)
                    .lineLimit(1)

                Spacer()

                Text(voice.permissionGuideMessage)
                    .font(.system(size: 10))
                    .foregroundColor(.gray)
                    .lineLimit(1)
            }
            .padding(6)
            .background(Color(white: 0.08))
            .cornerRadius(4)

            // 볼륨 VU 미터
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 3).fill(Color(white: 0.14))
                    RoundedRectangle(cornerRadius: 3).fill(Color.green)
                        .frame(width: geo.size.width * CGFloat(voice.inputVolume))
                }
            }
            .frame(height: 4)
        }
        .padding(10)
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
