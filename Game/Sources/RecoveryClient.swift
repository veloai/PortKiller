import AppKit
import SwiftUI
import Darwin
import RecoveryShared

private final class ReplyOnce: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<RecoveryReply, Error>?
    init(_ continuation: CheckedContinuation<RecoveryReply, Error>) { self.continuation = continuation }
    func finish(_ result: Result<RecoveryReply, Error>) {
        lock.lock(); let pending = continuation; continuation = nil; lock.unlock()
        pending?.resume(with: result)
    }
}

@MainActor final class RecoveryClient: ObservableObject {
    static let shared = RecoveryClient()
    @Published var statusMessage = "로컬 복구 도우미 확인 전"
    @Published var ready: Bool?
    @Published var plan: RecoveryPlan?
    @Published var current: RecoveryRecord?
    @Published var records: [RecoveryRecord] = []
    @Published var busy = false
    @Published var observedAt: Date?
    private var connection: NSXPCConnection?
    private var timer: Timer?
    private var window: NSWindow?
    private var refreshing = false
    private var polling = false
    private let pendingKey = "pkp.local-recovery.pending-operation"

    func open() {
        if window == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 560, height: 580),
                                  styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
            window.title = "PKP · 이 Mac의 ALFA 복구"
            window.minSize = NSSize(width: 430, height: 450)
            window.isReleasedWhenClosed = false
            window.contentView = NSHostingView(rootView: RecoveryView(client: self))
            window.center(); self.window = window
        }
        window?.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        Task { await refresh() }
    }

    func refresh() async {
        guard !refreshing else { return }; refreshing = true; defer { refreshing = false }
        do {
            let status = try await request(RecoveryRequest(action: "status"))
            statusMessage = status.message; ready = status.ready; observedAt = Date()
            let history = try await request(RecoveryRequest(action: "history"))
            records = history.records ?? []
            if let pending = UserDefaults.standard.string(forKey: pendingKey) {
                let result = try await request(RecoveryRequest(action: "receipt", operationID: pending))
                current = result.record
                if let record = current {
                    if record.state == "awaiting_confirmation" { plan = record.plan }
                    if record.state == "running" { startPolling() }
                    else if record.state != "awaiting_confirmation" { stopPolling() }
                }
            }
        } catch { ready = nil; statusMessage = "복구 도우미에 연결할 수 없습니다.설치 상태를 확인해 주세요." }
    }

    func makePlan() async {
        guard !busy else { return }; busy = true; defer { busy = false }
        do {
            let id = UUID().uuidString
            let result = try await request(RecoveryRequest(action: "plan", operationID: id))
            guard result.error == nil, let plan = result.plan else { statusMessage = result.message; return }
            self.plan = plan; current = result.record
            UserDefaults.standard.set(id, forKey: pendingKey); statusMessage = result.message
        } catch { statusMessage = "복구 계획을 만들지 못했습니다.새 명령은 실행하지 않았습니다." }
    }

    func execute() async {
        guard !busy, let plan else { return }; busy = true; defer { busy = false }
        do {
            let result = try await request(RecoveryRequest(action: "execute", operationID: plan.operationID, planDigest: plan.digest))
            current = result.record; statusMessage = result.message
            if result.record?.state == "running" { startPolling() }
            await refresh()
        } catch {
            statusMessage = "응답을 확인하지 못했습니다.같은 작업 기록을 조회하며 자동 재실행하지 않습니다."
            startPolling()
        }
    }
    func reconcile() async {
        guard !busy, let current else { return }; busy = true; defer { busy = false }
        do {
            let result = try await request(RecoveryRequest(action: "reconcile", operationID: current.operationID, planDigest: current.plan.digest))
            self.current = result.record ?? current; statusMessage = result.message
            await refresh()
        } catch { statusMessage = "실제 결과를 확인하지 못했습니다.재실행하지 않습니다." }
    }
    private func startPolling() {
        guard timer == nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            Task { @MainActor in await self?.pollReceipt() }
        }
    }
    private func pollReceipt() async {
        guard !polling else { return }; polling = true; defer { polling = false }
        guard let id = UserDefaults.standard.string(forKey: pendingKey) else { stopPolling(); return }
        do {
            let reply = try await request(RecoveryRequest(action: "receipt", operationID: id))
            current = reply.record; statusMessage = reply.message
            if let current, !["running", "awaiting_confirmation"].contains(current.state) {
                stopPolling(); await refresh()
            }
        } catch { statusMessage = "연결 결과 확인 대기.자동 재실행하지 않습니다." }
    }
    private func stopPolling() { timer?.invalidate(); timer = nil }

    private func connected() throws -> NSXPCConnection {
        if let connection { return connection }
        let file = RecoveryWire.directory.appendingPathComponent("policy.json")
        var info = stat()
        guard lstat(file.path, &info) == 0, info.st_mode & S_IFMT == S_IFREG,
              info.st_uid == getuid(), info.st_mode & 0o077 == 0, info.st_size <= 1_048_576 else { throw CocoaError(.fileReadNoPermission) }
        let fd = Darwin.open(file.path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC)
        guard fd >= 0 else { throw CocoaError(.fileReadNoPermission) }; defer { close(fd) }
        var opened = stat()
        guard fstat(fd, &opened) == 0, opened.st_dev == info.st_dev, opened.st_ino == info.st_ino,
              opened.st_nlink == 1 else { throw CocoaError(.fileReadNoPermission) }
        let data = try FileHandle(fileDescriptor: fd, closeOnDealloc: false).read(upToCount: 1_048_577) ?? Data()
        guard data.count <= 1_048_576 else { throw CocoaError(.fileReadTooLarge) }
        guard let policy = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let requirement = policy["helperRequirement"] as? String, !requirement.isEmpty else { throw CocoaError(.fileReadCorruptFile) }
        let connection = NSXPCConnection(machServiceName: RecoveryWire.service)
        connection.remoteObjectInterface = NSXPCInterface(with: PKPRecoveryProtocol.self)
        connection.setCodeSigningRequirement(requirement)
        connection.invalidationHandler = { [weak self] in Task { @MainActor in self?.connection = nil } }
        connection.interruptionHandler = { [weak self] in Task { @MainActor in self?.connection?.invalidate(); self?.connection = nil } }
        connection.resume(); self.connection = connection; return connection
    }
    private func request(_ request: RecoveryRequest) async throws -> RecoveryReply {
        let connection = try connected(), data = try JSONEncoder().encode(request)
        return try await withCheckedThrowingContinuation { continuation in
            let once = ReplyOnce(continuation)
            guard let remote = connection.remoteObjectProxyWithErrorHandler({ error in once.finish(.failure(error)) }) as? PKPRecoveryProtocol else {
                once.finish(.failure(CocoaError(.coderInvalidValue))); return
            }
            DispatchQueue.global().asyncAfter(deadline: .now() + 110) { once.finish(.failure(CocoaError(.userCancelled))) }
            remote.handle(data) { data in
                guard data.count <= RecoveryWire.maximumBytes else { once.finish(.failure(CocoaError(.fileReadTooLarge))); return }
                do { once.finish(.success(try JSONDecoder().decode(RecoveryReply.self, from: data))) }
                catch { once.finish(.failure(error)) }
            }
        }
    }
}

