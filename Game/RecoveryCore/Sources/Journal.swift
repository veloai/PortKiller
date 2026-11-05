import Foundation
import Darwin
import CSQLite
import RecoveryShared

/// One helper holds a process-lifetime flock before opening this journal.
public final class RecoveryJournal {
    private var db: OpaquePointer?
    private let lock = NSRecursiveLock()
    private let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
    private let states: Set<String> = ["awaiting_confirmation", "running", "unknown", "succeeded", "failed", "cancelled"]

    public init(directory: URL) throws {
        try checkedDirectory(directory, create: true)
        let file = directory.appendingPathComponent("recovery.sqlite3")
        let fd = open(file.path, O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC, 0o600)
        guard fd >= 0 else { throw RecoveryFailure.stateUnavailable }
        defer { close(fd) }
        _ = try checkedData(file, maximum: 64 * 1_048_576, privateFile: true)
        for suffix in ["-journal", "-wal", "-shm"] {
            let sidecar = URL(fileURLWithPath: file.path + suffix)
            var info = stat()
            if lstat(sidecar.path, &info) == 0 { _ = try checkedData(sidecar, maximum: 64 * 1_048_576, privateFile: true) }
            else if errno != ENOENT { throw RecoveryFailure.unsafePath }
        }
        guard sqlite3_open_v2(file.path, &db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX | SQLITE_OPEN_NOFOLLOW, nil) == SQLITE_OK else {
            if let db { sqlite3_close(db) }; db = nil; throw RecoveryFailure.stateUnavailable
        }
        sqlite3_busy_timeout(db, 5_000)
        // Apple's system SQLite omits the extension-loading API. This module
        // links only that system library and never enables extension loading.
        try sql("PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA fullfsync=ON; PRAGMA foreign_keys=ON; PRAGMA trusted_schema=OFF;")
        try transaction {
        let version = try query("PRAGMA user_version").first?.first
        let identity = try query("PRAGMA application_id").first?.first
        if version == "0" && identity == "0" {
        guard try query("SELECT name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'").isEmpty else { throw RecoveryFailure.stateUnavailable }
        try sql("""
        CREATE TABLE operations (
          operation_id TEXT PRIMARY KEY, request_digest TEXT NOT NULL, state TEXT NOT NULL,
          plan_json TEXT NOT NULL, result_json TEXT, created_at REAL NOT NULL, updated_at REAL NOT NULL
        );
        CREATE UNIQUE INDEX one_recovery ON operations((1)) WHERE state IN ('running','unknown');
        CREATE TABLE events (
          event_id INTEGER PRIMARY KEY AUTOINCREMENT, operation_id TEXT NOT NULL REFERENCES operations(operation_id),
          kind TEXT NOT NULL, detail TEXT NOT NULL, created_at REAL NOT NULL
        );
        CREATE TRIGGER events_no_update BEFORE UPDATE ON events BEGIN SELECT RAISE(ABORT,'append-only'); END;
        CREATE TRIGGER events_no_delete BEFORE DELETE ON events BEGIN SELECT RAISE(ABORT,'append-only'); END;
        """)
        try sql("PRAGMA application_id=\(0x504B5052); PRAGMA user_version=1;")
        } else {
            guard version == "1", identity == String(0x504B5052) else { throw RecoveryFailure.stateUnavailable }
        }
        // Preserve SQL string literals: 'RUNNING' and 'run ning' are not 'running'.
        let normalize: (String) -> String = { source in
            var quoted = false; var result = ""
            for character in source {
                if character == "'" { quoted.toggle(); result.append(character) }
                else if quoted { result.append(character) }
                else if !character.isWhitespace { result += String(character).lowercased() }
            }
            return result
        }
        let critical: [String: String] = [
            "operations": "CREATE TABLE operations (operation_id TEXT PRIMARY KEY, request_digest TEXT NOT NULL, state TEXT NOT NULL, plan_json TEXT NOT NULL, result_json TEXT, created_at REAL NOT NULL, updated_at REAL NOT NULL)",
            "events": "CREATE TABLE events (event_id INTEGER PRIMARY KEY AUTOINCREMENT, operation_id TEXT NOT NULL REFERENCES operations(operation_id), kind TEXT NOT NULL, detail TEXT NOT NULL, created_at REAL NOT NULL)",
            "one_recovery": "CREATE UNIQUE INDEX one_recovery ON operations((1)) WHERE state IN ('running','unknown')",
            "events_no_update": "CREATE TRIGGER events_no_update BEFORE UPDATE ON events BEGIN SELECT RAISE(ABORT,'append-only'); END",
            "events_no_delete": "CREATE TRIGGER events_no_delete BEFORE DELETE ON events BEGIN SELECT RAISE(ABORT,'append-only'); END"
        ]
        for (name, definition) in critical {
            guard let actual = try query("SELECT sql FROM sqlite_master WHERE name=?", [name]).first?.first,
                  normalize(actual) == normalize(definition) else { throw RecoveryFailure.stateUnavailable }
        }
        guard try query("PRAGMA quick_check").first?.first == "ok",
              try query("PRAGMA foreign_key_check").isEmpty else { throw RecoveryFailure.stateUnavailable }
        }
        // With the helper's exclusive flock, no prior process can still own these rows.
        try transaction {
            let interrupted = try query("SELECT operation_id,plan_json,state,result_json,updated_at,request_digest FROM operations WHERE state='running'")
            for row in interrupted { _ = try change(try decode(row), to: "unknown") }
        }
    }
    deinit { if let db { sqlite3_close_v2(db) } }

