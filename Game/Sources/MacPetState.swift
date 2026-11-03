import Foundation
import AppKit
import SwiftUI

/// macOS 네이티브 데스크톱 펫 상태 관리자
@MainActor
public final class MacPetState: ObservableObject {
    public static let shared = MacPetState()

    @Published public var species: String = "day" // "day" (레서판다/다람쥐 갈색), "evening"
    @Published public var stage: String = "child"  // "baby", "child", "adult", "egg"
    @Published public var pose: String = "idle"   // "idle", "walk1", "walk2", "happy", "eat", "sleep", "sulk", "sick"

    @Published public var speechMessage: String? = "반가워요! mac-air에서 일하는 중이에요~ 🐾"
    @Published public var isPoopPresent: Bool = false
    @Published public var hunger: Double = 85.0
    @Published public var hygiene: Double = 90.0
    @Published public var happiness: Double = 95.0

    private var speechTimer: Timer?
    private var animationTimer: Timer?
    private var idleWalkState: Int = 0

    public init() {
        startAnimationLoop()
        scheduleSpeechClear(after: 4.0)
    }

    /// 스프라이트 이미지 파일 경로 조회
    public func resolveCurrentSprite() -> NSImage? {
        if stage == "egg" {
            return loadSpriteImage(subpath: "egg/\(pose)")
        }
        return loadSpriteImage(subpath: "\(species)/\(stage)/\(pose)")
    }

    /// 똥 스프라이트 이미지 파일 경로 조회
    public func resolvePoopSprite() -> NSImage? {
        let candidateDirs = [
            "assets/sprites",
            "../assets/sprites",
            "/Users/aiso/Monster/github/PortKiller/assets/sprites"
        ]
        for dir in candidateDirs {
            let path = "\(dir)/poop.png"
            if FileManager.default.fileExists(atPath: path), let img = NSImage(contentsOfFile: path) {
                return img
            }
        }
        return nil
    }

    private func loadSpriteImage(subpath: String) -> NSImage? {
        let candidateDirs = [
            "assets/sprites/chars",
            "../assets/sprites/chars",
            "/Users/aiso/Monster/github/PortKiller/assets/sprites/chars",
            Bundle.main.resourcePath.map { "\($0)/assets/sprites/chars" } ?? ""
        ]
        for dir in candidateDirs {
            let fullPath = "\(dir)/\(subpath).png"
            if FileManager.default.fileExists(atPath: fullPath), let img = NSImage(contentsOfFile: fullPath) {
                return img
            }
        }
        // 기본 idle fallback
        for dir in candidateDirs {
            let fallback = "\(dir)/day/child/idle.png"
            if FileManager.default.fileExists(atPath: fallback), let img = NSImage(contentsOfFile: fallback) {
                return img
            }
        }
        return nil
    }

    // MARK: - 인터랙션

    /// 쓰다듬기 반응
    public func pet() {
        pose = "happy"
        happiness = min(100.0, happiness + 10.0)
        say(message: "기분 최고예요! 헤헤~ ❤️", duration: 3.0)
        revertToIdle(after: 2.5)
    }

    /// 간식 주기
    public func feed() {
        pose = "eat"
        hunger = min(100.0, hunger + 25.0)
        say(message: "냠냠! 맛있는 간식 고마워요! 🍎", duration: 3.0)
        revertToIdle(after: 2.5)
    }

    /// 똥 치우기
    public func cleanPoop() {
        if isPoopPresent {
            isPoopPresent = false
            hygiene = 100.0
            say(message: "청소해 줘서 상쾌해요! ✨", duration: 3.0)
        } else {
            say(message: "지금은 깨끗해요! 문제 없어요~", duration: 2.0)
        }
    }

    /// 브리핑 보고
    public func briefing() {
        pose = "happy"
        say(message: "📊 mac-air 개발 환경 정상 가동 중! 오늘도 화이팅!", duration: 4.0)
        revertToIdle(after: 3.0)
    }

    /// 말풍선 출력
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
            if self.pose == "happy" || self.pose == "eat" {
                self.pose = "idle"
            }
        }
    }

    // MARK: - 애니메이션 루프

    private func startAnimationLoop() {
        animationTimer = Timer.scheduledTimer(withTimeInterval: 2.5, repeats: true) { [weak self] _ in
            Task { @MainActor in
                guard let self = self else { return }
                // 특수 포즈 중이 아니면 걷기/아이들 교대
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
