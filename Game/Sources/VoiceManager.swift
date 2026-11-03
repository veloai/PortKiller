import Foundation
import Speech
import AVFoundation
import AppKit
import CoreAudio

/// 오디오 및 음성인식 권한 상태 정의
public enum AudioPermissionStatus: Equatable {
    case notDetermined
    case restricted
    case denied
    case authorized

    public var localizedDescription: String {
        switch self {
        case .notDetermined: return "미결정 (권한 요청 대기)"
        case .restricted: return "제한됨 (시스템 정책)"
        case .denied: return "거부됨 (설정에서 허용 필요)"
        case .authorized: return "허용됨 (정상)"
        }
    }
}

/// Apple Speech & AVFoundation & CoreAudio 기반의 마이크 음소거 및 로컬 저지연 음성 명령 매니저
@MainActor
public final class VoiceManager: NSObject, ObservableObject {
    public static let shared = VoiceManager()

    @Published public var isMicMuted: Bool = false
    @Published public var isListening: Bool = false
    @Published public var recognizedText: String = ""
    @Published public var lastCommandFeedback: String = "대기 중 ('물약', '지도', '도발' 등을 말해보세요)"
    @Published public var inputVolume: Float = 0.0

    // 권한 상태 관리
    @Published public var micPermission: AudioPermissionStatus = .notDetermined
    @Published public var speechPermission: AudioPermissionStatus = .notDetermined
    @Published public var permissionGuideMessage: String = ""

    private let audioEngine = AVAudioEngine()
    private var speechRecognizer = SFSpeechRecognizer(locale: Locale(identifier: "ko-KR"))
    private var recognitionRequest: SFSpeechAudioBufferRecognitionRequest?
    private var recognitionTask: SFSpeechRecognitionTask?

    public var canStartListening: Bool {
        return micPermission == .authorized && speechPermission == .authorized
    }

    public override init() {
        super.init()
        checkPermissionStatus()
        checkSystemMicMute()
    }

    /// 현재 시스템 권한 상태를 조회하고 안내 문구를 갱신합니다. (팝업 수락이나 강제 시작 없음)
    public func checkPermissionStatus() {
        let micRaw = AVCaptureDevice.authorizationStatus(for: .audio)
        switch micRaw {
        case .authorized: self.micPermission = .authorized
        case .denied: self.micPermission = .denied
        case .restricted: self.micPermission = .restricted
        case .notDetermined: self.micPermission = .notDetermined
        @unknown default: self.micPermission = .notDetermined
        }

        let speechRaw = SFSpeechRecognizer.authorizationStatus()
        switch speechRaw {
        case .authorized: self.speechPermission = .authorized
        case .denied: self.speechPermission = .denied
        case .restricted: self.speechPermission = .restricted
        case .notDetermined: self.speechPermission = .notDetermined
        @unknown default: self.speechPermission = .notDetermined
        }

        updateGuideMessage()
    }

    public func updateGuideMessage() {
        if micPermission == .authorized && speechPermission == .authorized {
            permissionGuideMessage = "✅ 마이크 및 음성 인식 권한이 모두 허용되어 정상 작동 가능합니다."
        } else if micPermission == .denied || speechPermission == .denied {
            permissionGuideMessage = "🚫 권한 거부됨: [시스템 설정 > 개인정보 보호 및 보안 > 마이크 및 음성 인식]에서 PortKiller 앱을 활성화해야 합니다."
        } else {
            permissionGuideMessage = "⚠️ 권한 대기 중: 마이크(\(micPermission.localizedDescription)), 음성 인식(\(speechPermission.localizedDescription)). 사용자의 승인이 필요합니다."
        }
    }

    /// 마이크 권한 요청 (macOS 시스템 승인 팝업 요청)
    public func requestMicrophonePermission(completion: (@Sendable (Bool) -> Void)? = nil) {
        AVCaptureDevice.requestAccess(for: .audio) { [weak self] granted in
            DispatchQueue.main.async {
                self?.checkPermissionStatus()
                completion?(granted)
            }
        }
    }

