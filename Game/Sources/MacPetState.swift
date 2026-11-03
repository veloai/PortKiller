import Foundation
import AppKit
import SwiftUI

/// macOS 네이티브 데스크톱/상태창 펫 상태 관리자 (다마고치 요소 제거 - 앰비언트 비서/마스코트 모드)
@MainActor
public final class MacPetState: ObservableObject {
    public static let shared = MacPetState()

    @Published public var species: String = "duck"
    @Published public var stage: String = ""
    @Published public var pose: String = "idle"   // "idle", "walk1", "walk2", "happy", "shock", "sleep"

    @Published public var speechMessage: String? = "꽥! 버그 없는 평화로운 개발 환경 가동 중 🦆"
    @Published public var devStatusSummary: String = "Docker & ALFA 백엔드 정상"

    private var speechTimer: Timer?
    private var animationTimer: Timer?
    private var idleWalkState: Int = 0

    public init() {
        startAnimationLoop()
        scheduleSpeechClear(after: 5.0)
    }

    /// 스프라이트 이미지 파일 경로 조회
    public func resolveCurrentSprite() -> NSImage? {
        let duckDirs = [
            "assets/sprites/duck",
            "../assets/sprites/duck",
            "/Users/aiso/Monster/github/PortKiller/assets/sprites/duck",
            Bundle.main.resourcePath.map { "\($0)/assets/sprites/duck" } ?? ""
        ]
        for dir in duckDirs where !dir.isEmpty {
            let path = "\(dir)/\(pose).png"
            if FileManager.default.fileExists(atPath: path), let img = NSImage(contentsOfFile: path) {
                return img
            }
        }
        for dir in duckDirs where !dir.isEmpty {
            let fallback = "\(dir)/idle.png"
            if FileManager.default.fileExists(atPath: fallback), let img = NSImage(contentsOfFile: fallback) {
                return img
            }
        }
        return loadLegacySprite(subpath: "\(species)/\(stage)/\(pose)")
    }

    private func loadLegacySprite(subpath: String) -> NSImage? {
        let candidateDirs = [
            "assets/sprites/chars",
            "../assets/sprites/chars",
            "/Users/aiso/Monster/github/PortKiller/assets/sprites/chars",
            Bundle.main.resourcePath.map { "\($0)/assets/sprites/chars" } ?? ""
        ]
        for dir in candidateDirs where !dir.isEmpty {
            let fullPath = "\(dir)/\(subpath).png"
            if FileManager.default.fileExists(atPath: fullPath), let img = NSImage(contentsOfFile: fullPath) {
                return img
            }
        }
        return nil
    }

    // MARK: - 알림 및 브리핑

    /// 개발 환경 및 시스템 브리핑
    public func briefing() {
        pose = "happy"
        say(message: "📊 \(devStatusSummary) - mac-air 연결 완료!", duration: 4.0)
        revertToIdle(after: 3.0)
    }

    /// 상태 메시지 / 말풍선 출력
    public func say(message: String, duration: TimeInterval = 3.5) {
        speechTimer?.invalidate()
        speechMessage = message
        scheduleSpeechClear(after: duration)
    }

    private func scheduleSpeechClear(after duration: TimeInterval) {
        speechTimer = Timer.scheduledTimer(withTimeInterval: duration, repeats: false) { [weak self] _ in
            Task { @MainActor in
                self?.speechMessage = nil
            }
        }
    }

    private func revertToIdle(after duration: TimeInterval) {
        DispatchQueue.main.asyncAfter(deadline: .now() + duration) { [weak self] in
            guard let self = self else { return }
            if self.pose == "happy" {
                self.pose = "idle"
            }
        }
    }

    // MARK: - 애니메이션 루프 (상태창/데스크톱 앰비언트 산책)

    private func startAnimationLoop() {
        animationTimer = Timer.scheduledTimer(withTimeInterval: 2.5, repeats: true) { [weak self] _ in
            Task { @MainActor in
                guard let self = self else { return }
                if self.pose == "idle" || self.pose == "walk1" || self.pose == "walk2" {
                    self.idleWalkState = (self.idleWalkState + 1) % 4
                    switch self.idleWalkState {
                    case 0: self.pose = "idle"
                    case 1: self.pose = "walk1"
                    case 2: self.pose = "walk2"
                    case 3: self.pose = "idle"
                    default: self.pose = "idle"
                    }
                }
            }
        }
    }
}
