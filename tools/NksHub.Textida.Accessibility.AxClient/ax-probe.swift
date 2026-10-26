// Out-of-process NSAccessibility client for the accessibility host.
// Build: swiftc -O ax-probe.swift -o ax-probe        Run: ax-probe <host pid> <command pipe>
// The process running it needs the Accessibility permission (System Settings, Privacy and Security).
import Cocoa
import ApplicationServices

let sample = "Hello Textida\r\nsecond line, with words\r\nthird: \u{011B}\u{0161}\u{010D}\u{0159} \u{1F600} end\r\n"
var failures = 0
var selectionEvents = 0
var valueEvents = 0

func check(_ name: String, _ ok: Bool, _ detail: String = "") {
    print("\(ok ? "PASS" : "FAIL") \(name)\(detail.isEmpty ? "" : ": " + detail)")
    if !ok { failures += 1 }
}

func show(_ s: String) -> String { s.replacingOccurrences(of: "\r", with: "\\r").replacingOccurrences(of: "\n", with: "\\n") }

func copy(_ el: AXUIElement, _ name: String) -> CFTypeRef? {
    var v: CFTypeRef?
    let e = AXUIElementCopyAttributeValue(el, name as CFString, &v)
    if e != .success { print("  (\(name) -> AXError \(e.rawValue))") }
    return e == .success ? v : nil
}

func copyParam(_ el: AXUIElement, _ name: String, _ p: CFTypeRef) -> CFTypeRef? {
    var v: CFTypeRef?
    let e = AXUIElementCopyParameterizedAttributeValue(el, name as CFString, p, &v)
    if e != .success { print("  (\(name) -> AXError \(e.rawValue))") }
    return e == .success ? v : nil
}

func range(_ v: CFTypeRef?) -> CFRange? {
    guard let v = v, CFGetTypeID(v) == AXValueGetTypeID() else { return nil }
    var r = CFRange()
    return AXValueGetValue(v as! AXValue, .cfRange, &r) ? r : nil
}

func rangeValue(_ loc: Int, _ len: Int) -> CFTypeRef {
    var r = CFRange(location: loc, length: len)
    return AXValueCreate(.cfRange, &r)!
}

func findTextArea(_ el: AXUIElement, _ depth: Int = 0) -> AXUIElement? {
    if depth > 8 { return nil }
    if let role = copy(el, kAXRoleAttribute) as? String, role == "AXTextArea" { return el }
    if let kids = copy(el, kAXChildrenAttribute) as? [AXUIElement] {
        for k in kids { if let f = findTextArea(k, depth + 1) { return f } }
    }
    return nil
}

func pump(_ seconds: Double) { CFRunLoopRunInMode(.defaultMode, seconds, false) }

func send(_ pipe: String, _ lines: [String]) {
    if let h = FileHandle(forWritingAtPath: pipe) {
        h.write((lines.joined(separator: "\n") + "\n").data(using: .utf8)!)
        try? h.close()
    }
}

let args = CommandLine.arguments
guard args.count >= 3, let pid = pid_t(args[1]) else { print("usage: ax-probe <pid> <command pipe>"); exit(2) }
let pipe = args[2]

check("process is trusted for accessibility", AXIsProcessTrusted())
if !AXIsProcessTrusted() { print("ABORT: no accessibility permission"); exit(3) }

let app = AXUIElementCreateApplication(pid)
var area: AXUIElement?
for _ in 0..<50 { area = findTextArea(app); if area != nil { break }; pump(0.2) }
guard let el = area else { print("FAIL no AXTextArea found in the host"); exit(1) }

check("role", (copy(el, kAXRoleAttribute) as? String) == "AXTextArea")
check("label", (copy(el, kAXDescriptionAttribute) as? String) == "Textida probe editor", "\(String(describing: copy(el, kAXDescriptionAttribute)))")
let value = copy(el, kAXValueAttribute) as? String ?? ""
check("AXValue is the document text", value == sample, show(value))
check("AXNumberOfCharacters", (copy(el, kAXNumberOfCharactersAttribute) as? Int) == (sample as NSString).length, "\(String(describing: copy(el, kAXNumberOfCharactersAttribute)))")
check("initial caret", range(copy(el, kAXSelectedTextRangeAttribute)).map { $0.location == 0 && $0.length == 0 } ?? false)
check("AXVisibleCharacterRange", range(copy(el, kAXVisibleCharacterRangeAttribute)).map { $0.location == 0 && $0.length > 0 } ?? false)