    /// 음성인식 권한 요청 (macOS 시스템 승인 팝업 요청)
    public func requestSpeechRecognitionPermission(completion: (@Sendable (SFSpeechRecognizerAuthorizationStatus) -> Void)? = nil) {
        SFSpeechRecognizer.requestAuthorization { [weak self] status in
            DispatchQueue.main.async {
                self?.checkPermissionStatus()
                completion?(status)
            }
        }
    }

    /// 사용자가 UI 버튼을 클릭했을 때 순차적으로 마이크 및 음성 인식 권한을 시스템에 요청합니다.
    public func requestPermissions() {
        lastCommandFeedback = "📢 macOS 시스템 권한 요청 팝업이 표시됩니다. '허용'을 선택해 주세요."

        if micPermission == .notDetermined {
            AVCaptureDevice.requestAccess(for: .audio) { [weak self] granted in
                DispatchQueue.main.async {
                    guard let self = self else { return }
                    self.checkPermissionStatus()

                    if granted && self.speechPermission == .notDetermined {
                        SFSpeechRecognizer.requestAuthorization { [weak self] _ in
                            DispatchQueue.main.async {
                                self?.checkPermissionStatus()
                                if self?.canStartListening == true {
                                    self?.lastCommandFeedback = "✅ 권한이 모두 승인되었습니다. [마이크 듣기 시작] 버튼을 누르면 작동합니다."
                                }
                            }
                        }
                    } else if granted {
                        if self.speechPermission == .authorized {
                            self.lastCommandFeedback = "✅ 권한이 모두 승인되었습니다. [마이크 듣기 시작] 버튼을 누르면 작동합니다."
                        }
                    } else {
                        self.lastCommandFeedback = "❌ 마이크 권한이 거부되었습니다. [시스템 설정에서 권한 허용] 버튼을 눌러주세요."
                    }
                }
            }
        } else if speechPermission == .notDetermined {
            SFSpeechRecognizer.requestAuthorization { [weak self] status in
                DispatchQueue.main.async {
                    guard let self = self else { return }
                    self.checkPermissionStatus()
                    if status == .authorized {
                        self.lastCommandFeedback = "✅ 권한이 모두 승인되었습니다. [마이크 듣기 시작] 버튼을 누르면 작동합니다."
                    } else {
                        self.lastCommandFeedback = "❌ 음성 인식 권한이 거부되었습니다. [시스템 설정에서 권한 허용] 버튼을 눌러주세요."
                    }
                }
            }
        } else if micPermission == .denied || speechPermission == .denied || micPermission == .restricted || speechPermission == .restricted {
            openSystemSettings()
        }
    }

    /// 거부 또는 제한된 권한을 수정할 수 있도록 macOS 개인정보 보호 설정 창을 엽니다.
    public func openSystemSettings() {
        lastCommandFeedback = "⚙️ [시스템 설정 > 개인정보 보호 및 보안 > 마이크] 설정 창을 엽니다."
        if let micUrl = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone"),
           NSWorkspace.shared.open(micUrl) {
            return
        }
        if let generalUrl = URL(string: "x-apple.systempreferences:") {
            NSWorkspace.shared.open(generalUrl)
        }
    }

