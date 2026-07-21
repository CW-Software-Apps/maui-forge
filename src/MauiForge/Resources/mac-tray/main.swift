import Cocoa
import Foundation

private struct AgentStatus: Codable {
    let isRunning: Bool?
    let isConnected: Bool?
    let isIdle: Bool?
    let statusText: String?
    let currentJob: String?
    let lastBuildName: String?
    let lastBuildResult: String?
    let buildsTotal: Int?
    let buildsSuccess: Int?
    let buildsFailed: Int?
    let lastError: String?
    let version: String?
}

class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var pollingTimer: Timer?
    private var statusText = "Conectando ao MAUI Forge..."
    private var isConnected = false
    private var isBuilding = false
    private var currentJobName = ""
    private var lastBuildInfo = "Nenhum build recente"
    private var appVersion = "1.6.30"

    private var agentPort: UInt16 {
        let args = ProcessInfo.processInfo.arguments
        if let i = args.firstIndex(of: "--port"), i + 1 < args.count {
            return UInt16(args[i + 1]) ?? 5123
        }
        return 5123
    }

    private var baseURL: String {
        "http://localhost:\(agentPort)"
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.autosaveName = "com.cwsoftware.mauiforge.tray"
        if let button = statusItem.button {
            button.image = iconFor(state: "offline")
            button.toolTip = "MAUI Forge - Status & Control"
        }

        buildMenu()
        pollStatus()
        pollingTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { _ in
            self.pollStatus()
        }

        Timer.scheduledTimer(timeInterval: 2.0, target: self, selector: #selector(checkVisibility), userInfo: nil, repeats: false)
    }

    @objc private func checkVisibility() {
        guard let button = statusItem.button else { return }
        let visible = button.window?.isVisible ?? false
        if !visible {
            NSLog("[MAUI Forge] NSStatusItem nao esta visivel — possivelmente bloqueado pelo macOS ControlCenter.")
            let alert = NSAlert()
            alert.messageText = "Ícone não aparece na Menu Bar?"
            alert.informativeText = "O macOS (incluindo versões recentes como Tahoe/Sequoia) pode ocultar ícones da barra de menus.\n\n"
                + "Abra System Settings → Menu Bar e procure por \"mac-tray\" ou \"maui-forge\".\n\n"
                + "Se estiver lá, marque como \"Show in Menu Bar\"."
            alert.addButton(withTitle: "Abrir Configurações")
            alert.addButton(withTitle: "OK")
            let response = alert.runModal()
            if response == .alertFirstButtonReturn {
                if let url = URL(string: "x-apple.systempreferences:com.apple.MenuBarSettings") {
                    NSWorkspace.shared.open(url)
                }
            }
        }
    }

    private func buildMenu() {
        let menu = NSMenu()

        let titleItem = NSMenuItem(title: "🔨 MAUI Forge", action: nil, keyEquivalent: "")
        titleItem.isEnabled = false
        menu.addItem(titleItem)

        menu.addItem(NSMenuItem.separator())

        let statusMenuItem = NSMenuItem(title: statusText, action: nil, keyEquivalent: "")
        statusMenuItem.tag = 100
        statusMenuItem.isEnabled = false
        menu.addItem(statusMenuItem)

        let lastBuildItem = NSMenuItem(title: "Último build: \(lastBuildInfo)", action: nil, keyEquivalent: "")
        lastBuildItem.tag = 101
        lastBuildItem.isEnabled = false
        menu.addItem(lastBuildItem)

        menu.addItem(NSMenuItem.separator())

        menu.addItem(NSMenuItem(title: "🌐 Abrir Web Dashboard", action: #selector(openWebUI), keyEquivalent: "w"))
        menu.addItem(NSMenuItem(title: "📂 Abrir Pasta de Logs", action: #selector(openLogsFolder), keyEquivalent: "l"))
        menu.addItem(NSMenuItem(title: "🐙 GitHub Repositório", action: #selector(openGitHub), keyEquivalent: "g"))

        menu.addItem(NSMenuItem.separator())

        menu.addItem(NSMenuItem(title: "⚙ Config. Menu Bar (System Settings)", action: #selector(openMenuBarSettings), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "🔄 Reiniciar MAUI Forge", action: #selector(restartAgent), keyEquivalent: "r"))
        menu.addItem(NSMenuItem(title: "❌ Sair do Tray", action: #selector(quitApp), keyEquivalent: "q"))

        statusItem.menu = menu
    }

    private func updateMenu() {
        guard let menu = statusItem.menu else { return }
        if let item = menu.item(withTag: 100) {
            item.title = "Status: \(statusText)"
        }
        if let item = menu.item(withTag: 101) {
            item.title = "Último build: \(lastBuildInfo)"
        }
    }

    private func pollStatus() {
        guard let url = URL(string: "\(baseURL)/api/status") else { return }

        URLSession.shared.dataTask(with: url) { [weak self] data, _, error in
            guard let self = self else { return }
            if let data = data, error == nil {
                do {
                    let s = try JSONDecoder().decode(AgentStatus.self, from: data)
                    DispatchQueue.main.async {
                        self.isConnected = s.isConnected ?? true
                        self.isBuilding = !(s.isIdle ?? true)
                        self.currentJobName = s.currentJob ?? ""
                        if let ver = s.version { self.appVersion = ver }

                        if self.isBuilding {
                            self.statusText = "Compilando \(self.currentJobName)..."
                        } else if self.isConnected {
                            self.statusText = "Pronto / Ativo (Porta \(self.agentPort))"
                        } else {
                            self.statusText = "Offline"
                        }

                        if let name = s.lastBuildName, let result = s.lastBuildResult {
                            self.lastBuildInfo = "\(name) (\(result))"
                        } else {
                            self.lastBuildInfo = "Nenhum no momento"
                        }

                        if let button = self.statusItem.button {
                            button.image = self.iconFor(
                                state: self.isBuilding ? "busy" : (self.isConnected ? "online" : "offline")
                            )
                        }

                        self.updateMenu()
                    }
                } catch {
                    self.setOfflineState()
                }
            } else {
                self.setOfflineState()
            }
        }.resume()
    }

    private func setOfflineState() {
        DispatchQueue.main.async {
            self.isConnected = false
            self.isBuilding = false
            self.statusText = "MAUI Forge Parado"
            self.lastBuildInfo = "—"
            if let button = self.statusItem.button {
                button.image = self.iconFor(state: "offline")
            }
            self.updateMenu()
        }
    }

    private func iconFor(state: String) -> NSImage {
        let size = NSSize(width: 18, height: 18)
        let img = NSImage(size: size)

        img.lockFocus()
        let rect = NSRect(origin: .zero, size: size)

        let fill: NSColor
        switch state {
        case "busy": fill = NSColor(calibratedRed: 0.2, green: 0.5, blue: 1.0, alpha: 1.0)
        case "online": fill = NSColor(calibratedRed: 0.15, green: 0.75, blue: 0.35, alpha: 1.0)
        default: fill = NSColor(calibratedRed: 0.8, green: 0.2, blue: 0.2, alpha: 1.0)
        }

        let circle = NSBezierPath(ovalIn: rect)
        fill.setFill()
        circle.fill()

        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.boldSystemFont(ofSize: 10),
            .foregroundColor: NSColor.white
        ]
        let letter = NSAttributedString(string: "MF", attributes: attrs)
        let letterSize = letter.size()
        let letterRect = NSRect(
            x: (size.width - letterSize.width) / 2,
            y: (size.height - letterSize.height) / 2 - 0.5,
            width: letterSize.width,
            height: letterSize.height
        )
        letter.draw(in: letterRect)

        img.unlockFocus()
        img.isTemplate = false
        return img
    }

    @objc private func openWebUI() {
        guard let url = URL(string: "\(baseURL)/") else { return }
        NSWorkspace.shared.open(url)
    }

    @objc private func openLogsFolder() {
        let path = NSString(string: "~/.maui-forge/build_logs").expandingTildeInPath
        let url = URL(fileURLWithPath: path)
        NSWorkspace.shared.open(url)
    }

    @objc private func openGitHub() {
        guard let url = URL(string: "https://github.com/CW-Software-Apps/maui-forge") else { return }
        NSWorkspace.shared.open(url)
    }

    @objc private func openMenuBarSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.MenuBarSettings") {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func restartAgent() {
        let task = Process()
        task.launchPath = "/bin/bash"
        task.arguments = ["-c", "launchctl kickstart -k gui/$(id -u)/com.cwsoftware.mauiforge 2>/dev/null || pkill -f maui-forge; sleep 1; nohup maui-forge > /dev/null 2>&1 &"]
        task.launch()
    }

    @objc private func quitApp() {
        NSApp.terminate(nil)
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
