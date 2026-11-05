import Foundation
import Darwin
import CryptoKit
import RecoveryShared

public struct FilePin: Codable, Equatable {
    public let path: String
    public let canonicalPath: String
    public let sha256: String?
    public static func capture(_ url: URL) throws -> FilePin {
        var info = stat()
        if lstat(url.path, &info) != 0 {
            guard errno == ENOENT else { throw RecoveryFailure.unsafePath }
            return FilePin(path: url.path, canonicalPath: url.resolvingSymlinksInPath().path, sha256: nil)
        }
        guard info.st_mode & S_IFMT == S_IFREG, info.st_nlink == 1,
              info.st_uid == getuid() || info.st_uid == 0, info.st_mode & 0o022 == 0,
              info.st_size < 1_500_000_000 else { throw RecoveryFailure.unsafePath }
        let canonical = url.resolvingSymlinksInPath()
        let fd = open(canonical.path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC)
        guard fd >= 0 else { throw RecoveryFailure.unsafePath }; defer { close(fd) }
        var opened = stat()
        guard fstat(fd, &opened) == 0, opened.st_dev == info.st_dev, opened.st_ino == info.st_ino else { throw RecoveryFailure.unsafePath }
        let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: false)
        var hash = SHA256(); var size = 0
        while let chunk = try handle.read(upToCount: 1_048_576), !chunk.isEmpty {
            size += chunk.count
            guard size <= opened.st_size else { throw RecoveryFailure.policyChanged }
            hash.update(data: chunk)
        }
        var after = stat()
        guard fstat(fd, &after) == 0, after.st_size == opened.st_size,
              after.st_mtimespec.tv_sec == opened.st_mtimespec.tv_sec,
              after.st_mtimespec.tv_nsec == opened.st_mtimespec.tv_nsec else { throw RecoveryFailure.policyChanged }
        return FilePin(path: url.path, canonicalPath: canonical.path,
                       sha256: hash.finalize().map { String(format: "%02x", $0) }.joined())
    }
}

public struct InstalledPolicy: Codable {
    public let version: Int
    public let installationID: String
    public let ownerUID: UInt32
    public let appRequirement: String
    public let helperRequirement: String
    public let home: String
    public let pins: [FilePin]

