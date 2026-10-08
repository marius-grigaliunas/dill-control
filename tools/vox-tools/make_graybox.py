"""Writes the graybox compound's pieces as .vox, so they break like every other
voxel model. Run from the project folder: python tools/vox-tools/make_graybox.py

Same sizes as the BoxMesh and CylinderMesh placeholders they replace, at 0.5 m
a voxel (the importer's voxel/size for these files): a 1 m checker in two
greys, with a light band every 4 m in place of the grid shader's major lines.
"""
import struct

VOXEL = 0.5
BASE, ALT, MAJOR = 1, 2, 3
PALETTE = {BASE: (0x69, 0x6b, 0x6e), ALT: (0x5d, 0x5f, 0x62), MAJOR: (0x8a, 0x8c, 0x8f)}

# Towers: five colours a scheme (slab, spandrel, column, glass, core), from 8 up.
SLAB, SPANDREL, COLUMN, GLASS, CORE = range(5)
SCHEMES = [
    [(0xb8, 0xb4, 0xaa), (0x8e, 0x8a, 0x82), (0xa6, 0xa2, 0x98), (0x3a, 0x4c, 0x5e), (0x7a, 0x76, 0x70)],  # concrete
    [(0xc9, 0xcd, 0xd2), (0x5a, 0x6e, 0x84), (0x9a, 0xa4, 0xae), (0x2c, 0x48, 0x66), (0x80, 0x84, 0x8a)],  # blue glass
    [(0xd6, 0xc4, 0xa0), (0xa8, 0x8e, 0x6a), (0xc0, 0xaa, 0x84), (0x34, 0x3e, 0x48), (0x96, 0x86, 0x6c)],  # sandstone
]
for s, scheme in enumerate(SCHEMES):
    for k, rgb in enumerate(scheme):
        PALETTE[8 + s * 8 + k] = rgb


