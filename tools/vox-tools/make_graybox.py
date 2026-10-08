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