    public static func load(directory: URL = RecoveryWire.directory) throws -> InstalledPolicy {
        try checkedDirectory(directory)
        let data = try checkedData(directory.appendingPathComponent("policy.json"), privateFile: true)
        let result = try JSONDecoder().decode(InstalledPolicy.self, from: data)
        guard result.version == 1, result.ownerUID == getuid(), result.home == NSHomeDirectory(),
              UUID(uuidString: result.installationID) != nil, !result.appRequirement.isEmpty,
              !result.helperRequirement.isEmpty, (10...32).contains(result.pins.count) else { throw RecoveryFailure.unauthorized }
        for label in ["com.alfa.backend.blue", "com.alfa.backend.green", "com.alfa.router"] {
            let path = URL(fileURLWithPath: result.home).appendingPathComponent("Library/LaunchAgents/\(label).plist")
            guard result.permits(path) else { throw RecoveryFailure.configurationRequired }
        }
        return result
    }
    public func validate() throws {
        guard ownerUID == getuid(), home == NSHomeDirectory(), version == 1 else { throw RecoveryFailure.unauthorized }
        for pin in pins {
            guard try FilePin.capture(URL(fileURLWithPath: pin.path)) == pin else { throw RecoveryFailure.policyChanged }
        }
    }
    public func permits(_ url: URL) -> Bool { pins.contains { $0.path == url.path && $0.sha256 != nil } }
    public static func codeRequirement(_ url: URL) throws -> String {
        let runner = BoundedCommandRunner()
        let verify = try runner.run("/usr/bin/codesign", ["--verify", "--strict", url.path], timeout: 15)
        guard verify.code == 0 else { throw RecoveryFailure.configurationRequired }
        let result = try runner.run("/usr/bin/codesign", ["-d", "-r-", url.path], timeout: 15)
        guard result.code == 0 else { throw RecoveryFailure.configurationRequired }
        return try parseRequirement(result.output)
    }
    static func parseRequirement(_ output: String) throws -> String {
        // codesign prefixes an implicit ad-hoc requirement with '# '. The actual
        // requirement is still the exact cdhash, not a made-up developer identity.
        let prefixes = ["designated => ", "# designated => "]
        let lines = output.components(separatedBy: "\n").filter { line in prefixes.contains { line.hasPrefix($0) } }
        guard lines.count == 1, let line = lines.first,
              let prefix = prefixes.first(where: { line.hasPrefix($0) }) else { throw RecoveryFailure.configurationRequired }
        let requirement = String(line.dropFirst(prefix.count))
        guard !requirement.isEmpty, requirement.utf8.count < 16_384 else { throw RecoveryFailure.configurationRequired }
        return requirement
    }
    public static func capture(appURL: URL, helperURL: URL) throws -> InstalledPolicy {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let agents = home.appendingPathComponent("Library/LaunchAgents")
        var urls = [appURL.appendingPathComponent("Contents/MacOS/PortKillerPet"), helperURL]
        let launcher = home.appendingPathComponent("Monster/github/llmwk/scripts/alfa-backend-launch")
        urls.append(launcher)
        let common = ["alfa-device.env", "alfa-backend.env", "alfa-telegram.env", "alfa-terminal.env"]
        for name in common + ["alfa-backend-blue.env", "alfa-backend-green.env"] {
            urls.append(home.appendingPathComponent(".agent-context/\(name)"))
        }
        // Interpret exactly the bytes whose fingerprints will be installed. Capture absent
        // optional files too; a file appearing during capture invalidates the installation.
        var captured: [String: FilePin] = [:]
        func pin(_ file: URL) throws -> FilePin {
            if let previous = captured[file.path] { return previous }
            let value = try FilePin.capture(file); captured[file.path] = value; return value
        }
        func data(_ file: URL) throws -> Data {
            let expected = try pin(file)
            guard let fingerprint = expected.sha256 else { throw RecoveryFailure.configurationRequired }
            let bytes = try checkedData(file)
            guard digest(bytes) == fingerprint else { throw RecoveryFailure.policyChanged }
            return bytes
        }
        func plist(_ file: URL) throws -> [String: Any] {
            guard let value = try PropertyListSerialization.propertyList(from: data(file), format: nil) as? [String: Any] else { throw RecoveryFailure.configurationRequired }
            return value
        }
        for url in urls { _ = try pin(url) }
        for color in ["blue", "green"] {
            let file = agents.appendingPathComponent("com.alfa.backend.\(color).plist")
            urls.append(file)
            let job = try plist(file)
            guard job["Label"] as? String == "com.alfa.backend.\(color)",
                  job["ProgramArguments"] as? [String] == [launcher.path],
                  let environment = job["EnvironmentVariables"] as? [String: String],
                  environment["ALFA_COLOR"] == color,
                  environment["ALFA_PORT"] == (color == "blue" ? "7711" : "7712") else { throw RecoveryFailure.configurationRequired }
            guard environment["HOME"] == nil || environment["HOME"] == home.path else { throw RecoveryFailure.configurationRequired }
            var values = environment; values["HOME"] = home.path
            for name in common + ["alfa-backend-\(color).env"] {
                let url = home.appendingPathComponent(".agent-context/\(name)")
                if try pin(url).sha256 != nil { values = try RecoveryEnvironment.read(data(url), base: values) }
            }
            for flag in ["ALFA_HERMES_AUTOSTART", "ALFA_BUZZ_AUTOSTART", "ALFA_BUZZ_RELAY_AUTOSTART", "ALFA_BUZZ_SEED_ON_START"] {
                guard values[flag] == "0" else { throw RecoveryFailure.configurationRequired }
            }
            guard !values.keys.contains(where: RecoveryEnvironment.isInjectionVariable),
                  values["HOME"] == home.path, RecoveryEnvironment.isLocalProfile(values["ALFA_PROFILE"]) else { throw RecoveryFailure.configurationRequired }
            guard let jar = values["ALFA_RUNTIME_JAR"], jar.hasPrefix(home.path + "/"),
                  let javaHome = values["JAVA_HOME"] ?? environment["JAVA_HOME"], javaHome.hasPrefix("/"),
                  values["ALFA_PORT"] == (color == "blue" ? "7711" : "7712") else { throw RecoveryFailure.configurationRequired }
            for key in ["ALFA_DB_URL", "ALFA_R2DBC_URL"] {
                guard let raw = values[key], RecoveryEnvironment.isAirDatabase(raw) else { throw RecoveryFailure.configurationRequired }
            }
            urls.append(URL(fileURLWithPath: jar)); urls.append(URL(fileURLWithPath: javaHome).appendingPathComponent("bin/java"))
        }
        let router = agents.appendingPathComponent("com.alfa.router.plist")
        let routerJob = try plist(router)
        let routerEnvironment = routerJob["EnvironmentVariables"] as? [String: String] ?? [:]
        guard !routerEnvironment.keys.contains(where: RecoveryEnvironment.isInjectionVariable),
              routerEnvironment["HOME"] == nil || routerEnvironment["HOME"] == home.path else { throw RecoveryFailure.configurationRequired }
        guard routerJob["Label"] as? String == "com.alfa.router",
              let routerArgs = routerJob["ProgramArguments"] as? [String], routerArgs.count == 6,
              routerArgs[1] == home.appendingPathComponent("Monster/github/llmwk/scripts/alfa-port-router").path,
              Array(routerArgs[2...]) == ["--listen", "7701", "--target-file", home.appendingPathComponent(".alfa/router-target").path] else { throw RecoveryFailure.configurationRequired }
        urls += [router, URL(fileURLWithPath: routerArgs[0]), URL(fileURLWithPath: routerArgs[1])]
        for url in urls { _ = try pin(url) }
        let pins = captured.values.sorted { $0.path < $1.path }
        let result = InstalledPolicy(version: 1, installationID: UUID().uuidString, ownerUID: getuid(),
                               appRequirement: try codeRequirement(appURL), helperRequirement: try codeRequirement(helperURL),
                               home: home.path, pins: pins)
        try result.validate()
        return result
    }
}

