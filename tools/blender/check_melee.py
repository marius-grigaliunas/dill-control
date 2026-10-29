"""Checks the melee actions in fighter_moves.blend on what Blender evaluates,
frame by frame (not on what make_melee.py meant to do):

    blender --background art/_source/FieldFighter/fighter_moves.blend --python tools/blender/check_melee.py

Per action: whether the first and last frames are the standing pose (a move
blends out to the walker, so it must end where it began), the lowest any foot
or fist goes (a part through the ground), the widest any joint comes apart
(a leg that does not meet its shin), and the deepest an arm sinks into the
core. Prints FAIL lines and exits 1 if one is over its limit.
"""
import sys

import bpy
from mathutils import Vector

ACTIONS = {'swing_l': ('ArmL',), 'swing_r': ('ArmR',), 'leap_slam': None}  # None: every part
LIMITS = {'rest': 0.01, 'ground': -0.03, 'gap': 0.01, 'sink': 0.25}

rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
scene = bpy.context.scene
parts = {o['dill_part']: o for o in rig.children_recursive if o.get('dill_part')}
fails = 0


def corners(name):
    o = parts[name]
    return [o.matrix_world @ Vector(c) for c in o.bound_box]


def stand():
    """The standing pose with the feet's IK off: the pose the game's parts
    have at rest. (With it on, the foot's Copy Rotation lays each foot on its
    side in this rig; a move that leaves the legs alone never sees it.)"""
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
        for c in pb.constraints:
            c.influence = 0.0
    bpy.context.view_layer.update()
    return {n: [c.copy() for c in corners(n)] for n in parts}


def apart(a, b):
    return max((p - q).length for p, q in zip(a, b))


rest = stand()
box = rest['Body']
lo = Vector((min(p.x for p in box), min(p.y for p in box), min(p.z for p in box)))
hi = Vector((max(p.x for p in box), max(p.y for p in box), max(p.z for p in box)))

for name, claimed in ACTIONS.items():
    action = bpy.data.actions.get(name)
    if action is None:
        print(f'FAIL {name}: no such action')
        fails += 1
        continue
    rig.animation_data.action = action
    first, last = (int(round(x)) for x in action.frame_range)
    worst_rest = 0.0
    low = {'FootL': 9.0, 'FootR': 9.0, 'ArmL': 9.0, 'ArmR': 9.0}
    gap = 0.0
    sink = 0.0
    for f in range(first, last + 1):
        scene.frame_set(f)
        if f in (first, last):
            worst_rest = max(worst_rest, max(apart(corners(n), rest[n]) for n in (claimed or parts)))
        for n in low:
            low[n] = min(low[n], min(c.z for c in corners(n)))
        pb = rig.pose.bones
        for s in 'LR':
            gap = max(gap, (pb['Thigh' + s].tail - pb['Shin' + s].head).length, (pb['Shin' + s].tail - pb['Foot' + s].head).length)
        for arm in ('ArmL', 'ArmR'):
            if name.startswith('swing') and arm[-1].lower() != name[-1]:
                continue
            m = pb[arm].matrix
            for t in (1.0, 1.4, 1.8, 2.2, 2.6, 3.0, 3.4):
                q = m @ Vector((0, t, 0))
                inside = min(q.x - lo.x, hi.x - q.x, q.y - lo.y, hi.y - q.y, q.z - lo.z, hi.z - q.z)
                if name.startswith('swing'):
                    sink = max(sink, inside)
    ground = min(low[n] for n in ('FootL', 'FootR', 'ArmL', 'ArmR')) if name == 'leap_slam' else 0.0
    print(f'[melee-check] {name}: frames {first}-{last}, ends off standing by {worst_rest:.3f} m, '
          f'lowest foot/fist {ground:.3f} m, joints apart {gap:.4f} m, arm in core {sink:.2f} m')
    for label, value, ok in (('ends off standing', worst_rest, worst_rest <= LIMITS['rest']),
                             ('through the ground', ground, ground >= LIMITS['ground']),
                             ('joint apart', gap, gap <= LIMITS['gap']),
                             ('arm in the core', sink, sink <= LIMITS['sink'])):
        if not ok:
            print(f'FAIL {name}: {label} ({value:.3f})')
            fails += 1

stand()
sys.exit(1 if fails else 0)
