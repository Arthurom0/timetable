// Timetable for macOS: a native window that shows Timetable.html.
// The native side stores the data and downloads calendar links. Build with build.sh.
import Cocoa
import WebKit
import UniformTypeIdentifiers

let storeDir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Timetable")
let storeFile = storeDir.appendingPathComponent("store.json")

final class Bridge: NSObject, WKScriptMessageHandler, WKScriptMessageHandlerWithReply {
    var store: [String: String] = [:]

    override init() {
        super.init()
        if let data = try? Data(contentsOf: storeFile), let dict = try? JSONSerialization.jsonObject(with: data) as? [String: String] {
            store = dict
        }
    }

    // "save": the page hands over a key and value to keep between launches
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let body = message.body as? [String: Any], let key = body["key"] as? String, let value = body["value"] as? String else { return }
        store[key] = value
        try? FileManager.default.createDirectory(at: storeDir, withIntermediateDirectories: true)
        if let data = try? JSONSerialization.data(withJSONObject: store) { try? data.write(to: storeFile, options: .atomic) }
    }

    // "ics": the page asks for a calendar link to be downloaded
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage, replyHandler: @escaping (Any?, String?) -> Void) {
        guard let text = message.body as? String, let url = URL(string: text), url.scheme?.lowercased() == "https" else {
            replyHandler(nil, "Paste a link that starts with https://"); return
        }
        var request = URLRequest(url: url)
        request.setValue("Timetable/1.0", forHTTPHeaderField: "User-Agent")
        request.cachePolicy = .reloadIgnoringLocalCacheData
        URLSession.shared.dataTask(with: request) { data, response, _ in
            DispatchQueue.main.async {
                if let data = data, let http = response as? HTTPURLResponse, http.statusCode == 200,
                   let body = String(data: data, encoding: .utf8) ?? String(data: data, encoding: .isoLatin1) {
                    replyHandler(body, nil)
                } else {
                    replyHandler(nil, "Could not download - check the link and your connection")
                }
            }
        }.resume()
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate, WKUIDelegate {
    var window: NSWindow!
    var webView: WKWebView!
    let bridge = Bridge()

    func applicationDidFinishLaunching(_ notification: Notification) {
        let config = WKWebViewConfiguration()
        let content = config.userContentController
        content.add(bridge, name: "save")
        content.addScriptMessageHandler(bridge, contentWorld: .page, name: "ics")
        let json = (try? JSONSerialization.data(withJSONObject: bridge.store)).flatMap { String(data: $0, encoding: .utf8) } ?? "{}"
        content.addUserScript(WKUserScript(source: "window.__ttStore = \(json);", injectionTime: .atDocumentStart, forMainFrameOnly: true))

        webView = WKWebView(frame: .zero, configuration: config)
        webView.uiDelegate = self
        webView.autoresizingMask = [.width, .height]

        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1320, height: 860),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered, defer: false)
        window.title = "Timetable"
        window.titlebarAppearsTransparent = true
        window.minSize = NSSize(width: 980, height: 600)
        window.contentView = webView
        window.center()
        window.setFrameAutosaveName("TimetableMain")
        window.makeKeyAndOrderFront(nil)

        guard let page = Bundle.main.url(forResource: "Timetable", withExtension: "html") else {
            let alert = NSAlert(); alert.messageText = "Timetable.html is missing from the app."; alert.runModal(); NSApp.terminate(nil); return
        }
        webView.loadFileURL(page, allowingReadAccessTo: page.deletingLastPathComponent())
        buildMenu()
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    // The page's "Choose .ics file" button
    func webView(_ webView: WKWebView, runOpenPanelWith parameters: WKOpenPanelParameters, initiatedByFrame frame: WKFrameInfo,
                 completionHandler: @escaping ([URL]?) -> Void) {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = parameters.allowsMultipleSelection
        panel.canChooseDirectories = false
        if let ics = UTType(filenameExtension: "ics") { panel.allowedContentTypes = [ics] }
        panel.beginSheetModal(for: window) { result in completionHandler(result == .OK ? panel.urls : nil) }
    }

    // Copy / paste and Quit only work when the app has these menus
    func buildMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem(); main.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Hide Timetable", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(NSMenuItem.separator())
        appMenu.addItem(withTitle: "Quit Timetable", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu

        let editItem = NSMenuItem(); main.addItem(editItem)
        let edit = NSMenu(title: "Edit")
        edit.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        edit.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = edit

        let windowItem = NSMenuItem(); main.addItem(windowItem)
        let windowMenu = NSMenu(title: "Window")
        windowMenu.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowItem.submenu = windowMenu
        NSApp.mainMenu = main
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
