"""Sends one pose of a move to the Godot editor's Lab tab, as the live mirror
does, from a background Blender:

    blender --background art/_source/FieldFighter/fighter_moves.blend --python tools/blender/check_live.py -- slam_r 17
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from dill_mech import live  # noqa: E402

args = sys.argv[sys.argv.index('--') + 1:]
action, frame = args[0], int(args[1])
rig = next(o for o in bpy.data.objects if o.get('dill_mech'))
rig.animation_data.action = bpy.data.actions[action]
bpy.context.scene.frame_set(frame)
print('[live]', live._send(rig['dill_project'], live.pose_args(rig)))
