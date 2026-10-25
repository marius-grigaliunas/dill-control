"""Move the voxels in a box out of one object of a .vox into a new object.

usage: split_part.py <in.vox> <out.vox> <from> <new> <x0> <x1> <y0> <y1> <z0> <z1> <above|below>

The box is inclusive, in the <from> object's own grid. The new object gets a
pivot voxel (index 255) one voxel above the centre of its top layer, or below
the centre of its bottom layer: a head sits on its neck, a leg hangs from its
hip. It stands exactly where its voxels stood, and every other chunk (the
palette, materials, layers, the other objects and their transforms) is copied
through unchanged, so the rest of the model does not move.
"""
import struct, sys

src, dst, source_name, new_name = sys.argv[1:5]
x0, x1, y0, y1, z0, z1 = map(int, sys.argv[5:11])
side = sys.argv[11]
PIVOT = 255

d = open(src, 'rb').read()
assert d[:4] == b'VOX '


def i32(*a): return struct.pack('<' + 'i' * len(a), *a)
def s(x): x = x.encode(); return i32(len(x)) + x
def dct(dd): return i32(len(dd)) + b''.join(s(k) + s(v) for k, v in dd.items())
def chunk(cid, content, children=b''): return cid + i32(len(content), len(children)) + content + children


class Reader:
    def __init__(self, b): self.b, self.o = b, 0
    def i(self): v = struct.unpack_from('<i', self.b, self.o)[0]; self.o += 4; return v
    def s(self): n = self.i(); v = self.b[self.o:self.o + n].decode(); self.o += n; return v
    def d(self): return {self.s(): self.s() for _ in range(self.i())}


# MAIN's children, in order, as (id, content).
main_children = 12 + struct.unpack_from('<i', d, 12)[0]
i = 8 + main_children
chunks = []
while i < len(d):
    cid = d[i:i + 4]
    n, m = struct.unpack_from('<ii', d, i + 4)
    chunks.append([cid, d[i + 12:i + 12 + n]])
    i += 12 + n + m

model_chunks = [k for k, c in enumerate(chunks) if c[0] == b'XYZI']
transforms, shapes, groups = {}, {}, {}
for k, (cid, b) in enumerate(chunks):
    r = Reader(b)
    if cid == b'nTRN':
        nid = r.i(); attrs = r.d(); child = r.i(); r.i(); layer = r.i(); frames = [r.d() for _ in range(r.i())]
        transforms[nid] = dict(attrs=attrs, child=child, layer=layer, frames=frames)
    elif cid == b'nSHP':
        nid = r.i(); r.d(); count = r.i(); model = r.i()
        shapes[nid] = model
    elif cid == b'nGRP':
        nid = r.i(); r.d(); groups[nid] = (k, [r.i() for _ in range(r.i())])

source = [(nid, t) for nid, t in transforms.items() if t['attrs'].get('_name') == source_name]
if len(source) != 1:
    raise SystemExit(f'expected one object named {source_name}, found {len(source)}')
if any(t['attrs'].get('_name') == new_name for t in transforms.values()):
    raise SystemExit(f'{new_name} already exists')
source_id, source_trn = source[0]
if '_r' in source_trn['frames'][0]:
    raise SystemExit(f'{source_name} is rotated; split it by hand')
model_index = shapes[source_trn['child']]
parent_group = next(g for g, (_, kids) in groups.items() if source_id in kids)

size_chunk = chunks[model_chunks[model_index] - 1]
size = struct.unpack('<iii', size_chunk[1])
xyzi = chunks[model_chunks[model_index]][1]
count = struct.unpack_from('<i', xyzi, 0)[0]
voxels = [tuple(xyzi[4 + 4 * k:8 + 4 * k]) for k in range(count)]
inside = lambda v: x0 <= v[0] <= x1 and y0 <= v[1] <= y1 and z0 <= v[2] <= z1
moved = [v for v in voxels if inside(v)]
kept = [v for v in voxels if not inside(v)]
if not moved:
    raise SystemExit('no voxels in that box')
if any(v[3] == PIVOT for v in moved):
    raise SystemExit(f'the box holds {source_name}\'s pivot voxel')

# The pivot, as split_mech.py places it, but at whichever end was asked for.
end = max(v[2] for v in moved) if side == 'above' else min(v[2] for v in moved)
layer = [v for v in moved if v[2] == end]
px = round(sum(v[0] for v in layer) / len(layer) - 0.5)
py = round(sum(v[1] for v in layer) / len(layer) - 0.5)
pz = end + 1 if side == 'above' else end - 1
if pz < 0 or pz > 255:
    raise SystemExit('the pivot falls outside the grid')
everything = moved + [(px, py, pz, PIVOT)]

lo = tuple(min(v[a] for v in everything) for a in range(3))
new_size = tuple(max(v[a] for v in everything) - lo[a] + 1 for a in range(3))
local = [(v[0] - lo[0], v[1] - lo[1], v[2] - lo[2], v[3]) for v in everything]
# MagicaVoxel puts floor(size / 2) of a model at its translation.
t = [int(c) for c in source_trn['frames'][0].get('_t', '0 0 0').split()]
new_t = [t[a] + lo[a] + new_size[a] // 2 - size[a] // 2 for a in range(3)]

chunks[model_chunks[model_index]][1] = i32(len(kept)) + b''.join(bytes(v) for v in kept)
last = model_chunks[-1]
new_model = len(model_chunks)
chunks[last + 1:last + 1] = [
    [b'SIZE', i32(*new_size)],
    [b'XYZI', i32(len(local)) + b''.join(bytes(v) for v in local)],
]

next_id = max(list(transforms) + list(shapes) + list(groups)) + 1
trn_id, shp_id = next_id, next_id + 1
group_chunk, kids = groups[parent_group]
if group_chunk > last:
    group_chunk += 2  # the two chunks inserted above
r = Reader(chunks[group_chunk][1]); r.i(); attrs = r.d()
chunks[group_chunk][1] = i32(parent_group) + dct(attrs) + i32(len(kids) + 1) + i32(*kids, trn_id)
chunks.append([b'nTRN', i32(trn_id) + dct({'_name': new_name}) + i32(shp_id, -1, source_trn['layer'], 1)
               + dct({'_t': f'{new_t[0]} {new_t[1]} {new_t[2]}'})])
chunks.append([b'nSHP', i32(shp_id) + dct({}) + i32(1) + i32(new_model) + dct({})])

out = b''.join(chunk(cid, b) for cid, b in chunks)
open(dst, 'wb').write(d[:8] + chunk(b'MAIN', b'', out))
print(f'{new_name}: {len(moved)} voxels from {source_name} ({len(kept)} left), pivot ({px}, {py}, {pz}) in {source_name}\'s grid, at {new_t}')
