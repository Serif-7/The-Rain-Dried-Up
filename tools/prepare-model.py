"""Shrinks a .glb's textures and optionally rescales it, in place (docs/model-prep.md).

python3 tools/prepare-model.py <model.glb> <largest texture side> [longest side in metres]    (needs Pillow and numpy)

Every embedded image is resized to at most the given side (PNG). With a length, every position is scaled evenly so the model's longest side is
that many metres (baked into the vertices; the model's nodes must carry no scale).
"""
import io, json, struct, sys
import numpy as np
from PIL import Image

path, side = sys.argv[1], int(sys.argv[2])
longest = float(sys.argv[3]) if len(sys.argv) > 3 else None
data = open(path, 'rb').read()
jl = struct.unpack('<I', data[12:16])[0]
j = json.loads(data[20:20 + jl])
bo = 20 + jl
bl = struct.unpack('<I', data[bo:bo + 4])[0]
binc = data[bo + 8:bo + 8 + bl]

views = [bytearray(binc[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']]) for v in j['bufferViews']]

for im in j.get('images', []):
    raw = bytes(views[im['bufferView']])
    img = Image.open(io.BytesIO(raw))
    img.load()
    if max(img.size) > side:
        f = side / max(img.size)
        img = img.resize((max(1, round(img.width * f)), max(1, round(img.height * f))), Image.LANCZOS)
    out = io.BytesIO()
    img.save(out, 'PNG', optimize=True)
    views[im['bufferView']] = bytearray(out.getvalue())
    im['mimeType'] = 'image/png'

if longest:
    mins = np.full(3, 1e9); maxs = np.full(3, -1e9)
    pos = {pr['attributes']['POSITION'] for m in j['meshes'] for pr in m['primitives']}
    for a in pos:
        acc = j['accessors'][a]; mins = np.minimum(mins, acc['min']); maxs = np.maximum(maxs, acc['max'])
    f = longest / float((maxs - mins).max())
    for a in pos:
        acc = j['accessors'][a]; v = j['bufferViews'][acc['bufferView']]
        assert acc['componentType'] == 5126 and v.get('byteStride', 12) == 12
        off = acc.get('byteOffset', 0)
        arr = np.frombuffer(bytes(views[acc['bufferView']]), dtype='<f4', count=acc['count'] * 3, offset=off).reshape(-1, 3) * f
        buf = views[acc['bufferView']]
        buf[off:off + arr.nbytes] = arr.astype('<f4').tobytes()
        acc['min'] = [float(x) for x in arr.min(0)]; acc['max'] = [float(x) for x in arr.max(0)]

blob = bytearray()
for v, b in zip(j['bufferViews'], views):
    while len(blob) % 4: blob.append(0)
    v['byteOffset'] = len(blob); v['byteLength'] = len(b); v['buffer'] = 0
    blob += b
while len(blob) % 4: blob.append(0)
j['buffers'] = [{'byteLength': len(blob)}]
js = json.dumps(j, separators=(',', ':')).encode()
js += b' ' * ((4 - len(js) % 4) % 4)
total = 12 + 8 + len(js) + 8 + len(blob)
with open(path, 'wb') as fo:
    fo.write(struct.pack('<III', 0x46546C67, 2, total))
    fo.write(struct.pack('<II', len(js), 0x4E4F534A)); fo.write(js)
    fo.write(struct.pack('<II', len(blob), 0x004E4942)); fo.write(blob)
print(path, total)
