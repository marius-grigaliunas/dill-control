"""First passes at the fighter's new attacks, blocked out as key poses in
art/_source/FieldFighter/fighter_moves.blend and exported as MechMoves:

    blender --background art/_source/FieldFighter/fighter_moves.blend --python tools/blender/make_moves.py

- smash: both fists raised over the head, then down together in front;
- uppercut_r: the right fist drawn back low, then driven up;
- backhand_l: the left arm swept across the body and out.

Each pose is (time in seconds, {arm: (pitch, yaw, roll) degrees}) in the
game's terms for an arm: pitch about the model's X lifts the fist backward,
yaw about its up carries it to the left, roll about its forward axis lifts
it out to the side. Keys are smooth (Blender's own easing): edit them in the
.blend and export again from the Dill tab.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy  # noqa: E402
from mathutils import Matrix  # noqa: E402
from dill_mech import export  # noqa: E402

FPS = 60

MOVES = {
    'smash': ([
        (0.0, {'ArmR': (0, 0, 0), 'ArmL': (0, 0, 0)}),
        (0.35, {'ArmR': (-155, 0, -12), 'ArmL': (-155, 0, 12)}),
        (0.5, {'ArmR': (-165, 0, -8), 'ArmL': (-165, 0, 8)}),
        (0.64, {'ArmR': (-30, 0, 4), 'ArmL': (-30, 0, -4)}),
        (0.8, {'ArmR': (-35, 0, 4), 'ArmL': (-35, 0, -4)}),
        (1.15, {'ArmR': (0, 0, 0), 'ArmL': (0, 0, 0)}),
    ], {'hit': 0.62, 'shake': 0.62}),
    'uppercut_r': ([
        (0.0, {'ArmR': (0, 0, 0)}),
        (0.25, {'ArmR': (45, 15, 0)}),
        (0.42, {'ArmR': (-135, -20, 0)}),
        (0.55, {'ArmR': (-140, -20, 0)}),
        (0.85, {'ArmR': (0, 0, 0)}),
    ], {'hit': 0.4}),
    'backhand_l': ([
        (0.0, {'ArmL': (0, 0, 0)}),
        (0.25, {'ArmL': (-80, -70, 0)}),
        (0.42, {'ArmL': (-80, 65, 0)}),
        (0.5, {'ArmL': (-75, 70, 0)}),
        (0.85, {'ArmL': (0, 0, 0)}),
    ], {'hit': 0.36}),
}


def arm_matrix(rest, pitch, yaw, roll):
    """The game's Basis(Right, pitch) * Basis(Up, yaw) * Basis(Back, roll) in
    Blender's axes: its up is Blender's Z, its back (+Z) Blender's -Y."""
    p, y, r = (math.radians(a) for a in (pitch, yaw, roll))
    loc = Matrix.Translation(rest.to_translation())
    rot = Matrix.Rotation(p, 4, 'X') @ Matrix.Rotation(y, 4, 'Z') @ Matrix.Rotation(-r, 4, 'Y')
    return loc @ rot @ rest.to_3x3().to_4x4()


scene = bpy.context.scene
scene.render.fps = FPS
scene.render.fps_base = 1.0
rig = next(o for o in bpy.data.objects if o.get('dill_mech'))
parts = {o['dill_part']: o for o in export.part_objects(rig)}
rig.animation_data_create()

for name, (poses, marks) in MOVES.items():
    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig.animation_data.action = action
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    inv = rig.matrix_world.inverted()
    offsets = {}
    rests = {}
    for arm in {a for _, pose in poses for a in pose}:
        obj_rest = inv @ parts[arm].matrix_world
        rests[arm] = obj_rest
        offsets[arm] = rig.pose.bones[arm].matrix.inverted() @ obj_rest
    for t, pose in poses:
        frame = int(round(t * FPS))
        for arm, (p, y, r) in pose.items():
            pb = rig.pose.bones[arm]
            pb.matrix = arm_matrix(rests[arm], p, y, r) @ offsets[arm].inverted()
            bpy.context.view_layer.update()
            pb.keyframe_insert('rotation_quaternion', frame=frame)
            pb.keyframe_insert('location', frame=frame)
    for mark, t in marks.items():
        m = action.pose_markers.new(mark)
        m.frame = int(round(t * FPS))
    last = int(round(poses[-1][0] * FPS))
    path, claimed = export.export_action(rig, action, 0, last)
    print(f'[moves] {name}: {path} claims {claimed}')

rig.animation_data.action = None
bpy.ops.wm.save_mainfile()
print('[moves] saved', bpy.data.filepath)
