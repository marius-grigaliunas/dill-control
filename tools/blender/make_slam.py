"""The Field Fighter's slam as Blender actions, posed on the very curves
MechSlasher used to compute (resources/vehicles/mch_field_fighter_slash.tres),
saved to art/_source/FieldFighter/fighter_moves.blend for editing, and
exported as resources/vehicles/moves/mch_field_fighter_slam_r.tres and _l:

    blender --background --factory-startup --python tools/blender/make_slam.py

Opens nothing of the user's. Once the moves exist the user edits the
actions in the .blend and exports again from the Dill tab.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.dirname(os.path.dirname(HERE))

import bpy  # noqa: E402
from mathutils import Matrix  # noqa: E402
import dill_mech  # noqa: E402
from dill_mech import export, rig  # noqa: E402

# mch_field_fighter_slash.tres and MechSlashStats' defaults for what it leaves out.
WINDUP, STRIKE, RECOVERY = 0.28, 0.36, 0.32
COCK_PITCH, STRIKE_PITCH, COCK_YAW, STRIKE_YAW = 75.0, 55.0, 40.0, 28.0
FPS = 60


def smooth(t):
    return t * t * (3.0 - 2.0 * t)


def arm_angles(elapsed, side):
    """MechSlasher.ArmAngles: pitch and yaw in radians at a time."""
    cock_p, strike_p = math.radians(COCK_PITCH), -math.radians(STRIKE_PITCH)
    cock_y, strike_y = side * math.radians(COCK_YAW), -side * math.radians(STRIKE_YAW)
    if elapsed < WINDUP:
        t = smooth(elapsed / WINDUP)
        return cock_p * t, cock_y * t
    if elapsed < WINDUP + STRIKE:
        t = smooth((elapsed - WINDUP) / STRIKE)
        return cock_p + (strike_p - cock_p) * t, cock_y + (strike_y - cock_y) * t
    t = smooth(min(1.0, (elapsed - WINDUP - STRIKE) / RECOVERY))
    return strike_p + (0.0 - strike_p) * t, strike_y + (0.0 - strike_y) * t


dill_mech.register()
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
scene = bpy.context.scene
scene.render.fps = FPS
scene.render.fps_base = 1.0
r = rig.import_mech(os.path.join(ROOT, 'art', 'voxels', 'mch_field_fighter.vox'))
r.animation_data_create()
parts = {o['dill_part']: o for o in export.part_objects(r)}
last = int(round((WINDUP + STRIKE + RECOVERY) * FPS))

for arm_name, side, move in (('ArmR', -1.0, 'slam_r'), ('ArmL', 1.0, 'slam_l')):
    action = bpy.data.actions.new(move)
    action.use_fake_user = True
    r.animation_data.action = action
    pb = r.pose.bones[arm_name]
    obj = parts[arm_name]
    for b in r.pose.bones:
        b.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    inv = r.matrix_world.inverted()
    obj_rest = inv @ obj.matrix_world
    bone_rest = pb.matrix.copy()
    # The part rides the bone rigidly: object = bone @ offset.
    offset = bone_rest.inverted() @ obj_rest
    rest_loc = Matrix.Translation(obj_rest.to_translation())
    rest_rot = obj_rest.to_3x3().to_4x4()
    for frame in range(0, last + 1):
        pitch, yaw = arm_angles(frame / FPS, side)
        # Godot's Basis(Right, pitch) * Basis(Up, yaw) is Blender's X then Z.
        target = rest_loc @ Matrix.Rotation(pitch, 4, 'X') @ Matrix.Rotation(yaw, 4, 'Z') @ rest_rot
        pb.matrix = target @ offset.inverted()
        bpy.context.view_layer.update()
        pb.keyframe_insert('rotation_quaternion', frame=frame)
        pb.keyframe_insert('location', frame=frame)
    hit = action.pose_markers.new('hit')
    hit.frame = int(round((WINDUP + 0.45 * STRIKE) * FPS))
    shake = action.pose_markers.new('shake')
    shake.frame = int(round(WINDUP * FPS))
    path, claimed = export.export_action(r, action, 0, last)
    print(f'[slam] {move}: {path} claims {claimed}')

r.animation_data.action = None
out = os.path.join(ROOT, 'art', '_source', 'FieldFighter', 'fighter_moves.blend')
bpy.ops.wm.save_as_mainfile(filepath=out)
print(f'[slam] saved {out}')