// notifications
let callback: AXObserverCallback = { _, _, notification, _ in
    if (notification as String) == "AXSelectedTextChanged" { selectionEvents += 1 } else { valueEvents += 1 }
}
var observer: AXObserver?
AXObserverCreate(pid, callback, &observer)
if let obs = observer {
    AXObserverAddNotification(obs, el, "AXSelectedTextChanged" as CFString, nil)
    AXObserverAddNotification(obs, el, "AXValueChanged" as CFString, nil)
    CFRunLoopAddSource(CFRunLoopGetCurrent(), AXObserverGetRunLoopSource(obs), .defaultMode)
}

// caret moves driven through the host's command hook
send(pipe, Array(repeating: "key right", count: 5))
pump(1.5)
check("caret moved to 5", range(copy(el, kAXSelectedTextRangeAttribute)).map { $0.location == 5 && $0.length == 0 } ?? false)
check("AXSelectedTextChanged notifications", selectionEvents >= 5, "\(selectionEvents)")
check("AXInsertionPointLineNumber", (copy(el, "AXInsertionPointLineNumber") as? Int) == 0)

// selection set from the client side
let before = selectionEvents
let setErr = AXUIElementSetAttributeValue(el, kAXSelectedTextRangeAttribute as CFString, rangeValue(6, 7))
pump(0.7)
check("set AXSelectedTextRange", setErr == .success && range(copy(el, kAXSelectedTextRangeAttribute)).map { $0.location == 6 && $0.length == 7 } ?? false, "AXError \(setErr.rawValue)")
check("AXSelectedText", (copy(el, kAXSelectedTextAttribute) as? String) == "Textida")
check("selection notification after set", selectionEvents > before)

// parameterized attributes
check("AXLineForIndex", (copyParam(el, "AXLineForIndex", 20 as CFNumber) as? Int) == 1)
check("AXRangeForLine", range(copyParam(el, "AXRangeForLine", 1 as CFNumber)).map { $0.location == 15 && $0.length == 25 } ?? false)
check("AXStringForRange", (copyParam(el, "AXStringForRange", rangeValue(0, 5)) as? String) == "Hello")
let emoji = (sample as NSString).range(of: "\u{1F600}").location
check("AXRangeForIndex covers a surrogate pair", range(copyParam(el, "AXRangeForIndex", emoji as CFNumber)).map { $0.location == emoji && $0.length == 2 } ?? false)

var frame = CGRect.zero
if let b = copyParam(el, "AXBoundsForRange", rangeValue(7, 1)), AXValueGetValue(b as! AXValue, .cgRect, &frame) {
    var windowFrame = CGRect.zero
    if let w = copy(el, "AXFrame"), AXValueGetValue(w as! AXValue, .cgRect, &windowFrame) {
        check("AXBoundsForRange lies inside the element frame", frame.width > 3 && frame.height > 8 && windowFrame.insetBy(dx: -1, dy: -1).contains(frame), "range \(frame) element \(windowFrame)")
    }
    var p = CGPoint(x: frame.midX, y: frame.midY)
    if let pv = AXValueCreate(.cgPoint, &p) {
        check("AXRangeForPosition", range(copyParam(el, "AXRangeForPosition", pv)).map { $0.location == 7 } ?? false, "\(String(describing: range(copyParam(el, "AXRangeForPosition", pv))))")
    }
} else { check("AXBoundsForRange", false) }

// text changes
let valuesBefore = valueEvents
send(pipe, ["type XY"])
pump(1.0)
let typed = copy(el, kAXValueAttribute) as? String ?? ""
check("typing replaces the selection and raises AXValueChanged", typed.hasPrefix("Hello XY\r\nsecond") && valueEvents - valuesBefore >= 2, "\(valueEvents - valuesBefore) events, \(show(String(typed.prefix(20))))")
check("caret after typing", range(copy(el, kAXSelectedTextRangeAttribute)).map { $0.location == 8 && $0.length == 0 } ?? false)
send(pipe, ["back", "back"])
pump(0.7)
check("backspace", (copy(el, kAXValueAttribute) as? String ?? "").hasPrefix("Hello \r\nsecond"))

send(pipe, ["quit"])
print(failures == 0 ? "ALL PASSED" : "\(failures) FAILED")
exit(Int32(failures))
