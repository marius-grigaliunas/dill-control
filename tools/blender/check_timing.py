"""Timing from Godot's asset lab, read back and exported again: nothing lost.

    blender --background <copy of a moves .blend> --python tools/blender/check_timing.py -- smash

Run on a copy: it changes the action and writes the move's .tres. Before it,
save the move's timing in the Lab (lab_timing ... save). It checks that
Export refuses the unread timing, that Read Timing takes it, and that the
move exported after it holds the lab's speed, markers, blend and stamp and
poses every part as the lab's file did at every moment of it.
"""
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bpy  # noqa: E402
from dill_mech import export, timing  # noqa: E402

move_name = sys.argv[sys.argv.index('--') + 1]
rig = next(o for o in bpy.data.objects if o.get('dill_mech'))
action = bpy.data.actions[move_name]
if rig.animation_data is None:
    rig.animation_data_create()
rig.animation_data.action = action
path = timing.move_path(rig, action)
failures = []


def check(what, ok):
    print(('  ok    ' if ok else '  FAIL  ') + what)
    if not ok:
        failures.append(what)


def parse(text):
    def inside(key):
        m = re.search(rf'^{key} = [A-Za-z0-9]*\((.*)\)$', text, re.M)
        return [float(v) for v in m.group(1).split(',')] if m and m.group(1).strip() else []
    m = re.search(r'^Parts = PackedStringArray\((.*)\)$', text, re.M)
    parts = re.findall(r'"([^"]*)"', m.group(1))
    fps = float(re.search(r'^Fps = (.*)$', text, re.M).group(1))
    frames = int(re.search(r'^Frames = (.*)$', text, re.M).group(1))
    return parts, fps, frames, inside('Positions'), inside('Rotations')


def pose(move, part, t):
    """A part's position and rotation at t seconds, between frames."""
    parts, fps, frames, pos, rot = move
    p = parts.index(part)
    f = min(max(t * fps, 0.0), frames - 1)
    a = int(f)
    b = min(a + 1, frames - 1)
    w = f - a
    at = lambda arr, n, k: arr[(k * len(parts) + p) * n:(k * len(parts) + p) * n + n]  # noqa: E731
    pa, pb = at(pos, 3, a), at(pos, 3, b)
    qa, qb = at(rot, 4, a), at(rot, 4, b)
    if sum(x * y for x, y in zip(qa, qb)) < 0:
        qb = [-x for x in qb]
    q = [x + (y - x) * w for x, y in zip(qa, qb)]
    n = math.sqrt(sum(x * x for x in q))
    return [x + (y - x) * w for x, y in zip(pa, pb)], [x / n for x in q]


lab_text = open(path, encoding='utf-8').read()
lab = timing.read_move(path)
check(f'the lab saved a stamp ({lab["stamp"]})', bool(lab['stamp']))

try:
    export.export_action(rig, action)
    check('export refuses timing it has not read', False)
except export.TimingNotRead:
    check('export refuses timing it has not read', True)
check('the file is untouched by the refusal', open(path, encoding='utf-8').read() == lab_text)

print('  read: ' + timing.read_timing(rig, action))
export.export_action(rig, action)
out = timing.read_move(path)
scene_fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
check(f'exported at the scene\'s {scene_fps:g} fps', abs(out['fps'] - scene_fps) < 1e-3)
check(f'markers kept: {list(zip(out["names"], out["times"]))}',
      out['names'] == lab['names'] and all(abs(a - b) <= 0.5 / scene_fps + 1e-4 for a, b in zip(out['times'], lab['times'])))
check(f'blend kept: {out["blend"]}', abs(out['blend'] - lab['blend']) < 1e-4)
check('stamp kept', out['stamp'] == lab['stamp'])

before, after = parse(lab_text), parse(open(path, encoding='utf-8').read())
length = (before[2] - 1) / before[1]
check(f'length kept: {length:.3f} s', abs((after[2] - 1) / after[1] - length) <= 1.0 / scene_fps + 1e-4)
worst = (0.0, 0.0, '', 0.0)
for part in before[0]:
    for k in range(41):
        t = length * k / 40
        (pa, qa), (pb, qb) = pose(before, part, t), pose(after, part, t)
        dp = math.dist(pa, pb)
        dq = math.degrees(2 * math.acos(min(1.0, abs(sum(x * y for x, y in zip(qa, qb))))))
        if dp + dq * 0.01 > worst[0] + worst[1] * 0.01:
            worst = (dp, dq, part, t)
check(f'poses kept: worst {worst[0] * 100:.1f} cm, {worst[1]:.1f} deg ({worst[2]} at {worst[3]:.2f} s)',
      worst[0] < 0.05 and worst[1] < 3.0)

print('PASS' if not failures else f'FAIL: {len(failures)}')
