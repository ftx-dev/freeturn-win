// ICO с классическими BMP-кадрами (16, 32, 48): NSIS не читает PNG внутри ICO.
// Запуск: swift scripts/make-bmp-ico.swift assets/logo.png assets/installer.ico
import AppKit

let args = CommandLine.arguments
let source = NSImage(contentsOfFile: args[1])!
var frames: [(Int, Data)] = []
for size in [16, 32, 48] {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: size * 4, bitsPerPixel: 32)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    NSGraphicsContext.current?.imageInterpolation = .high
    source.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
    NSGraphicsContext.restoreGraphicsState()
    var dib = Data()
    func u32(_ v: UInt32) { withUnsafeBytes(of: v.littleEndian) { dib.append(contentsOf: $0) } }
    func u16(_ v: UInt16) { withUnsafeBytes(of: v.littleEndian) { dib.append(contentsOf: $0) } }
    // BITMAPINFOHEADER: высота удвоена (цвет + маска), строки снизу вверх, BGRA.
    u32(40); u32(UInt32(size)); u32(UInt32(size * 2)); u16(1); u16(32); u32(0); u32(0); u32(0); u32(0); u32(0); u32(0)
    let p = rep.bitmapData!
    for y in (0..<size).reversed() {
        for x in 0..<size {
            let i = (y * size + x) * 4
            // NSBitmapImageRep хранит premultiplied RGBA; для ICO нужен прямой BGRA.
            let a = p[i + 3]
            func un(_ v: UInt8) -> UInt8 { a == 0 ? 0 : UInt8(min(255, Int(v) * 255 / Int(a))) }
            dib.append(contentsOf: [un(p[i + 2]), un(p[i + 1]), un(p[i]), a])
        }
    }
    // Маска AND: всё нули - прозрачность берётся из альфы. Строка выровнена на 4 байта.
    let maskRow = ((size + 31) / 32) * 4
    dib.append(Data(count: maskRow * size))
    frames.append((size, dib))
}
var ico = Data()
func w16(_ v: UInt16) { withUnsafeBytes(of: v.littleEndian) { ico.append(contentsOf: $0) } }
func w32(_ v: UInt32) { withUnsafeBytes(of: v.littleEndian) { ico.append(contentsOf: $0) } }
w16(0); w16(1); w16(UInt16(frames.count))
var offset = 6 + 16 * frames.count
for (size, dib) in frames {
    ico.append(contentsOf: [UInt8(size), UInt8(size), 0, 0]); w16(1); w16(32); w32(UInt32(dib.count)); w32(UInt32(offset))
    offset += dib.count
}
for (_, dib) in frames { ico.append(dib) }
try! ico.write(to: URL(fileURLWithPath: args[2]))