@MainActor private struct RecoveryView: View {
    @ObservedObject var client: RecoveryClient
    private let gold = Color(red: 1, green: 0.718, blue: 0.012)
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("이 Mac의 ALFA 복구").font(.custom("Doto", size: 22).weight(.bold))
                Text("Intel이나 ALFA 로그인 없이, 이 Mac에서 사용자 확인 후 처리합니다.").foregroundColor(.secondary)
                HStack { Rectangle().fill(client.ready == true ? Color.green : Color.secondary).frame(width: 8, height: 8); Text(client.statusMessage).textSelection(.enabled) }
                if let date = client.observedAt { Text("확인: \(date.formatted())").font(.caption).foregroundColor(.secondary) }
                HStack {
                    Button("상태·기록 새로고침") { Task { await client.refresh() } }.frame(minHeight: 28)
                    Button("복구 계획 확인") { Task { await client.makePlan() } }.frame(minHeight: 28).disabled(client.busy)
                }.controlSize(.regular)
                if let plan = client.plan {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("시작 대상").font(.headline)
                        Text(verbatim: "\(plan.backendLabel) · 포트 \(plan.backendPort)\ncom.alfa.router · 포트 7701").font(.system(.body, design: .monospaced))
                        Text("현재 실행 중인 서버는 강제로 재시작하지 않습니다. 데이터베이스 복원·다른 서비스·원격 기기는 변경하지 않습니다.").foregroundColor(.secondary)
                        Text("계획 만료: \(plan.expiresAt.formatted())").font(.caption)
                        Button("사용자 확인 후 서버 시작") { Task { await client.execute() } }
                            .frame(minHeight: 28)
                            .buttonStyle(.borderedProminent).tint(gold).foregroundColor(.black)
                            .disabled(client.busy || client.current?.state != "awaiting_confirmation")
                        if client.busy { ProgressView("운영체제 사용자 확인 또는 응답 대기") }
                    }.padding(16).background(Color(NSColor.controlBackgroundColor)).cornerRadius(16)
                }
                if let current = client.current {
                    VStack(alignment: .leading, spacing: 8) {
                        Text(current.message).font(.headline)
                        Text(current.operationID).font(.custom("JetBrains Mono", size: 11)).textSelection(.enabled)
                        if current.state == "unknown" {
                            Button("사용자 확인 후 실제 결과 재확인") { Task { await client.reconcile() } }.disabled(client.busy)
                        }
                    }
                }
                Divider()
                Text("최근 로컬 기록 · 최대 20건").font(.headline)
                if client.records.isEmpty { Text("아직 조회된 기록이 없습니다.").foregroundColor(.secondary) }
                ForEach(client.records) { record in
                    VStack(alignment: .leading, spacing: 3) {
                        Text(record.message)
                        Text("\(record.updatedAt.formatted()) · \(record.operationID)").font(.caption).foregroundColor(.secondary).textSelection(.enabled)
                    }.padding(.vertical, 4)
                }
            }.padding(20).frame(maxWidth: .infinity, alignment: .leading)
                .font(.custom("Pretendard", size: 13))
        }
    }
}