    public func create(plan: RecoveryPlan) throws -> RecoveryRecord {
        try synchronized {
            guard UUID(uuidString: plan.operationID) != nil, UUID(uuidString: plan.installationID) != nil,
                  plan.ownerUID == getuid(), plan.digest.count == 64, plan.definitionDigest.count == 64 else { throw RecoveryFailure.invalidRequest }
            return try transaction {
                if let old = try get(plan.operationID) {
                    guard old.plan == plan else { throw RecoveryFailure.conflict }; return old
                }
                guard plan.expiresAt > Date() else { throw RecoveryFailure.planExpired }
                let count = try query("SELECT COUNT(*) FROM operations").first?.first.flatMap(Int.init) ?? 5_000
                guard count < 5_000 else { throw RecoveryFailure.stateUnavailable }
                let data = try encoded(plan)
                guard data.count <= RecoveryWire.maximumBytes, let text = String(data: data, encoding: .utf8) else { throw RecoveryFailure.invalidRequest }
                let now = String(Date().timeIntervalSince1970)
                try execute("INSERT INTO operations VALUES(?,?,?, ?,NULL,?,?)", [plan.operationID, plan.digest, "awaiting_confirmation", text, now, now])
                try event(plan.operationID, "awaiting_confirmation")
                return try require(plan.operationID)
            }
        }
    }
    public func get(_ id: String) throws -> RecoveryRecord? {
        try synchronized {
            guard UUID(uuidString: id) != nil else { throw RecoveryFailure.invalidRequest }
            return try query("SELECT operation_id,plan_json,state,result_json,updated_at,request_digest FROM operations WHERE operation_id=?", [id]).first.map(decode)
        }
    }
    public func history() throws -> [RecoveryRecord] {
        try synchronized { try query("SELECT operation_id,plan_json,state,result_json,updated_at,request_digest FROM operations ORDER BY updated_at DESC LIMIT 20").map(decode) }
    }
    public func claim(id: String, digest: String) throws -> RecoveryRecord {
        try synchronized {
            try transaction {
                let old = try require(id)
                guard old.plan.digest == digest else { throw RecoveryFailure.conflict }
                guard old.state == "awaiting_confirmation" else { return old }
                guard old.plan.expiresAt > Date() else { throw RecoveryFailure.planExpired }
                let busy = try query("SELECT operation_id FROM operations WHERE state IN ('running','unknown') LIMIT 1")
                guard busy.isEmpty else { throw RecoveryFailure.busy }
                return try change(old, to: "running")
            }
        }
    }
    public func transition(id: String, from: [String], to: String, message: String) throws -> RecoveryRecord {
        try synchronized {
            try transaction {
                let old = try require(id)
                guard from.contains(old.state), states.contains(to) else { throw RecoveryFailure.conflict }
                let allowed = old.state == "awaiting_confirmation" ? ["cancelled", "failed"] :
                              old.state == "running" ? ["succeeded", "failed", "unknown"] :
                              old.state == "unknown" ? ["succeeded"] : []
                guard allowed.contains(to) else { throw RecoveryFailure.conflict }
                // Only state-derived safe text is persisted, never arbitrary exception text.
                return try change(old, to: to)
            }
        }
    }
    private func change(_ old: RecoveryRecord, to state: String) throws -> RecoveryRecord {
        let result = String(decoding: try encoded(["state": state, "message": publicMessage(state)]), as: UTF8.self)
        try execute("UPDATE operations SET state=?,result_json=?,updated_at=? WHERE operation_id=? AND state=?",
                    [state, result, String(Date().timeIntervalSince1970), old.operationID, old.state])
        guard sqlite3_changes(db) == 1 else { throw RecoveryFailure.conflict }
        try event(old.operationID, state); return try require(old.operationID)
    }
    private func event(_ id: String, _ state: String) throws {
        try execute("INSERT INTO events(operation_id,kind,detail,created_at) VALUES(?,?,?,?)",
                    [id, state, publicMessage(state), String(Date().timeIntervalSince1970)])
    }
    private func require(_ id: String) throws -> RecoveryRecord {
        guard let r = try get(id) else { throw RecoveryFailure.invalidRequest }; return r
    }
    private func decode(_ row: [String]) throws -> RecoveryRecord {
        guard row.count == 6, let bytes = row[1].data(using: .utf8), bytes.count <= RecoveryWire.maximumBytes,
              let time = Double(row[4]), time.isFinite, states.contains(row[2]) else { throw RecoveryFailure.stateUnavailable }
        let plan = try JSONDecoder().decode(RecoveryPlan.self, from: bytes)
        guard plan.operationID == row[0], plan.ownerUID == getuid(), UUID(uuidString: plan.installationID) != nil,
              UUID(uuidString: plan.operationID) != nil, plan.digest == row[5],
              plan.digest.range(of: "^[a-f0-9]{64}$", options: .regularExpression) != nil,
              plan.definitionDigest.range(of: "^[a-f0-9]{64}$", options: .regularExpression) != nil,
              [7711, 7712].contains(plan.backendPort), plan.expiresAt.timeIntervalSince1970.isFinite else { throw RecoveryFailure.stateUnavailable }
        return RecoveryRecord(operationID: row[0], state: row[2], message: publicMessage(row[2]), updatedAt: Date(timeIntervalSince1970: time), plan: plan)
    }
    private func publicMessage(_ state: String) -> String {
        switch state {
        case "awaiting_confirmation": return "사용자 확인 대기"
        case "running": return "로컬 복구 진행 중"
        case "succeeded": return "로컬 서버 준비 확인 완료"
        case "cancelled": return "사용자가 복구를 취소했습니다."
        case "unknown": return "결과 확인 대기.자동 재실행하지 않습니다."
        default: return "복구 실패.상태를 확인해 주세요."
        }
    }
    private func synchronized<T>(_ body: () throws -> T) rethrows -> T { lock.lock(); defer { lock.unlock() }; return try body() }
    private func transaction<T>(_ body: () throws -> T) throws -> T {
        try sql("BEGIN IMMEDIATE")
        do { let result = try body(); try sql("COMMIT"); return result }
        catch { _ = sqlite3_exec(db, "ROLLBACK", nil, nil, nil); throw error }
    }
    private func sql(_ text: String) throws {
        guard sqlite3_exec(db, text, nil, nil, nil) == SQLITE_OK else { throw RecoveryFailure.stateUnavailable }
    }
    private func prepare(_ sql: String, _ values: [String]) throws -> OpaquePointer {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &statement, nil) == SQLITE_OK, let result = statement else { throw RecoveryFailure.stateUnavailable }
        for (offset, value) in values.enumerated() {
            guard !value.contains("\0"), value.withCString({ sqlite3_bind_text(result, Int32(offset + 1), $0, -1, transient) }) == SQLITE_OK else {
                sqlite3_finalize(result); throw RecoveryFailure.stateUnavailable
            }
        }
        return result
    }
    private func execute(_ sql: String, _ values: [String]) throws {
        let statement = try prepare(sql, values); defer { sqlite3_finalize(statement) }
        guard sqlite3_step(statement) == SQLITE_DONE else { throw RecoveryFailure.stateUnavailable }
    }
    private func query(_ sql: String, _ values: [String] = []) throws -> [[String]] {
        let statement = try prepare(sql, values); defer { sqlite3_finalize(statement) }
        var rows: [[String]] = []
        while true {
            let status = sqlite3_step(statement)
            if status == SQLITE_DONE { return rows }
            guard status == SQLITE_ROW else { throw RecoveryFailure.stateUnavailable }
            var row: [String] = []
            for column in 0..<sqlite3_column_count(statement) {
                let size = Int(sqlite3_column_bytes(statement, column))
                guard size <= RecoveryWire.maximumBytes else { throw RecoveryFailure.stateUnavailable }
                row.append(sqlite3_column_text(statement, column).map { String(cString: $0) } ?? "")
            }
            rows.append(row)
        }
    }
}
