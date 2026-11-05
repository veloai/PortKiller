import Foundation
import Darwin
import RecoveryShared

public struct RecoverySnapshot: Codable, Equatable {
    public let backendLabel: String
    public let backendPort: Int
    public let definitionDigest: String
    public init(backendLabel: String, backendPort: Int, definitionDigest: String) {
        self.backendLabel = backendLabel; self.backendPort = backendPort; self.definitionDigest = definitionDigest
    }
}
public protocol LocalRecoveryRuntime {
    func snapshot() throws -> RecoverySnapshot
    func isReady() throws -> Bool
    func startAndWait(_ snapshot: RecoverySnapshot) throws
}
private final class HealthProbe: NSObject, URLSessionDataDelegate {
    let done = DispatchSemaphore(value: 0)
    private let lock = NSLock()
    private var body = Data()
    private var accepted = false
    private var healthy = false
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        lock.lock(); accepted = (response as? HTTPURLResponse)?.statusCode == 200 && response.expectedContentLength <= 65_536
        let allow = accepted; lock.unlock(); completionHandler(allow ? .allow : .cancel)
    }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        lock.lock()
        guard accepted, body.count + data.count <= 65_536 else { accepted = false; lock.unlock(); dataTask.cancel(); return }
        body.append(data); lock.unlock()
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        lock.lock()
        if error == nil, accepted, let value = try? JSONSerialization.jsonObject(with: body) as? [String: Any] { healthy = value["status"] as? String == "UP" }
        lock.unlock(); done.signal()
    }
    func result() -> Bool { lock.lock(); defer { lock.unlock() }; return healthy }
}
private final class LocalHTTP {
    func healthy(_ port: Int) -> Bool {
        let config = URLSessionConfiguration.ephemeral
        config.connectionProxyDictionary = [:]; config.timeoutIntervalForRequest = 2; config.timeoutIntervalForResource = 3
        config.httpShouldSetCookies = false; config.urlCredentialStorage = nil
        let probe = HealthProbe()
        let session = URLSession(configuration: config, delegate: probe, delegateQueue: nil)
        defer { session.invalidateAndCancel() }
        let task = session.dataTask(with: URL(string: "http://127.0.0.1:\(port)/actuator/health")!)
        task.resume()
        guard probe.done.wait(timeout: .now() + 4) == .success else { task.cancel(); return false }
        return probe.result()
    }
}