def colour(x, y, z):
    if z % 8 == 0 or (x % 8 == 0 and y % 8 == 0):
        return MAJOR
    return BASE if ((x // 2) + (y // 2) + (z // 2)) % 2 == 0 else ALT


def box(sx, sy, sz):
    return [(x, y, z, colour(x, y, z)) for z in range(sz) for y in range(sy) for x in range(sx)]


def cylinder(diameter, height):
    r = diameter / 2.0
    out = []
    for z in range(height):
        for y in range(diameter):
            for x in range(diameter):
                if (x + 0.5 - r) ** 2 + (y + 0.5 - r) ** 2 <= r * r:
                    out.append((x, y, z, colour(x, y, z)))
    return out


def tower(w, d, storeys, scheme, setbacks=(), lobby=12, storey=8, antenna=0):
    """A hollow skyscraper, built the way the load check reads one: an open
    lobby of 1 m columns round the edge and a 4 m core in the middle, then a
    slab every storey carrying a facade ring (columns every 4 m, a spandrel
    band, glass between) and the core. Bring the lobby's columns down and the
    rest has nothing to stand on.

    setbacks: (storey, inset) pairs; from that storey up the footprint is inset
    by that many voxels each side, and the slab below it is a terrace.
    """
    c = lambda k: 8 + scheme * 8 + k
    out = []

    def inset(s):
        i = 0
        for at, by in setbacks:
            if s >= at:
                i = by
        return i

    cx0, cy0 = w // 2 - 4, d // 2 - 4

    def core(z):
        for y in range(cy0, cy0 + 8):
            for x in range(cx0, cx0 + 8):
                if x in (cx0, cx0 + 7) or y in (cy0, cy0 + 7):
                    out.append((x, y, z, c(CORE)))

    def column_at(t, lo, hi):
        # Along a side from lo to hi (exclusive): 2-voxel columns every 8, and at the far end.
        return (t - lo) % 8 < 2 or t >= hi - 2

    def slab(z, i):
        for y in range(i, d - i):
            for x in range(i, w - i):
                out.append((x, y, z, c(SLAB)))

    slab(0, 0)
    for z in range(1, lobby):
        for y in range(d):
            for x in range(w):
                edge_x, edge_y = x in (0, 1, w - 2, w - 1), y in (0, 1, d - 2, d - 1)
                if (edge_y and column_at(x, 0, w)) or (edge_x and column_at(y, 0, d)):
                    out.append((x, y, z, c(COLUMN)))
        core(z)

    for s in range(storeys):
        z0 = lobby + s * storey
        i = inset(s)
        slab(z0, min(i, inset(s - 1)) if s > 0 else 0)
        for z in range(z0 + 1, z0 + storey):
            for y in range(i, d - i):
                for x in range(i, w - i):
                    on_x, on_y = x in (i, w - i - 1), y in (i, d - i - 1)
                    if not (on_x or on_y):
                        continue
                    if (on_y and column_at(x, i, w - i)) or (on_x and column_at(y, i, d - i)):
                        k = COLUMN
                    else:
                        k = SPANDREL if z == z0 + 1 else GLASS
                    out.append((x, y, z, c(k)))
            core(z)

    top = lobby + storeys * storey
    i = inset(storeys - 1)
    slab(top, i)
    for z in (top + 1, top + 2):
        for y in range(i, d - i):
            for x in range(i, w - i):
                if x in (i, w - i - 1) or y in (i, d - i - 1):
                    out.append((x, y, z, c(SLAB)))
    for z in range(top + 1, top + 1 + antenna):
        for y in (d // 2 - 1, d // 2):
            for x in (w // 2 - 1, w // 2):
                out.append((x, y, z, c(COLUMN)))
    height = top + 3 + max(0, antenna - 2)
    return (w, d, height), out


def i32(*a):
    return struct.pack('<' + 'i' * len(a), *a)


def s(x):
    x = x.encode()
    return i32(len(x)) + x


def dct(d):
    return i32(len(d)) + b''.join(s(k) + s(v) for k, v in d.items())


def chunk(cid, content, children=b''):
    return cid + i32(len(content), len(children)) + content + children


def write(path, size, voxels):
    body = chunk(b'SIZE', i32(*size))
    body += chunk(b'XYZI', i32(len(voxels)) + b''.join(bytes(v) for v in voxels))
    # Stood on MagicaVoxel's ground: the bottom layer is the game's ground.
    body += chunk(b'nTRN', i32(0) + dct({}) + i32(1, -1, -1, 1) + dct({}))
    body += chunk(b'nGRP', i32(1) + dct({}) + i32(1) + i32(2))
    body += chunk(b'nTRN', i32(2) + dct({'_name': 'Body'}) + i32(3, -1, 0, 1) + dct({'_t': f'0 0 {size[2] // 2}'}))
    body += chunk(b'nSHP', i32(3) + dct({}) + i32(1) + i32(0) + dct({}))
    rgba = bytearray(256 * 4)
    for index, (r, g, b) in PALETTE.items():
        rgba[(index - 1) * 4:(index - 1) * 4 + 4] = bytes((r, g, b, 255))
    rgba[254 * 4:255 * 4] = bytes((255, 0, 255, 255))
    body += chunk(b'RGBA', bytes(rgba))
    open(path, 'wb').write(b'VOX ' + i32(150) + chunk(b'MAIN', b'', body))
    print(path, size, len(voxels), 'voxels')


m = lambda metres: int(round(metres / VOXEL))
write('art/voxels/prp_graybox_pillar.vox', (m(4), m(4), m(36)), box(m(4), m(4), m(36)))
write('art/voxels/prp_graybox_column.vox', (m(5), m(5), m(26)), cylinder(m(5), m(26)))
write('art/voxels/prp_graybox_block.vox', (m(10), m(10), m(6)), box(m(10), m(10), m(6)))
# Skyscrapers to swing between: 14 m at 95 m, a 16 x 28 m slab at 60 m, and a
# 16 m tower at 110 m stepping in twice, with a mast. Each is one section wide
# (32 voxels) so it draws in as few pieces as its height allows.
write('art/voxels/prp_graybox_tower_a.vox', *tower(28, 28, 22, scheme=0))
write('art/voxels/prp_graybox_tower_b.vox', *tower(32, 56, 13, scheme=1))
write('art/voxels/prp_graybox_tower_c.vox', *tower(32, 32, 26, scheme=2, setbacks=((12, 4), (20, 8)), antenna=24))
