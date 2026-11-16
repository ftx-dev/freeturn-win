"""Собирает .ico из PNG (Windows Vista+ понимает PNG внутри ICO). python3 scripts/pack-ico.py assets/FreeTurnX.ico assets/icon-*.png"""
import struct, sys
out, files = sys.argv[1], sys.argv[2:]
images = []
for f in files:
    data = open(f, 'rb').read()
    w, h = struct.unpack('>II', data[16:24])
    images.append((w, h, data))
images.sort()
header = struct.pack('<HHH', 0, 1, len(images))
offset = 6 + 16 * len(images)
entries, blobs = b'', b''
for w, h, data in images:
    entries += struct.pack('<BBBBHHII', w % 256, h % 256, 0, 0, 1, 32, len(data), offset + len(blobs))
    blobs += data
open(out, 'wb').write(header + entries + blobs)