public final class AirRecoveryRuntime: LocalRecoveryRuntime {
    private let policy: InstalledPolicy
    private let runner: RecoveryCommandRunning
    private let http = LocalHTTP()
    public init(policy: InstalledPolicy, runner: RecoveryCommandRunning = BoundedCommandRunner()) {
        self.policy = policy; self.runner = runner
    }
    public func snapshot() throws -> RecoverySnapshot {
        let target = URL(fileURLWithPath: policy.home).appendingPathComponent(".alfa/router-target")
        let text = String(decoding: try checkedData(target, maximum: 64), as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
        guard let port = Int(text), [7711, 7712].contains(port) else { throw RecoveryFailure.configurationRequired }
        let definition = try encoded(policy) + Data("|\(port)".utf8)
        return RecoverySnapshot(backendLabel: port == 7711 ? "com.alfa.backend.blue" : "com.alfa.backend.green",
                                backendPort: port, definitionDigest: digest(definition))
    }
    public func isReady() throws -> Bool {
        try policy.validate()
        let target = try snapshot()
        guard try ownedReady(target.backendLabel, port: target.backendPort), try ownedReady("com.alfa.router", port: 7701) else { return false }
        return try snapshot() == target
    }
    public func startAndWait(_ approved: RecoverySnapshot) throws {
        try policy.validate(); try unchanged(approved)
        try startIfStopped(approved.backendLabel, port: approved.backendPort, approved: approved)
        try wait(approved.backendLabel, port: approved.backendPort, approved: approved)
        try unchanged(approved)
        try startIfStopped("com.alfa.router", port: 7701, approved: approved)
        try wait("com.alfa.router", port: 7701, approved: approved)
        guard try isReady() else { throw RecoveryFailure.manualRecovery }
    }
    private func unchanged(_ approved: RecoverySnapshot) throws {
        guard try snapshot() == approved else { throw RecoveryFailure.policyChanged }
    }
    private func plist(_ label: String) throws -> URL {
        guard ["com.alfa.backend.blue", "com.alfa.backend.green", "com.alfa.router"].contains(label) else { throw RecoveryFailure.unauthorized }
        let file = URL(fileURLWithPath: policy.home).appendingPathComponent("Library/LaunchAgents/\(label).plist")
        guard policy.permits(file) else { throw RecoveryFailure.policyChanged }; return file
    }
    private func job(_ label: String) throws -> (registered: Bool, pid: Int32?) {
        let file = try plist(label)
        let result = try runner.run("/bin/launchctl", ["print", "gui/\(policy.ownerUID)/\(label)"], timeout: 6)
        if result.code != 0 {
            guard result.code == 113 && result.output.contains("Could not find service") else { throw RecoveryFailure.manualRecovery }
            return (false, nil)
        }
        let template = try propertyList(file)
        try validateRegisteredDefinition(result.output, template: template)
        let pid = capture(result.output, "(?m)^\\s*pid = ([0-9]+)$").flatMap(Int32.init)
        return (true, pid)
    }
    private func listeners(_ port: Int) throws -> Set<Int32> {
        let result = try runner.run("/usr/sbin/lsof", ["-nP", "-iTCP:\(port)", "-sTCP:LISTEN", "-t"], timeout: 6)
        if result.code == 1 && result.output.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return [] }
        guard result.code == 0 else { throw RecoveryFailure.manualRecovery }
        let values = result.output.split(whereSeparator: \.isWhitespace)
        let ids = values.compactMap { Int32($0) }
        guard ids.count == values.count else { throw RecoveryFailure.manualRecovery }; return Set(ids)
    }
    private func ownedReady(_ label: String, port: Int) throws -> Bool {
        let before = try job(label)
        guard let pid = before.pid else { return false }
        guard try listeners(port) == [pid] else { return false }
        let owner = try runner.run("/bin/ps", ["-p", String(pid), "-o", "uid="], timeout: 4)
        guard owner.code == 0, owner.output.trimmingCharacters(in: .whitespacesAndNewlines) == String(policy.ownerUID) else { return false }
        guard http.healthy(port), try job(label).pid == pid, try listeners(port) == [pid] else { return false }
        return true
    }
    private func startIfStopped(_ label: String, port: Int, approved: RecoverySnapshot) throws {
        let state = try job(label), portOwners = try listeners(port)
        if let pid = state.pid {
            guard portOwners.isEmpty || portOwners == [pid] else { throw RecoveryFailure.manualRecovery }
            return // Running but unhealthy: observe only; never kill or kickstart -k.
        }
        guard portOwners.isEmpty else { throw RecoveryFailure.manualRecovery }
        // A newly bootstrapped job inherits its login domain. Inspect this as well as
        // existing job definitions; do not assume the helper's clean env controls launchd.
        let domain = try runner.run("/bin/launchctl", ["print", "gui/\(policy.ownerUID)"], timeout: 6)
        guard domain.code == 0 else { throw RecoveryFailure.manualRecovery }
        try validateInheritedEnvironment(domain.output)
        try policy.validate(); try unchanged(approved)
        let arguments = state.registered ? ["kickstart", "gui/\(policy.ownerUID)/\(label)"] :
                                             ["bootstrap", "gui/\(policy.ownerUID)", try plist(label).path]
        let result = try runner.run("/bin/launchctl", arguments, timeout: 12)
        guard result.code == 0 else { throw RecoveryFailure.commandFailed }
        try unchanged(approved)
    }
    private func wait(_ label: String, port: Int, approved: RecoverySnapshot) throws {
        let deadline = Date().addingTimeInterval(90)
        repeat {
            try unchanged(approved)
            if try ownedReady(label, port: port) { return }
            Thread.sleep(forTimeInterval: 0.5)
        } while Date() < deadline
        throw RecoveryFailure.commandTimeout
    }
    private func capture(_ source: String, _ pattern: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: source, range: NSRange(source.startIndex..., in: source)),
              let range = Range(match.range(at: 1), in: source) else { return nil }
        return String(source[range]).trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
