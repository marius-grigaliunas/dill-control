"""Rebuild a MagicaVoxel .vox from MagicaVoxel's own OBJ export.

The export is axis-aligned faces on the voxel grid (0.1 units per voxel),
UV-mapped onto a 256x1 palette strip. Solid cells are found by scanlines along
X between faces pointing -X (entering) and +X (leaving); each cell takes the
colour of the nearest face along its scanline. Writes one model with the
palette from the PNG and no materials.
"""
import struct, sys, zlib
from collections import defaultdict

obj, png, out = sys.argv[1], sys.argv[2], sys.argv[3]
V, T, N, F = [], [], [], []
for line in open(obj):
    p = line.split()
    if not p: continue
    if p[0] == 'v': V.append(tuple(float(a) for a in p[1:4]))
    elif p[0] == 'vt': T.append(float(p[1]))
    elif p[0] == 'vn': N.append(tuple(float(a) for a in p[1:4]))
    elif p[0] == 'f':
        F.append([tuple(int(i) if i else 0 for i in q.split('/')) for q in p[1:]])

S = 10.0  # voxels per unit
# OBJ space: x, y up, z. MagicaVoxel: x, y = -objz, z = objy.
def vox(v): return (round(v[0] * S), round(-v[2] * S), round(v[1] * S))
VV = [vox(v) for v in V]
mins = [min(v[i] for v in VV) for i in range(3)]
VV = [tuple(v[i] - mins[i] for i in range(3)) for v in VV]
size = [max(v[i] for v in VV) for i in range(3)]

# Faces with a normal along MagicaVoxel X, as (plane x, yz polygon, sign, colour).
cross = defaultdict(list)  # (y,z) cell -> [(x plane, sign, colour)]
for f in F:
    pts = [VV[i[0] - 1] for i in f]
    colour = int(T[f[0][1] - 1] * 256) + 1 if f[0][1] else 1
    xs = {p[0] for p in pts}
    if len(xs) != 1: continue
    n = N[f[0][2] - 1]
    sign = 1 if n[0] > 0 else -1
    x = pts[0][0]
    ys = [p[1] for p in pts]; zs = [p[2] for p in pts]
    # Triangles: rasterise cell centres inside the triangle.
    (y0, z0), (y1, z1), (y2, z2) = [(p[1], p[2]) for p in pts[:3]]
    def inside(py, pz):
        d0 = (y1 - y0) * (pz - z0) - (z1 - z0) * (py - y0)
        d1 = (y2 - y1) * (pz - z1) - (z2 - z1) * (py - y1)
        d2 = (y0 - y2) * (pz - z2) - (z0 - z2) * (py - y2)
        neg = d0 < 0 or d1 < 0 or d2 < 0; pos = d0 > 0 or d1 > 0 or d2 > 0
        return not (neg and pos)
    for cy in range(min(ys), max(ys)):
        for cz in range(min(zs), max(zs)):
            if inside(cy + 0.5, cz + 0.5):
                cross[(cy, cz)].append((x, sign, colour))

cells = {}
for (cy, cz), hits in cross.items():
    hits = sorted(set((x, s) for x, s, c in hits)); colours = {(x, s): c for x, s, c in cross[(cy, cz)]}; hits = [(x, s, colours[(x, s)]) for x, s in hits]
    depth = 0; start = None; scol = 1
    for x, sign, colour in hits:
        if sign < 0:  # entering
            if depth == 0: start, scol = x, colour
            depth += 1
        else:
            depth -= 1
            if depth == 0 and start is not None:
                for cx in range(start, x):
                    # nearer face's colour; the interior never shows
                    cells[(cx, cy, cz)] = scol if cx - start <= x - 1 - cx else colour
                start = None

# Colours of faces along Y and Z override cells they bound, so tops and sides
# keep their own paint.
for f in F:
    pts = [VV[i[0] - 1] for i in f]
    n = N[f[0][2] - 1]
    colour = int(T[f[0][1] - 1] * 256) + 1 if f[0][1] else 1
    for axis, comp in ((1, -n[2]), (2, n[1])):
        if abs(comp) < 0.5: continue
        if len({p[axis] for p in pts}) != 1: continue
        plane = pts[0][axis]
        cell_off = -1 if comp > 0 else 0
        others = [a for a in range(3) if a != axis]
        a0 = [p[others[0]] for p in pts]; a1 = [p[others[1]] for p in pts]
        for u in range(min(a0), max(a0)):
            for v in range(min(a1), max(a1)):
                c = [0, 0, 0]; c[axis] = plane + cell_off; c[others[0]] = u; c[others[1]] = v
                if tuple(c) in cells: cells[tuple(c)] = colour

# Palette from the PNG strip (256x1 RGBA, no filtering of interest).
raw = open(png, 'rb').read()
i = 8; idat = b''; w = h = 0
while i < len(raw):
    n, t = struct.unpack('>I4s', raw[i:i + 8]); body = raw[i + 8:i + 8 + n]
    if t == b'IHDR': w, h, bd, ct = struct.unpack('>IIBB', body[:10])
    if t == b'IDAT': idat += body
    i += 12 + n
data = zlib.decompress(idat)
bpp = 4 if ct == 6 else 3
row = data[1:1 + w * bpp]
if data[0] != 0:
    # Sub/Up/etc filters for one row: Up and None are the same for row 0.
    f = data[0]; r = bytearray(row)
    for k in range(len(r)):
        a = r[k - bpp] if k >= bpp else 0
        if f == 1 or f == 4: r[k] = (r[k] + a) & 255
        elif f == 3: r[k] = (r[k] + a // 2) & 255
    row = bytes(r)
pal = b''
for k in range(256):
    px = row[k * bpp:k * bpp + bpp] if k < w else b'\0\0\0'
    pal += px[:3] + b'\xff'
pal = pal[:255 * 4] + b'\0\0\0\0'

def i32(*a): return struct.pack('<' + 'i' * len(a), *a)
def chunk(cid, content, children=b''): return cid + i32(len(content), len(children)) + content + children
xyzi = i32(len(cells)) + b''.join(bytes((x, y, z, c)) for (x, y, z), c in cells.items())
body = chunk(b'SIZE', i32(*size)) + chunk(b'XYZI', xyzi) + chunk(b'RGBA', pal)
open(out, 'wb').write(b'VOX ' + i32(150) + chunk(b'MAIN', b'', body))
print('size', size, 'voxels', len(cells))
