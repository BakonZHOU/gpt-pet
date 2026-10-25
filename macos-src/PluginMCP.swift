import Foundation
import Darwin

enum PluginMCP {
    static func run(pluginRoot: String) {
        while let line = readLine() {
            guard let data = line.data(using: .utf8),
                  let request = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let id = request["id"], let method = request["method"] as? String else { continue }
            do {
                let result: Any
                switch method {
                case "initialize":
                    _ = ensureGUI(pluginRoot: pluginRoot, explicit: false)
                    let protocolVersion = ((request["params"] as? [String: Any])?["protocolVersion"] as? String) ?? "2025-06-18"
                    result = ["protocolVersion": protocolVersion, "serverInfo": ["name": "codex-pet-plugin-macos", "version": "0.4.0"], "capabilities": ["tools": [:]]]
                case "ping": result = [:]
                case "tools/list":
                    result = ["tools": [
                        ["name": "pet_show", "title": "显示插件桌宠", "description": "显示macOS插件自带浮窗。", "inputSchema": ["type": "object", "properties": [:], "additionalProperties": false]],
                        ["name": "pet_status", "title": "插件桌宠状态", "description": "检查macOS插件桌宠状态。", "inputSchema": ["type": "object", "properties": [:], "additionalProperties": false]]
                    ]]
                case "resources/list": result = ["resources": []]
                case "resources/templates/list": result = ["resourceTemplates": []]
                case "tools/call":
                    let tool = ((request["params"] as? [String: Any])?["name"] as? String) ?? ""
                    let running = tool == "pet_show" ? ensureGUI(pluginRoot: pluginRoot, explicit: true) : guiIsRunning()
                    result = ["content": [["type": "text", "text": running ? "插件桌宠已运行，右键人物设置。" : "插件桌宠未运行。"]], "structuredContent": ["running": running]]
                default: throw PetError.message("不支持的方法")
                }
                write(["jsonrpc": "2.0", "id": id, "result": result])
            } catch {
                write(["jsonrpc": "2.0", "id": id, "error": ["code": -32603, "message": "\(error)"]])
            }
        }
    }

    private static var pidFile: String { NSTemporaryDirectory() + "codex-pet-plugin-macos.pid" }

    static func guiIsRunning() -> Bool {
        guard let text = try? String(contentsOfFile: pidFile), let pid = Int32(text.trimmingCharacters(in: .whitespacesAndNewlines)) else { return false }
        return kill(pid, 0) == 0
    }

    @discardableResult static func ensureGUI(pluginRoot: String, explicit: Bool) -> Bool {
        if ProcessInfo.processInfo.environment["CODEX_PET_NO_GUI"] == "1" { return true }
        if guiIsRunning() {
            if explicit { DistributedNotificationCenter.default().post(name: .init("com.bakon.codexpet.show"), object: nil) }
            return true
        }
        let child = Process()
        child.executableURL = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL
        child.arguments = ["--gui", "--plugin-root", pluginRoot, "--mcp-pid", "\(getpid())"]
        child.standardInput = FileHandle.nullDevice; child.standardOutput = FileHandle.nullDevice; child.standardError = FileHandle.nullDevice
        do { try child.run(); return true } catch { return false }
    }

    @discardableResult static func launchFromHook(pluginRoot: String) -> Bool {
        if guiIsRunning() {
            DistributedNotificationCenter.default().post(name: .init("com.bakon.codexpet.show"), object: nil)
            return true
        }
        return startIndependentGUI(pluginRoot: pluginRoot)
    }

    @discardableResult static func ensureFromHook(pluginRoot: String) -> Bool {
        if guiIsRunning() { return true }
        return startIndependentGUI(pluginRoot: pluginRoot)
    }

    private static func startIndependentGUI(pluginRoot: String) -> Bool {
        let child = Process()
        child.executableURL = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL
        child.arguments = ["--gui", "--plugin-root", pluginRoot]
        child.standardInput = FileHandle.nullDevice; child.standardOutput = FileHandle.nullDevice; child.standardError = FileHandle.nullDevice
        do { try child.run(); return true } catch { return false }
    }

    static func writePID() { try? "\(getpid())".write(toFile: pidFile, atomically: true, encoding: .utf8) }
    static func clearPID() { try? FileManager.default.removeItem(atPath: pidFile) }

    private static func write(_ object: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: object), var text = String(data: data, encoding: .utf8) else { return }
        text += "\n"; FileHandle.standardOutput.write(text.data(using: .utf8)!)
    }
}
