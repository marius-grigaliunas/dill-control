"""A move's timing as the Godot asset lab saved it, read back into its action
(docs/decisions/0046; docs/handoffs/lab-walk-and-moves.md).

The lab edits a move's speed, blend and marker times and saves them to its
.tres with a stamp. Poses stay here: Read Timing rescales the action's keys
for the speed, puts its markers where the lab put them, and keeps the blend
and the stamp on the action, so the next export carries them. Export will
not write over a stamp the action has not read.
"""
import os
import re

import bpy

# MechMove's defaults: Godot leaves a property out of the .tres when it has one.
DEFAULT_FPS = 30.0


def move_path(rig, action):
    move = action.name.lower().replace(' ', '_')
    return os.path.join(rig['dill_project'], 'resources', 'vehicles', 'moves', f"{rig['dill_mech']}_{move}.tres")


def _field(text, key):
    m = re.search(rf'^{key} = (.*)$', text, re.M)
    return m.group(1).strip() if m else None


def _inside(value):
    """What is between a packed array's brackets."""
    if not value or '(' not in value:
        return ''
    return value[value.index('(') + 1:value.rindex(')')]


def read_move(path):
    """The timing in a move's .tres: {fps, names, times, blend, stamp}, or None."""
    if not os.path.exists(path):
        return None
    with open(path, encoding='utf-8') as f:
        text = f.read()
    fps = _field(text, 'Fps')
    blend = _field(text, 'Blend')
    stamp = _field(text, 'TimingStamp')
    return {
        'fps': float(fps) if fps else DEFAULT_FPS,
        'names': re.findall(r'"([^"]*)"', _inside(_field(text, 'EventNames'))),
        'times': [float(t) for t in _inside(_field(text, 'EventTimes')).split(',') if t.strip()],
        'blend': float(blend) if blend else -1.0,
        'stamp': stamp.strip('"') if stamp else '',
    }


def fcurves(action):
    """Every curve of a layered action (Blender 4.4 on), every slot's."""
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                yield from bag.fcurves


def read_timing(rig, action):
    """Rescales `action`'s keys to the speed the lab saved, puts its markers
    at the lab's times, and keeps the blend and stamp on it. Returns a line
    saying what changed, or raises ValueError when there is nothing to read."""
    path = move_path(rig, action)
    move = read_move(path)
    if move is None:
        raise ValueError(f'no move at {path}: export it first')
    scene = bpy.context.scene
    scene_fps = scene.render.fps / scene.render.fps_base
    start = action.frame_range[0]
    said = []

    # A move the lab sped up plays at more frames a second than the scene:
    # its keys come closer together by as much.
    factor = scene_fps / move['fps']
    if abs(factor - 1.0) > 1e-4:
        for fc in fcurves(action):
            for kp in fc.keyframe_points:
                kp.co.x = start + (kp.co.x - start) * factor
                kp.handle_left.x = start + (kp.handle_left.x - start) * factor
                kp.handle_right.x = start + (kp.handle_right.x - start) * factor
            fc.update()
        if action.use_frame_range:
            action.frame_end = start + (action.frame_end - start) * factor
        said.append(f'keys x{factor:.3f} ({move["fps"]:g} fps to {scene_fps:g})')

    marks = list(zip(move['names'], move['times']))
    if marks:
        for m in list(action.pose_markers):
            action.pose_markers.remove(m)
        for name, time in marks:
            action.pose_markers.new(name).frame = int(round(start + time * scene_fps))
        said.append('markers ' + ', '.join(f'{n} {t:.3f} s' for n, t in marks))

    if move['blend'] >= 0.0:
        action['dill_blend'] = move['blend']
        said.append(f'blend {move["blend"]:g} s')
    elif 'dill_blend' in action:
        del action['dill_blend']
    action['dill_timing_stamp'] = move['stamp']
    return '; '.join(said) or 'nothing to change'


def unread_stamp(rig, action):
    """The lab's stamp on the move's file when the action has not read it,
    else None."""
    move = read_move(move_path(rig, action))
    if move is None or not move['stamp']:
        return None
    return None if move['stamp'] == action.get('dill_timing_stamp', '') else move['stamp']
