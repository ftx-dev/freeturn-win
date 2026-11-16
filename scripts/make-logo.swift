// Варианты логотипа FreeTurnX. Запуск: swift scripts/make-logo.swift <variant> <shape> <pixels> <out.png>
//   variant: standard | black | x      shape: squircle (иконка macOS) | tile (иконка Windows) | circle (аватар Telegram)
// Нужны assets/AppIcon.svg (полная иконка) и assets/AppIconForeground.svg (только знак).
import AppKit

let args = CommandLine.arguments
guard args.count == 5, let px = Int(args[3]) else { print("usage: make-logo.swift standard|black|x squircle|circle <pixels> <out.png>"); exit(2) }
let (variant, shape, out) = (args[1], args[2], args[4])
let root = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
let full = NSImage(contentsOf: root.appendingPathComponent("assets/AppIcon.svg"))!
let mark = NSImage(contentsOf: root.appendingPathComponent("assets/AppIconForeground.svg"))!

func c(_ hex: Int) -> NSColor { NSColor(red: CGFloat(hex >> 16 & 255) / 255, green: CGFloat(hex >> 8 & 255) / 255, blue: CGFloat(hex & 255) / 255, alpha: 1) }
func poly(_ pts: [(CGFloat, CGFloat)]) -> NSBezierPath {
    let p = NSBezierPath(); p.move(to: NSPoint(x: pts[0].0, y: pts[0].1))
    for q in pts.dropFirst() { p.line(to: NSPoint(x: q.0, y: q.1)) }
    p.close(); return p
}

let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
NSGraphicsContext.current?.imageInterpolation = .high
let size = CGFloat(px)

// Форма: аватар Telegram - круг на всю площадь; иконка - скруглённый квадрат 824/1024 (сетка macOS).
let box: NSRect
let outline: NSBezierPath
if shape == "circle" {
    box = NSRect(x: 0, y: 0, width: size, height: size)
    outline = NSBezierPath(ovalIn: box)
} else {
    // squircle - сетка иконок macOS (824/1024); tile - Windows: почти на всю клетку, иначе в трее знак не читается.
    let side = shape == "tile" ? size * 0.94 : size * 824 / 1024
    box = NSRect(x: (size - side) / 2, y: (size - side) / 2, width: side, height: side)
    outline = NSBezierPath(roundedRect: box, xRadius: side * 0.2237, yRadius: side * 0.2237)
}
let u = box.width   // единица длины - сторона формы
func at(_ x: CGFloat, _ y: CGFloat) -> (CGFloat, CGFloat) { (box.minX + u * x, box.minY + u * y) }

NSGraphicsContext.saveGraphicsState()
outline.addClip()
if variant == "standard" {
    // Как на Android: синий фон, центр слоя увеличен (Android показывает только середину иконки).
    let zoom: CGFloat = 1.3
    full.draw(in: box.insetBy(dx: -u * (zoom - 1) / 2, dy: -u * (zoom - 1) / 2))
} else {
    NSColor.black.setFill()
    outline.fill()
    // Мягкое свечение за знаком: на чистом чёрном так он не теряется.
    NSGradient(colors: [NSColor(red: 0.42, green: 0.55, blue: 1, alpha: 0.22), NSColor(red: 0.2, green: 0.3, blue: 0.8, alpha: 0)])!
        .draw(in: NSBezierPath(ovalIn: box.insetBy(dx: u * 0.1, dy: u * 0.1)), relativeCenterPosition: .zero)
    if variant == "x" {
        // Знак слева, справа под его полосой - строгая буква X (толстый \, тонкий / с разрезом).
        let side = u * 1.16
        mark.draw(in: NSRect(x: box.minX + (u - side) / 2 - u * 0.125, y: box.minY + (u - side) / 2 + u * 0.025, width: side, height: side))
        let (l, b) = at(0.615, 0.235), (r, t) = at(0.845, 0.495)
        let thick = u * 0.085, thin = u * 0.03, gap = u * 0.022
        NSGradient(colors: [c(0xD9E3FF), c(0xB0C2FF), c(0x94AEFA)])!.draw(in: poly([(l, t), (l + thick, t), (r, b), (r - thick, b)]), angle: -60)
        NSGraphicsContext.saveGraphicsState()
        let cut = NSBezierPath(rect: box)
        let dx = thick * 0.5 + gap
        cut.append(poly([(l - dx, t), (l + thick + dx, t), (r + dx, b), (r - thick - dx, b)]).reversed)
        cut.addClip()
        c(0xB6C9FF).setFill()
        poly([(l, b), (l + thin, b), (r, t), (r - thin, t)]).fill()
        NSGraphicsContext.restoreGraphicsState()
    } else {
        let zoom: CGFloat = 1.3
        mark.draw(in: box.insetBy(dx: -u * (zoom - 1) / 2, dy: -u * (zoom - 1) / 2))
    }
}
NSGraphicsContext.restoreGraphicsState()

// Тонкое голубое кольцо по краю - у чёрных вариантов, чтобы форма читалась на тёмном фоне.
if variant != "standard" {
    let w = u * 0.022
    let ring: NSBezierPath = shape == "circle"
        ? NSBezierPath(ovalIn: box.insetBy(dx: w / 2, dy: w / 2))
        : NSBezierPath(roundedRect: box.insetBy(dx: w / 2, dy: w / 2), xRadius: u * 0.2237 - w / 2, yRadius: u * 0.2237 - w / 2)
    ring.lineWidth = w
    NSGraphicsContext.saveGraphicsState()
    ring.setClip()
    NSGraphicsContext.restoreGraphicsState()
    let stroke = ring.copy() as! NSBezierPath
    NSGraphicsContext.saveGraphicsState()
    // Градиент по кольцу: обводку превращаем в контур и заливаем.
    let cg = NSGraphicsContext.current!.cgContext
    cg.addPath(stroke.cgPath)
    cg.setLineWidth(w)
    cg.replacePathWithStrokedPath()
    cg.clip()
    NSGradient(colors: [c(0xB6C9FF), c(0x94AEFA), c(0x6F8FF0)])!.draw(in: box, angle: -50)
    NSGraphicsContext.restoreGraphicsState()
}
NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
