import AppKit

struct PetTheme {
    let ink: NSColor
    let outline: NSColor
    let hint: NSColor
    let fill: NSColor
}

struct PetSkin {
    let id: String
    let name: String
    let image: String
    let theme: PetTheme

    static let all = [
        PetSkin(id: "whale", name: "欧鲸鲸", image: "DSniang1.png",
                theme: PetTheme(ink: .hex(0x536BA9), outline: .hex(0x203170), hint: .hex(0x9FB0D9), fill: .white)),
        PetSkin(id: "mutsumi", name: "若叶睦", image: "Skins/Mutsumi.png",
                theme: PetTheme(ink: .hex(0x556B57), outline: .hex(0x304B3A), hint: .hex(0x8A9B86), fill: .hex(0xF5F8EF))),
        PetSkin(id: "dragon", name: "龙娘", image: "Skins/WhiteDragon.png",
                theme: PetTheme(ink: .hex(0x77668F), outline: .hex(0x534264), hint: .hex(0xA59AB8), fill: .hex(0xF8F3FC)))
    ]

    static func find(_ id: String) -> PetSkin { all.first { $0.id == id } ?? all[0] }
}

extension NSColor {
    static func hex(_ value: UInt32, alpha: CGFloat = 1) -> NSColor {
        NSColor(srgbRed: CGFloat((value >> 16) & 255) / 255,
                green: CGFloat((value >> 8) & 255) / 255,
                blue: CGFloat(value & 255) / 255, alpha: alpha)
    }
}

final class PetPreferences {
    private let defaults = UserDefaults(suiteName: "com.bakon.codex-pet-plugin")!
    var scale: Double { didSet { defaults.set(scale, forKey: "scale") } }
    var skin: String { didSet { defaults.set(skin, forKey: "skin") } }
    var bubble: Bool { didSet { defaults.set(bubble, forKey: "bubble") } }
    var sound: Bool { didSet { defaults.set(sound, forKey: "sound") } }
    var topmost: Bool { didSet { defaults.set(topmost, forKey: "topmost") } }
    var quoteChance: Double { didSet { defaults.set(quoteChance, forKey: "quoteChance") } }
    var breatheSeconds: Double { didSet { defaults.set(breatheSeconds, forKey: "breatheSeconds") } }
    var ahogeSeconds: Double { didSet { defaults.set(ahogeSeconds, forKey: "ahogeSeconds") } }

    init() {
        scale = defaults.object(forKey: "scale") as? Double ?? 0.8
        skin = defaults.string(forKey: "skin") ?? "dragon"
        bubble = defaults.object(forKey: "bubble") as? Bool ?? true
        sound = defaults.object(forKey: "sound") as? Bool ?? true
        topmost = defaults.object(forKey: "topmost") as? Bool ?? true
        quoteChance = defaults.object(forKey: "quoteChance") as? Double ?? 0.15
        breatheSeconds = defaults.object(forKey: "breatheSeconds") as? Double ?? 4.4
        ahogeSeconds = defaults.object(forKey: "ahogeSeconds") as? Double ?? 2.8
        scale = min(2.5, max(0.6, scale))
        breatheSeconds = min(6, max(0.1, breatheSeconds))
        ahogeSeconds = min(6, max(0.1, ahogeSeconds))
    }
}

struct QuotaWindow {
    var remaining: Double?
    var resetsAt: Date?
}

struct QuotaSnapshot {
    var fiveHour = QuotaWindow()
    var weekly = QuotaWindow()
    var resetCount: Int?
}
