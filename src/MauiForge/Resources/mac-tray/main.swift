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
    private var isUpdateAvailable = false
    private var latestVersionFound = ""

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
            button.toolTip = "MAUI Forge — Version & Build Manager"
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

        // 1. Header com Nome e Versão estilo Dashboard
        let titleItem = NSMenuItem(title: "🔨 MAUI Forge   v\(appVersion)", action: nil, keyEquivalent: "")
        titleItem.tag = 99
        titleItem.isEnabled = false
        menu.addItem(titleItem)

        // 2. Botão de Update em destaque no topo
        let updateTitle = isUpdateAvailable ? "⬆ Nova Versão Disponível (\(latestVersionFound)) — Atualizar" : "🔄 Checar Atualizações (Update)"
        let updateItem = NSMenuItem(title: updateTitle, action: #selector(checkForUpdates), keyEquivalent: "u")
        updateItem.tag = 102
        updateItem.image = systemIcon("arrow.triangle.2.circlepath")
        menu.addItem(updateItem)

        menu.addItem(NSMenuItem.separator())

        // 3. Status detalhado
        let statusMenuItem = NSMenuItem(title: "● Status: \(statusText)", action: nil, keyEquivalent: "")
        statusMenuItem.tag = 100
        statusMenuItem.isEnabled = false
        statusMenuItem.image = systemIcon("smallcircle.filled.circle")
        menu.addItem(statusMenuItem)

        let lastBuildItem = NSMenuItem(title: "📦 Último build: \(lastBuildInfo)", action: nil, keyEquivalent: "")
        lastBuildItem.tag = 101
        lastBuildItem.isEnabled = false
        lastBuildItem.image = systemIcon("cube.box")
        menu.addItem(lastBuildItem)

        menu.addItem(NSMenuItem.separator())

        // 4. Ações Principais
        let webItem = NSMenuItem(title: "🌐 Abrir Web Dashboard", action: #selector(openWebUI), keyEquivalent: "w")
        webItem.image = systemIcon("safari")
        menu.addItem(webItem)

        let logsItem = NSMenuItem(title: "📂 Abrir Pasta de Logs", action: #selector(openLogsFolder), keyEquivalent: "l")
        logsItem.image = systemIcon("folder")
        menu.addItem(logsItem)

        let gitItem = NSMenuItem(title: "🐙 Repositório GitHub", action: #selector(openGitHub), keyEquivalent: "g")
        gitItem.image = systemIcon("arrow.up.right.square")
        menu.addItem(gitItem)

        menu.addItem(NSMenuItem.separator())

        // 5. Configurações e Gerenciamento
        let settingsItem = NSMenuItem(title: "⚙ Config. Menu Bar (System Settings)", action: #selector(openMenuBarSettings), keyEquivalent: "")
        settingsItem.image = systemIcon("gearshape")
        menu.addItem(settingsItem)

        let restartItem = NSMenuItem(title: "🔄 Reiniciar MAUI Forge", action: #selector(restartAgent), keyEquivalent: "r")
        restartItem.image = systemIcon("arrow.clockwise.circle")
        menu.addItem(restartItem)

        let quitItem = NSMenuItem(title: "❌ Sair do MacAgent", action: #selector(quitApp), keyEquivalent: "q")
        quitItem.image = systemIcon("xmark.circle")
        menu.addItem(quitItem)

        statusItem.menu = menu
    }

    private func systemIcon(_ name: String) -> NSImage? {
        if #available(macOS 11.0, *) {
            let img = NSImage(systemSymbolName: name, accessibilityDescription: nil)
            img?.isTemplate = true
            return img
        }
        return nil
    }

    private func updateMenu() {
        guard let menu = statusItem.menu else { return }
        if let item = menu.item(withTag: 99) {
            item.title = "🔨 MAUI Forge   v\(appVersion)"
        }
        if let item = menu.item(withTag: 100) {
            item.title = "● Status: \(statusText)"
        }
        if let item = menu.item(withTag: 101) {
            item.title = "📦 Último build: \(lastBuildInfo)"
        }
        if let item = menu.item(withTag: 102) {
            item.title = isUpdateAvailable ? "⬆ Nova Versão Disponível (\(latestVersionFound)) — Atualizar" : "🔄 Checar Atualizações (Update)"
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
        case "busy": fill = NSColor.systemBlue
        case "online": fill = NSColor.systemGreen
        default: fill = NSColor.systemRed
        }

        let circle = NSBezierPath(ovalIn: rect)
        fill.setFill()
        circle.fill()

        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.boldSystemFont(ofSize: 9.5),
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

    @objc private func checkForUpdates() {
        guard let url = URL(string: "https://api.nuget.org/v3-flatcontainer/cwsoftware.mauiforge/index.json") else { return }
        
        URLSession.shared.dataTask(with: url) { [weak self] data, _, error in
            guard let self = self else { return }
            
            DispatchQueue.main.async {
                if let data = data, error == nil,
                   let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                   let versions = json["versions"] as? [String], let latest = versions.last {
                    
                    if latest != self.appVersion {
                        self.isUpdateAvailable = true
                        self.latestVersionFound = latest
                        self.updateMenu()
                        
                        let updateAlert = NSAlert()
                        updateAlert.messageText = "⬆ Nova Versão Disponível!"
                        updateAlert.informativeText = "Uma nova versão do MAUI Forge foi encontrada no NuGet:\n\n"
                            + "• Versão instalada: \(self.appVersion)\n"
                            + "• Nova versão: \(latest)\n\n"
                            + "Deseja atualizar agora automaticamente?"
                        updateAlert.addButton(withTitle: "Atualizar Agora")
                        updateAlert.addButton(withTitle: "Agora Não")
                        let resp = updateAlert.runModal()
                        if resp == .alertFirstButtonReturn {
                            self.runUpdateCommand()
                        }
                    } else {
                        let upToDateAlert = NSAlert()
                        upToDateAlert.messageText = "✓ Sistema Atualizado"
                        upToDateAlert.informativeText = "O MAUI Forge já está rodando a versão mais recente (\(self.appVersion))."
                        upToDateAlert.addButton(withTitle: "OK")
                        upToDateAlert.runModal()
                    }
                } else {
                    let errAlert = NSAlert()
                    errAlert.messageText = "⚠ Falha ao Checar Atualizações"
                    errAlert.informativeText = "Não foi possível verificar novas versões no NuGet.org no momento. Verifique sua conexão de rede."
                    errAlert.addButton(withTitle: "OK")
                    errAlert.runModal()
                }
            }
        }.resume()
    }

    private func runUpdateCommand() {
        let task = Process()
        task.launchPath = "/bin/bash"
        task.arguments = ["-c", "maui-forge --update"]
        task.launch()
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
