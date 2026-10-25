import AppKit
import Darwin

final class ArrowView: NSView {
    var anchor = CGPoint.zero
    var angle: CGFloat = 0
    var length: CGFloat = 180
    override var isFlipped: Bool { false }
    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        context.saveGState(); context.translateBy(x: anchor.x, y: anchor.y); context.rotate(by: angle)
        context.setStrokeColor(NSColor.hex(0xCF5967).cgColor); context.setLineWidth(4.5)
        context.setLineCap(.round); context.setLineJoin(.round); context.setLineDash(phase: 0, lengths: [7, 8])
        context.move(to: .zero); context.addLine(to: CGPoint(x: length - 15, y: 0)); context.strokePath()
        context.setLineDash(phase: 0, lengths: []); context.move(to: CGPoint(x: length - 25, y: 11))
        context.addQuadCurve(to: CGPoint(x: length, y: 0), control: CGPoint(x: length - 12, y: 6))
        context.addQuadCurve(to: CGPoint(x: length - 25, y: -11), control: CGPoint(x: length - 12, y: -6)); context.strokePath()
        context.restoreGState()
    }
}

final class ArrowOverlay {
    private let window: NSWindow
    private let view = ArrowView()
    init() {
        let union = NSScreen.screens.map(\.frame).reduce(NSRect.zero) { $0.union($1) }
        window = NSWindow(contentRect: union, styleMask: [.borderless], backing: .buffered, defer: false)
        window.backgroundColor = .clear; window.isOpaque = false; window.hasShadow = false
        window.ignoresMouseEvents = true; window.level = .screenSaver
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        view.frame = NSRect(origin: .zero, size: union.size); view.autoresizingMask = [.width, .height]; window.contentView = view
    }
    func show(anchor screenPoint: CGPoint, angle: CGFloat, scale: CGFloat) {
        view.anchor = CGPoint(x: screenPoint.x - window.frame.minX, y: screenPoint.y - window.frame.minY)
        view.angle = angle; view.length = 180 * scale; view.needsDisplay = true
        window.orderFrontRegardless()
    }
    func hide() { window.orderOut(nil) }
}

final class PetView: NSView {
    enum Phase { case idle, flying, falling, resting, returning }
    let preferences: PetPreferences
    let pluginRoot: String
    weak var controller: PetController?
    private let quotaClient = QuotaClient()
    private let arrow = ArrowOverlay()
    private var skin: PetSkin
    private var image: NSImage?
    private var snapshot: QuotaSnapshot?
    private var errorText: String?
    private var bubbleStage = 0
    private var bubbleClose: Timer?
    private var pollTimer: Timer?
    private var animationTimer: Timer?
    private var pressAt: Date?
    private var pressPoint = CGPoint.zero
    private var lastMouse = CGPoint.zero
    private var dragging = false
    private var charging = false
    private var aim: CGFloat = 0
    private var phase = Phase.idle
    private var velocity = CGVector.zero
    private var rotation: CGFloat = 0
    private var spin: CGFloat = 0
    private var impacts = 0
    private var flightStarted = Date()
    private var restStarted = Date()
    private var returnStarted = Date()
    private var returnOrigin = CGPoint.zero
    private var returnAngle: CGFloat = 0
    private var lastFrame = Date()
    private var impactScale = CGPoint(x: 1, y: 1)
    private var previousFive: Double?
    private var alertValues: [Double] = []
    private var alertTimer: Timer?
    private var pressSound: NSSound?
    private var releaseSound: NSSound?

    override var acceptsFirstResponder: Bool { true }
    override var isFlipped: Bool { false }

