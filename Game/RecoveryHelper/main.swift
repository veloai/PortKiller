import Foundation
import LocalAuthentication
import Darwin
import RecoveryShared
import RecoveryCore

final class RecoveryService: NSObject, PKPRecoveryProtocol {
    let engine: RecoveryEngine
    let queue = DispatchQueue(label: "com.veloai.portkiller.recovery.requests")
    private var authentication: (id: String, context: LAContext, connection: ObjectIdentifier, token: UUID, reconcile: Bool, reply: (Data) -> Void)?
    init(engine: RecoveryEngine) { self.engine = engine }

    func handle(_ request: Data, withReply reply: @escaping (Data) -> Void) {
        // Connection code requirement is enforced by NSXPC for incoming messages.
        guard let connection = NSXPCConnection.current(), connection.effectiveUserIdentifier == getuid(),
              request.count <= RecoveryWire.maximumBytes else {
            send(RecoveryReply(error: "LOCAL_ACCESS_DENIED", message: "허용된 로컬 앱 연결이 아닙니다."), reply); return
        }
        queue.async { [self] in
            do {
                guard let object = try JSONSerialization.jsonObject(with: request) as? [String: Any],
                      Set(object.keys).isSubset(of: ["action", "operationID", "planDigest"]) else { throw RecoveryFailure.invalidRequest }
                let input = try JSONDecoder().decode(RecoveryRequest.self, from: request)
                switch input.action {
                case "status": send(engine.status(), reply)
                case "history": send(engine.history(), reply)
                case "plan":
                    guard let id = input.operationID else { throw RecoveryFailure.invalidRequest }
                    send(engine.plan(operationID: id), reply)
                case "receipt":
                    guard let id = input.operationID else { throw RecoveryFailure.invalidRequest }
                    send(engine.record(id), reply)
                case "execute", "reconcile":
                    guard let id = input.operationID, let digest = input.planDigest else { throw RecoveryFailure.invalidRequest }
                    let record = try engine.validateForConfirmation(id, digest: digest)
                    let reconcile = input.action == "reconcile"
                    if record.state != (reconcile ? "unknown" : "awaiting_confirmation") { send(engine.record(id), reply); return }
                    guard authentication == nil else { throw RecoveryFailure.busy }
                    let context = LAContext(); context.localizedCancelTitle = "취소"
                    var error: NSError?
                    guard context.canEvaluatePolicy(.deviceOwnerAuthentication, error: &error) else { throw RecoveryFailure.unauthorized }
                    let token = UUID()
                    authentication = (id, context, ObjectIdentifier(connection), token, reconcile, reply)
                    let reason = reconcile ? "PKP가 이 Mac의 서버 상태를 재확인하여 복구 결과를 기록합니다.서버 시작 명령은 실행하지 않습니다." :
                        "PKP가 이 Mac의 \(record.plan.backendLabel) 서버와 라우터를 시작합니다. 실행 중인 서버는 종료하지 않습니다."
                    let remaining = reconcile ? 90 : max(0, min(90, record.plan.expiresAt.timeIntervalSinceNow))
                    queue.asyncAfter(deadline: .now() + remaining) { [weak self] in self?.cancelAuthentication(token: token) }
                    context.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: reason) { [weak self] success, _ in
                        guard let self else { return }
                        self.queue.async {
                            guard self.authentication?.token == token else { return }
                            self.authentication = nil; context.invalidate()
                            let result = success ? (reconcile ? self.engine.reconcile(id, digest: digest) : self.engine.beginConfirmed(id, digest: digest)) :
                                (reconcile ? self.engine.record(id) : self.engine.cancelled(id))
                            self.send(result, reply)
                        }
                    }
                default: throw RecoveryFailure.invalidRequest
                }
            } catch {
                send(RecoveryReply(error: (error as? RecoveryFailure)?.rawValue ?? "RECOVERY_FAILED",
                                   message: "복구 요청을 거절했습니다.설정과 현재 상태를 확인해 주세요."), reply)
            }
        }
    }
    func disconnected(_ identifier: ObjectIdentifier) {
        queue.async { [self] in
            guard let pending = authentication, pending.connection == identifier else { return }
            cancelAuthentication(token: pending.token)
        }
    }
    private func cancelAuthentication(token: UUID) {
        guard let pending = authentication, pending.token == token else { return }
        authentication = nil; pending.context.invalidate()
        send(pending.reconcile ? engine.record(pending.id) : engine.cancelled(pending.id), pending.reply)
    }
    private func send(_ result: RecoveryReply, _ reply: @escaping (Data) -> Void) {
        guard let data = try? encoded(result), data.count <= RecoveryWire.maximumBytes else {
            reply(Data("{\"error\":\"RESPONSE_LIMIT\",\"message\":\"결과 크기 제한\"}".utf8)); return
        }
        reply(data)
    }
}

final class RecoveryListener: NSObject, NSXPCListenerDelegate {
    let policy: InstalledPolicy
    let service: RecoveryService
    init(policy: InstalledPolicy, service: RecoveryService) { self.policy = policy; self.service = service }
    func listener(_ listener: NSXPCListener, shouldAcceptNewConnection connection: NSXPCConnection) -> Bool {
        guard connection.effectiveUserIdentifier == policy.ownerUID else { return false }
        connection.setCodeSigningRequirement(policy.appRequirement)
        connection.exportedInterface = NSXPCInterface(with: PKPRecoveryProtocol.self)
        connection.exportedObject = service
        let identifier = ObjectIdentifier(connection)
        connection.invalidationHandler = { [weak service = service] in service?.disconnected(identifier) }
        connection.interruptionHandler = { [weak service = service] in service?.disconnected(identifier) }
        connection.resume()
        return true
    }
}

do {
    guard CommandLine.arguments == [CommandLine.arguments[0], "--serve"], getuid() != 0 else { throw RecoveryFailure.unauthorized }
    umask(0o077)
    let directory = RecoveryWire.directory
    try checkedDirectory(directory)
    let lockURL = directory.appendingPathComponent("helper.lock")
    let lockFD = open(lockURL.path, O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC, 0o600)
    guard lockFD >= 0 else { throw RecoveryFailure.unsafePath }
    _ = try checkedData(lockURL, maximum: 64, privateFile: true)
    guard flock(lockFD, LOCK_EX | LOCK_NB) == 0 else { throw RecoveryFailure.busy }
    let policy = try InstalledPolicy.load()
    try policy.validate()
    let journal = try RecoveryJournal(directory: directory)
    let engine = RecoveryEngine(policy: policy, runtime: AirRecoveryRuntime(policy: policy), journal: journal)
    let service = RecoveryService(engine: engine)
    let delegate = RecoveryListener(policy: policy, service: service)
    let listener = NSXPCListener(machServiceName: RecoveryWire.service)
    listener.delegate = delegate; listener.resume()
    // Keep listener, delegate, journal and the flock alive independently of the app/ALFA.
    withExtendedLifetime((listener, delegate, service, journal, lockFD)) { RunLoop.main.run() }
    close(lockFD)
} catch {
    fputs("PKP_RECOVERY_UNAVAILABLE\n", stderr)
    exit(1)
}
