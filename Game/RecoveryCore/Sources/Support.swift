import Foundation
import CryptoKit
import Darwin

public enum RecoveryFailure: String, Error {
    case invalidRequest = "INVALID_REQUEST", policyChanged = "POLICY_CHANGED"
    case unsafePath = "UNSAFE_PATH", configurationRequired = "CONFIGURATION_REQUIRED"
    case commandFailed = "COMMAND_FAILED", commandTimeout = "COMMAND_TIMEOUT"
    case manualRecovery = "MANUAL_RECOVERY_REQUIRED", stateUnavailable = "STATE_UNAVAILABLE"
    case busy = "RESOURCE_BUSY", conflict = "REQUEST_CONFLICT", planExpired = "PLAN_EXPIRED"
    case unauthorized = "LOCAL_ACCESS_DENIED", cancelled = "USER_CANCELLED"
}
public func digest(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }
public func encoded<T: Encodable>(_ value: T) throws -> Data {
    let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys]; return try encoder.encode(value)
}
public func checkedDirectory(_ url: URL, create: Bool = false) throws {
    if create && !FileManager.default.fileExists(atPath: url.path) {
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
    }
    var info = stat()
    guard lstat(url.path, &info) == 0, info.st_mode & S_IFMT == S_IFDIR,
          info.st_uid == getuid(), info.st_mode & 0o077 == 0 else { throw RecoveryFailure.unsafePath }
    var parent = url.deletingLastPathComponent()
    while parent.path != "/" {
        var ancestor = stat()
        guard lstat(parent.path, &ancestor) == 0, ancestor.st_mode & S_IFMT == S_IFDIR,
              ancestor.st_uid == getuid() || ancestor.st_uid == 0,
              ancestor.st_mode & 0o022 == 0 else { throw RecoveryFailure.unsafePath }
        parent.deleteLastPathComponent()
    }
}
public func checkedData(_ url: URL, maximum: Int = 1_048_576, privateFile: Bool = false) throws -> Data {
    let fd = open(url.path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC)
    guard fd >= 0 else { throw RecoveryFailure.unsafePath }; defer { close(fd) }
    var info = stat()
    guard fstat(fd, &info) == 0, info.st_mode & S_IFMT == S_IFREG, info.st_uid == getuid(),
          info.st_nlink == 1, info.st_size <= maximum,
          info.st_mode & (privateFile ? 0o077 : 0o022) == 0 else { throw RecoveryFailure.unsafePath }
    let data = try FileHandle(fileDescriptor: fd, closeOnDealloc: false).read(upToCount: maximum + 1) ?? Data()
    var after = stat()
    guard data.count <= maximum, fstat(fd, &after) == 0, after.st_size == info.st_size,
          after.st_mtimespec.tv_sec == info.st_mtimespec.tv_sec,
          after.st_mtimespec.tv_nsec == info.st_mtimespec.tv_nsec else { throw RecoveryFailure.unsafePath }; return data
}
public struct CommandResult {
    public let code: Int32
    public let output: String
    public init(code: Int32, output: String) { self.code = code; self.output = output }
}
public protocol RecoveryCommandRunning {
    func run(_ executable: String, _ arguments: [String], timeout: TimeInterval) throws -> CommandResult
}
public final class BoundedCommandRunner: RecoveryCommandRunning {
    public init() {}
    public func run(_ executable: String, _ arguments: [String], timeout: TimeInterval = 8) throws -> CommandResult {
        let task = Process()
        // A private temporary file avoids waiting forever for EOF from a descendant.
        let temporary = FileManager.default.temporaryDirectory.appendingPathComponent("pkp-command-\(UUID().uuidString)")
        let fd = open(temporary.path, O_RDWR | O_CREAT | O_EXCL | O_NOFOLLOW | O_CLOEXEC, 0o600)
        guard fd >= 0 else { throw RecoveryFailure.stateUnavailable }
        let outputFile = FileHandle(fileDescriptor: fd, closeOnDealloc: false)
        defer { try? outputFile.close(); try? FileManager.default.removeItem(at: temporary) }
        // Commands may print environment values. Keep only the anonymous open file,
        // never a named output/log that another component could later collect.
        guard unlink(temporary.path) == 0 else { throw RecoveryFailure.unsafePath }
        task.executableURL = URL(fileURLWithPath: executable); task.arguments = arguments
        task.environment = ["PATH": "/usr/bin:/bin:/usr/sbin:/sbin", "HOME": NSHomeDirectory(), "LC_ALL": "C"]
        task.standardOutput = outputFile; task.standardError = outputFile
        try task.run()
        let deadline = Date().addingTimeInterval(timeout)
        var oversized = false
        while task.isRunning && Date() < deadline {
            var info = stat()
            guard fstat(fd, &info) == 0 else { task.terminate(); throw RecoveryFailure.stateUnavailable }
            if info.st_size > 1_048_576 { oversized = true; break }
            Thread.sleep(forTimeInterval: 0.025)
        }
        let timedOut = task.isRunning && !oversized
        if task.isRunning {
            task.terminate(); let grace = Date().addingTimeInterval(1)
            while task.isRunning && Date() < grace { Thread.sleep(forTimeInterval: 0.025) }
            if task.isRunning { kill(task.processIdentifier, SIGKILL) }
        }
        let finalDeadline = Date().addingTimeInterval(1)
        while task.isRunning && Date() < finalDeadline { Thread.sleep(forTimeInterval: 0.025) }
        if timedOut { throw RecoveryFailure.commandTimeout }
        guard !task.isRunning, !oversized else { throw RecoveryFailure.commandFailed }
        try outputFile.seek(toOffset: 0)
        let data = try outputFile.read(upToCount: 1_048_577) ?? Data()
        guard data.count <= 1_048_576 else { throw RecoveryFailure.commandFailed }
        return CommandResult(code: task.terminationStatus, output: String(decoding: data, as: UTF8.self))
    }
}
