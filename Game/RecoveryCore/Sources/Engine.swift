import Foundation
import RecoveryShared

/// All admission changes are serialized; the long service operation runs separately.
/// Authentication is performed by the native helper, never by a request Boolean.
public final class RecoveryEngine {
    private let policy: InstalledPolicy
    private let runtime: LocalRecoveryRuntime
    private let journal: RecoveryJournal
    private let admission = NSRecursiveLock()
    private let work = DispatchQueue(label: "com.veloai.portkiller.recovery.execution")
    private let now: () -> Date

    public init(policy: InstalledPolicy, runtime: LocalRecoveryRuntime, journal: RecoveryJournal,
                now: @escaping () -> Date = Date.init) {
        self.policy = policy; self.runtime = runtime; self.journal = journal; self.now = now
    }

    public func status() -> RecoveryReply {
        do {
            try policy.validate()
            let ready = try runtime.isReady()
            return RecoveryReply(message: ready ? "로컬 ALFA가 준비됐습니다." : "로컬 ALFA의 시작 또는 확인이 필요합니다.", ready: ready)
        } catch { return failure(error) }
    }

    public func plan(operationID: String) -> RecoveryReply {
        admission.lock(); defer { admission.unlock() }
        do {
            guard UUID(uuidString: operationID) != nil else { throw RecoveryFailure.invalidRequest }
            if let previous = try journal.get(operationID) {
                return RecoveryReply(message: previous.message, plan: previous.plan, record: previous)
            }
            try policy.validate()
            let snapshot = try runtime.snapshot(), expiry = now().addingTimeInterval(90)
            let binding = "\(operationID)|\(policy.installationID)|\(policy.ownerUID)|\(snapshot.definitionDigest)|\(expiry.timeIntervalSince1970)"
            let plan = RecoveryPlan(operationID: operationID, installationID: policy.installationID,
                                    ownerUID: policy.ownerUID, backendLabel: snapshot.backendLabel,
                                    backendPort: snapshot.backendPort, definitionDigest: snapshot.definitionDigest,
                                    expiresAt: expiry, digest: digest(Data(binding.utf8)))
            let record = try journal.create(plan: plan)
            return RecoveryReply(message: "현재 선택된 서버와 라우터만 시작합니다.실행 중인 서버는 강제 재시작하지 않습니다.", plan: plan, record: record)
        } catch { return failure(error) }
    }

    public func record(_ id: String) -> RecoveryReply {
        do {
            guard UUID(uuidString: id) != nil, let record = try journal.get(id) else { throw RecoveryFailure.invalidRequest }
            return RecoveryReply(message: record.message, record: record)
        } catch { return failure(error) }
    }
    public func history() -> RecoveryReply {
        do { return RecoveryReply(message: "최근 로컬 복구 기록", records: try journal.history()) }
        catch { return failure(error) }
    }

    public func validateForConfirmation(_ id: String, digest: String) throws -> RecoveryRecord {
        admission.lock(); defer { admission.unlock() }
        guard let record = try journal.get(id), record.plan.digest == digest else { throw RecoveryFailure.conflict }
        if record.state != "awaiting_confirmation" { return record }
        try validate(record.plan)
        return record
    }

    /// Only call after the helper's fresh LocalAuthentication result for this exact plan.
    public func beginConfirmed(_ id: String, digest: String) -> RecoveryReply {
        admission.lock(); defer { admission.unlock() }
        do {
            let previous = try validateForConfirmation(id, digest: digest)
            guard previous.state == "awaiting_confirmation" else {
                return RecoveryReply(message: previous.message, record: previous)
            }
            let claimed = try journal.claim(id: id, digest: digest)
            guard claimed.state == "running" else { throw RecoveryFailure.conflict }
            work.async { [self] in
                do {
                    // Detect target/policy changes even after authentication/admission.
                    try validate(claimed.plan)
                    let snapshot = try runtime.snapshot()
                    guard snapshot.backendLabel == claimed.plan.backendLabel,
                          snapshot.backendPort == claimed.plan.backendPort,
                          snapshot.definitionDigest == claimed.plan.definitionDigest else { throw RecoveryFailure.policyChanged }
                    try runtime.startAndWait(snapshot)
                    _ = try journal.transition(id: id, from: ["running"], to: "succeeded", message: "로컬 서버 준비를 확인했습니다.")
                } catch {
                    // A command may have taken effect; never replay it on an ambiguous result.
                    _ = try? journal.transition(id: id, from: ["running"], to: "unknown", message: "실제 결과 확인이 필요합니다.자동 재실행하지 않습니다.")
                }
            }
            return RecoveryReply(message: claimed.message, record: claimed)
        } catch { return failure(error) }
    }

    public func cancelled(_ id: String) -> RecoveryReply {
        admission.lock(); defer { admission.unlock() }
        do {
            let record = try journal.transition(id: id, from: ["awaiting_confirmation"], to: "cancelled", message: "사용자가 복구를 취소했습니다.")
            return RecoveryReply(message: record.message, record: record)
        } catch { return failure(error) }
    }

    /// Reconcile an interrupted operation by observing; never execute a service command here.
    public func reconcile(_ id: String, digest: String) -> RecoveryReply {
        admission.lock(); defer { admission.unlock() }
        do {
            guard let record = try journal.get(id), record.plan.digest == digest else { throw RecoveryFailure.conflict }
            guard record.state == "unknown" else { return RecoveryReply(message: record.message, record: record) }
            guard record.plan.installationID == policy.installationID, record.plan.ownerUID == policy.ownerUID else { throw RecoveryFailure.policyChanged }
            try policy.validate()
            let current = try runtime.snapshot()
            guard current.definitionDigest == record.plan.definitionDigest,
                  current.backendLabel == record.plan.backendLabel, current.backendPort == record.plan.backendPort,
                  try runtime.isReady() else { throw RecoveryFailure.manualRecovery }
            let result = try journal.transition(id: id, from: ["unknown"], to: "succeeded", message: "재실행 없이 실제 준비 상태를 확인했습니다.")
            return RecoveryReply(message: result.message, record: result)
        } catch { return failure(error) }
    }

    private func validate(_ plan: RecoveryPlan) throws {
        guard plan.expiresAt > now() else { throw RecoveryFailure.planExpired }
        guard plan.installationID == policy.installationID, plan.ownerUID == policy.ownerUID else { throw RecoveryFailure.unauthorized }
        try policy.validate()
        let current = try runtime.snapshot()
        guard current.definitionDigest == plan.definitionDigest, current.backendLabel == plan.backendLabel,
              current.backendPort == plan.backendPort else { throw RecoveryFailure.policyChanged }
    }
    private func failure(_ error: Error) -> RecoveryReply {
        let code = (error as? RecoveryFailure)?.rawValue ?? "RECOVERY_FAILED"
        return RecoveryReply(error: code, message: "복구 요청을 처리하지 못했습니다.\(code)")
    }
}
