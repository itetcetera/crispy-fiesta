"""Writes the original's character set (the first 64 characters at $1800 in the memory snapshot) as
client/public/assets/font.png: 16 x 4 characters of 8 x 8 pixels, white on transparent, in the
order of the Atari's internal codes (space, punctuation, digits, letters).

    python3 tools/atari_font.py
"""
import os
import struct
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SNAPSHOT = os.path.join(ROOT, "reference", "MULE - Memory Snapshot.bin")
OUT = os.path.join(ROOT, "client", "public", "assets", "font.png")

data = open(SNAPSHOT, "rb").read()
W, H = 16 * 8, 4 * 8
rows = []
for y in range(H):
    row = bytearray([0])
    for x in range(W):
        c = (y // 8) * 16 + x // 8
        on = data[0x1800 + c * 8 + y % 8] & (0x80 >> (x % 8))
        row += bytes([255, 255, 255, 255 if on else 0])
    rows.append(bytes(row))


def chunk(t, b):
    return struct.pack(">I", len(b)) + t + b + struct.pack(">I", zlib.crc32(t + b) & 0xFFFFFFFF)


with open(OUT, "wb") as f:
    f.write(b"\x89PNG\r\n\x1a\n")
    f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0)))
    f.write(chunk(b"IDAT", zlib.compress(b"".join(rows))))
    f.write(chunk(b"IEND", b""))
print(OUT)
