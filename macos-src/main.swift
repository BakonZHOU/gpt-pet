import AppKit
import Foundation

func argument(_ name: String) -> String? {
    guard let index = CommandLine.arguments.firstIndex(of: name), index + 1 < CommandLine.arguments.count else { return nil }
    return CommandLine.arguments[index + 1]
}

let root = URL(fileURLWithPath: argument("--plugin-root") ?? URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent().path).standardizedFileURL.path

if CommandLine.arguments.contains("--self-test") {
    let fixture: [String: Any] = ["rateLimitsByLimitId": ["codex": ["primary": ["windowDurationMins": 300, "usedPercent": 24.0, "resetsAt": 2_000_000_000], "secondary": ["windowDurationMins": 10080, "usedPercent": 26.0, "resetsAt": 2_000_000_100]]], "rateLimitResetCredits": ["availableCount": 2]]
    let value = QuotaClient.parse(fixture)
    let names = PetSkin.all.map(\.name)
    guard abs((value.fiveHour.remaining ?? -1) - 76) < 0.001,
          abs((value.weekly.remaining ?? -1) - 74) < 0.001,
          value.resetCount == 2, names == ["欧鲸鲸", "若叶睦", "龙娘"] else {
        fputs("FAIL five=\(String(describing: value.fiveHour.remaining)) week=\(String(describing: value.weekly.remaining)) resets=\(String(describing: value.resetCount)) names=\(names)\n", stderr)
        exit(1)
    }
    print("PASS macOS quota parser, skin names and version 0.4.0")
} else if CommandLine.arguments.contains("--mcp") {
    PluginMCP.run(pluginRoot: root)
} else if CommandLine.arguments.contains("--launch") {
    exit(PluginMCP.launchFromHook(pluginRoot: root) ? 0 : 1)
} else if CommandLine.arguments.contains("--ensure") {
    exit(PluginMCP.ensureFromHook(pluginRoot: root) ? 0 : 1)
} else {
    let app = NSApplication.shared; app.setActivationPolicy(.accessory)
    let delegate = PetController(pluginRoot: root, hostPID: argument("--mcp-pid").flatMap(Int32.init))
    app.delegate = delegate; app.run()
}
