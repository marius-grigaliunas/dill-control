"""Split a one-model .vox into named parts with pivot voxels and markers.

usage: split_mech.py <fighter|storage> <in.vox> <out.vox>

A part's pivot is the centre of its top layer, one voxel above it: where a
thigh hangs from the hip, a shin from the knee, a foot from the ankle, an arm
from the shoulder. The pivot voxel sits in the part's own empty space, so it
never leaves a hole.
"""
import struct, sys

kind, src, dst = sys.argv[1], sys.argv[2], sys.argv[3]
d = open(src, 'rb').read()
i = 20; chunks = []; size = None; vox = []
while i < len(d):
    cid = d[i:i + 4]; n, m = struct.unpack_from('<ii', d, i + 4); b = d[i + 12:i + 12 + n]
    if cid != b'MAIN':
        chunks.append((cid, b))
    if cid == b'SIZE': size = struct.unpack('<iii', b)
    if cid == b'XYZI':
        c = struct.unpack_from('<i', b, 0)[0]
        vox = [tuple(b[4 + 4 * k:8 + 4 * k]) for k in range(c)]
    i += 12 + n + (m if cid != b'MAIN' else 0)
CX, CY = size[0] // 2, size[1] // 2
PIVOT = 255

if kind == 'fighter':
    # 38 x 16 x 59. Front is -Y. Legs: thigh, shin, spike foot; arms are the
    # long pods hanging from the shoulders.
    def part(x, y, z):
        if z <= 47 and x <= 6: return 'ArmR'
        if z <= 47 and x >= 31: return 'ArmL'
        if z <= 43 and 7 <= x <= 15: side = 'R'
        elif z <= 43 and 22 <= x <= 30: side = 'L'
        else: return 'Body'
        if z <= 9: return 'Foot' + side
        if z <= 25: return 'Shin' + side
        return 'Thigh' + side
    order = ['Body', 'ThighL', 'ShinL', 'FootL', 'ThighR', 'ShinR', 'FootR', 'ArmL', 'ArmR']
    markers = {'Seat': (18, -2, 55), 'Muzzle': (4, 2, 18), 'Cannon': (33, 2, 18)}
    emit = {}
elif kind == 'storage':
    # 50 x 34 x 46. Four legs under a box with a saddlebag either side.
    def part(x, y, z):
        if z > 22: return 'Body'
        if 12 <= x <= 21: side = 'R'
        elif 28 <= x <= 37: side = 'L'
        else: return 'Body'
        end = 'F' if y <= 12 else 'B'
        if z <= 3: return 'Foot' + end + side
        if z <= 13: return 'Shin' + end + side
        return 'Thigh' + end + side
    order = ['Body'] + [p + k for k in ('FL', 'FR', 'BL', 'BR') for p in ('Thigh', 'Shin', 'Foot')]
    markers = {}
    # The OBJ this came from carried no materials: the eye and the lights
    # are made to glow again.
    emit = {7: ('1', '2'), 15: ('1', '2'), 8: ('1', '3')}
else:
    raise SystemExit('unknown kind')

parts = {}
for v in vox:
    parts.setdefault(part(*v[:3]), []).append(v)

pivots = {}
for name, vs in parts.items():
    if name == 'Body':
        continue
    top = max(v[2] for v in vs)
    layer = [v for v in vs if v[2] == top]
    px = round(sum(v[0] for v in layer) / len(layer) - 0.5)
    py = round(sum(v[1] for v in layer) / len(layer) - 0.5)
    pivots[name] = (px, py, top + 1)

def i32(*a): return struct.pack('<' + 'i' * len(a), *a)
def s(x): x = x.encode(); return i32(len(x)) + x
def dct(dd): return i32(len(dd)) + b''.join(s(k) + s(v) for k, v in dd.items())
def chunk(cid, content, children=b''): return cid + i32(len(content), len(children)) + content + children

items = [(n, parts[n] + ([(*pivots[n], PIVOT)] if n in pivots else [])) for n in order if n in parts]
items += [('@' + n, [(*p, 1)]) for n, p in markers.items()]
missing = [n for n in order if n not in parts]
if missing:
    print('no voxels for', missing)

models, nodes = [], []
for name, vs in items:
    xs = [v[0] for v in vs]; ys = [v[1] for v in vs]; zs = [v[2] for v in vs]
    lo = (min(xs), min(ys), min(zs))
    sz = (max(xs) - lo[0] + 1, max(ys) - lo[1] + 1, max(zs) - lo[2] + 1)
    local = [(v[0] - lo[0], v[1] - lo[1], v[2] - lo[2], v[3]) for v in vs]
    t = (lo[0] + sz[0] // 2 - CX, lo[1] + sz[1] // 2 - CY, lo[2] + sz[2] // 2)
    models.append((sz, local)); nodes.append((name, t))
    print(f'{name:10s} voxels={len(vs):6d} pivot={pivots.get(name)}')

out = b''
for sz, local in models:
    out += chunk(b'SIZE', i32(*sz))
    out += chunk(b'XYZI', i32(len(local)) + b''.join(bytes(v) for v in local))
out += chunk(b'nTRN', i32(0) + dct({}) + i32(1, -1, -1, 1) + dct({}))
out += chunk(b'nGRP', i32(1) + dct({}) + i32(len(nodes)) + i32(*[2 + 2 * k for k in range(len(nodes))]))
for k, (name, t) in enumerate(nodes):
    out += chunk(b'nTRN', i32(2 + 2 * k) + dct({'_name': name}) + i32(3 + 2 * k, -1, 0, 1) + dct({'_t': f'{t[0]} {t[1]} {t[2]}'}))
    out += chunk(b'nSHP', i32(3 + 2 * k) + dct({}) + i32(1) + i32(k) + dct({}))
for cid, b in chunks:
    if cid == b'RGBA':
        b = bytearray(b); b[254 * 4:255 * 4] = bytes((255, 0, 255, 255)); b = bytes(b)
    if cid in (b'RGBA', b'MATL', b'LAYR', b'rOBJ', b'rCAM', b'NOTE'):
        out += chunk(cid, b)
for index, (weight, flux) in emit.items():
    out += chunk(b'MATL', i32(index) + dct({'_type': '_emit', '_weight': weight, '_flux': flux}))
open(dst, 'wb').write(b'VOX ' + i32(150) + chunk(b'MAIN', b'', out))
