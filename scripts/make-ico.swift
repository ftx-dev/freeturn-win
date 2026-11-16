// Иконка приложения для Windows из assets/AppIcon.svg (адаптивная иконка Android-клиента).
// Запуск на macOS: swift scripts/make-ico.swift assets/AppIcon.svg assets/
// Пишет PNG нужных размеров; scripts/pack-ico.py собирает из них .ico.
import AppKit

let args = CommandLine.arguments
guard args.count == 3, let svg = NSImage(contentsOfFile: args[1]) else { print("usage: make-ico.swift <in.svg> <outdir>"); exit(2) }

func render(_ pixels: Int, round: Bool) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    let size = CGFloat(pixels)
    // Маленькие размеры (значок в трее) - почти без полей, иначе знак не читается.
    let inset = pixels <= 32 ? size * 0.02 : size * 0.06
    let rect = NSRect(x: inset, y: inset, width: size - inset * 2, height: size - inset * 2)
    NSBezierPath(roundedRect: rect, xRadius: rect.width * (round ? 0.5 : 0.2), yRadius: rect.width * (round ? 0.5 : 0.2)).addClip()
    let zoom: CGFloat = 1.3
    svg.draw(in: rect.insetBy(dx: -rect.width * (zoom - 1) / 2, dy: -rect.height * (zoom - 1) / 2))
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

for size in [16, 20, 24, 32, 40, 48, 64, 128, 256] {
    try render(size, round: false).write(to: URL(fileURLWithPath: "\(args[2])/icon-\(size).png"))
}
try render(512, round: false).write(to: URL(fileURLWithPath: "\(args[2])/logo.png"))
