import XCTest
import Darwin
@testable import RecoveryCore
import RecoveryShared
import CSQLite

final class FakeRuntime: LocalRecoveryRuntime {
    let lock = NSLock()
    var value = RecoverySnapshot(backendLabel: "com.alfa.backend.blue", backendPort: 7711, definitionDigest: String(repeating: "a", count: 64))
    var ready = false
    var starts = 0
    var startError: Error?
    var snapshotCalls = 0
    var switchOnCall: Int?
    func snapshot() throws -> RecoverySnapshot {
        lock.lock(); defer { lock.unlock() }; snapshotCalls += 1
        if snapshotCalls == switchOnCall {
            value = RecoverySnapshot(backendLabel: "com.alfa.backend.green", backendPort: 7712, definitionDigest: String(repeating: "b", count: 64))
        }
        return value
    }
    func isReady() throws -> Bool { lock.lock(); defer { lock.unlock() }; return ready }
    func startAndWait(_ snapshot: RecoverySnapshot) throws {
        lock.lock(); defer { lock.unlock() }; starts += 1
        if let startError { throw startError }; ready = true
    }
}

final class RecoveryTests: XCTestCase {
    func directory() throws -> URL {
        // The production journal rejects writable ancestors such as /private/tmp.
        let url = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".pkp-recovery-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        addTeardownBlock { try? FileManager.default.removeItem(at: url) }; return url
    }
    func policy(id: String = UUID().uuidString) -> InstalledPolicy {
        InstalledPolicy(version: 1, installationID: id, ownerUID: getuid(), appRequirement: "fixture", helperRequirement: "fixture", home: NSHomeDirectory(), pins: [])
    }
    func plan(id: String = UUID().uuidString, installation: String = UUID().uuidString) -> RecoveryPlan {
        RecoveryPlan(operationID: id, installationID: installation, ownerUID: getuid(), backendLabel: "com.alfa.backend.blue", backendPort: 7711,
                     definitionDigest: String(repeating: "a", count: 64), expiresAt: Date().addingTimeInterval(90), digest: String(repeating: "b", count: 64))
    }
    func waitForTerminal(_ engine: RecoveryEngine, _ id: String) throws -> RecoveryRecord {
        let until = Date().addingTimeInterval(3)
        while Date() < until {
            if let r = engine.record(id).record, r.state != "running" { return r }
            Thread.sleep(forTimeInterval: 0.01)
        }
        XCTFail("local fixture timed out"); throw RecoveryFailure.commandTimeout
    }
    func testPlanIsDurableAndSameRequestDoesNotCreateAnotherPlan() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime()
        let engine = RecoveryEngine(policy: policy(), runtime: runtime, journal: journal)
        let id = UUID().uuidString
        let first = engine.plan(operationID: id)
        XCTAssertEqual(first.plan, engine.plan(operationID: id).plan)
        XCTAssertEqual(try journal.history().count, 1); XCTAssertEqual(runtime.starts, 0)
    }
    func testWrongDigestCannotStart() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime()
        let engine = RecoveryEngine(policy: policy(), runtime: runtime, journal: journal)
        let id = UUID().uuidString; _ = engine.plan(operationID: id)
        XCTAssertNotNil(engine.beginConfirmed(id, digest: "wrong").error)
        XCTAssertEqual(runtime.starts, 0)
    }
    func testCancelledPlanCannotBeExecuted() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime()
        let engine = RecoveryEngine(policy: policy(), runtime: runtime, journal: journal)
        let p = try XCTUnwrap(engine.plan(operationID: UUID().uuidString).plan)
        _ = engine.cancelled(p.operationID)
        XCTAssertEqual(engine.beginConfirmed(p.operationID, digest: p.digest).record?.state, "cancelled")
        XCTAssertEqual(runtime.starts, 0)
    }
    func testDuplicateExecutionStartsOnlyOnce() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime()
        let engine = RecoveryEngine(policy: policy(), runtime: runtime, journal: journal)
        let p = try XCTUnwrap(engine.plan(operationID: UUID().uuidString).plan)
        _ = engine.beginConfirmed(p.operationID, digest: p.digest)
        _ = engine.beginConfirmed(p.operationID, digest: p.digest)
        XCTAssertEqual(try waitForTerminal(engine, p.operationID).state, "succeeded")
        XCTAssertEqual(runtime.starts, 1)
    }
    func testChangedActiveTargetRejectsApprovedPlan() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime()
        let engine = RecoveryEngine(policy: policy(), runtime: runtime, journal: journal)
        let p = try XCTUnwrap(engine.plan(operationID: UUID().uuidString).plan)
        runtime.value = RecoverySnapshot(backendLabel: "com.alfa.backend.green", backendPort: 7712, definitionDigest: String(repeating: "c", count: 64))
        XCTAssertEqual(engine.beginConfirmed(p.operationID, digest: p.digest).error, "POLICY_CHANGED")
        XCTAssertEqual(runtime.starts, 0)
    }
    func testAmbiguousCommandBlocksOtherRecoveryAndDoesNotReplay() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime()
        runtime.startError = RecoveryFailure.commandTimeout
        let engine = RecoveryEngine(policy: policy(), runtime: runtime, journal: journal)
        let first = try XCTUnwrap(engine.plan(operationID: UUID().uuidString).plan)
        _ = engine.beginConfirmed(first.operationID, digest: first.digest)
        XCTAssertEqual(try waitForTerminal(engine, first.operationID).state, "unknown")
        let other = try XCTUnwrap(engine.plan(operationID: UUID().uuidString).plan)
        XCTAssertEqual(engine.beginConfirmed(other.operationID, digest: other.digest).error, "RESOURCE_BUSY")
        _ = engine.beginConfirmed(first.operationID, digest: first.digest)
        XCTAssertEqual(runtime.starts, 1)
    }
    func testHelperRestartPreservesUncertainOperation() throws {
        let dir = try directory(), p = plan()
        do {
            let journal = try RecoveryJournal(directory: dir)
            _ = try journal.create(plan: p); _ = try journal.claim(id: p.operationID, digest: p.digest)
        }
        let reopened = try RecoveryJournal(directory: dir)
        XCTAssertEqual(try reopened.get(p.operationID)?.state, "unknown")
        XCTAssertEqual(try reopened.claim(id: p.operationID, digest: p.digest).state, "unknown")
    }
    func testReconciliationDoesNotExecuteService() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime(), installed = policy()
        let p = plan(installation: installed.installationID)
        _ = try journal.create(plan: p); _ = try journal.claim(id: p.operationID, digest: p.digest)
        _ = try journal.transition(id: p.operationID, from: ["running"], to: "unknown", message: "never store secret")
        runtime.ready = true
        let engine = RecoveryEngine(policy: installed, runtime: runtime, journal: journal)
        XCTAssertEqual(engine.reconcile(p.operationID, digest: p.digest).record?.state, "succeeded")
        XCTAssertEqual(runtime.starts, 0)
    }
    func testDifferentInstallationCannotReconcileOldOperation() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime(), p = plan()
        _ = try journal.create(plan: p); _ = try journal.claim(id: p.operationID, digest: p.digest)
        _ = try journal.transition(id: p.operationID, from: ["running"], to: "unknown", message: "")
        runtime.ready = true
        XCTAssertEqual(RecoveryEngine(policy: policy(), runtime: runtime, journal: journal).reconcile(p.operationID, digest: p.digest).error, "POLICY_CHANGED")
    }
    func testSymlinkJournalIsRejected() throws {
        let dir = try directory(), target = dir.appendingPathComponent("elsewhere")
        try Data().write(to: target)
        try FileManager.default.createSymbolicLink(at: dir.appendingPathComponent("recovery.sqlite3"), withDestinationURL: target)
        XCTAssertThrowsError(try RecoveryJournal(directory: dir))
    }
    func testDanglingJournalSidecarIsRejected() throws {
        let dir = try directory()
        try FileManager.default.createSymbolicLink(at: dir.appendingPathComponent("recovery.sqlite3-journal"), withDestinationURL: dir.appendingPathComponent("missing"))
        XCTAssertThrowsError(try RecoveryJournal(directory: dir))
    }
    func testEnvironmentNeverExecutesShellAndPreservesLiteralValues() throws {
        XCTAssertThrowsError(try RecoveryEnvironment.read(Data("VALUE=$(touch /tmp/never-run)".utf8), base: [:]))
        XCTAssertThrowsError(try RecoveryEnvironment.read(Data("source /tmp/never-source".utf8), base: [:]))
        let env = try RecoveryEnvironment.read(Data("A='literal $VALUE'\nB=\"${HOME}/safe\"".utf8), base: ["HOME": "/fixture"])
        XCTAssertEqual(env["A"], "literal $VALUE"); XCTAssertEqual(env["B"], "/fixture/safe")
        XCTAssertTrue(RecoveryEnvironment.isLocalProfile("local-pg,wiki-git"))
        XCTAssertFalse(RecoveryEnvironment.isLocalProfile("local-pg,production"))
        XCTAssertTrue(RecoveryEnvironment.isAirDatabase("jdbc:postgresql://127.0.0.1:15432/alfa_air_local?currentSchema=alfa"))
        XCTAssertFalse(RecoveryEnvironment.isAirDatabase("jdbc:postgresql://127.0.0.1:5432/alfa_air_local?currentSchema=alfa"))
        XCTAssertFalse(RecoveryEnvironment.isAirDatabase("r2dbc:postgresql://127.0.0.1:15432/other?currentSchema=alfa"))
        XCTAssertFalse(RecoveryEnvironment.isAirDatabase("jdbc:postgresql://127.0.0.1:15432/alfa_air_local?host=remote"))
    }
    func testFilePinRejectsChangedDefinition() throws {
        let implicit = "cdhash H\"0123456789012345678901234567890123456789\""
        XCTAssertEqual(try InstalledPolicy.parseRequirement("Executable=/fixture/helper\n# designated => \(implicit)\n"), implicit)
        XCTAssertThrowsError(try InstalledPolicy.parseRequirement("# no requirement present"))
        let dir = try directory(), file = dir.appendingPathComponent("definition")
        try Data("original".utf8).write(to: file)
        let first = try FilePin.capture(file)
        try Data("changed".utf8).write(to: file)
        XCTAssertNotEqual(first, try FilePin.capture(file))
    }
    func testPolicyChangedDuringConfirmationCannotStart() throws {
        let dir = try directory(), file = dir.appendingPathComponent("definition")
        try Data("approved".utf8).write(to: file)
        let installed = InstalledPolicy(version: 1, installationID: UUID().uuidString, ownerUID: getuid(), appRequirement: "fixture",
                                        helperRequirement: "fixture", home: NSHomeDirectory(), pins: [try FilePin.capture(file)])
        let journal = try RecoveryJournal(directory: dir), runtime = FakeRuntime()
        let engine = RecoveryEngine(policy: installed, runtime: runtime, journal: journal)
        let p = try XCTUnwrap(engine.plan(operationID: UUID().uuidString).plan)
        try Data("changed".utf8).write(to: file)
        XCTAssertEqual(engine.beginConfirmed(p.operationID, digest: p.digest).error, "POLICY_CHANGED")
        XCTAssertEqual(runtime.starts, 0)
    }
    func testChangedRegisteredEnvironmentIsRejected() throws {
        let template: [String: Any] = ["ProgramArguments": ["/fixture/launcher"], "EnvironmentVariables": ["ALFA_PORT": "7711"]]
        let good = "program = /fixture/launcher\narguments = {\n/fixture/launcher\n}\nenvironment = {\nALFA_PORT => 7711\n}\n"
        XCTAssertNoThrow(try validateRegisteredDefinition(good, template: template))
        XCTAssertThrowsError(try validateRegisteredDefinition(good.replacingOccurrences(of: "=> 7711", with: "=> 7712"), template: template))
        XCTAssertThrowsError(try validateRegisteredDefinition(good + "inherited environment = {\nBASH_ENV => /fixture/inject\n}\n", template: template))
    }
    func testWrongCaseIndexPredicateDoesNotPassSchemaValidation() throws {
        let dir = try directory()
        do { _ = try RecoveryJournal(directory: dir) }
        var db: OpaquePointer?
        XCTAssertEqual(sqlite3_open(dir.appendingPathComponent("recovery.sqlite3").path, &db), SQLITE_OK)
        defer { sqlite3_close(db) }
        XCTAssertEqual(sqlite3_exec(db, "DROP INDEX one_recovery; CREATE UNIQUE INDEX one_recovery ON operations((1)) WHERE state IN ('RUNNING','unknown')", nil, nil, nil), SQLITE_OK)
        XCTAssertThrowsError(try RecoveryJournal(directory: dir))
    }
    func testConcurrentDuplicateSubmissionsStartOnce() throws {
        let journal = try RecoveryJournal(directory: directory()), runtime = FakeRuntime()
        let engine = RecoveryEngine(policy: policy(), runtime: runtime, journal: journal)
        let p = try XCTUnwrap(engine.plan(operationID: UUID().uuidString).plan)
        DispatchQueue.concurrentPerform(iterations: 12) { _ in _ = engine.beginConfirmed(p.operationID, digest: p.digest) }
        XCTAssertEqual(try waitForTerminal(engine, p.operationID).state, "succeeded")
        XCTAssertEqual(runtime.starts, 1)
    }
}
