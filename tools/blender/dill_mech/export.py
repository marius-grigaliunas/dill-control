"""A posed action baked to a MechMove the game plays (docs/decisions/0046).

Each frame, each part's transform in the mech's own space (the rig's), turned
from Blender's axes (Z up) to Godot's (Y up): Godot (x, y, z) is Blender
(x, z, -y). A part is in the move if it moves at all; the rest keep walking.
Timeline markers on the action (or the scene, inside the frame range) are its
events: 'hit', 'sfx:<name>', 'shake'. A blend and a timing stamp the Godot
asset lab saved come back through Read Timing (timing.py) and go out again.
"""
import os

import bpy
from mathutils import Matrix

# Blender to Godot axes, and back.
TO_GODOT = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
FROM_GODOT = TO_GODOT.inverted()

STILL = 1e-4


def part_objects(rig):
    return sorted((o for o in rig.children_recursive if o.get('dill_part')), key=lambda o: o['dill_part'])


def godot_transforms(rig):
    """Every part's transform now, in the rig's space, in Godot's axes:
    {name: Matrix 4x4}."""
    inv = rig.matrix_world.inverted()
    return {o['dill_part']: TO_GODOT @ (inv @ o.matrix_world) @ FROM_GODOT for o in part_objects(rig)}


def _same(a, b):
    return all(abs(a[r][c] - b[r][c]) < STILL for r in range(3) for c in range(4))


def sample(rig, action, frame_start, frame_end):
    """(frames, {name: [Matrix per frame]}, rest {name: Matrix}) for an action."""
    scene = bpy.context.scene
    keep_action = rig.animation_data.action if rig.animation_data else None
    keep_frame = scene.frame_current
    if rig.animation_data is None:
        rig.animation_data_create()

    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    scene.frame_set(frame_start)
    rest = godot_transforms(rig)

    rig.animation_data.action = action
    tracks = {name: [] for name in rest}
    frames = 0
    for f in range(frame_start, frame_end + 1):
        scene.frame_set(f)
        now = godot_transforms(rig)
        for name in tracks:
            tracks[name].append(now[name])
        frames += 1

    rig.animation_data.action = keep_action
    scene.frame_set(keep_frame)
    return frames, tracks, rest


def events(action, scene, frame_start, frame_end, fps):
    marks = list(action.pose_markers) or [m for m in scene.timeline_markers if frame_start <= m.frame <= frame_end]
    return sorted(((m.name, (m.frame - frame_start) / fps) for m in marks), key=lambda e: e[1])


def _f(x):
    return f'{x:.6g}'


def write_move(path, mech, move_name, fps, frames, tracks, rest, marks, blend=-1.0, stamp=''):
    """The .tres: the parts it claims, and per frame each one's position and
    rotation (x, y, z, w) in the model's space."""
    claimed = [n for n in sorted(tracks) if any(not _same(m, rest[n]) for m in tracks[n])]
    positions, rotations = [], []
    for f in range(frames):
        for n in claimed:
            m = tracks[n][f]
            t = m.to_translation()
            q = m.to_quaternion().normalized()
            positions.append(f'{_f(t.x)}, {_f(t.y)}, {_f(t.z)}')
            rotations.extend([_f(q.x), _f(q.y), _f(q.z), _f(q.w)])
    names = ', '.join(f'"{n}"' for n in claimed)
    event_names = ', '.join(f'"{e[0]}"' for e in marks)
    event_times = ', '.join(_f(e[1]) for e in marks)
    text = (
        '[gd_resource type="Resource" script_class="MechMove" load_steps=2 format=3]\n\n'
        '[ext_resource type="Script" path="res://scripts/vehicles/MechMove.cs" id="1_script"]\n\n'
        '[resource]\n'
        f'resource_name = "{mech}_{move_name}"\n'
        'script = ExtResource("1_script")\n'
        f'Mech = "{mech}"\n'
        f'Fps = {_f(fps)}\n'
        f'Frames = {frames}\n'
        f'Parts = PackedStringArray({names})\n'
        f'Positions = PackedVector3Array({", ".join(positions)})\n'
        f'Rotations = PackedFloat32Array({", ".join(rotations)})\n'
        f'EventNames = PackedStringArray({event_names})\n'
        f'EventTimes = PackedFloat32Array({event_times})\n'
        f'Blend = {_f(blend)}\n'
        f'TimingStamp = "{stamp}"\n'
    )
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    return claimed


class TimingNotRead(Exception):
    """The asset lab saved this move's timing after the action last read it."""


def export_action(rig, action, frame_start=None, frame_end=None, force=False):
    """Bakes `action` on `rig` to resources/vehicles/moves/<mech>_<action>.tres
    in the project the mech came from. Returns (path, parts claimed). Raises
    TimingNotRead rather than write over timing the lab saved and the action
    has not read, unless `force`."""
    from . import timing
    stamp = timing.unread_stamp(rig, action)
    if stamp and not force:
        raise TimingNotRead(stamp)
    scene = bpy.context.scene
    if frame_start is None or frame_end is None:
        lo, hi = action.frame_range
        frame_start, frame_end = int(round(lo)), int(round(hi))
    fps = scene.render.fps / scene.render.fps_base
    frames, tracks, rest = sample(rig, action, frame_start, frame_end)
    mech = rig['dill_mech']
    move = action.name.lower().replace(' ', '_')
    path = os.path.join(rig['dill_project'], 'resources', 'vehicles', 'moves', f'{mech}_{move}.tres')
    claimed = write_move(path, mech, move, fps, frames, tracks, rest, events(action, scene, frame_start, frame_end, fps),
                         action.get('dill_blend', -1.0), action.get('dill_timing_stamp', ''))
    return path, claimed