public func propertyList(_ file: URL) throws -> [String: Any] {
    guard let result = try PropertyListSerialization.propertyList(from: checkedData(file), format: nil) as? [String: Any] else { throw RecoveryFailure.configurationRequired }
    return result
}

/// launchctl output stays in memory only. Never persist its environment values.
public func validateRegisteredDefinition(_ output: String, template: [String: Any]) throws {
    func capture(_ pattern: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: output, range: NSRange(output.startIndex..., in: output)),
              let range = Range(match.range(at: 1), in: output) else { return nil }
        return String(output[range]).trimmingCharacters(in: .whitespacesAndNewlines)
    }
    guard let args = template["ProgramArguments"] as? [String], !args.isEmpty,
          capture("(?m)^[ \\t]*program = (.+)$") == (template["Program"] as? String ?? args[0]),
          let block = capture("(?ms)^[ \\t]*arguments = \\{[ \\t]*\\n(.*?)^[ \\t]*\\}") else { throw RecoveryFailure.manualRecovery }
    let actual = block.components(separatedBy: "\n").map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }
    guard actual == args else { throw RecoveryFailure.manualRecovery }
    // Also inspect inherited/default environment blocks for execution injection.
    try validateInheritedEnvironment(output)
    let expected = template["EnvironmentVariables"] as? [String: String] ?? [:]
    if !expected.isEmpty {
        guard let environment = capture("(?ms)^[ \\t]*environment = \\{[ \\t]*\\n(.*?)^[ \\t]*\\}") else { throw RecoveryFailure.manualRecovery }
        var found: [String: String] = [:]
        for line in environment.components(separatedBy: "\n") {
            let parts = line.trimmingCharacters(in: .whitespaces).components(separatedBy: " => ")
            if parts.count == 2 { found[parts[0]] = parts[1] }
        }
        for (key, value) in expected { guard found[key] == value else { throw RecoveryFailure.manualRecovery } }
    }
}

public func validateInheritedEnvironment(_ output: String) throws {
    let regex = try NSRegularExpression(pattern: "(?m)^[ \\t]*([A-Za-z_][A-Za-z0-9_]*) => (.*)$")
    for match in regex.matches(in: output, range: NSRange(output.startIndex..., in: output)) {
        guard let key = Range(match.range(at: 1), in: output) else { throw RecoveryFailure.manualRecovery }
        let name = String(output[key])
        guard !RecoveryEnvironment.isInjectionVariable(name) else { throw RecoveryFailure.manualRecovery }
    }
}

