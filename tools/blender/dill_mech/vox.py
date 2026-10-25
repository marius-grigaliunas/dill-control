"""A MagicaVoxel .vox read the way the game's importer reads it.

Mirrors scripts/voxel/VoxModelBuilder.cs, so a part stands in Blender
exactly where it stands in Godot:

- a group is a frame of its own; an object is a part, named after it;
- an object whose name starts with '@' is a marker at the centre of its voxels;
- a voxel in the pivot index (255) is not material: its centre is the part's
  origin, else the origin is the centre MagicaVoxel places, floor(size / 2);
- the part sits at translation + rotation * (origin - floor(size / 2));
- hidden objects and hidden layers are left out.

MagicaVoxel's axes are Blender's (Z up, the front looking along -Y), so
nothing is turned here; one voxel is `voxel_size` metres. Pure Python: no
bpy, so it is tested outside Blender too.
"""
import struct

PIVOT = 255


class Rotation:
    """A MagicaVoxel rotation byte as a 3x3 matrix of +1, -1 and 0, row-major."""

    def __init__(self, byte=4):
        c0, c1 = byte & 3, (byte >> 2) & 3
        c2 = 3 - c0 - c1
        s = [(-1 if byte & bit else 1) for bit in (16, 32, 64)]
        m = [[0, 0, 0], [0, 0, 0], [0, 0, 0]]
        m[0][c0], m[1][c1], m[2][c2] = s[0], s[1], s[2]
        self.m = m

    def apply(self, v):
        m = self.m
        return tuple(m[r][0] * v[0] + m[r][1] * v[1] + m[r][2] * v[2] for r in range(3))


IDENTITY = Rotation(4)  # rows pick columns 0, 1 and 2: the identity


class Part:
    def __init__(self, name, kind):
        self.name = name
        self.kind = kind          # 'shape', 'marker' or 'group'
        self.rotation = IDENTITY  # within its parent
        self.location = (0.0, 0.0, 0.0)  # metres, within its parent
        self.children = []
        self.size = (0, 0, 0)
        self.voxels = []          # (x, y, z, colour index), pivot left out
        self.origin = (0.0, 0.0, 0.0)  # voxel space point that is the node's origin
        self.pivoted = False


class Model:
    def __init__(self):
        self.root = Part('', 'group')
        self.palette = [(0, 0, 0, 0)] * 256
        self.voxel_size = 0.1

    def shapes(self):
        """Every shape and marker with its frame in the model's space:
        (part, rotation 3x3 rows, location in metres)."""
        found = []

        def walk(part, rot, loc):
            for child in part.children:
                r = _mul(rot, child.rotation.m)
                l = _add(loc, _apply(rot, child.location))
                if child.kind == 'group':
                    walk(child, r, l)
                else:
                    found.append((child, r, l))

        walk(self.root, IDENTITY.m, (0.0, 0.0, 0.0))
        return found


def _mul(a, b):
    return [[sum(a[r][k] * b[k][c] for k in range(3)) for c in range(3)] for r in range(3)]


def _apply(m, v):
    return tuple(m[r][0] * v[0] + m[r][1] * v[1] + m[r][2] * v[2] for r in range(3))


