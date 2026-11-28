"""Prints what matters about .glb models before they go into Structures or ItemModels (docs/model-prep.md).

python3 tools/model-info.py <model.glb>...    (standard library only)

For each model: its size in metres (X, Y up, Z), longest side, triangles, materials, and each embedded texture's pixel size and bytes.
"""
import json
import struct
import sys


def png_size(data):
    """Width and height of a PNG or JPEG image, or None."""
    if data[:8] == b"\x89PNG\r\n\x1a\n":
        return struct.unpack(">II", data[16:24])
    if data[:2] == b"\xff\xd8":
        i = 2
        while i < len(data) - 9:
            marker, length = data[i + 1], struct.unpack(">H", data[i + 2:i + 4])[0]
            if marker in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", data[i + 5:i + 9])
                return w, h
            i += 2 + length
    return None


def info(path):
    data = open(path, "rb").read()
    if data[:4] != b"glTF":
        raise SystemExit(f"{path}: not a .glb file")
    json_length = struct.unpack("<I", data[12:16])[0]
    gltf = json.loads(data[20:20 + json_length])
    blob_start = 20 + json_length + 8

    lows, highs = [float("inf")] * 3, [float("-inf")] * 3
    triangles = 0
    for mesh in gltf["meshes"]:
        for primitive in mesh["primitives"]:
            position = gltf["accessors"][primitive["attributes"]["POSITION"]]
            lows = [min(a, b) for a, b in zip(lows, position["min"])]
            highs = [max(a, b) for a, b in zip(highs, position["max"])]
            count = gltf["accessors"][primitive["indices"]]["count"] if "indices" in primitive else position["count"]
            triangles += count // 3
    size = [h - l for h, l in zip(highs, lows)]
    nodes = [n for n in gltf.get("nodes", []) if any(k in n for k in ("scale", "rotation", "matrix"))]

    print(f"{path}")
    print(f"  size m (X, Y up, Z): {size[0]:.3f} x {size[1]:.3f} x {size[2]:.3f}, longest {max(size):.3f}")
    print(f"  triangles: {triangles}, materials: {len(gltf.get('materials', []))}")
    for image in gltf.get("images", []):
        view = gltf["bufferViews"][image["bufferView"]]
        start = blob_start + view.get("byteOffset", 0)
        pixels = png_size(data[start:start + view["byteLength"]])
        shown = f"{pixels[0]}x{pixels[1]}" if pixels else "?"
        print(f"  texture: {shown}, {view['byteLength'] / 1e6:.1f} MB")
    if nodes:
        print("  note: nodes carry scale/rotation; sizes above are before them")


for model in sys.argv[1:]:
    info(model)
