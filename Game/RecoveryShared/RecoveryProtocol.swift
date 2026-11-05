import Foundation

public enum RecoveryWire {
    public static let service = "com.veloai.portkiller.recovery"
    public static let maximumBytes = 65_536
    public static var directory: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/PortKillerRecovery")
    }
}
@objc public protocol PKPRecoveryProtocol {
    func handle(_ request: Data, withReply reply: @escaping (Data) -> Void)
}
public struct RecoveryRequest: Codable, Sendable {
    public let action: String
    public let operationID: String?
    public let planDigest: String?
    public init(action: String, operationID: String? = nil, planDigest: String? = nil) {
        self.action = action; self.operationID = operationID; self.planDigest = planDigest
    }
}
public struct RecoveryPlan: Codable, Equatable, Sendable {
    public let operationID: String
    public let installationID: String
    public let ownerUID: UInt32
    public let backendLabel: String
    public let backendPort: Int
    public let definitionDigest: String
    public let expiresAt: Date
    public let digest: String
    public init(operationID: String, installationID: String, ownerUID: UInt32, backendLabel: String,
                backendPort: Int, definitionDigest: String, expiresAt: Date, digest: String) {
        self.operationID = operationID; self.installationID = installationID; self.ownerUID = ownerUID
        self.backendLabel = backendLabel; self.backendPort = backendPort
        self.definitionDigest = definitionDigest; self.expiresAt = expiresAt; self.digest = digest
    }
}
public struct RecoveryRecord: Codable, Equatable, Identifiable, Sendable {
    public var id: String { operationID }
    public let operationID: String
    public let state: String
    public let message: String
    public let updatedAt: Date
    public let plan: RecoveryPlan
    public init(operationID: String, state: String, message: String, updatedAt: Date, plan: RecoveryPlan) {
        self.operationID = operationID; self.state = state; self.message = message
        self.updatedAt = updatedAt; self.plan = plan
    }
}
public struct RecoveryReply: Codable, Sendable {
    public var error: String?
    public var message: String
    public var ready: Bool?
    public var plan: RecoveryPlan?
    public var record: RecoveryRecord?
    public var records: [RecoveryRecord]?
    public init(error: String? = nil, message: String, ready: Bool? = nil,
                plan: RecoveryPlan? = nil, record: RecoveryRecord? = nil, records: [RecoveryRecord]? = nil) {
        self.error = error; self.message = message; self.ready = ready
        self.plan = plan; self.record = record; self.records = records
    }
}
