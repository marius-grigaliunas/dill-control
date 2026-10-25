"""A mech's .vox made into a rig in Blender: one mesh per part, each fixed
whole to a bone of its own (rigid, never skinned), the bones chained the way
the game's damage map chains the parts, IK handles on the feet, markers as
empties. Re-importing a changed model swaps the meshes and keeps the bones,
the IK and every action.
"""
import math
import os

import bpy
from mathutils import Matrix, Vector

from . import vox

FACES = (
    ((1, 0, 0), ((1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1))),
    ((-1, 0, 0), ((0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0))),
    ((0, 1, 0), ((0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0))),
    ((0, -1, 0), ((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1))),
    ((0, 0, 1), ((0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1))),
    ((0, 0, -1), ((0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0))),
)


def project_root(path):
    """The Godot project a file is in: the folder above it with project.godot."""
    d = os.path.dirname(os.path.abspath(path))
    while True:
        if os.path.isfile(os.path.join(d, 'project.godot')):
            return d
        up = os.path.dirname(d)
        if up == d:
            return ''
        d = up


def _frame(rot, loc):
    m = Matrix.Identity(4)
    for r in range(3):
        for c in range(3):
            m[r][c] = rot[r][c]
        m[r][3] = loc[r]
    return m


def build_mesh(name, part, model):
    """The part's voxels as faces where they meet air, coloured from the
    palette, round the part's origin."""
    vs = model.voxel_size
    solid = {(x, y, z) for x, y, z, _ in part.voxels}
    verts, faces, colours = [], [], []
    ox, oy, oz = part.origin
    for x, y, z, ci in part.voxels:
        rgba = model.palette[ci]
        colour = (rgba[0] / 255.0, rgba[1] / 255.0, rgba[2] / 255.0, 1.0)
        for normal, corners in FACES:
            if (x + normal[0], y + normal[1], z + normal[2]) in solid:
                continue
            start = len(verts)
            for cx, cy, cz in corners:
                verts.append(((x + cx - ox) * vs, (y + cy - oy) * vs, (z + cz - oz) * vs))
            faces.append((start, start + 1, start + 2, start + 3))
            colours.append(colour)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    attr = mesh.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
    for poly, colour in zip(mesh.polygons, colours):
        for li in poly.loop_indices:
            attr.data[li].color = colour
    mesh.update()
    mat = bpy.data.materials.get('DillVoxel')
    if mat is None:
        mat = bpy.data.materials.new('DillVoxel')
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        bsdf = nodes.get('Principled BSDF')
        col = nodes.new('ShaderNodeVertexColor')
        col.layer_name = 'Col'
        if bsdf is not None:
            mat.node_tree.links.new(col.outputs['Color'], bsdf.inputs['Base Color'])
    mesh.materials.append(mat)
    return mesh


def parents(model, shapes):
    """Each part's parent, as the game's damage map finds it: the part with
    a voxel nearest its pivot. A part with no pivot is a root (the core)."""
    vs = model.voxel_size
    cells = {}
    for part, rot, loc in shapes:
        if part.kind != 'shape':
            continue
        keys = []
        for x, y, z, _ in part.voxels:
            local = (x + 0.5 - part.origin[0], y + 0.5 - part.origin[1], z + 0.5 - part.origin[2])
            at = vox._apply(rot, local)
            keys.append((at[0] + loc[0] / vs, at[1] + loc[1] / vs, at[2] + loc[2] / vs))
        cells[part.name] = keys
    found = {}
    for part, rot, loc in shapes:
        if part.kind != 'shape' or not part.pivoted:
            continue
        pivot = (loc[0] / vs, loc[1] / vs, loc[2] / vs)
        best, best_d = None, float('inf')
        for other, keys in cells.items():
            if other == part.name:
                continue
            for k in keys:
                d = (k[0] - pivot[0]) ** 2 + (k[1] - pivot[1]) ** 2 + (k[2] - pivot[2]) ** 2
                if d < best_d:
                    best, best_d = other, d
        found[part.name] = best
    return found