    // MARK: - CoreAudio 시스템 마이크 음소거 제어
    public func checkSystemMicMute() {
        var defaultInputDeviceID = AudioDeviceID(0)
        var propertySize = UInt32(MemoryLayout<AudioDeviceID>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultInputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        let err = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &propertySize, &defaultInputDeviceID)
        guard err == noErr else { return }

        var muteAddress = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyMute,
            mScope: kAudioDevicePropertyScopeInput,
            mElement: kAudioObjectPropertyElementMain
        )
        if AudioObjectHasProperty(defaultInputDeviceID, &muteAddress) {
            var isMuted: UInt32 = 0
            var muteSize = UInt32(MemoryLayout<UInt32>.size)
            if AudioObjectGetPropertyData(defaultInputDeviceID, &muteAddress, 0, nil, &muteSize, &isMuted) == noErr {
                self.isMicMuted = (isMuted != 0)
            }
        }
    }

    public func setSystemMicMute(_ mute: Bool) {
        var defaultInputDeviceID = AudioDeviceID(0)
        var propertySize = UInt32(MemoryLayout<AudioDeviceID>.size)
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultInputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        let err = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &propertySize, &defaultInputDeviceID)
        guard err == noErr else { return }

        var muteAddress = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyMute,
            mScope: kAudioDevicePropertyScopeInput,
            mElement: kAudioObjectPropertyElementMain
        )
        var val: UInt32 = mute ? 1 : 0
        let setErr = AudioObjectSetPropertyData(defaultInputDeviceID, &muteAddress, 0, nil, UInt32(MemoryLayout<UInt32>.size), &val)
        if setErr == noErr {
            self.isMicMuted = mute
        } else {
            // Mute 프로퍼티 미지원 장치인 경우 볼륨 0 조절 대체
            var volAddress = AudioObjectPropertyAddress(
                mSelector: kAudioDevicePropertyVolumeScalar,
                mScope: kAudioDevicePropertyScopeInput,
                mElement: kAudioObjectPropertyElementMain
            )
            var vol: Float32 = mute ? 0.0 : 0.75
            _ = AudioObjectSetPropertyData(defaultInputDeviceID, &volAddress, 0, nil, UInt32(MemoryLayout<Float32>.size), &vol)
            self.isMicMuted = mute
        }

        self.lastCommandFeedback = mute ? "🔇 마이크 꺼짐 (음소거됨)" : "🎙️ 마이크 켜짐 (정상 동작)"

        if mute && isListening {
            stopListening()
        }
    }

    public func toggleMicMute() {
        setSystemMicMute(!isMicMuted)
    }

    /// 버튼 클릭 시 동작: 마이크 음소거 토글 및 음성인식 연동
    public func toggleListening() {
        if isMicMuted {
            setSystemMicMute(false)
            if canStartListening && !isListening {
                startListening()
            }
        } else {
            setSystemMicMute(true)
            if isListening {
                stopListening()
            }
        }
    }

    public func startListening() {
        guard canStartListening else {
            lastCommandFeedback = "⚠️ 마이크/음성 권한 미부여 상태입니다 (설정 필요)"
            if micPermission == .notDetermined || speechPermission == .notDetermined {
                requestPermissions()
            }
            return
        }

        if isMicMuted {
            setSystemMicMute(false)
        }

        guard !audioEngine.isRunning else { return }

        recognitionTask?.cancel()
        recognitionTask = nil

        let inputNode = audioEngine.inputNode
        let recordingFormat = inputNode.outputFormat(forBus: 0)

        // 오디오 포맷 유효성 검사 (0Hz 또는 0채널일 때 크래시 방지)
        guard recordingFormat.sampleRate > 0, recordingFormat.channelCount > 0 else {
            lastCommandFeedback = "오디오 입력 장치를 초기화할 수 없습니다. 마이크 연결을 확인하세요."
            return
        }

        recognitionRequest = SFSpeechAudioBufferRecognitionRequest()
        guard let request = recognitionRequest else { return }
        request.shouldReportPartialResults = true

        inputNode.removeTap(onBus: 0)
        inputNode.installTap(onBus: 0, bufferSize: 1024, format: recordingFormat) { [weak self] buffer, _ in
            self?.recognitionRequest?.append(buffer)

            // 볼륨 미터 계산
            let channelData = buffer.floatChannelData?[0]
            let frames = buffer.frameLength
            if let data = channelData, frames > 0 {
                var sum: Float = 0
                for i in 0..<Int(frames) { sum += abs(data[i]) }
                let avg = sum / Float(frames)
                DispatchQueue.main.async {
                    self?.inputVolume = min(1.0, avg * 8.0)
                }
            }
        }

        audioEngine.prepare()
        do {
            try audioEngine.start()
            isListening = true
            lastCommandFeedback = "🎙️ 마이크 켜짐: 음성 명령 대기 중..."
        } catch {
            lastCommandFeedback = "마이크 시작 실패: \(error.localizedDescription)"
            return
        }

        recognitionTask = speechRecognizer?.recognitionTask(with: request) { [weak self] result, error in
            guard let self = self else { return }

            if let result = result {
                let text = result.bestTranscription.formattedString.trimmingCharacters(in: .whitespacesAndNewlines)
                DispatchQueue.main.async {
                    self.recognizedText = text
                    self.evaluateCommand(text)
                }
            }

            if error != nil || (result?.isFinal ?? false) {
                // 발화 문장 종료 후에도 마이크가 켜져 있으면 연속 청취 유지
                DispatchQueue.main.async {
                    if self.isListening && !self.isMicMuted {
                        self.restartRecognitionTask()
                    }
                }
            }
        }
    }

    private func restartRecognitionTask() {
        recognitionTask?.cancel()
        recognitionTask = nil
        recognitionRequest?.endAudio()

        recognitionRequest = SFSpeechAudioBufferRecognitionRequest()
        guard let request = recognitionRequest, audioEngine.isRunning else { return }
        request.shouldReportPartialResults = true

        recognitionTask = speechRecognizer?.recognitionTask(with: request) { [weak self] result, error in
            guard let self = self else { return }
            if let result = result {
                let text = result.bestTranscription.formattedString.trimmingCharacters(in: .whitespacesAndNewlines)
                DispatchQueue.main.async {
                    self.recognizedText = text
                    self.evaluateCommand(text)
                }
            }
            if error != nil || (result?.isFinal ?? false) {
                DispatchQueue.main.async {
                    if self.isListening && !self.isMicMuted {
                        self.restartRecognitionTask()
                    }
                }
            }
        }
    }

    public func stopListening() {
        if audioEngine.isRunning {
            audioEngine.stop()
            audioEngine.inputNode.removeTap(onBus: 0)
        }
        recognitionRequest?.endAudio()
        recognitionRequest = nil
        recognitionTask?.cancel()
        recognitionTask = nil
        isListening = false
        inputVolume = 0.0
    }

    private func evaluateCommand(_ text: String) {
        if text.contains("물약") {
            lastCommandFeedback = "⚡ [물약] 감지됨! → 9번 슬롯 치유 물약 발동"
            InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.num9, isPressed: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.num9, isPressed: false)
            }
        } else if text.contains("지도") {
            lastCommandFeedback = "🗺️ [지도] 감지됨! → M키 세계 지도 토글"
            InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.m, isPressed: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.m, isPressed: false)
            }
        } else if text.contains("도발") {
            lastCommandFeedback = "⚔️ [도발] 감지됨! → T키 전사 도발 스킬 발동"
            InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.t, isPressed: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.t, isPressed: false)
            }
        } else if text.contains("탈것") {
            lastCommandFeedback = "🐎 [탈것] 감지됨! → Z키 탈것 탑승"
            InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.z, isPressed: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.z, isPressed: false)
            }
        } else if text.contains("가방") {
            lastCommandFeedback = "🎒 [가방] 감지됨! → B키 가방 열기"
            InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.b, isPressed: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.b, isPressed: false)
            }
        } else if text.contains("전투") {
            lastCommandFeedback = "🛡️ [전투 태세] 감지됨! → 1번 스킬 발동"
            InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.num1, isPressed: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
                InputSimulator.shared.sendKey(keyCode: InputSimulator.Keys.num1, isPressed: false)
            }
        }
    }
}