    init(frame: NSRect, pluginRoot: String, preferences: PetPreferences) {
        self.pluginRoot = pluginRoot; self.preferences = preferences
        skin = PetSkin.find(preferences.skin)
        super.init(frame: frame)
        wantsLayer = true; layer?.backgroundColor = NSColor.clear.cgColor
        loadSkin(); startTimers(); refreshQuota()
        DistributedNotificationCenter.default().addObserver(self, selector: #selector(showRequested), name: .init("com.bakon.codexpet.show"), object: nil)
    }
    required init?(coder: NSCoder) { fatalError() }
    deinit { quotaClient.stop() }

    @objc private func showRequested() { controller?.show() }

    private var petRect: CGRect {
        let side = bounds.width * 0.5945
        return CGRect(x: bounds.maxX - side, y: 0, width: side, height: side)
    }
    private var chestPoint: CGPoint { CGPoint(x: petRect.midX, y: petRect.minY + petRect.height * 0.28) }

    private func startTimers() {
        let timer = Timer(timeInterval: 1.0 / 120.0, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(timer, forMode: .common); animationTimer = timer
        pollTimer = Timer.scheduledTimer(withTimeInterval: 10, repeats: true) { [weak self] _ in self?.refreshQuota() }
    }

    private func loadSkin() {
        skin = PetSkin.find(preferences.skin)
        image = NSImage(contentsOfFile: (pluginRoot as NSString).appendingPathComponent("assets/\(skin.image)"))
        needsDisplay = true
    }

    func selectSkin(_ id: String) { preferences.skin = id; loadSkin(); bubbleStage = 0 }
    func resizePet(_ value: Double) { controller?.resize(scale: value) }
    func setTopmost(_ value: Bool) { controller?.window.level = value ? .floating : .normal }
    func setBubbles(_ value: Bool) { if !value { bubbleStage = 0; bubbleClose?.invalidate(); needsDisplay = true } }
    func editQuotes() { NSWorkspace.shared.open(URL(fileURLWithPath: quoteFile())) }
    func hidePet() { controller?.window.orderOut(nil) }
    func quitPet() { NSApp.terminate(nil) }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        if bubbleStage > 0 && preferences.bubble { drawBubble() }
        drawPet()
    }

    private func drawPet() {
        guard let image, let context = NSGraphicsContext.current?.cgContext else { return }
        let now = Date().timeIntervalSinceReferenceDate
        let breath = CGFloat(preferences.breatheSeconds > 0 ? sin(now * .pi * 2 / preferences.breatheSeconds) : 0)
        let sx = (1 + breath * 0.010) * impactScale.x
        let sy = (1 + breath * 0.022) * impactScale.y
        let rect = petRect
        context.saveGState(); context.translateBy(x: rect.midX, y: rect.midY)
        context.rotate(by: rotation * .pi / 180); context.scaleBy(x: sx, y: sy)
        image.draw(in: CGRect(x: -rect.width / 2, y: -rect.height / 2, width: rect.width, height: rect.height),
                   from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: [.interpolation: NSImageInterpolation.high])
        context.restoreGState()
    }

    private func drawBubble() {
        let theme = skin.theme
        let main = CGRect(x: 5, y: bounds.height - 100, width: bounds.width * 0.78, height: 90)
        let path = NSBezierPath(roundedRect: main, xRadius: 45, yRadius: 45)
        path.lineWidth = 4; theme.fill.setFill(); theme.outline.setStroke(); path.fill(); path.stroke()
        for dot in [CGRect(x: bounds.width * 0.39, y: bounds.height - 121, width: 22, height: 15),
                    CGRect(x: bounds.width * 0.52, y: bounds.height - 137, width: 14, height: 10)] {
            let p = NSBezierPath(ovalIn: dot); p.lineWidth = 3; p.fill(); p.stroke()
        }
        let lines = bubbleLines()
        let paragraph = NSMutableParagraphStyle(); paragraph.alignment = .center
        var y = main.midY + CGFloat(lines.count - 1) * 12
        for (index, line) in lines.enumerated() {
            let quoteOnly = bubbleStage == 2 && lines.count == 1
            var size: CGFloat = quoteOnly ? (line.count <= 8 ? 25 : line.count <= 14 ? 18 : 15) : (index == 1 ? 25 : 13)
            if line.count > 12 { size = max(10, 24 - CGFloat(line.count - 12) * 0.7) }
            let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: size, weight: index == 1 ? .bold : .semibold), .foregroundColor: index == lines.count - 1 ? theme.hint : theme.ink, .paragraphStyle: paragraph]
            let box = CGRect(x: main.minX + 10, y: y - size / 2, width: main.width - 20, height: size * 1.3)
            (line as NSString).draw(in: box, withAttributes: attrs); y -= size * 1.1
        }
    }

    private func bubbleLines() -> [String] {
        if bubbleStage == 2 {
            if snapshot?.fiveHour.remaining ?? 1 <= 0 { return [requiredQuote()] }
            if Double.random(in: 0...1) < preferences.quoteChance { return [randomQuote()] }
            return ["剩余重置次数", snapshot?.resetCount.map { "\($0) 次" } ?? "--"]
        }
        let five = percent(snapshot?.fiveHour.remaining)
        let week = percent(snapshot?.weekly.remaining)
        return ["5小时剩余额度", five, errorText ?? "每周剩余 \(week)"]
    }

    private func percent(_ value: Double?) -> String { value.map { String(format: "%.0f%%", $0) } ?? "--" }
    private func quoteFile() -> String {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("CodexPetPlugin")
        try? FileManager.default.createDirectory(at: support, withIntermediateDirectories: true)
        let names = ["whale": "语录-欧鲸鲸.txt", "mutsumi": "语录-若叶睦.txt", "dragon": "语录-龙娘.txt"]
        let target = support.appendingPathComponent(names[skin.id]!).path
        if !FileManager.default.fileExists(atPath: target) {
            let source = (pluginRoot as NSString).appendingPathComponent("quotes/\(skin.id).txt")
            try? FileManager.default.copyItem(atPath: source, toPath: target)
        }
        return target
    }
    private func quotes() -> [String] {
        let text = (try? String(contentsOfFile: quoteFile(), encoding: .utf8)) ?? "区…"
        return text.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty && !$0.hasPrefix("#") }
    }
    private func randomQuote() -> String { quotes().randomElement() ?? "区…" }
    private func requiredQuote() -> String { quotes().filter { $0.hasPrefix("区") }.randomElement() ?? "区…" }

    private func showBubble(stage: Int, autoClose: Bool = true) {
        guard preferences.bubble else { return }
        bubbleStage = stage; bubbleClose?.invalidate()
        if autoClose { bubbleClose = Timer.scheduledTimer(withTimeInterval: 5, repeats: false) { [weak self] _ in self?.bubbleStage = 0; self?.needsDisplay = true } }
        needsDisplay = true
    }

    private func click() {
        stopSounds()
        if bubbleStage == 0 { showBubble(stage: 1); refreshQuota(); play(down: false) }
        else if bubbleStage == 1 { showBubble(stage: 2); play(down: false) }
        else { bubbleStage = 0; bubbleClose?.invalidate(); needsDisplay = true }
    }

    private func refreshQuota() {
        quotaClient.read { [weak self] result in
            guard let self else { return }
            switch result {
            case .success(let fresh):
                errorText = nil; snapshot = fresh
                if let old = previousFive, let new = fresh.fiveHour.remaining, new < old {
                    var point = floor(old) - 1
                    while point >= floor(new) { alertValues.append(point); point -= 1 }
                    runAlerts()
                }
                previousFive = fresh.fiveHour.remaining
            case .failure: errorText = "离线 · 点击重试"
            }
            needsDisplay = true
        }
    }

    private func runAlerts() {
        guard alertTimer == nil, !alertValues.isEmpty else { return }
        func next() {
            guard !self.alertValues.isEmpty else { self.alertTimer = nil; return }
            let value = self.alertValues.removeFirst()
            var shown = self.snapshot ?? QuotaSnapshot(); shown.fiveHour.remaining = value
            let current = self.snapshot; self.snapshot = shown; self.showBubble(stage: 1, autoClose: false); self.snapshot = current
            self.alertTimer = Timer.scheduledTimer(withTimeInterval: 5.5, repeats: false) { _ in next() }
        }
        next()
    }

    private func play(down: Bool) {
        stopSounds(); guard preferences.sound else { return }
        let file = down ? "Ya1.mp3" : "Ya2.mp3"
        let sound = NSSound(contentsOfFile: (pluginRoot as NSString).appendingPathComponent("assets/\(file)"), byReference: false)
        if down { pressSound = sound } else { releaseSound = sound }; sound?.play()
    }
    private func stopSounds() { pressSound?.stop(); releaseSound?.stop(); pressSound = nil; releaseSound = nil }

    override func mouseDown(with event: NSEvent) {
        guard phase == .idle else { return }
        pressAt = Date(); pressPoint = event.locationInWindow; lastMouse = NSEvent.mouseLocation; dragging = false; play(down: true)
    }
    override func mouseDragged(with event: NSEvent) {
        guard phase == .idle, let pressAt else { return }
        let current = NSEvent.mouseLocation
        if !charging && !dragging && hypot(current.x - lastMouse.x, current.y - lastMouse.y) > 3 && Date().timeIntervalSince(pressAt) < 1.22 { dragging = true }
        if dragging, let window {
            let delta = CGPoint(x: current.x - lastMouse.x, y: current.y - lastMouse.y)
            window.setFrameOrigin(CGPoint(x: window.frame.origin.x + delta.x, y: window.frame.origin.y + delta.y)); lastMouse = current
        }
    }
    override func mouseUp(with event: NSEvent) {
        stopSounds()
        if charging { fire(); pressAt = nil; return }
        let wasClick = pressAt != nil && !dragging; pressAt = nil
        if wasClick { click() }
    }
    override func rightMouseDown(with event: NSEvent) { controller?.toggleSettings() }

    private func tick() {
        let now = Date(); let dt = CGFloat(min(0.04, now.timeIntervalSince(lastFrame))); lastFrame = now
        if phase == .idle {
            if let pressed = pressAt, !dragging, now.timeIntervalSince(pressed) >= 1.22 {
                charging = true; bubbleStage = 0
                let mouse = NSEvent.mouseLocation; let chest = chestScreenPoint()
                aim = atan2(mouse.y - chest.y, mouse.x - chest.x)
                let energy = min(1, max(0, (now.timeIntervalSince(pressed) - 1.22) / 13.78))
                impactScale = CGPoint(x: 1.05 - CGFloat(energy) * 0.46, y: 0.88 - CGFloat(energy) * 0.40)
                arrow.show(anchor: chest, angle: aim, scale: bounds.width / 250)
            }
            needsDisplay = true; return
        }
        updateFlight(dt: dt, now: now)
    }

    private func chestScreenPoint() -> CGPoint {
        guard let window else { return .zero }
        let inWindow = convert(chestPoint, to: nil)
        return window.convertPoint(toScreen: inWindow)
    }

    private func fire() {
        guard let pressed = pressAt else { return }
        let energy = min(1, max(0, (Date().timeIntervalSince(pressed) - 1.22) / 13.78))
        charging = false; arrow.hide(); impactScale = CGPoint(x: 0.7, y: 0.7)
        let speed = CGFloat(420 + energy * 2200)
        velocity = CGVector(dx: cos(aim) * speed, dy: sin(aim) * speed)
        spin = CGFloat(Bool.random() ? -1 : 1) * CGFloat(90 + energy * 375)
        rotation = 0; impacts = 0; phase = .flying; flightStarted = Date(); lastFrame = Date()
    }

    private func updateFlight(dt: CGFloat, now: Date) {
        guard let window, let screen = window.screen ?? NSScreen.main else { return }
        let elapsed = now.timeIntervalSince(flightStarted), area = screen.visibleFrame
        var origin = window.frame.origin
        if phase == .flying {
            let steps = max(1, Int(ceil(dt / 0.004)))
            for _ in 0..<steps {
                let step = dt / CGFloat(steps)
                velocity.dy -= 900 * step
                let drag = CGFloat(Darwin.exp(Double(-0.28 * step))); velocity.dx *= drag; velocity.dy *= drag
                origin.x += velocity.dx * step; origin.y += velocity.dy * step
                var hitX = false, hitY = false, normal: CGFloat = 0
                if origin.x < area.minX || origin.x > area.maxX - window.frame.width {
                    normal = abs(velocity.dx); origin.x = min(area.maxX - window.frame.width, max(area.minX, origin.x)); velocity.dx *= -0.8; hitX = true
                }
                if origin.y < area.minY || origin.y > area.maxY - window.frame.height {
                    normal = max(normal, abs(velocity.dy)); origin.y = min(area.maxY - window.frame.height, max(area.minY, origin.y)); velocity.dy *= -0.74; hitY = true
                }
                if hitX || hitY { impacts += 1; spin *= 0.8; squash(horizontal: hitX && !hitY, speed: normal) }
                if elapsed >= 4.2 || impacts >= 8 { phase = .falling; velocity.dy = min(velocity.dy, 0); break }
            }
            rotation += spin * dt; spin *= CGFloat(Darwin.exp(Double(-0.24 * dt)))
        } else if phase == .falling {
            velocity.dy -= 1500 * dt
            velocity.dx *= CGFloat(Darwin.exp(Double(-6 * dt))); spin *= CGFloat(Darwin.exp(Double(-3 * dt)))
            origin.x = min(area.maxX - window.frame.width, max(area.minX, origin.x + velocity.dx * dt)); origin.y += velocity.dy * dt; rotation += spin * dt
            if origin.y <= area.minY { origin.y = area.minY; phase = .resting; restStarted = now; velocity = .zero; spin = 0 }
        } else if phase == .resting, now.timeIntervalSince(restStarted) >= 2 {
            phase = .returning; returnStarted = now; returnOrigin = origin; returnAngle = rotation.truncatingRemainder(dividingBy: 360)
            if returnAngle > 180 { returnAngle -= 360 }
        } else if phase == .returning {
            let t = CGFloat(min(1, now.timeIntervalSince(returnStarted) / 0.65)), ease = t * t * (3 - 2 * t)
            let target = CGPoint(x: area.maxX - window.frame.width, y: area.minY)
            origin.x = returnOrigin.x + (target.x - returnOrigin.x) * ease; origin.y = returnOrigin.y + (target.y - returnOrigin.y) * ease
            rotation = returnAngle * (1 - ease); impactScale = CGPoint(x: 0.7 + 0.3 * ease, y: 0.7 + 0.3 * ease)
            if t >= 1 { phase = .idle; rotation = 0; impactScale = CGPoint(x: 1, y: 1) }
        }
        window.setFrameOrigin(origin); needsDisplay = true
    }

    private func squash(horizontal: Bool, speed: CGFloat) {
        let force = min(1, max(0, (speed - 50) / 1800)); guard force > 0 else { return }
        let compressed = 0.7 * (1 - 0.3 * force), stretched = 0.7 / CGFloat(Darwin.sqrt(Double(1 - 0.3 * force)))
        impactScale = horizontal ? CGPoint(x: compressed, y: stretched) : CGPoint(x: stretched, y: compressed)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.08 + 0.08 * Double(force)) { [weak self] in
            if self?.phase != .idle { self?.impactScale = CGPoint(x: 0.7, y: 0.7) }
        }
    }
}

