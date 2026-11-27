"""Packs branding/png/icon-*.png into src/SnapForge.App/Assets/SnapForge.ico (PNG-compressed entries)."""
import struct, pathlib
root = pathlib.Path(__file__).resolve().parent
sizes = [16, 20, 24, 32, 40, 48, 64, 256]
images = [(s, (root / "png" / f"icon-{s}.png").read_bytes()) for s in sizes]
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + 16 * len(images)
entries, blobs = b"", b""
for s, data in images:
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), offset + len(blobs))
    blobs += data
(root.parent / "src/SnapForge.App/Assets/SnapForge.ico").write_bytes(header + entries + blobs)
print("wrote SnapForge.ico", len(header + entries + blobs), "bytes,", sizes)
