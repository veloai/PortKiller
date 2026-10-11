import Foundation
import Speech
import AVFoundation

/// Apple Speech & AVFoundation 기반의 로컬 저지연 음성 명령 매니저
public final class VoiceManager: NSObject, ObservableObject {
    public static let shared = VoiceManager()

    @Published public var isListening: Bool = false
    @Published public var recognizedText: String = ""
    @Published public var lastCommandFeedback: String = "대기 중 ('물약', '지도', '도발' 등을 말해보세요)"
    @Published public var inputVolume: Float = 0.0

    private let audioEngine = AVAudioEngine()
    private var speechRecognizer = SFSpeechRecognizer(locale: Locale(identifier: "ko-KR"))
    private var recognitionRequest: SFSpeechAudioBufferRecognitionRequest?
    private var recognitionTask: SFSpeechRecognitionTask?

    public override init() {
        super.init()
        requestAuthorization()
    }

    private func requestAuthorization() {
        SFSpeechRecognizer.requestAuthorization { authStatus in
            DispatchQueue.main.async {
                switch authStatus {
                case .authorized:
                    self.lastCommandFeedback = "음성 인식 준비 완료 (마이크를 켜세요)"
                case .denied, .restricted:
                    self.lastCommandFeedback = "음성 인식 권한이 거부되었습니다."
                case .notDetermined:
                    self.lastCommandFeedback = "음성 권한 대기 중"
                @unknown default:
                    break
                }
            }
        }
    }

    public func toggleListening() {
        if isListening {
            stopListening()
        } else {
            startListening()
        }
    }

    public func startListening() {
        guard !audioEngine.isRunning else { return }

        recognitionTask?.cancel()
        recognitionTask = nil

        let audioSession = AVCaptureDevice.default(for: .audio)
        _ = audioSession

        recognitionRequest = SFSpeechAudioBufferRecognitionRequest()
        guard let request = recognitionRequest else { return }
        request.shouldReportPartialResults = true

        let inputNode = audioEngine.inputNode
        let recordingFormat = inputNode.outputFormat(forBus: 0)

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
            lastCommandFeedback = "🎙️ 듣는 중... 말해보세요"
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
                self.stopListening()
            }
        }
    }

    public func stopListening() {
        audioEngine.stop()
        audioEngine.inputNode.removeTap(onBus: 0)
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
        } else if text.contains("지도") {
            lastCommandFeedback = "🗺️ [지도] 감지됨! → M키 세계 지도 토글"
        } else if text.contains("도발") {
            lastCommandFeedback = "⚔️ [도발] 감지됨! → T키 전사 도발 스킬 발동"
        } else if text.contains("탈것") {
            lastCommandFeedback = "🐎 [탈것] 감지됨! → Z키 탈것 탑승"
        } else if text.contains("가방") {
            lastCommandFeedback = "🎒 [가방] 감지됨! → B키 가방 열기"
        } else if text.contains("전투") {
            lastCommandFeedback = "🛡️ [전투 태세] 감지됨! → F1키 태세 전환"
        }
    }
}