final class SettingsPanel: NSPanel {
    private weak var pet: PetView?
    private let preferences: PetPreferences
    init(pet: PetView, preferences: PetPreferences) {
        self.pet = pet; self.preferences = preferences
        super.init(contentRect: NSRect(x: 0, y: 0, width: 286, height: 350), styleMask: [.titled, .closable, .utilityWindow], backing: .buffered, defer: false)
        title = "桌宠设置"; isReleasedWhenClosed = false; level = .floating
        let skin = NSPopUpButton(); skin.addItems(withTitles: PetSkin.all.map(\.name)); skin.selectItem(at: PetSkin.all.firstIndex { $0.id == preferences.skin } ?? 0); skin.target = self; skin.action = #selector(changeSkin)
        let scale = slider(preferences.scale, 0.6, 2.5, #selector(changeScale))
        let breathe = slider(preferences.breatheSeconds, 0.1, 6, #selector(changeBreathe))
        let ahoge = slider(preferences.ahogeSeconds, 0.1, 6, #selector(changeAhoge))
        let quote = slider(preferences.quoteChance, 0, 1, #selector(changeQuote))
        let sound = NSButton(checkboxWithTitle: "音效", target: self, action: #selector(toggleSound)); sound.state = preferences.sound ? .on : .off
        let bubbles = NSButton(checkboxWithTitle: "气泡", target: self, action: #selector(toggleBubble)); bubbles.state = preferences.bubble ? .on : .off
        let topmost = NSButton(checkboxWithTitle: "置顶显示", target: self, action: #selector(toggleTopmost)); topmost.state = preferences.topmost ? .on : .off
        let edit = NSButton(title: "编辑语录", target: self, action: #selector(editQuotes)); edit.bezelStyle = .rounded
        let hide = NSButton(title: "隐藏桌宠", target: self, action: #selector(hidePet)); hide.bezelStyle = .rounded
        let quit = NSButton(title: "退出", target: self, action: #selector(quitPet)); quit.bezelStyle = .rounded
        let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 9; stack.edgeInsets = NSEdgeInsets(top: 14, left: 16, bottom: 14, right: 16)
        for row in [row("皮肤", skin), row("大小", scale), row("呼吸速度", breathe), row("呆毛速度", ahoge), row("语录概率", quote)] { stack.addArrangedSubview(row) }
        let checks = NSStackView(views: [sound, bubbles, topmost]); checks.spacing = 12; stack.addArrangedSubview(checks)
        let buttons = NSStackView(views: [edit, hide, quit]); buttons.spacing = 8; stack.addArrangedSubview(buttons)
        stack.frame = contentView!.bounds; stack.autoresizingMask = [.width, .height]; contentView?.addSubview(stack)
    }
    private func slider(_ value: Double, _ min: Double, _ max: Double, _ action: Selector) -> NSSlider {
        let slider = NSSlider(value: value, minValue: min, maxValue: max, target: self, action: action); slider.numberOfTickMarks = Int((max - min) * 10) + 1; slider.allowsTickMarkValuesOnly = false; slider.frame.size.width = 160; return slider
    }
    private func row(_ title: String, _ control: NSView) -> NSView {
        let label = NSTextField(labelWithString: title); label.frame.size.width = 72
        let stack = NSStackView(views: [label, control]); stack.spacing = 8; return stack
    }
    @objc private func changeSkin(_ sender: NSPopUpButton) { pet?.selectSkin(PetSkin.all[sender.indexOfSelectedItem].id) }
    @objc private func changeScale(_ sender: NSSlider) { preferences.scale = sender.doubleValue; pet?.resizePet(sender.doubleValue) }
    @objc private func changeBreathe(_ sender: NSSlider) { preferences.breatheSeconds = sender.doubleValue }
    @objc private func changeAhoge(_ sender: NSSlider) { preferences.ahogeSeconds = sender.doubleValue }
    @objc private func changeQuote(_ sender: NSSlider) { preferences.quoteChance = sender.doubleValue }
    @objc private func toggleSound(_ sender: NSButton) { preferences.sound = sender.state == .on }
    @objc private func toggleBubble(_ sender: NSButton) { preferences.bubble = sender.state == .on; pet?.setBubbles(preferences.bubble) }
    @objc private func toggleTopmost(_ sender: NSButton) { preferences.topmost = sender.state == .on; pet?.setTopmost(preferences.topmost) }
    @objc private func editQuotes() { pet?.editQuotes() }
    @objc private func hidePet() { orderOut(nil); pet?.hidePet() }
    @objc private func quitPet() { pet?.quitPet() }
}

final class PetController: NSObject, NSApplicationDelegate {
    let pluginRoot: String
    let hostPID: Int32?
    let preferences = PetPreferences()
    var window: NSWindow!
    private var pet: PetView!
    private var settings: SettingsPanel!
    private var lifetimeTimer: Timer?
    private var hostAbsent = 0
    private let startupAt = Date()
    private var hostObserved = false
    init(pluginRoot: String, hostPID: Int32?) { self.pluginRoot = pluginRoot; self.hostPID = hostPID }
    func applicationDidFinishLaunching(_ notification: Notification) {
        PluginMCP.writePID()
        let side = 250 * preferences.scale
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: side, height: side), styleMask: [.borderless], backing: .buffered, defer: false)
        window.backgroundColor = .clear; window.isOpaque = false; window.hasShadow = false; window.level = preferences.topmost ? .floating : .normal
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]; window.isMovableByWindowBackground = false
        pet = PetView(frame: NSRect(x: 0, y: 0, width: side, height: side), pluginRoot: pluginRoot, preferences: preferences); pet.controller = self; window.contentView = pet
        settings = SettingsPanel(pet: pet, preferences: preferences)
        placeBottomRight(); window.orderFrontRegardless()
        lifetimeTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.checkHost() }
    }
    func applicationWillTerminate(_ notification: Notification) { PluginMCP.clearPID() }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func show() { window.orderFrontRegardless() }
    func resize(scale: Double) {
        let old = window.frame, side = 250 * scale
        window.setFrame(NSRect(x: old.maxX - side, y: old.minY, width: side, height: side), display: true)
    }
    func toggleSettings() {
        if settings.isVisible { settings.orderOut(nil); return }
        let origin = CGPoint(x: max((window.screen?.visibleFrame.minX ?? 0), window.frame.minX - settings.frame.width + window.frame.width), y: window.frame.maxY)
        settings.setFrameOrigin(origin); settings.orderFrontRegardless()
    }
    private func placeBottomRight() {
        let area = NSScreen.main?.visibleFrame ?? .zero
        window.setFrameOrigin(CGPoint(x: area.maxX - window.frame.width, y: area.minY))
    }
    private func checkHost() {
        let running: Bool
        if let hostPID { running = kill(hostPID, 0) == 0 }
        else {
            running = NSWorkspace.shared.runningApplications.contains { app in
                let name = app.localizedName?.lowercased() ?? ""
                let path = app.executableURL?.path.lowercased() ?? ""
                return name == "chatgpt" || name == "codex" || path.contains("openai.codex")
            }
        }
        if running { hostObserved = true; hostAbsent = 0 }
        else if hostObserved { hostAbsent += 1 }
        else { hostAbsent = 0 }
        // The session hook may fire before the desktop process list settles.
        // Keep the early window alive instead of falling back to MCP a minute later.
        if Date().timeIntervalSince(startupAt) >= 15 && hostObserved && hostAbsent >= 12 {
            NSApp.terminate(nil)
        }
    }
}