def import_mech(path, collection_name=None):
    """Imports the mech at `path` (a .vox in the project), or refreshes its
    meshes if it was imported before. Returns the rig object."""
    name = collection_name or os.path.splitext(os.path.basename(path))[0]
    model = vox.load(path, vox.voxel_size_of(path))
    shapes = model.shapes()
    rig_name = f'Rig.{name}'
    rig = bpy.data.objects.get(rig_name)
    if rig is not None:
        return _refresh(rig, model, shapes)

    coll = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(coll)
    arm = bpy.data.armatures.new(rig_name)
    rig = bpy.data.objects.new(rig_name, arm)
    coll.objects.link(rig)
    rig['dill_mech'] = name
    rig['dill_vox'] = os.path.abspath(path)
    rig['dill_project'] = project_root(path)
    rig['dill_voxel_size'] = model.voxel_size

    parent_of = parents(model, shapes)
    frames = {p.name: _frame(rot, loc) for p, rot, loc in shapes}
    children = {}
    for child, parent in parent_of.items():
        if parent:
            children.setdefault(parent, []).append(child)

    view = bpy.context.view_layer
    view.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    bones = {}
    for part, rot, loc in shapes:
        if part.kind != 'shape':
            continue
        b = arm.edit_bones.new(part.name)
        head = Vector(loc)
        kids = children.get(part.name, [])
        if part.pivoted and len(kids) == 1:
            tail = Vector(frames[kids[0]].translation)
        elif part.pivoted and part.voxels:
            # Its far end: the voxel farthest from its pivot.
            far = max(part.voxels, key=lambda c: (c[0] + 0.5 - part.origin[0]) ** 2 + (c[1] + 0.5 - part.origin[1]) ** 2 + (c[2] + 0.5 - part.origin[2]) ** 2)
            local = Vector(((far[0] + 0.5 - part.origin[0]) * model.voxel_size,
                            (far[1] + 0.5 - part.origin[1]) * model.voxel_size,
                            (far[2] + 0.5 - part.origin[2]) * model.voxel_size))
            tail = head + Matrix([r for r in rot]).to_4x4() @ local
        else:
            tail = head + Vector((0, 0, 0.6))
        if (tail - head).length < 0.05:
            tail = head + Vector((0, 0, 0.3))
        b.head, b.tail = head, tail
        bones[part.name] = b
    for child, parent in parent_of.items():
        if parent and child in bones and parent in bones:
            bones[child].parent = bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')

    for part, rot, loc in shapes:
        world = frames[part.name]
        if part.kind == 'marker':
            empty = bpy.data.objects.new('@' + part.name, None)
            empty.empty_display_type = 'SPHERE'
            empty.empty_display_size = 0.15
            coll.objects.link(empty)
            empty.parent = rig
            empty.matrix_world = world
            continue
        mesh = build_mesh(part.name, part, model)
        obj = bpy.data.objects.new(part.name, mesh)
        coll.objects.link(obj)
        obj['dill_part'] = part.name
        obj.parent = rig
        obj.parent_type = 'BONE'
        obj.parent_bone = part.name
        obj.matrix_world = world

    _add_ik(rig, parent_of, frames, coll)
    return rig


def _add_ik(rig, parent_of, frames, coll):
    """An IK handle at each foot: an empty the shin and thigh bend to reach."""
    for foot, shin in parent_of.items():
        if not shin or not foot.startswith('Foot') or not parent_of.get(shin):
            continue
        handle = bpy.data.objects.new(f'IK.{foot}', None)
        handle.empty_display_type = 'CUBE'
        handle.empty_display_size = 0.25
        coll.objects.link(handle)
        handle.parent = rig
        handle.matrix_world = frames[foot]
        c = rig.pose.bones[shin].constraints.new('IK')
        c.target = handle
        c.chain_count = 2
        # The foot keeps its own turn, from the handle.
        cr = rig.pose.bones[foot].constraints.new('COPY_ROTATION')
        cr.target = handle


def _refresh(rig, model, shapes):
    """The model changed: new meshes for the parts, bones and actions kept."""
    objects = {o['dill_part']: o for o in rig.children_recursive if o.get('dill_part')}
    for part, rot, loc in shapes:
        if part.kind != 'shape':
            continue
        obj = objects.get(part.name)
        if obj is None:
            continue
        old = obj.data
        obj.data = build_mesh(part.name, part, model)
        if old.users == 0:
            bpy.data.meshes.remove(old)
    return rig