/// Never source a shell file to discover a recipe. Unsupported shell syntax fails closed.
enum RecoveryEnvironment {
    static func isLocalProfile(_ raw: String?) -> Bool {
        guard let raw else { return true }
        let names = raw.components(separatedBy: ",")
        return names == ["local-pg"] || (names.count == 2 && Set(names) == ["local-pg", "wiki-git"])
    }
    static func isAirDatabase(_ raw: String) -> Bool {
        let address = raw.replacingOccurrences(of: "r2dbc:", with: "").replacingOccurrences(of: "jdbc:", with: "")
        guard let url = URLComponents(string: address), url.scheme == "postgresql",
              url.host == "127.0.0.1", url.port == 15432, url.path == "/alfa_air_local", url.fragment == nil else { return false }
        // currentSchema is part of the existing Air launcher, not a second DB.
        // No query option may override the endpoint or inject arbitrary connection options.
        let items = url.queryItems ?? []
        return items.isEmpty || (items.count == 1 && items[0].name == "currentSchema" && items[0].value == "alfa")
    }
    static func isInjectionVariable(_ key: String) -> Bool {
        ["BASH_ENV", "ENV", "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "_JAVA_OPTIONS", "NODE_OPTIONS", "NODE_PATH", "SHELLOPTS", "BASHOPTS", "CDPATH"].contains(key)
        || key.hasPrefix("DYLD_") || key.hasPrefix("LD_") || key.hasPrefix("BASH_FUNC_")
    }
    static func read(_ data: Data, base: [String: String]) throws -> [String: String] {
        guard let text = String(data: data, encoding: .utf8), !text.contains("\0"), !text.contains("\r") else { throw RecoveryFailure.configurationRequired }
        var values = base
        for raw in text.components(separatedBy: "\n") {
            var line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty || line.hasPrefix("#") { continue }
            if line.hasPrefix("export ") { line = String(line.dropFirst(7)).trimmingCharacters(in: .whitespaces) }
            guard let equal = line.firstIndex(of: "=") else { throw RecoveryFailure.configurationRequired }
            let key = String(line[..<equal])
            guard key.range(of: "^[A-Za-z_][A-Za-z0-9_]*$", options: .regularExpression) != nil else { throw RecoveryFailure.configurationRequired }
            let rhs = Array(line[line.index(after: equal)...]); var i = 0; var quote: Character?; var value = ""
            while i < rhs.count {
                let c = rhs[i]
                if quote == "'" { if c == "'" { quote = nil } else { value.append(c) }; i += 1; continue }
                if c == "\"" { quote = quote == "\"" ? nil : "\""; i += 1; continue }
                if quote == nil && c == "'" { quote = "'"; i += 1; continue }
                if c == "\\" {
                    guard i + 1 < rhs.count else { throw RecoveryFailure.configurationRequired }
                    let next = rhs[i + 1]
                    if quote == "\"" && !["$", "`", "\"", "\\"].contains(next) { value.append("\\") }
                    value.append(next); i += 2; continue
                }
                if c == "$" {
                    i += 1; let name: String
                    if i < rhs.count && rhs[i] == "{" {
                        i += 1; let start = i
                        while i < rhs.count && rhs[i] != "}" { i += 1 }
                        guard i < rhs.count else { throw RecoveryFailure.configurationRequired }
                        name = String(rhs[start..<i]); i += 1
                    } else {
                        let start = i
                        while i < rhs.count && String(rhs[i]).range(of: "^[A-Za-z0-9_]$", options: .regularExpression) != nil { i += 1 }
                        name = String(rhs[start..<i])
                    }
                    guard let resolved = values[name] else { throw RecoveryFailure.configurationRequired }; value += resolved; continue
                }
                if c == "`" || (quote == nil && ";&|<>()~".contains(c)) { throw RecoveryFailure.configurationRequired }
                if quote == nil && (c == " " || c == "\t") {
                    let tail = String(rhs[i...]).trimmingCharacters(in: .whitespaces)
                    guard tail.isEmpty || tail.hasPrefix("#") else { throw RecoveryFailure.configurationRequired }; break
                }
                value.append(c); i += 1
            }
            guard quote == nil else { throw RecoveryFailure.configurationRequired }; values[key] = value
        }
        return values
    }
}
