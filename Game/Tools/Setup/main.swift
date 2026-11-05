import Foundation
import Darwin
import RecoveryShared
import RecoveryCore

func writePrivate(_ data: Data, to file: URL) throws {
    let fd = open(file.path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW | O_CLOEXEC, 0o600)
    guard fd >= 0 else { throw RecoveryFailure.unsafePath }
    let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
    try handle.write(contentsOf: data); try handle.synchronize(); try handle.close()
}

do {
    guard getuid() != 0, CommandLine.arguments.count == 2 else { throw RecoveryFailure.invalidRequest }
    umask(0o077)
    let action = CommandLine.arguments[1], home = FileManager.default.homeDirectoryForCurrentUser
    let app = home.appendingPathComponent("Applications/PortKillerPet.app")
    let helper = app.appendingPathComponent("Contents/Helpers/PKPRecoveryHelper")
    let directory = RecoveryWire.directory
    let agent = home.appendingPathComponent("Library/LaunchAgents/\(RecoveryWire.service).plist")
    let policyFile = directory.appendingPathComponent("policy.json")
    let runner = BoundedCommandRunner(), domain = "gui/\(getuid())"
    switch action {
    case "inspect":
        let policy = try InstalledPolicy.load(); try policy.validate()
        print("PKP_RECOVERY_CONFIGURED uid=\(policy.ownerUID) installation=\(policy.installationID)")
    case "install":
        // Never silently replace a prior policy/helper while it could own a recovery.
        var info = stat()
        guard lstat(agent.path, &info) != 0 && errno == ENOENT,
              lstat(policyFile.path, &info) != 0 && errno == ENOENT else { throw RecoveryFailure.configurationRequired }
        let policy = try InstalledPolicy.capture(appURL: app, helperURL: helper)
        try policy.validate(); try checkedDirectory(directory, create: true)
        let job: [String: Any] = [
            "Label": RecoveryWire.service, "ProgramArguments": [helper.path, "--serve"],
            "RunAtLoad": true, "KeepAlive": ["SuccessfulExit": false], "ThrottleInterval": 15,
            "MachServices": [RecoveryWire.service: true],
            "ProcessType": "Interactive", "LimitLoadToSessionType": "Aqua",
            "StandardOutPath": directory.appendingPathComponent("helper.log").path,
            "StandardErrorPath": directory.appendingPathComponent("helper.log").path
        ]
        let plist = try PropertyListSerialization.data(fromPropertyList: job, format: .xml, options: 0)
        try writePrivate(try encoded(policy), to: policyFile)
        do {
            try writePrivate(plist, to: agent)
            let result = try runner.run("/bin/launchctl", ["bootstrap", domain, agent.path], timeout: 15)
            guard result.code == 0 else { throw RecoveryFailure.commandFailed }
            print("PKP_RECOVERY_INSTALLED; no backend/router command executed")
        } catch {
            // Retain policy/data if registration outcome is uncertain; do not erase evidence.
            fputs("PKP_RECOVERY_INSTALL_INCOMPLETE; policy retained for explicit review\n", stderr)
            throw error
        }
    case "uninstall":
        // Exact helper only; backend, router, original pet autostart and journal are preserved.
        let job = try propertyList(agent)
        guard job["Label"] as? String == RecoveryWire.service,
              job["ProgramArguments"] as? [String] == [helper.path, "--serve"] else { throw RecoveryFailure.unauthorized }
        let policy = try InstalledPolicy.load()
        guard policy.ownerUID == getuid() else { throw RecoveryFailure.unauthorized }
        let registered = try runner.run("/bin/launchctl", ["print", "\(domain)/\(RecoveryWire.service)"], timeout: 6)
        guard registered.code == 0 else { throw RecoveryFailure.manualRecovery }
        try validateRegisteredDefinition(registered.output, template: job)
        let result = try runner.run("/bin/launchctl", ["bootout", "\(domain)/\(RecoveryWire.service)"], timeout: 15)
        guard result.code == 0 else { throw RecoveryFailure.commandFailed }
        try FileManager.default.removeItem(at: agent)
        let archived = directory.appendingPathComponent("policy-removed-\(UUID().uuidString).json")
        try FileManager.default.moveItem(at: policyFile, to: archived)
        print("PKP_RECOVERY_REMOVED; journal and server services preserved")
    default: throw RecoveryFailure.invalidRequest
    }
} catch {
    let code = (error as? RecoveryFailure)?.rawValue ?? "SETUP_FAILED"
    fputs("\(code)\n", stderr); exit(1)
}
