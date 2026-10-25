import Foundation

final class QuotaClient {
    private let queue = DispatchQueue(label: "com.bakon.codexpet.quota")
    private var process: Process?
    private var input: FileHandle?
    private var output: FileHandle?
    private var buffer = Data()
    private var nextID = 0

    func read(_ completion: @escaping (Result<QuotaSnapshot, Error>) -> Void) {
        queue.async {
            do {
                try self.ensureStarted()
                let object = try self.request("account/rateLimits/read", params: nil)
                guard let result = object["result"] as? [String: Any] else { throw PetError.message("额度响应无效") }
                let snapshot = Self.parse(result)
                DispatchQueue.main.async { completion(.success(snapshot)) }
            } catch {
                self.stop()
                DispatchQueue.main.async { completion(.failure(error)) }
            }
        }
    }

    func stop() {
        if let process, process.isRunning { process.terminate() }
        process = nil; input = nil; output = nil; buffer.removeAll(keepingCapacity: true)
    }

    private func ensureStarted() throws {
        if let process, process.isRunning { return }
        let executable = try Self.findCodex()
        let p = Process(); let stdin = Pipe(); let stdout = Pipe(); let stderr = Pipe()
        p.executableURL = URL(fileURLWithPath: executable)
        p.arguments = ["app-server", "--listen", "stdio://"]
        p.standardInput = stdin; p.standardOutput = stdout; p.standardError = stderr
        try p.run()
        process = p; input = stdin.fileHandleForWriting; output = stdout.fileHandleForReading
        _ = try request("initialize", params: ["clientInfo": ["name": "codex_pet_macos", "title": "Codex Pet", "version": "0.4.0"]])
        try send(["jsonrpc": "2.0", "method": "initialized"])
    }

    private func request(_ method: String, params: Any?) throws -> [String: Any] {
        nextID += 1; let wanted = nextID
        var request: [String: Any] = ["jsonrpc": "2.0", "id": wanted, "method": method]
        if let params { request["params"] = params }
        try send(request)
        while let line = try readLine() {
            guard let value = try JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
            if (value["id"] as? Int) == wanted {
                if value["error"] != nil { throw PetError.message("Codex未能提供额度") }
                return value
            }
        }
        throw PetError.message("Codex额度连接已断开")
    }

    private func send(_ object: [String: Any]) throws {
        guard let input else { throw PetError.message("额度连接未启动") }
        var data = try JSONSerialization.data(withJSONObject: object); data.append(0x0A)
        try input.write(contentsOf: data)
    }

    private func readLine() throws -> Data? {
        while true {
            if let newline = buffer.firstIndex(of: 0x0A) {
                let line = buffer.prefix(upTo: newline)
                buffer.removeSubrange(...newline)
                return Data(line)
            }
            guard let output, let chunk = try output.read(upToCount: 4096), !chunk.isEmpty else { return nil }
            buffer.append(chunk)
        }
    }

    private static func findCodex() throws -> String {
        let environment = ProcessInfo.processInfo.environment
        var candidates = [environment["CODEX_PET_CODEX"], "/Applications/Codex.app/Contents/Resources/codex",
                          "/Applications/Codex.app/Contents/MacOS/codex", "/opt/homebrew/bin/codex", "/usr/local/bin/codex"]
        let which = Process(); let pipe = Pipe()
        which.executableURL = URL(fileURLWithPath: "/usr/bin/which"); which.arguments = ["codex"]; which.standardOutput = pipe
        try? which.run(); which.waitUntilExit()
        if which.terminationStatus == 0 {
            let value = String(data: pipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines)
            candidates.append(value)
        }
        if let found = candidates.compactMap({ $0 }).first(where: { FileManager.default.isExecutableFile(atPath: $0) }) { return found }
        throw PetError.message("未找到Codex")
    }

    static func parse(_ result: [String: Any]) -> QuotaSnapshot {
        var snapshot = QuotaSnapshot()
        var bucket: [String: Any]?
        if let pools = result["rateLimitsByLimitId"] as? [String: Any] { bucket = pools["codex"] as? [String: Any] }
        if bucket == nil { bucket = result["rateLimits"] as? [String: Any] }
        for key in ["primary", "secondary"] {
            guard let window = bucket?[key] as? [String: Any], let minutes = window["windowDurationMins"] as? Int else { continue }
            let used = (window["usedPercent"] as? NSNumber)?.doubleValue
            let reset = (window["resetsAt"] as? NSNumber).map { Date(timeIntervalSince1970: $0.doubleValue) }
            let parsed = QuotaWindow(remaining: used.map { min(100, max(0, 100 - $0)) }, resetsAt: reset)
            if minutes == 300 { snapshot.fiveHour = parsed }
            if minutes == 10080 { snapshot.weekly = parsed }
        }
        if let credits = result["rateLimitResetCredits"] as? [String: Any] {
            snapshot.resetCount = (credits["availableCount"] as? NSNumber)?.intValue
        }
        return snapshot
    }
}

enum PetError: Error { case message(String) }
