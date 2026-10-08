"""Split the Quadruped Tank's one merged model into named parts with pivot voxels."""
import struct, sys
src, dst = sys.argv[1], sys.argv[2]
d = open(src, 'rb').read()
i = 20; chunks = []; size = None; vox = []
while i < len(d):
    cid = d[i:i+4]; n, m = struct.unpack_from('<ii', d, i+4); b = d[i+12:i+12+n]
    chunks.append((cid, b))
    if cid == b'SIZE': size = struct.unpack('<iii', b)
    if cid == b'XYZI':
        c = struct.unpack_from('<i', b, 0)[0]
        vox = [tuple(b[4+4*k:8+4*k]) for k in range(c)]
    i += 12 + n + m
CX, CY = size[0] // 2, size[1] // 2   # original centring; original T = (0, 0, size_z/2)
PIVOT = 255

# Legs: x <= 10 is the +X... careful: model faces -Y, so its left is +X.
legs = {  # name suffix: (x range, y range, shin column x, hip x, knee/ankle x)
    'FL': ((27, 37), (0, 31)), 'FR': ((0, 10), (0, 31)),
    'BL': ((27, 37), (32, 49)), 'BR': ((0, 10), (32, 49)),
}
def leg_of(x, y):
    for k, ((x0, x1), (y0, y1)) in legs.items():
        if x0 <= x <= x1 and y0 <= y <= y1: return k
parts = {}
def put(name, v): parts.setdefault(name, []).append(v)
for v in vox:
    x, y, z, c = v
    k = leg_of(x, y) if z <= 21 else None
    if k:
        shin_x = (33, 34) if k[1] == 'L' else (3, 4)
        if z <= 5: put('Foot' + k, v)
        elif z <= 9 and shin_x[0] <= x <= shin_x[1]: put('Shin' + k, v)
        else: put('Thigh' + k, v)
    elif z >= 47 or (44 <= z <= 46 and 17 <= x <= 20 and 31 <= y <= 34):
        put('Turret', v)
    else:
        put('Body', v)

pivots = {'Turret': (18, 32, 43)}
for k in legs:
    yc = 23 if k[0] == 'F' else 39
    left = k[1] == 'L'
    pivots['Thigh' + k] = (28 if left else 9, yc, 14)    # hip, in the gap beside the strut
    pivots['Shin' + k] = (34 if left else 3, yc, 10)     # knee, inside the thigh housing
    pivots['Foot' + k] = (34 if left else 3, yc, 6)      # ankle, where the shin meets the foot
markers = {'Seat': (18, 51, 63), 'Muzzle': (18, 0, 33), 'TurretMuzzle': (18, 19, 51)}

def i32(*a): return struct.pack('<' + 'i' * len(a), *a)
def s(x): x = x.encode(); return i32(len(x)) + x
def dct(dd): return i32(len(dd)) + b''.join(s(k) + s(v) for k, v in dd.items())
def chunk(cid, content, children=b''): return cid + i32(len(content), len(children)) + content + children

models, nodes = [], []
order = ['Body', 'Turret'] + [p + k for k in ('FL', 'FR', 'BL', 'BR') for p in ('Thigh', 'Shin', 'Foot')]
items = [(n, parts[n] + ([(*pivots[n], PIVOT)] if n in pivots else [])) for n in order] + [('@' + n, [(*p, 1)]) for n, p in markers.items()]
for name, vs in items:
    xs = [v[0] for v in vs]; ys = [v[1] for v in vs]; zs = [v[2] for v in vs]
    lo = (min(xs), min(ys), min(zs)); sz = (max(xs) - lo[0] + 1, max(ys) - lo[1] + 1, max(zs) - lo[2] + 1)
    local = [(v[0] - lo[0], v[1] - lo[1], v[2] - lo[2], v[3]) for v in vs]
    t = (lo[0] + sz[0] // 2 - CX, lo[1] + sz[1] // 2 - CY, lo[2] + sz[2] // 2)
    models.append((sz, local)); nodes.append((name, t))
    print(f'{name:14s} voxels={len(vs):6d} size={sz} t={t}')

out = b''
for sz, local in models:
    out += chunk(b'SIZE', i32(*sz))
    out += chunk(b'XYZI', i32(len(local)) + b''.join(bytes(v) for v in local))
# Scene graph: nTRN 0 -> nGRP 1 -> (nTRN 2+2k -> nSHP 3+2k)
out += chunk(b'nTRN', i32(0) + dct({}) + i32(1, -1, -1, 1) + dct({}))
out += chunk(b'nGRP', i32(1) + dct({}) + i32(len(nodes)) + i32(*[2 + 2 * k for k in range(len(nodes))]))
for k, (name, t) in enumerate(nodes):
    out += chunk(b'nTRN', i32(2 + 2 * k) + dct({'_name': name}) + i32(3 + 2 * k, -1, 0, 1) + dct({'_t': f'{t[0]} {t[1]} {t[2]}'}))
    out += chunk(b'nSHP', i32(3 + 2 * k) + dct({}) + i32(1) + i32(k) + dct({}))
for cid, b in chunks:
    if cid == b'RGBA':
        b = bytearray(b); b[254*4:255*4] = bytes((255, 0, 255, 255)); b = bytes(b)  # pivot index 255 in magenta
    if cid in (b'RGBA', b'MATL', b'LAYR', b'rOBJ', b'rCAM', b'NOTE'):
        out += chunk(cid, b)
open(dst, 'wb').write(b'VOX ' + i32(150) + chunk(b'MAIN', b'', out))