def _add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _centre(size):
    return (size[0] // 2, size[1] // 2, size[2] // 2)


class _Reader:
    def __init__(self, b):
        self.b, self.o = b, 0

    def i(self):
        v = struct.unpack_from('<i', self.b, self.o)[0]
        self.o += 4
        return v

    def s(self):
        n = self.i()
        v = self.b[self.o:self.o + n].decode('utf-8', 'replace')
        self.o += n
        return v

    def d(self):
        return {self.s(): self.s() for _ in range(self.i())}


def load(path, voxel_size=0.1):
    data = open(path, 'rb').read()
    if data[:4] != b'VOX ':
        raise ValueError(f'{path} is not a .vox')
    i = 8 + 12
    sizes, models = [], []
    transforms, groups, shapes, hidden_layers = {}, {}, {}, set()
    palette = None
    while i + 12 <= len(data):
        cid = data[i:i + 4]
        n, m = struct.unpack_from('<ii', data, i + 4)
        b = data[i + 12:i + 12 + n]
        r = _Reader(b)
        if cid == b'SIZE':
            sizes.append(struct.unpack('<iii', b))
        elif cid == b'XYZI':
            count = r.i()
            models.append([tuple(b[4 + 4 * k:8 + 4 * k]) for k in range(count)])
        elif cid == b'RGBA':
            palette = [tuple(b[4 * k:4 * k + 4]) for k in range(256)]
        elif cid == b'nTRN':
            nid = r.i(); attrs = r.d(); child = r.i(); r.i(); layer = r.i()
            frames = [r.d() for _ in range(r.i())]
            transforms[nid] = (attrs, child, layer, frames[0] if frames else {})
        elif cid == b'nGRP':
            nid = r.i(); r.d()
            groups[nid] = [r.i() for _ in range(r.i())]
        elif cid == b'nSHP':
            nid = r.i(); r.d(); r.i()
            shapes[nid] = r.i()
        elif cid == b'LAYR':
            lid = r.i(); attrs = r.d()
            if attrs.get('_hidden') == '1':
                hidden_layers.add(lid)
        i += 12 + n + m

    model = Model()
    model.voxel_size = voxel_size
    if palette:
        # MagicaVoxel's palette is shifted by one: index k is entry k - 1.
        model.palette = [(0, 0, 0, 0)] + palette[:255]
    unnamed = [0]

    def add_shape(model_index, trn, name, parent):
        if model_index >= len(models):
            return
        size, cells = sizes[model_index], models[model_index]
        marker = name.startswith('@')
        if marker:
            pts = [c for c in cells]
            n = max(1, len(pts))
            origin = (sum(c[0] + 0.5 for c in pts) / n, sum(c[1] + 0.5 for c in pts) / n, sum(c[2] + 0.5 for c in pts) / n)
            pivoted = False
        else:
            pivots = [c for c in cells if c[3] == PIVOT]
            pivoted = bool(pivots)
            origin = (pivots[0][0] + 0.5, pivots[0][1] + 0.5, pivots[0][2] + 0.5) if pivots else _centre(size)
        attrs, frame = (trn[0], trn[3]) if trn else ({}, {})
        rot = Rotation(int(frame['_r'])) if '_r' in frame else IDENTITY
        t = tuple(int(v) for v in frame.get('_t', '0 0 0').split())
        offset = tuple(origin[k] - _centre(size)[k] for k in range(3))
        placed = _add(t, rot.apply(offset))
        part = Part(name[1:] if marker else name, 'marker' if marker else 'shape')
        part.rotation = rot
        part.location = tuple(v * voxel_size for v in placed)
        part.size = size
        part.origin = origin
        part.pivoted = pivoted
        part.voxels = [] if marker else [c for c in cells if c[3] != PIVOT]
        parent.children.append(part)

    def walk(nid, parent, is_root=False):
        if nid not in transforms:
            return
        attrs, child, layer, frame = transforms[nid]
        if attrs.get('_hidden') == '1' or layer in hidden_layers:
            return
        if child in groups:
            target = parent
            if not is_root:
                target = Part(attrs.get('_name') or f'Group{nid}', 'group')
                target.rotation = Rotation(int(frame['_r'])) if '_r' in frame else IDENTITY
                t = tuple(int(v) for v in frame.get('_t', '0 0 0').split())
                target.location = tuple(v * voxel_size for v in t)
                parent.children.append(target)
            for c in groups[child]:
                walk(c, target)
        elif child in shapes:
            name = attrs.get('_name') or f'Model{unnamed[0]}'
            if not attrs.get('_name'):
                unnamed[0] += 1
            add_shape(shapes[child], transforms[nid], name, parent)

    if transforms:
        walk(0, model.root, is_root=True)
    else:
        for k in range(len(models)):
            add_shape(k, None, f'Model{k}', model.root)
    return model


def voxel_size_of(vox_path, fallback=0.1):
    """The import option the game uses for this file (voxel/size in its .import)."""
    try:
        for line in open(vox_path + '.import', encoding='utf-8'):
            if line.startswith('voxel/size='):
                return float(line.split('=', 1)[1])
    except OSError:
        pass
    return fallback
